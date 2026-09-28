using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;

namespace PdmVariableStudio.Core.Services;

/// <summary>Montaj açılımının bir dosyası: kimliği ve dışa aktarım kapsamı.</summary>
public sealed class ExpandedFile
{
    public ExpandedFile(PdmFileIdentity file, FileExportScope scope)
    {
        File = file;
        Scope = scope;
    }

    public PdmFileIdentity File { get; }

    public FileExportScope Scope { get; }
}

/// <summary>
/// Montaj ağacını (bulunuşlar) dışa aktarılacak dosya listesine çevirir.
/// </summary>
/// <remarks>
/// <para>
/// PDM'den bağımsız, saf hesap — bu yüzden Core'da ve PDM istemcisi olmadan test ediliyor.
/// Adaptör ağacı olduğu gibi verir; burada üç iş yapılır:
/// </para>
/// <list type="number">
/// <item><b>Toplam adet.</b> Bir bileşenin adedi, üst montajın TEK kopyası içindir. Kök montaj
/// için gereken toplam, yol boyunca çarpılarak bulunur: 2 adet alt montaj × içinde 3 cıvata = 6.
/// Aynı dosya ağacın birden fazla dalında geçiyorsa toplamlar toplanır.</item>
/// <item><b>Tekilleştirme.</b> Dosya × konfigürasyon başına tek satır. Aynı cıvatayı dört
/// satırda göstermek, Excel'de dört ayrı yere aynı değeri yazmak ve içe aktarımda aynı karta
/// dört kez yazmaya çalışmak demek olurdu.</item>
/// <item><b>Dosya düzeyi satırı.</b> Montajın kullandığı konfigürasyonun yanına her zaman
/// <c>@</c> (SOLIDWORKS'teki Custom sekmesi) satırı eklenir; kullanıcıların en çok doldurduğu
/// alanlar (açıklama, malzeme…) çoğu zaman orada.</item>
/// </list>
/// </remarks>
public static class AssemblyExpansion
{
    /// <param name="root">Kök montaj.</param>
    /// <param name="rootConfiguration">Açılımı yapılan kök konfigürasyon.</param>
    /// <param name="occurrences">
    /// Bulunuşlar, DERİNLİK ÖNCELİKLİ sırayla (bir montajın çocukları, montajın hemen
    /// ardından). Toplam adet hesabı bu sıraya dayanır.
    /// </param>
    /// <param name="includeRoot">Kök montajın kendisi de listeye girsin mi.</param>
    public static IReadOnlyList<ExpandedFile> Expand(
        PdmFileIdentity root,
        ConfigurationKey rootConfiguration,
        IReadOnlyList<AssemblyOccurrence> occurrences,
        bool includeRoot)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var order = new List<PdmFileIdentity>();
        var accumulators = new Dictionary<PdmFileIdentity, FileAccumulator>();

        if (includeRoot)
        {
            Accumulate(order, accumulators, root, rootConfiguration, level: 0, total: 1, parentName: string.Empty);
        }

        // multipliers[L] = şu anki dalda L seviyesindeki montajın, kökün BİR kopyası için
        // toplam adedi. Seviye 0 (kök) her zaman 1.
        var multipliers = new List<int> { 1 };

        foreach (var occurrence in occurrences)
        {
            var level = occurrence.Level;

            // Ağaçta yukarı çıkıldıysa derin seviyelerin çarpanları artık geçersiz.
            if (multipliers.Count > level)
            {
                multipliers.RemoveRange(level, multipliers.Count - level);
            }

            // Sıra bozuksa (bir seviye atlandıysa) eksik çarpanları 1 say: yanlış adet
            // göstermektense ebeveynin adedini bilmediğimizi varsaymak daha az yanıltıcı.
            while (multipliers.Count < level)
            {
                multipliers.Add(1);
            }

            var total = checked(occurrence.Quantity * multipliers[level - 1]);
            multipliers.Add(total);

            Accumulate(order, accumulators, occurrence.File, occurrence.Configuration,
                level, total, occurrence.ParentName);
        }

        var result = new List<ExpandedFile>(order.Count);
        foreach (var file in order)
        {
            result.Add(new ExpandedFile(file, accumulators[file].ToScope()));
        }

        return result;
    }

    private static void Accumulate(
        List<PdmFileIdentity> order,
        Dictionary<PdmFileIdentity, FileAccumulator> accumulators,
        PdmFileIdentity file,
        ConfigurationKey configuration,
        int level,
        int total,
        string parentName)
    {
        if (!accumulators.TryGetValue(file, out var accumulator))
        {
            accumulator = new FileAccumulator();
            accumulators[file] = accumulator;
            order.Add(file);
        }

        accumulator.Add(configuration, level, total, parentName);
    }

    /// <summary>Bir dosyanın konfigürasyon bazında birikmiş yer bilgisi.</summary>
    private sealed class FileAccumulator
    {
        private readonly List<ConfigurationKey> _order = new();
        private readonly Dictionary<ConfigurationKey, Placement> _byConfiguration = new();

        public void Add(ConfigurationKey configuration, int level, int total, string parentName)
        {
            if (!_byConfiguration.TryGetValue(configuration, out var placement))
            {
                placement = new Placement(parentName, level);
                _byConfiguration[configuration] = placement;
                _order.Add(configuration);
            }

            // Seviye: dosyanın ağaçtaki EN ÜST konumu; üst montaj: ilk görüldüğü yer.
            placement.Level = Math.Min(placement.Level, level);
            placement.Total = checked(placement.Total + total);
        }

        public FileExportScope ToScope()
        {
            var placements = new List<KeyValuePair<ConfigurationKey, AssemblyPlacement>>();

            // Dosya düzeyi satırı HER ZAMAN vardır ve önce gelir (çalışma kitabındaki mevcut
            // düzen: @ satırı konfigürasyon satırlarından önce). Adedi, dosyanın montajdaki
            // tüm kullanımı. Konfigürasyonsuz dosyalarda (generic) tek satır budur.
            {
                var level = int.MaxValue;
                var total = 0;
                var parent = string.Empty;

                foreach (var configuration in _order)
                {
                    var placement = _byConfiguration[configuration];
                    level = Math.Min(level, placement.Level);
                    total = checked(total + placement.Total);
                    if (parent.Length == 0)
                    {
                        parent = placement.ParentName;
                    }
                }

                placements.Add(new KeyValuePair<ConfigurationKey, AssemblyPlacement>(
                    ConfigurationKey.FileLevel, new AssemblyPlacement(parent, level, total)));
            }

            foreach (var configuration in _order)
            {
                if (configuration.IsFileLevel)
                {
                    continue;
                }

                var placement = _byConfiguration[configuration];
                placements.Add(new KeyValuePair<ConfigurationKey, AssemblyPlacement>(
                    configuration, new AssemblyPlacement(placement.ParentName, placement.Level, placement.Total)));
            }

            return new FileExportScope(placements);
        }

        private sealed class Placement
        {
            public Placement(string parentName, int level)
            {
                ParentName = parentName;
                Level = level;
            }

            public string ParentName { get; }

            public int Level { get; set; }

            public int Total { get; set; }
        }
    }
}
