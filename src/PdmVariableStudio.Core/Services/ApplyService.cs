using System;
using System.Collections.Generic;
using System.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.Core.Services;

/// <summary>Kullanıcının uygulama ekranında verdiği kararlar.</summary>
public sealed class ApplyOptions
{
    /// <summary>
    /// Kullanıcı check-out yapılacağını açıkça onayladı mı. Onaysız hiçbir dosya çekilmez.
    /// </summary>
    public bool CheckoutConsent { get; set; }

    /// <summary>
    /// İşlem sonunda, BİZİM çektiğimiz dosyalar check-in edilsin mi.
    /// </summary>
    /// <remarks>
    /// Varsayılan açık: 500 dosyayı çekili bırakmak vault için check-in'den daha zararlı.
    /// Ama karar sessiz değil — arayüzde görünür ve tek tıkla kapatılabilir. Check-in yeni bir
    /// VERSION üretir; revizyon artırmaz, iş akışı geçişi tetiklemez.
    /// </remarks>
    public bool CheckInAfterApply { get; set; } = true;

    public string CheckInComment { get; set; } = Settings.StudioSettings.DefaultCheckInComment();

    /// <summary>Undo ise, geri alınan işlemin kimliği.</summary>
    public Guid UndoesOperationId { get; set; }

    public OperationType OperationType { get; set; } = OperationType.Apply;
}

/// <summary>Uygulama sonucunun özeti.</summary>
public sealed class ApplyResult
{
    public ApplyResult(ApplyOperation operation, IReadOnlyList<ValidationIssue> issues)
    {
        Operation = operation;
        Issues = issues;
    }

    public ApplyOperation Operation { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public int AppliedCount => Operation.AppliedCount;

    public int FailedCount => Operation.FailedCount;

    public int SkippedCount => Operation.SkippedCount;

    public OperationOutcomeKind Outcome => Operation.Outcome;
}

/// <summary>
/// Seçilen değişiklikleri PDM'ye yazar.
/// </summary>
/// <remarks>
/// <para>Sıra ve gerekçeleri:</para>
/// <list type="number">
/// <item><b>Yeniden doğrula.</b> Önizleme ile onay arasında PDM değişmiş olabilir. Değişmişse
/// hücre çakışmaya döner ve yazılmaz — sessiz üzerine yazma bu ürünün kabul etmediği tek
/// şeydir.</item>
/// <item><b>Günlüğü aç.</b> Yazılamıyorsa işlem HİÇ başlamaz: geri alınamayacak bir değişiklik
/// yapmaktansa hiç yapmamak yeğdir.</item>
/// <item><b>Dosya döngüsü.</b> Check-out, niyet satırı, yazma, sonuç satırı, check-in.
/// Bir dosyanın başarısızlığı diğerlerini durdurmaz.</item>
/// <item><b>Kapat.</b> Kapanış satırı ve indeks özeti.</item>
/// </list>
/// </remarks>
public sealed class ApplyService
{
    private readonly IPdmVaultContext _vault;
    private readonly IPdmVariableReader _reader;
    private readonly IPdmVariableWriter _writer;
    private readonly IPdmCheckoutService _checkout;
    private readonly IOperationJournal _journal;
    private readonly IStudioLog _log;

    public ApplyService(
        IPdmVaultContext vault,
        IPdmVariableReader reader,
        IPdmVariableWriter writer,
        IPdmCheckoutService checkout,
        IOperationJournal journal,
        IStudioLog log)
    {
        _vault = vault;
        _reader = reader;
        _writer = writer;
        _checkout = checkout;
        _journal = journal;
        _log = log;
    }

