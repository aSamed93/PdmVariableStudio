using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.Core.Services;

/// <summary>
/// İçe aktarım boru hattı: çalışma kitabını oku, doğrula, PDM'nin güncel hâliyle karşılaştır,
/// uygulama planı çıkar.
/// </summary>
/// <remarks>
/// <b>Bu servis PDM'ye HİÇBİR ŞEY YAZMAZ.</b> Ürettiği <see cref="ChangeSet"/> yalnızca bir
/// öneridir; yazma yalnızca kullanıcı önizlemeyi onayladıktan sonra
/// <see cref="ApplyService"/> tarafından yapılır.
/// </remarks>
public sealed class ImportService
{
    private readonly IPdmVaultContext _vault;
    private readonly IPdmVariableReader _reader;
    private readonly IStudioLog _log;
    private readonly WorkbookReader _workbookReader;

    // Check-out servisine bilerek bağımlılık YOK: bu servis PDM'ye yazmıyor, dolayısıyla
    // check-out da yapmıyor. Kilit durumu okuma anlık görüntüsünden (PdmFileSnapshot) geliyor.
    public ImportService(
        IPdmVaultContext vault,
        IPdmVariableReader reader,
        IStudioLog log,
        WorkbookReader? workbookReader = null)
    {
        _vault = vault;
        _reader = reader;
        _log = log;
        _workbookReader = workbookReader ?? new WorkbookReader();
    }

    /// <summary>
    /// Kullanıcının kültürü. Excel'deki METİN hücrelerini sayı/tarihe çevirirken invariant
    /// denemesinden sonra bu kullanılır.
    /// </summary>
    public CultureInfo UserCulture { get; set; } = CultureInfo.CurrentCulture;

