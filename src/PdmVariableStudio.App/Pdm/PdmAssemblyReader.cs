using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Montaj yapısını PDM referans ağacından okur. Tüm çağrılar adanmış STA worker thread'inden
/// yapılmalıdır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden BOM şablonu değil, referans ağacı.</b> <c>IEdmFile7.GetComputedBOM</c> bir BOM
/// şablonuna bağlı ve her satır için yalnızca dosya yolu döndürüyor; kimliğe ulaşmak için
/// satır başına ek bir yol çözümlemesi gerekirdi. <c>IEdmReference10</c> ise bileşen başına
/// <c>FileID</c>, <c>FolderID</c>, montajın kullandığı konfigürasyonu
/// (<c>RefConfiguration</c>) ve adedi (<c>RefCount</c>) doğrudan veriyor.
/// </para>
/// <para>
/// <b>Gerçek vault'ta doğrulandı (2026-09-28, test vault, iki seviyeli bir montaj):</b>
/// referans ağacı hesaplanmış BOM ile aynı bileşenleri, aynı konfigürasyonları ve aynı
/// adetleri verdi; alt montaj altındaki üç parça seviye 2'de geldi.
/// </para>
/// <para>
/// <b>Tuzak:</b> <c>GetFirstChildPosition3</c>'e konfigürasyon BOŞ verilirse her bileşen
/// <c>"@"</c> konfigürasyonuyla döner — montajın gerçekte hangi konfigürasyonu kullandığı
/// kaybolur. Bu yüzden kök montajın adlandırılmış bir konfigürasyonu verilir ve her alt
/// montaja, üstünün onda kullandığı konfigürasyonla inilir.
/// </para>
/// <para>
/// <b>Adet</b> bileşenin montajdaki gerçek adedidir. PDM'in BOM görünümündeki "Qty" sütunu
/// ise kartta doldurulmuş bir <c>BOM Quantity</c> değişkenini dikkate alabilir; ikisi farklı
/// olabilir ve bu bilinçli — burada yapı gösteriliyor.
/// </para>
/// </remarks>
internal sealed class PdmAssemblyReader : IPdmAssemblyReader
{
    /// <summary>
    /// Döngüsel bir referansın (bozuk veri) sonsuz özyinelemeye dönmemesi için üst sınır.
    /// Gerçek montajlar bu derinliğe yaklaşmaz.
    /// </summary>
    private const int MaxDepth = 64;

    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmAssemblyReader(IEdmVault5 vault, IStudioLog log)
    {
        _vault = vault;
        _log = log;
    }

