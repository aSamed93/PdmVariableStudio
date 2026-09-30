using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Kullanıcının işlem listesine dosya eklemesini sağlayan PDM kaynakları.
/// Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
internal sealed class PdmFileBrowser : IPdmFileBrowser
{
    /// <summary>
    /// Bir aramada işlenecek üst sınır.
    /// </summary>
    /// <remarks>
    /// Çok geniş bir desen (ör. yalnızca <c>*</c>) vault'un tamamını döndürebiliyor.
    /// Sınır arayüzün kilitlenmesini önlemek için var; aşıldığında arama durur ve kullanıcıya
    /// söylenir.
    /// </remarks>
    private const int MaxResults = 20000;

    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmFileBrowser(IEdmVault5 vault, IStudioLog log)
    {
        _vault = vault;
        _log = log;
    }

    /// <summary>PDM'in kendi dosya seçme penceresini açar.</summary>
    public OperationOutcome<IReadOnlyList<PdmFileIdentity>> BrowseForFiles(IntPtr parentWindow)
    {
        using var scope = new ComScope();

        try
        {
            const int Flags = (int)(
                EdmBrowseFlag.EdmBws_ForOpen |
                EdmBrowseFlag.EdmBws_PermitMultipleSel |
                EdmBrowseFlag.EdmBws_PermitVaultFiles);

            var selection = scope.Track(_vault.BrowseForFile(
                parentWindow.ToInt32(),
                Flags,
                Loc.T(
                    "Tüm dosyalar (*.*)|*.*|SOLIDWORKS parça (*.sldprt)|*.sldprt|" +
                    "SOLIDWORKS montaj (*.sldasm)|*.sldasm|SOLIDWORKS teknik resim (*.slddrw)|*.slddrw||",
                    "All files (*.*)|*.*|SOLIDWORKS part (*.sldprt)|*.sldprt|" +
                    "SOLIDWORKS assembly (*.sldasm)|*.sldasm|SOLIDWORKS drawing (*.slddrw)|*.slddrw||"),
                string.Empty,
                string.Empty,
                string.Empty,
                Loc.T("İşleme alınacak dosyaları seçin", "Select the files to process")));

            if (selection is null)
            {
                return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Success(
                    Array.Empty<PdmFileIdentity>());
            }

            var files = new List<PdmFileIdentity>();
            var position = scope.Track(selection.GetHeadPosition());

            while (position is not null && !position.IsNull)
            {
                var path = selection.GetNext(position);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var identity = ResolveByPath(path!, scope);
                if (identity is not null)
                {
                    files.Add(identity);
                }
            }

            _log.Info($"Dosya seçme penceresi: {files.Count} dosya eklendi.");
            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Success(files);
        }
        catch (COMException exception)
        {
            _log.Error("Dosya seçme penceresi açılamadı.", exception);
            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    /// <summary>
    /// PDM arama motoruyla dosya arar.
    /// </summary>
    /// <remarks>
    /// Kendi SQL'imizi yazmak yerine <c>IEdmSearch</c> kullanılıyor: yetki süzgeci, paylaşılan
    /// dosyalar ve silinmiş öğe kuralları PDM tarafında zaten doğru uygulanıyor. Doğrudan
    /// veritabanına sormak bunların hepsini elle taklit etmek demek olurdu.
    /// </remarks>
    public OperationOutcome<IReadOnlyList<PdmFileIdentity>> Search(
        FileSearchCriteria criteria,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (criteria is null || criteria.IsEmpty)
        {
            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Failure(
                IssueCode.Unexpected, "Arama ölçütü boş.");
        }

        using var scope = new ComScope();

        try
        {
            var search = scope.Track(_vault.CreateSearch());

            search.FindFiles = true;
            search.FindFolders = false;

            // Geçmiş sürümler aranmaz: kart değerlerini yalnızca güncel sürümde düzenliyoruz.
            search.FindHistoricStates = false;

            // Çekili ve çekili olmayan dosyaların ikisi de gelsin; çekili olanlar önizlemede
            // zaten "başkası tarafından çekili" olarak işaretlenip uygulanmayacak. Onları
            // aramadan gizlemek, kullanıcıdan bir bilgiyi saklamak olurdu.
            search.FindLockedFiles = true;
            search.FindUnlockedFiles = true;

            search.Recursive = criteria.Recursive;

            if (criteria.StartFolderId > 0)
            {
                search.StartFolderID = criteria.StartFolderId;
            }

            if (!string.IsNullOrWhiteSpace(criteria.FileName))
            {
                search.FileName = criteria.FileName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(criteria.VariableName))
            {
                object name = criteria.VariableName.Trim();
                object value = criteria.VariableValue ?? string.Empty;
                search.AddVariable(ref name, ref value);
            }

            var files = new List<PdmFileIdentity>();
            var seen = new HashSet<PdmFileIdentity>();
            var truncated = false;

            var result = scope.Track(search.GetFirstResult());

            while (result is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (result.ObjectType == EdmObjectType.EdmObject_File)
                {
                    var identity = new PdmFileIdentity(
                        result.ID,
                        result.ParentFolderID,
                        result.Name,
                        PdmVaultContext.ToRelativePath(FolderPathOf(result.Path), _vault.RootFolderPath));

                    if (seen.Add(identity))
                    {
                        files.Add(identity);

                        if (files.Count % 50 == 0)
                        {
                            progress?.Report(files.Count);
                        }
                    }
                }

                if (files.Count >= MaxResults)
                {
                    truncated = true;
                    break;
                }

                result = scope.Track(search.GetNextResult());
            }

            progress?.Report(files.Count);

            if (truncated)
            {
                _log.Warn($"Arama {MaxResults} sonuçta kesildi (desen '{criteria.FileName}').");
            }

            _log.Info($"Arama: {files.Count} dosya bulundu (desen '{criteria.FileName}', " +
                      $"klasör {criteria.StartFolderId}, özyineli {criteria.Recursive}).");

            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Success(files);
        }
        catch (COMException exception)
        {
            _log.Error("Arama başarısız.", exception);
            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    /// <summary>Yerel yoldan dosya kimliğini çözer.</summary>
    private PdmFileIdentity? ResolveByPath(string path, ComScope scope)
    {
        try
        {
            IEdmFolder5? folder = null;
            var file = scope.Track(_vault.GetFileFromPath(path, out folder));

            if (file is null || folder is null)
            {
                _log.Warn($"Yol vault dosyasına çözülemedi: {path}");
                return null;
            }

            scope.Track(folder);

            return new PdmFileIdentity(
                file.ID,
                folder.ID,
                file.Name,
                PdmVaultContext.ToRelativePath(folder.LocalPath, _vault.RootFolderPath));
        }
        catch (COMException exception)
        {
            _log.Warn($"'{path}' çözülemedi: " + PdmErrorTranslator.Describe(exception, _vault));
            return null;
        }
    }

    /// <summary>Arama sonucundaki tam yoldan klasör kısmını alır.</summary>
    private static string FolderPathOf(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return string.Empty;
        }

        var index = fullPath!.LastIndexOf('\\');
        return index > 0 ? fullPath.Substring(0, index) : fullPath;
    }
}