    public OperationOutcome<ChangeSet> BuildChangeSet(
        string workbookPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1-5. adımlar: şema, damga, sütun ve satır eşlemesi.
        progress?.Report("Çalışma kitabı okunuyor");
        var workbook = _workbookReader.Read(workbookPath);

        if (workbook.IsRejected)
        {
            _log.Warn($"Çalışma kitabı reddedildi: {workbookPath}");
            return OperationOutcome<ChangeSet>.Success(Rejected(workbook));
        }

        // 2. adım (devamı): vault kimliği. Yanlış vault en tehlikeli durum — dosya kimlikleri
        // başka bir vault'ta bambaşka dosyalara denk gelir.
        if (!_vault.Vault.Equals(workbook.Vault))
        {
            _log.Warn($"Çalışma kitabı '{workbook.Vault.Name}' vault'undan, açık olan '{_vault.Vault.Name}'.");

            var issue = ValidationIssue.Fatal(
                IssueCode.WrongVault,
                workbook.Vault.Name,
                $"Beklenen '{_vault.Vault.Name}', gelen '{workbook.Vault.Name}'.");

            return OperationOutcome<ChangeSet>.Success(
                new ChangeSet(_vault.Vault, workbook.ExportSessionId, workbookPath,
                    Array.Empty<CellChange>(), Array.Empty<FileApplyPlan>(), new[] { issue }));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 6. adım: güncel PDM değerleri. Yalnızca değişmiş GÖRÜNEN satırların dosyaları için
        // PDM'ye gidilir; değişmemiş bir satır için tur atmanın anlamı yok.
        progress?.Report("PDM güncel değerleri okunuyor");
        var touchedFiles = CollectTouchedFiles(workbook);

        var snapshots = new Dictionary<PdmFileIdentity, PdmFileSnapshot>();
        if (touchedFiles.Count > 0)
        {
            var read = _reader.ReadSnapshots(touchedFiles, workbook.Variables, null, cancellationToken);
            if (read.IsFailure)
            {
                return OperationOutcome<ChangeSet>.Failure(read.Code, read.TechnicalDetail);
            }

            foreach (var snapshot in read.Value)
            {
                snapshots[snapshot.Identity] = snapshot;
            }
        }

        // Değişmiş görünüp okunamayan dosyalar: silinmiş, taşınmış ya da silinip yeniden
        // eklenmiş (yeni kimlik almış) olabilir. Bunlar için "güncel değer = orijinal"
        // varsayımı yapılamaz — yapılırsa her hücre güvenli değişiklik görünür ve ölü bir
        // kimliğe yazılmaya çalışılır.
        var missing = new HashSet<PdmFileIdentity>();
        foreach (var file in touchedFiles)
        {
            if (!snapshots.ContainsKey(file))
            {
                missing.Add(file);
            }
        }

        if (missing.Count > 0)
        {
            _log.Warn($"{missing.Count} dosya vault'ta dışa aktarımdaki klasöründe bulunamadı; " +
                      "satırları uygulanmayacak.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 7-11. adımlar: karşılaştırma, yetki/kilit, uygulanabilirlik.
        progress?.Report("Değişiklikler karşılaştırılıyor");
        var issues = new List<ValidationIssue>(workbook.Issues);
        foreach (var file in missing)
        {
            issues.Add(ValidationIssue.Error(IssueCode.FileNotFound, file.FileName));
        }

        var cells = BuildCells(workbook, snapshots, missing, issues, cancellationToken);
        var plans = BuildFilePlans(cells, snapshots, missing);

        var changeSet = new ChangeSet(
            _vault.Vault, workbook.ExportSessionId, workbookPath, cells, plans, issues);

        _log.Info($"İçe aktarım: {changeSet.SafeChangeCount} güvenli, {changeSet.ConflictCount} çakışma, " +
                  $"{changeSet.ErrorCount} hata, {changeSet.UnchangedCount} değişmemiş.");

        return OperationOutcome<ChangeSet>.Success(changeSet);
    }

    private ChangeSet Rejected(ImportedWorkbook workbook) =>
        new(_vault.Vault, workbook.ExportSessionId, workbook.Path,
            Array.Empty<CellChange>(), Array.Empty<FileApplyPlan>(), workbook.Issues);

    /// <summary>
    /// Excel'de bir değeri gerçekten değişmiş görünen satırların dosyaları.
    /// </summary>
    /// <remarks>
    /// Ön eleme metin düzeyinde değil, TİPLİ karşılaştırmayla yapılır: aksi hâlde "12,4" ile
    /// 12.4 farklı sanılır ve gereksiz yere binlerce dosya için PDM'ye gidilirdi.
    /// Güvenilmez satırlar da dahil edilmez — nasılsa uygulanmayacaklar.
    /// </remarks>
    private List<PdmFileIdentity> CollectTouchedFiles(ImportedWorkbook workbook)
    {
        var files = new List<PdmFileIdentity>();
        var seen = new HashSet<PdmFileIdentity>();

        foreach (var row in workbook.Rows)
        {
            if (!row.IsTrustworthy)
            {
                continue;
            }

            for (var i = 0; i < workbook.Variables.Count; i++)
            {
                if (workbook.VariableColumns[i] <= 0)
                {
                    continue;
                }

                var variable = workbook.Variables[i];
                var requested = VariableValue.From(row.RawExcelValues[i], variable.DataType, UserCulture);
                var original = row.OriginalValues[i];

                if (!requested.Equals(original) && seen.Add(row.File))
                {
                    files.Add(row.File);
                    break;
                }
            }
        }

        return files;
    }

    private List<CellChange> BuildCells(
        ImportedWorkbook workbook,
        IReadOnlyDictionary<PdmFileIdentity, PdmFileSnapshot> snapshots,
        ISet<PdmFileIdentity> missing,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var cells = new List<CellChange>();

        foreach (var row in workbook.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            snapshots.TryGetValue(row.File, out var snapshot);
            var isMissing = missing.Contains(row.File);
            var context = isMissing ? WriteContext.Missing : BuildWriteContext(row.File, snapshot);

            foreach (var issue in row.Issues)
            {
                issues.Add(issue);
            }

            for (var i = 0; i < workbook.Variables.Count; i++)
            {
                var variable = workbook.Variables[i];

                // Sütun silinmiş: mevcut PDM değerine dokunulmaz. Boş bir hücreyi "değeri sil"
                // olarak yorumlamak, silinen bir sütunun tüm değerleri silmesi demek olurdu.
                if (workbook.VariableColumns[i] <= 0)
                {
                    continue;
                }

                var coordinate = new CellCoordinate(row.File, row.Configuration, variable.VariableId);
                var original = row.OriginalValues[i];
                var requested = VariableValue.From(row.RawExcelValues[i], variable.DataType, UserCulture);

                // Anlık görüntü yoksa (dosyaya gidilmemişse) güncel değer olarak orijinal
                // kullanılır. Bu yalnızca değişmemiş satırlarda olur ve sonuç Unchanged'dır.
                var current = snapshot is not null ? snapshot.ValueAt(coordinate) : original;

                var cell = new CellChange(coordinate, variable, row.ExportRowId, original, current, requested);

                if (!row.IsTrustworthy)
                {
                    // Satır güvenilmez: durum ne olursa olsun uygulanmaz.
                    ThreeWayDiffEngine.Evaluate(cell, context);
                    cell.MarkSkipped(FirstIssueCode(row));
                }
                else
                {
                    ThreeWayDiffEngine.Evaluate(cell, context);
                }

                cells.Add(cell);
            }
        }

        return cells;
    }

    private static IssueCode FirstIssueCode(ImportedRow row)
    {
        foreach (var issue in row.Issues)
        {
            if (issue.Severity != IssueSeverity.Warning)
            {
                return issue.Code;
            }
        }

        return IssueCode.RowTampered;
    }

    private WriteContext BuildWriteContext(PdmFileIdentity file, PdmFileSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            return new WriteContext(snapshot.Checkout, snapshot.HasWritePermission);
        }

        // Anlık görüntü yok: dosyaya gidilmemiş demektir ve o satırda değişiklik de yok.
        // Yazılabilir varsayılır; yanlış olsa bile hiçbir hücre SafeChange olmayacağı için
        // sonucu etkilemez.
        return WriteContext.Writable;
    }

    /// <summary>
    /// Dosya başına uygulama planı: hangi dosya çekilecek, hangisi zaten çekili, hangisi
    /// engelli.
    /// </summary>
    private static List<FileApplyPlan> BuildFilePlans(
        IReadOnlyList<CellChange> cells,
        IReadOnlyDictionary<PdmFileIdentity, PdmFileSnapshot> snapshots,
        ISet<PdmFileIdentity> missing)
    {
        var plans = new Dictionary<PdmFileIdentity, FileApplyPlan>();

        foreach (var cell in cells)
        {
            var file = cell.Coordinate.File;

            if (!plans.TryGetValue(file, out var plan))
            {
                if (missing.Contains(file))
                {
                    plan = new FileApplyPlan(file, CheckoutState.Unknown, CheckoutAction.Blocked, IssueCode.FileNotFound);
                    plans[file] = plan;
                    plan.Cells.Add(cell);
                    continue;
                }

                // Anlık görüntü yoksa bu dosyaya HİÇ gidilmemiştir: kullanıcı onun hiçbir
                // değerine dokunmamış. Böyle bir dosya ne çekilir ne de engellenir —
                // "durum bilinmiyor"u engel saymak, değişmemiş yüzlerce dosyayı önizlemede
                // "engellendi" gibi göstererek kullanıcıyı boş yere korkuturdu.
                if (!snapshots.TryGetValue(file, out var snapshot))
                {
                    plan = new FileApplyPlan(file, CheckoutState.Unknown, CheckoutAction.None);
                    plans[file] = plan;
                    plan.Cells.Add(cell);
                    continue;
                }

                var (action, reason) = DecideAction(snapshot.Checkout, snapshot.HasWritePermission);
                plan = new FileApplyPlan(file, snapshot.Checkout, action, reason);
                plans[file] = plan;
            }

            plan.Cells.Add(cell);
        }

        var result = new List<FileApplyPlan>(plans.Count);
        foreach (var plan in plans.Values)
        {
            result.Add(plan);
        }

        return result;
    }

    private static (CheckoutAction Action, IssueCode Reason) DecideAction(CheckoutState checkout, bool hasPermission)
    {
        if (!hasPermission)
        {
            return (CheckoutAction.Blocked, IssueCode.PermissionDenied);
        }

        return checkout.Status switch
        {
            CheckoutStatus.CheckedOutByMe => (CheckoutAction.UseExisting, IssueCode.None),
            CheckoutStatus.NotCheckedOut => (CheckoutAction.CheckOut, IssueCode.None),
            CheckoutStatus.CheckedOutByOther => (CheckoutAction.Blocked, IssueCode.LockedByOtherUser),
            _ => (CheckoutAction.Blocked, IssueCode.PermissionDenied),
        };
    }
}