    public OperationOutcome<ApplyResult> Apply(
        ChangeSet changeSet,
        ApplyOptions options,
        IProgress<ApplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (changeSet is null)
        {
            throw new ArgumentNullException(nameof(changeSet));
        }

        options ??= new ApplyOptions();

        if (changeSet.IsRejected)
        {
            return OperationOutcome<ApplyResult>.Failure(
                IssueCode.WorkbookCorrupted, "Reddedilmiş bir çalışma kitabı uygulanamaz.");
        }

        // İlk seçim yalnızca "uygulanacak bir şey var mı" sorusunu yanıtlar; bulguları
        // atılıyor, çünkü yeniden doğrulamadan sonraki seçim aynı bulguları tekrar üretecek
        // ve ikisini birleştirmek kullanıcıya her uyarıyı iki kez gösterirdi.
        var plans = SelectApplicablePlans(changeSet, options, new List<ValidationIssue>());

        if (plans.Count == 0)
        {
            return OperationOutcome<ApplyResult>.Failure(
                IssueCode.Unexpected, "Uygulanabilir hücre yok.");
        }

        // --- 1. adım: yeniden doğrulama ---
        progress?.Report(ApplyProgress.Revalidating());
        RevalidateAgainstCurrentValues(changeSet, plans, cancellationToken);

        var issues = new List<ValidationIssue>();
        plans = SelectApplicablePlans(changeSet, options, issues);

        if (plans.Count == 0)
        {
            return OperationOutcome<ApplyResult>.Failure(
                IssueCode.ValueChangedSincePreview,
                "Önizlemeden sonra tüm değerler değiştiği için uygulanacak bir şey kalmadı.");
        }

        // --- 2. adım: günlüğü aç ---
        var operation = new ApplyOperation(
            Guid.NewGuid(),
            options.OperationType,
            DateTime.UtcNow,
            _vault.Vault,
            Environment.UserDomainName + "\\" + Environment.UserName,
            _vault.CurrentUserName)
        {
            SourceWorkbookPath = changeSet.SourceWorkbookPath,
            ExportSessionId = changeSet.ExportSessionId,
            UndoesOperationId = options.UndoesOperationId,
            CheckInRequested = options.CheckInAfterApply,
            FileCount = plans.Count,
        };

        var begin = _journal.Begin(operation);
        if (begin.IsFailure)
        {
            // Günlüğe yazılamayan bir değişiklik geri alınamaz. Bunu sessizce kabul etmek
            // ürünün temel vaadini bozardı; işlem hiç başlatılmıyor.
            _log.Error($"İşlem günlüğü açılamadı, uygulama başlatılmadı: {begin.TechnicalDetail}");
            return OperationOutcome<ApplyResult>.Failure(IssueCode.JournalWriteFailed, begin.TechnicalDetail);
        }

        _log.Info($"İşlem {operation.OperationId:D} başladı: {plans.Count} dosya, " +
                  $"tip {operation.Type}, check-in {options.CheckInAfterApply}.");

        // --- 3. adım: dosya döngüsü ---
        var cancelled = false;

        for (var i = 0; i < plans.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // İptal YALNIZCA dosya sınırında dinlenir. Devam eden bir Flush yarıda
                // kesilemez; kesilmesi dosyayı belirsiz durumda bırakırdı.
                cancelled = true;
                MarkRemainingSkipped(plans, i, operation);
                break;
            }

            var plan = plans[i];
            progress?.Report(ApplyProgress.Writing(i + 1, plans.Count, plan.File.FileName));

            ApplyFile(plan, operation, options);
        }

        // --- 4. adım: kapat ---
        operation.Outcome = DetermineOutcome(operation, cancelled);
        var complete = _journal.Complete(operation);

        if (complete.IsFailure)
        {
            // Değişiklikler yapıldı ama kapanış satırı yazılamadı. İşlem "yarım kaldı"
            // görünecek; Undo önizlemesi güncel değerlere bakarak yine de doğru karar verir.
            _log.Error($"İşlem kapanış satırı yazılamadı: {complete.TechnicalDetail}");
            issues.Add(ValidationIssue.Warning(IssueCode.JournalWriteFailed, operation.OperationId.ToString("D")));
        }

        if (options.OperationType == OperationType.Undo && options.UndoesOperationId != Guid.Empty)
        {
            _journal.LinkUndo(options.UndoesOperationId, operation.OperationId);
        }

        _log.Info($"İşlem {operation.OperationId:D} bitti: {operation.OutcomeText}, " +
                  $"{operation.AppliedCount} uygulandı, {operation.FailedCount} başarısız, " +
                  $"{operation.SkippedCount} atlandı.");

        return OperationOutcome<ApplyResult>.Success(new ApplyResult(operation, issues));
    }

    // ------------------------------------------------------------- dosya işlemi

