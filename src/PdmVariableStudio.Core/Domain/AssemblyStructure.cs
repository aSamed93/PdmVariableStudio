using System;
using System.Collections.Generic;

namespace PdmVariableStudio.Core.Domain;

/// <summary>
/// Montaj ağacında bir bileşenin TEK bir bulunuşu (occurrence).
/// </summary>
/// <remarks>
/// Aynı dosya ağaçta birden fazla yerde geçebilir (iki alt montajda da kullanılan bir cıvata);
/// her geçiş ayrı bir bulunuştur. Birleştirme <see cref="Services.AssemblyExpansion"/>'ın işi —
/// PDM adaptörü yalnızca ağacı olduğu gibi, derinlik öncelikli sırayla verir.
/// </remarks>
public sealed class AssemblyOccurrence
{
    public AssemblyOccurrence(
        PdmFileIdentity file,
        ConfigurationKey configuration,
        int level,
        int quantity,
        string? parentName)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bileşen seviyesi 1'den başlar.");
        }

        File = file ?? throw new ArgumentNullException(nameof(file));
        Configuration = configuration;
        Level = level;
        Quantity = Math.Max(0, quantity);
        ParentName = parentName ?? string.Empty;
    }

    public PdmFileIdentity File { get; }

    /// <summary>Montajın bu bileşende kullandığı konfigürasyon.</summary>
    public ConfigurationKey Configuration { get; }

    /// <summary>1 = kök montajın doğrudan alt bileşeni.</summary>
    public int Level { get; }

    /// <summary>
    /// Üst montajın TEK bir kopyası içindeki adet (PDM referansındaki <c>RefCount</c>).
    /// Toplam adet, yol boyunca çarpılarak bulunur.
    /// </summary>
    public int Quantity { get; }

    /// <summary>Doğrudan üstündeki montajın dosya adı. Yalnızca gösterim için.</summary>
    public string ParentName { get; }
}

/// <summary>
/// Çalışma kitabındaki bir satırın montajdaki yeri. Yalnızca GÖSTERİM içindir; eşleştirmede
/// ve karşılaştırmada hiçbir rol oynamaz.
/// </summary>
public sealed class AssemblyPlacement
{
    public AssemblyPlacement(string? parentName, int level, int totalQuantity)
    {
        ParentName = parentName ?? string.Empty;
        Level = level;
        TotalQuantity = totalQuantity;
    }

    /// <summary>Doğrudan üst montaj; kök montajın kendisi için boş.</summary>
    public string ParentName { get; }

    /// <summary>0 = kök montaj, 1 = doğrudan alt bileşen…</summary>
    public int Level { get; }

    /// <summary>Kök montajın BİR kopyası için gereken toplam adet.</summary>
    public int TotalQuantity { get; }
}

/// <summary>
/// Bir dosyanın dışa aktarım kapsamı: hangi konfigürasyonları satır olacak ve her birinin
/// montajdaki yeri.
/// </summary>
/// <remarks>
/// <para>
/// Kapsamı OLMAYAN bir dosya (klasörden, aramadan, tek tek seçilerek eklenen) tüm
/// konfigürasyonlarıyla dışa aktarılır — bugünkü davranış.
/// </para>
/// <para>
/// Montajdan gelen bir dosyada yalnızca montajın KULLANDIĞI konfigürasyonlar ve dosya düzeyi
/// (<c>@</c>, SOLIDWORKS'teki Custom sekmesi) satır olur. Bir parçanın montajda hiç
/// kullanılmayan on konfigürasyonunu listelemek, kullanıcıyı yanlış satırı düzenlemeye iter.
/// </para>
/// </remarks>
public sealed class FileExportScope
{
    private readonly Dictionary<ConfigurationKey, AssemblyPlacement> _placements;
    private readonly List<ConfigurationKey> _order;

    public FileExportScope(IEnumerable<KeyValuePair<ConfigurationKey, AssemblyPlacement>> placements)
    {
        _placements = new Dictionary<ConfigurationKey, AssemblyPlacement>();
        _order = new List<ConfigurationKey>();

        foreach (var pair in placements)
        {
            if (!_placements.ContainsKey(pair.Key))
            {
                _placements[pair.Key] = pair.Value;
                _order.Add(pair.Key);
            }
        }
    }

    /// <summary>Satır olacak konfigürasyonlar, ilk görülme sırasıyla.</summary>
    public IReadOnlyList<ConfigurationKey> Configurations => _order;

    public bool Includes(ConfigurationKey configuration) => _placements.ContainsKey(configuration);

    public AssemblyPlacement? PlacementFor(ConfigurationKey configuration) =>
        _placements.TryGetValue(configuration, out var placement) ? placement : null;

    /// <summary>
    /// İki kapsamı birleştirir: konfigürasyonlar birleşir, aynı konfigürasyonda İLK yer korunur.
    /// </summary>
    /// <remarks>
    /// Aynı dosya iki farklı kök montajdan eklendiğinde adetler toplanamaz — farklı ürünlerin
    /// adetleri. İlk eklenen montajın bilgisi kalır; satırın varlığı ise iki montaj için de
    /// doğrudur.
    /// </remarks>
    public FileExportScope Merge(FileExportScope other)
    {
        var merged = new List<KeyValuePair<ConfigurationKey, AssemblyPlacement>>();

        foreach (var configuration in _order)
        {
            merged.Add(new KeyValuePair<ConfigurationKey, AssemblyPlacement>(configuration, _placements[configuration]));
        }

        foreach (var configuration in other._order)
        {
            if (!_placements.ContainsKey(configuration))
            {
                merged.Add(new KeyValuePair<ConfigurationKey, AssemblyPlacement>(
                    configuration, other._placements[configuration]));
            }
        }

        return new FileExportScope(merged);
    }
}