    public OperationOutcome<IReadOnlyList<ConfigurationKey>> GetConfigurations(PdmFileIdentity assembly)
    {
        using var scope = new ComScope();

        try
        {
            var file = PdmFileLookup.GetLiveFile(_vault, assembly, scope);
            if (file is null)
            {
                return OperationOutcome<IReadOnlyList<ConfigurationKey>>.Failure(
                    IssueCode.FileNotFound, assembly.ToString());
            }

            // 0 = en son sürümün konfigürasyonları.
            object version = 0;
            var list = scope.Track(file.GetConfigurations(ref version));
            var result = new List<ConfigurationKey>();

            if (list is not null)
            {
                var position = scope.Track(list.GetHeadPosition());
                while (position is not null && !position.IsNull)
                {
                    var key = ConfigurationKey.Named(list.GetNext(position));
                    if (!key.IsFileLevel && !result.Contains(key))
                    {
                        result.Add(key);
                    }
                }
            }

            return OperationOutcome<IReadOnlyList<ConfigurationKey>>.Success(result);
        }
        catch (COMException exception)
        {
            _log.Warn($"{assembly} konfigürasyonları okunamadı: " + PdmErrorTranslator.Describe(exception, _vault));
            return OperationOutcome<IReadOnlyList<ConfigurationKey>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    public OperationOutcome<IReadOnlyList<AssemblyOccurrence>> ReadStructure(
        PdmFileIdentity assembly,
        ConfigurationKey configuration,
        bool includeSubassemblyContents,
        CancellationToken cancellationToken = default)
    {
        if (configuration.IsFileLevel)
        {
            // Boş konfigürasyonla her bileşen "@" döner (bkz. sınıf açıklaması); bu bir
            // çağıran hatası, sessizce yanlış veri üretmek yerine reddediyoruz.
            return OperationOutcome<IReadOnlyList<AssemblyOccurrence>>.Failure(
                IssueCode.ConfigurationNotFound, "Montaj açılımı için adlandırılmış bir konfigürasyon gerekli.");
        }

        using var scope = new ComScope();

        try
        {
            var file = PdmFileLookup.GetLiveFile(_vault, assembly, scope);
            if (file is null)
            {
                return OperationOutcome<IReadOnlyList<AssemblyOccurrence>>.Failure(
                    IssueCode.FileNotFound, assembly.ToString());
            }

            var root = scope.Track(file.GetReferenceTree(assembly.FolderId, 0));
            if (root is not IEdmReference10 root10)
            {
                return OperationOutcome<IReadOnlyList<AssemblyOccurrence>>.Failure(
                    IssueCode.PdmApiError, "Referans ağacı okunamadı (IEdmReference10 desteklenmiyor).");
            }

            var occurrences = new List<AssemblyOccurrence>();
            var folderPaths = new Dictionary<int, string>();
            var path = new HashSet<int> { assembly.FileId };
            var skipped = 0;

            Walk(root10, isTop: true, configuration.Name, level: 1, assembly.FileName,
                includeSubassemblyContents, occurrences, folderPaths, path, ref skipped, scope, cancellationToken);

            _log.Info($"Montaj okundu: {assembly} [{configuration}], {occurrences.Count} bulunuş" +
                      (skipped > 0 ? $", {skipped} bileşen atlandı" : string.Empty) +
                      $", alt montaj içerikleri {(includeSubassemblyContents ? "dahil" : "hariç")}.");

            return OperationOutcome<IReadOnlyList<AssemblyOccurrence>>.Success(occurrences);
        }
        catch (COMException exception)
        {
            _log.Error($"{assembly} montaj yapısı okunamadı.", exception);
            return OperationOutcome<IReadOnlyList<AssemblyOccurrence>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    private void Walk(
        IEdmReference10 parent,
        bool isTop,
        string configuration,
        int level,
        string parentName,
        bool recurse,
        List<AssemblyOccurrence> occurrences,
        Dictionary<int, string> folderPaths,
        HashSet<int> path,
        ref int skipped,
        ComScope scope,
        CancellationToken cancellationToken)
    {
        if (level > MaxDepth)
        {
            _log.Warn($"Montaj ağacı {MaxDepth} seviyeyi aştı ('{parentName}' altı); daha derine inilmedi.");
            return;
        }

        var projectName = string.Empty;

        // bPermitReadLocal = true: yerel kopyası olmayan dosyalar da listelenir (veritabanından).
        // EdmRef_File: yalnızca dosya referansları; konfigürasyon parametresi montajın bu
        // seviyede kullandığı konfigürasyon.
        var position = scope.Track(parent.GetFirstChildPosition3(
            ref projectName, isTop, true, (int)EdmRefFlags.EdmRef_File, configuration, 0));

        while (position is not null && !position.IsNull)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (scope.Track(parent.GetNextChild(position)) is not IEdmReference10 child)
            {
                continue;
            }

            var fileId = child.FileID;
            var folderId = child.FolderID;
            var name = child.Name ?? string.Empty;

            // Kasada olmayan bileşenler (Toolbox'tan kasaya alınmamış parça, dış referans)
            // kimliksiz gelir. Bunlara yazılamaz; sessizce kaybolmasınlar diye günlüğe.
            if (fileId <= 0 || folderId <= 0)
            {
                skipped++;
                _log.Warn($"Montaj bileşeni kasada değil, atlandı: '{name}' ('{parentName}' altında).");
                continue;
            }

            // Döngü koruması: aynı dosya kendi yolunda yeniden görünürse (bozuk referans verisi)
            // içine inilmez.
            if (path.Contains(fileId))
            {
                _log.Warn($"Döngüsel montaj referansı: '{name}' kendi üst montajlarından birinde; içine inilmedi.");
                continue;
            }

            var childConfiguration = child.RefConfiguration ?? string.Empty;
            var identity = new PdmFileIdentity(fileId, folderId, name, RelativePathOf(child, folderPaths, scope));

            occurrences.Add(new AssemblyOccurrence(
                identity,
                ConfigurationKey.Named(childConfiguration),
                level,
                child.RefCount,
                parentName));

            if (recurse && IsAssembly(name))
            {
                path.Add(fileId);
                Walk(child, isTop: false, childConfiguration, level + 1, name, recurse,
                    occurrences, folderPaths, path, ref skipped, scope, cancellationToken);
                path.Remove(fileId);
            }
        }
    }

    private string RelativePathOf(IEdmReference10 reference, Dictionary<int, string> cache, ComScope scope)
    {
        if (cache.TryGetValue(reference.FolderID, out var cached))
        {
            return cached;
        }

        var relative = string.Empty;
        try
        {
            var folder = scope.Track(reference.Folder);
            relative = PdmVaultContext.ToRelativePath(folder?.LocalPath, _vault.RootFolderPath);
        }
        catch (COMException exception)
        {
            // Yol yalnızca gösterim için; okunamazsa satır yine doğru dosyaya bağlı.
            _log.Warn($"Klasör yolu okunamadı (#{reference.FolderID}): " + PdmErrorTranslator.Describe(exception, _vault));
        }

        cache[reference.FolderID] = relative;
        return relative;
    }

    private static bool IsAssembly(string name) =>
        name.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
}