    private void ApplyFile(FileApplyPlan plan, ApplyOperation operation, ApplyOptions options)
    {
        var cells = SelectedApplicableCells(plan);
        if (cells.Count == 0)
        {
            return;
        }

        // 3a. check-out
        var checkout = _checkout.EnsureCheckedOut(plan.File);
        if (!checkout.Succeeded)
        {
            _log.Warn($"{plan.File}: check-out başarısız ({checkout.Code}). Dosya atlandı.");
            FailAll(cells, operation, checkout.Code, checkout.Detail);
            return;
        }

        plan.WeCheckedOut = checkout.WeCheckedOut;

        // 3b. niyet satırları — YAZMADAN ÖNCE
        var entries = new List<ApplyOperationEntry>(cells.Count);
        foreach (var cell in cells)
        {
            var entry = new ApplyOperationEntry(
                plan.File,
                cell.Coordinate.Configuration,
                cell.Variable,
                cell.Current,
                cell.Requested,
                fileVersion: 0);

            operation.Entries.Add(entry);
            entries.Add(entry);
            _journal.WriteIntent(operation.OperationId, entry);
        }

        // 3c. yazma
        var writes = new List<VariableWrite>(cells.Count);
        foreach (var cell in cells)
        {
            writes.Add(new VariableWrite(cell.Coordinate.Configuration, cell.Variable, cell.Requested));
        }

        var writeOutcome = _writer.WriteValues(plan.File, writes);

        // 3d. sonuç satırları
        if (writeOutcome.IsFailure)
        {
            _log.Warn($"{plan.File}: yazma başarısız ({writeOutcome.Code}).");
            for (var i = 0; i < cells.Count; i++)
            {
                RecordFailure(cells[i], entries[i], operation, writeOutcome.Code, writeOutcome.TechnicalDetail);
            }

            RollbackCheckout(plan);
            return;
        }

        ApplyWriteResults(cells, entries, operation, writeOutcome.Value);

        // 3e. check-in — YALNIZCA bizim çektiğimiz dosyalar
        if (plan.WeCheckedOut && options.CheckInAfterApply)
        {
            var checkIn = _checkout.CheckIn(plan.File, options.CheckInComment);
            if (checkIn.IsFailure)
            {
                // Değerler yazıldı; yalnızca iade edilemedi. Bu bir veri sorunu değil, bir
                // operasyon sorunu: kullanıcıya söylenir, hücreler başarılı kalır.
                _log.Warn($"{plan.File}: check-in başarısız ({checkIn.Code}). Dosya çekili kaldı.");
            }
        }
    }

    private void ApplyWriteResults(
        IReadOnlyList<CellChange> cells,
        IReadOnlyList<ApplyOperationEntry> entries,
        ApplyOperation operation,
        IReadOnlyList<VariableWriteResult> results)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var entry = entries[i];
            var result = FindResult(results, cell);

            if (result is null || result.Succeeded)
            {
                cell.MarkApplied();
                entry.Result = EntryResult.Applied;
                entry.Code = IssueCode.None;
                operation.AppliedCount++;
            }
            else
            {
                cell.MarkFailed(result.Code, result.Detail);
                entry.Result = EntryResult.Failed;
                entry.Code = result.Code;
                operation.FailedCount++;
            }

