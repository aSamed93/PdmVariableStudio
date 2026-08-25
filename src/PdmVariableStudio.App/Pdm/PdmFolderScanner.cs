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
/// Klasör içeriğini gezer. Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
internal sealed class PdmFolderScanner : IPdmFolderScanner
{
    /// <summary>
    /// Bir taramada işlenecek üst sınır.
    /// </summary>
    /// <remarks>
    /// Yanlışlıkla vault kökü seçilip alt klasörler açıldığında yüz binlerce dosya
    /// gelebiliyor. Sınır, arayüzün kilitlenmesini önlemek için var; aşıldığında tarama
    /// durur ve kullanıcıya daha dar bir kapsam seçmesi söylenir.
    /// </remarks>
    private const int MaxFiles = 50000;

    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmFolderScanner(IEdmVault5 vault, IStudioLog log)
    {
        _vault = vault;
        _log = log;
    }

    public OperationOutcome<IReadOnlyList<PdmFileIdentity>> ScanFolder(
        int folderId,
        bool includeSubfolders,
        IReadOnlyCollection<string> extensionFilter,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var files = new List<PdmFileIdentity>();
            var visitedFolders = new HashSet<int>();
            var pending = new Queue<int>();

            pending.Enqueue(folderId);
            visitedFolders.Add(folderId);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentId = pending.Dequeue();
                var outcome = ScanSingleFolder(
                    currentId, includeSubfolders, extensionFilter, files, visitedFolders, pending,
                    progress, cancellationToken);

                if (outcome.IsFailure)
                {
                    // Tek bir alt klasör okunamazsa (yetki yok gibi) tarama durmaz; o klasör
                    // atlanır. Kök klasör okunamıyorsa tarama biter.
                    if (currentId == folderId)
                    {
                        // Bu yol eskiden SESSİZCE hata dönüyordu: kullanıcı durum çubuğunda
                        // bir ileti görüyor ama günlükte hiçbir iz kalmıyordu ve sebebi
                        // anlamak imkânsızdı.
                        _log.Error($"Kök klasör {folderId} taranamadı: {outcome.Code}. " +
                                   outcome.TechnicalDetail);

                        return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Failure(
                            outcome.Code, outcome.TechnicalDetail);
                    }

                    _log.Warn($"Klasör {currentId} atlandı: {outcome.Code}.");
                }

                if (files.Count >= MaxFiles)
                {
                    _log.Warn($"Tarama {MaxFiles} dosyada durduruldu (klasör {folderId}).");
                    break;
                }
            }

            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Success(files);
        }
        catch (COMException exception)
        {
            _log.Error($"Klasör {folderId} taranamadı.", exception);
            return OperationOutcome<IReadOnlyList<PdmFileIdentity>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    private OperationOutcome ScanSingleFolder(
        int folderId,
        bool includeSubfolders,
        IReadOnlyCollection<string> extensionFilter,
        List<PdmFileIdentity> files,
        HashSet<int> visitedFolders,
        Queue<int> pending,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = new ComScope();

            if (_vault.GetObject(EdmObjectType.EdmObject_Folder, folderId) is not IEdmFolder5 folder)
            {
                return OperationOutcome.Failure(IssueCode.FolderNotFound, folderId.ToString());
            }

            scope.Track(folder);

            var relativePath = PdmVaultContext.ToRelativePath(folder.LocalPath, _vault.RootFolderPath);

            var filePosition = scope.Track(folder.GetFirstFilePosition());
            while (!filePosition.IsNull)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = scope.Track(folder.GetNextFile(filePosition));
                if (file is null)
                {
                    continue;
                }

                var name = file.Name ?? string.Empty;

                if (!MatchesFilter(name, extensionFilter))
                {
                    continue;
                }

                files.Add(new PdmFileIdentity(file.ID, folderId, name, relativePath));

                if (files.Count % 25 == 0)
                {
                    progress?.Report(files.Count);
                }

                if (files.Count >= MaxFiles)
                {
                    break;
                }
            }

            progress?.Report(files.Count);

            if (!includeSubfolders)
            {
                return OperationOutcome.Success();
            }

            var subPosition = scope.Track(folder.GetFirstSubFolderPosition());
            while (!subPosition.IsNull)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var subFolder = scope.Track(folder.GetNextSubFolder(subPosition));
                if (subFolder is null)
                {
                    continue;
                }

                // Ziyaret kümesi bilinçli: PDM'de klasör ağacında döngü beklemiyoruz ama
                // aynı klasörün iki kez kuyruğa girmesi dosyaları çiftlerdi.
                if (visitedFolders.Add(subFolder.ID))
                {
                    pending.Enqueue(subFolder.ID);
                }
            }

            return OperationOutcome.Success();
        }
        catch (COMException exception)
        {
            return OperationOutcome.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    /// <summary>Uzantı süzgeci. Boş süzgeç tüm dosyalar demektir.</summary>
    internal static bool MatchesFilter(string fileName, IReadOnlyCollection<string> extensionFilter)
    {
        if (extensionFilter.Count == 0)
        {
            return true;
        }

        foreach (var extension in extensionFilter)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