            _journal.WriteResult(operation.OperationId, entry);
        }
    }

    private static VariableWriteResult? FindResult(IReadOnlyList<VariableWriteResult> results, CellChange cell)
    {
        foreach (var result in results)
        {
            if (result.VariableId == cell.Variable.VariableId
                && result.Configuration.Equals(cell.Coordinate.Configuration))
            {
                return result;
            }
        }

        return null;
    }

    private void RecordFailure(
        CellChange cell,
        ApplyOperationEntry entry,
        ApplyOperation operation,
        IssueCode code,
        string detail)
    {
        cell.MarkFailed(code, detail);
        entry.Result = EntryResult.Failed;
        entry.Code = code;
        operation.FailedCount++;
        _journal.WriteResult(operation.OperationId, entry);
    }

    private void FailAll(
        IReadOnlyList<CellChange> cells,
        ApplyOperation operation,
        IssueCode code,
        string detail)
    {
        // Check-out başarısız: yazma hiç denenmedi, bu yüzden niyet satırı da yazılmaz.
        // Yazılsaydı Undo bunları "belirsiz" sayıp gereksiz yere PDM'ye sorardı.
        foreach (var cell in cells)
        {
            cell.MarkFailed(code, detail);
            operation.FailedCount++;
        }
    }

    private void RollbackCheckout(FileApplyPlan plan)
    {
        if (!plan.WeCheckedOut)
        {
            return;
        }

        // Yazma başarısızsa dosyada iz bırakmayız. Kullanıcının kendi check-out'una asla
        // dokunulmaz (WeCheckedOut false olurdu).
        var undo = _checkout.UndoCheckout(plan.File);
        if (undo.IsFailure)
        {
            _log.Warn($"{plan.File}: check-out geri alınamadı ({undo.Code}). Dosya çekili kaldı.");
        }
    }

    // --------------------------------------------------------- yeniden doğrulama

    /// <summary>
    /// Önizlemeden sonra PDM'de değişen değerleri çakışmaya çevirir.
    /// </summary>
    /// <remarks>
    /// İyimser eşzamanlılık kontrolü. Önizleme ile onay arasında dakikalar geçmiş olabilir;
    /// bu aralıkta başka bir kullanıcının yaptığı değişikliği sessizce ezmek kabul edilemez.
    /// </remarks>
    private void RevalidateAgainstCurrentValues(
        ChangeSet changeSet,
        IReadOnlyList<FileApplyPlan> plans,
        CancellationToken cancellationToken)
    {
        var variables = CollectVariables(changeSet);

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fresh = _reader.ReadSnapshot(plan.File, variables);
            if (fresh.IsFailure)
            {
                foreach (var cell in SelectedApplicableCells(plan))
                {
                    cell.DemoteToConflict(cell.Current, fresh.Code);
                }

                continue;
            }

            foreach (var cell in SelectedApplicableCells(plan))
            {
                var current = fresh.Value.ValueAt(cell.Coordinate);

                if (!current.Equals(cell.Current))
                {
                    _log.Warn($"{cell.Coordinate}: önizlemeden sonra değişti " +
                              $"('{cell.Current}' -> '{current}'). Çakışmaya çevrildi.");

                    cell.DemoteToConflict(current, IssueCode.ValueChangedSincePreview);
                }
            }
        }
    }

    private static List<PdmVariableDefinition> CollectVariables(ChangeSet changeSet)
    {
        var variables = new List<PdmVariableDefinition>();
        var seen = new HashSet<int>();

        foreach (var cell in changeSet.Cells)
        {
            if (seen.Add(cell.Variable.VariableId))
            {
                variables.Add(cell.Variable);
            }
        }

        return variables;
    }

    // ------------------------------------------------------------- seçim ve özet

    private static List<FileApplyPlan> SelectApplicablePlans(
        ChangeSet changeSet,
        ApplyOptions options,
        List<ValidationIssue> issues)
    {
        var plans = new List<FileApplyPlan>();

        foreach (var plan in changeSet.FilePlans)
        {
            if (SelectedApplicableCells(plan).Count == 0)
            {
                continue;
            }

            if (plan.Action == CheckoutAction.Blocked)
            {
                issues.Add(ValidationIssue.Error(plan.BlockReason, plan.File.FileName));
                continue;
            }

            // Check-out onayı yoksa çekilmesi gereken dosyalara dokunulmaz. Sessiz lifecycle
            // değişikliği bu üründe yasak.
            if (plan.Action == CheckoutAction.CheckOut && !options.CheckoutConsent)
            {
                issues.Add(ValidationIssue.Error(IssueCode.CheckoutRequired, plan.File.FileName));
                continue;
            }

            plans.Add(plan);
        }

        return plans;
    }

    private static List<CellChange> SelectedApplicableCells(FileApplyPlan plan)
    {
        var cells = new List<CellChange>();

        foreach (var cell in plan.Cells)
        {
            if (cell.IsSelected && cell.CanApply)
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    private static void MarkRemainingSkipped(IReadOnlyList<FileApplyPlan> plans, int fromIndex, ApplyOperation operation)
    {
        for (var i = fromIndex; i < plans.Count; i++)
        {
            foreach (var cell in SelectedApplicableCells(plans[i]))
            {
                cell.MarkSkipped(IssueCode.Cancelled);
                operation.SkippedCount++;
            }
        }
    }

    private static OperationOutcomeKind DetermineOutcome(ApplyOperation operation, bool cancelled)
    {
        if (cancelled)
        {
            return OperationOutcomeKind.Cancelled;
        }

        if (operation.FailedCount == 0 && operation.AppliedCount > 0)
        {
            return OperationOutcomeKind.Success;
        }

        if (operation.AppliedCount == 0)
        {
            return OperationOutcomeKind.Failed;
        }

        return OperationOutcomeKind.Partial;
    }
}

/// <summary>Uygulama ilerlemesi.</summary>
public sealed class ApplyProgress
{
    private ApplyProgress(string stage, int current, int total, string fileName)
    {
        Stage = stage;
        Current = current;
        Total = total;
        FileName = fileName;
    }

    public string Stage { get; }

    public int Current { get; }

    public int Total { get; }

    public string FileName { get; }

    public static ApplyProgress Revalidating() => new(Loc.T("Değerler yeniden doğrulanıyor", "Revalidating values"), 0, 0, string.Empty);

    public static ApplyProgress Writing(int current, int total, string fileName) =>
        new(Loc.T("Uygulanıyor", "Applying"), current, total, fileName);

    public string Describe() =>
        Total > 0 ? $"{Stage}: {Current} / {Total}  ({FileName})" : Stage;
}
