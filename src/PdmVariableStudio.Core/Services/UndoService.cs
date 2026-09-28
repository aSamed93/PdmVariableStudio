using System;
using System.Collections.Generic;
using System.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Services;

/// <summary>Undo önizlemesindeki tek bir kayıt.</summary>
public sealed class UndoCandidate
{
    public UndoCandidate(ApplyOperationEntry entry, VariableValue currentValue, UndoStatus status, IssueCode reason)
    {
        Entry = entry;
        CurrentValue = currentValue;
        Status = status;
        Reason = reason;
        IsSelected = status == UndoStatus.SafeUndo;
    }

    public ApplyOperationEntry Entry { get; }

    /// <summary>PDM'deki ŞU ANKİ değer. Taze okunur.</summary>
    public VariableValue CurrentValue { get; }

    /// <summary>Geri alınırsa yazılacak değer.</summary>
    public VariableValue ValueAfterUndo => Entry.PreviousValue;

    public UndoStatus Status { get; }

    public IssueCode Reason { get; }

    public bool IsSelected { get; set; }

    public bool CanUndo => Status == UndoStatus.SafeUndo;

    public PdmFileIdentity File => Entry.File;

    public string StatusText => Status switch
    {
        UndoStatus.SafeUndo => "Güvenli",
        UndoStatus.AlreadyReverted => "Zaten geri alınmış",
        UndoStatus.Conflict => "Çakışma",
        UndoStatus.Unavailable => "Kullanılamaz",
        UndoStatus.Reverted => "Geri alındı",
        UndoStatus.Failed => "Başarısız",
        _ => "Atlandı",
    };
}

/// <summary>Bir işlemin geri alma önizlemesi.</summary>
public sealed class UndoPreview
{
    public UndoPreview(ApplyOperation operation, IReadOnlyList<UndoCandidate> candidates)
    {
        Operation = operation;
        Candidates = candidates;
    }

    public ApplyOperation Operation { get; }

    public IReadOnlyList<UndoCandidate> Candidates { get; }

    public int SafeCount => Count(UndoStatus.SafeUndo);

    public int ConflictCount => Count(UndoStatus.Conflict);

    public int UnavailableCount => Count(UndoStatus.Unavailable);

    public int AlreadyRevertedCount => Count(UndoStatus.AlreadyReverted);

    private int Count(UndoStatus status)
    {
        var count = 0;
        foreach (var candidate in Candidates)
        {
            if (candidate.Status == status)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// Uygulanmış bir işlemi güvenle geri alır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Undo körlemesine eski değeri yazmaz.</b> Bizim <c>A -&gt; B</c> yazmamızdan sonra başka
/// bir kullanıcı <c>B -&gt; C</c> yapmış olabilir; o durumda <c>C -&gt; A</c> yazmak onun işini
/// yok eder. Bu yüzden geri alma da three-way bir güvenlik kontrolünden geçer:
/// </para>
/// <code>
/// A := journal.PreviousValue     (bizim yazmamızdan önceki)
/// B := journal.AppliedValue      (bizim yazdığımız)
/// C := PDM'deki güncel değer
///
/// C == B  ->  güvenli, B -> A yazılabilir
/// C == A  ->  zaten geri alınmış, yapılacak bir şey yok
/// aksi    ->  ÇAKIŞMA, yazma yok
/// </code>
/// <para>
/// Undo, uygulamanın kendisiyle AYNI boru hattını kullanır: aynı check-out politikası, aynı
/// yeniden doğrulama, aynı günlük. Undo işleminin kendisi de günlüğe yeni bir işlem olarak
/// yazılır ve İşlem Geçmişi'nde diğer işlemler gibi görünür.
/// </para>
/// </remarks>
public sealed class UndoService
{
    private readonly IPdmVaultContext _vault;
    private readonly IPdmVariableReader _reader;
    private readonly IOperationJournal _journal;
    private readonly ApplyService _applyService;
    private readonly IStudioLog _log;

    public UndoService(
        IPdmVaultContext vault,
        IPdmVariableReader reader,
        IOperationJournal journal,
        ApplyService applyService,
        IStudioLog log)
    {
        _vault = vault;
        _reader = reader;
        _journal = journal;
        _applyService = applyService;
        _log = log;
    }

    /// <summary>Geri alma önizlemesi üretir. PDM'ye hiçbir şey yazmaz.</summary>
    public OperationOutcome<UndoPreview> BuildPreview(
        Guid operationId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("İşlem kaydı okunuyor");

        var load = _journal.LoadOperation(_vault.Vault, operationId);
        if (load.IsFailure)
        {
            return OperationOutcome<UndoPreview>.Failure(load.Code, load.TechnicalDetail);
        }

        var operation = load.Value;

        if (operation.IsUndone)
        {
            return OperationOutcome<UndoPreview>.Failure(
                IssueCode.AlreadyReverted,
                $"Bu işlem {operation.UndoneByOperationId:D} tarafından geri alınmış.");
        }

        progress?.Report("PDM güncel değerleri okunuyor");

        var snapshots = ReadCurrentSnapshots(operation, cancellationToken);
        var candidates = new List<UndoCandidate>(operation.Entries.Count);

        foreach (var entry in operation.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            snapshots.TryGetValue(entry.File, out var snapshot);
            candidates.Add(Evaluate(entry, snapshot));
        }

        var preview = new UndoPreview(operation, candidates);

        _log.Info($"Undo önizlemesi {operationId:D}: {preview.SafeCount} güvenli, " +
                  $"{preview.ConflictCount} çakışma, {preview.UnavailableCount} kullanılamaz, " +
                  $"{preview.AlreadyRevertedCount} zaten geri alınmış.");

        return OperationOutcome<UndoPreview>.Success(preview);
    }

    /// <summary>
    /// Tek bir kaydın geri alınabilirliğini belirler. Ürünün Undo güvenlik kuralı budur.
    /// </summary>
    internal static UndoCandidate Evaluate(ApplyOperationEntry entry, PdmFileSnapshot? snapshot)
    {
        // Dosya artık okunamıyor: silinmiş, taşınmış ya da yetki kalkmış.
        if (snapshot is null)
        {
            return new UndoCandidate(entry, VariableValue.Empty, UndoStatus.Unavailable, IssueCode.FileNotFound);
        }

        if (!snapshot.HasWritePermission)
        {
            return new UndoCandidate(entry, VariableValue.Empty, UndoStatus.Unavailable, IssueCode.PermissionDenied);
        }

        // Konfigürasyon dışa aktarımdan sonra kaldırılmış olabilir.
        if (!ConfigurationExists(snapshot, entry.Configuration))
        {
            return new UndoCandidate(entry, VariableValue.Empty, UndoStatus.Unavailable, IssueCode.ConfigurationNotFound);
        }

        var current = snapshot.ValueAt(entry.Coordinate);

        // Hiç uygulanmamış kayıtlar geri alınmaz.
        if (entry.Result == EntryResult.Failed || entry.Result == EntryResult.Skipped)
        {
            return new UndoCandidate(entry, current, UndoStatus.Unavailable, IssueCode.NotApplied);
        }

        if (entry.Result == EntryResult.Unknown)
        {
            // Niyet yazılmış ama sonuç yazılmamış: süreç yazma sırasında ölmüş. Gerçekten
            // yazılıp yazılmadığını yalnızca PDM'deki güncel değer söyleyebilir.
            if (!current.Equals(entry.AppliedValue))
            {
                return new UndoCandidate(entry, current, UndoStatus.Unavailable, IssueCode.NeverApplied);
            }

            // Değer bizim yazdığımızla aynı: yazılmış kabul edilir ve aşağıdaki normal
            // güvenlik kontrolüne düşer.
        }

        // Zaten eski hâline dönmüş.
        if (current.Equals(entry.PreviousValue))
        {
            return new UndoCandidate(entry, current, UndoStatus.AlreadyReverted, IssueCode.AlreadyReverted);
        }

        // Bizim yazdığımız değer hâlâ yerinde: geri almak güvenli.
        if (current.Equals(entry.AppliedValue))
        {
            return new UndoCandidate(entry, current, UndoStatus.SafeUndo, IssueCode.None);
        }

        // Bizden sonra başka bir değişiklik olmuş. Geri almak onu yok ederdi.
        return new UndoCandidate(entry, current, UndoStatus.Conflict, IssueCode.Conflict);
    }

    private static bool ConfigurationExists(PdmFileSnapshot snapshot, ConfigurationKey configuration)
    {
        if (snapshot.Configurations.Count == 0)
        {
            return configuration.IsFileLevel;
        }

        foreach (var existing in snapshot.Configurations)
        {
            if (existing.Equals(configuration))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Seçilen kayıtları geri alır. Uygulamanın kendisiyle aynı boru hattını kullanır.
    /// </summary>
    public OperationOutcome<ApplyResult> Undo(
        UndoPreview preview,
        ApplyOptions options,
        IProgress<ApplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        options ??= new ApplyOptions();
        options.OperationType = OperationType.Undo;
        options.UndoesOperationId = preview.Operation.OperationId;

        if (string.IsNullOrWhiteSpace(options.CheckInComment)
            || options.CheckInComment == new ApplyOptions().CheckInComment)
        {
            options.CheckInComment =
                $"PDM Variable Studio: {preview.Operation.UtcTimestamp:yyyy-MM-dd HH:mm} işleminin geri alınması";
        }

        var changeSet = BuildChangeSet(preview);

        if (changeSet.Cells.Count == 0)
        {
            return OperationOutcome<ApplyResult>.Failure(
                IssueCode.Unexpected, "Geri alınabilecek seçili kayıt yok.");
        }

        return _applyService.Apply(changeSet, options, progress, cancellationToken);
    }

    /// <summary>
    /// Undo önizlemesini uygulama boru hattının anladığı bir <see cref="ChangeSet"/>'e çevirir.
    /// </summary>
    /// <remarks>
    /// Rollerin nasıl yer değiştirdiğine dikkat: <c>Original</c> ve <c>Current</c> ikisi de
    /// PDM'deki güncel değerdir, <c>Requested</c> ise geri konacak eski değer. Bu, diff
    /// motorunun kaydı <see cref="ChangeStatus.SafeChange"/> olarak sınıflandırmasını sağlar —
    /// güvenlik kontrolü zaten <see cref="Evaluate"/> içinde yapıldı ve burada tekrarlanmaz.
    /// Uygulama sırasındaki iyimser eşzamanlılık kontrolü ise yine devrede: geri alma
    /// onaylandıktan sonra değer değişirse yazma yapılmaz.
    /// </remarks>
    private ChangeSet BuildChangeSet(UndoPreview preview)
    {
        var cells = new List<CellChange>();
        var plans = new Dictionary<PdmFileIdentity, FileApplyPlan>();

        foreach (var candidate in preview.Candidates)
        {
            if (!candidate.IsSelected || !candidate.CanUndo)
            {
                continue;
            }

            var entry = candidate.Entry;

            var cell = new CellChange(
                entry.Coordinate,
                entry.Variable,
                exportRowId: 0,
                original: candidate.CurrentValue,
                current: candidate.CurrentValue,
                requested: entry.PreviousValue);

            ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);
            cell.IsSelected = cell.CanApply;

            cells.Add(cell);

            if (!plans.TryGetValue(entry.File, out var plan))
            {
                // Check-out durumu uygulama anında taze okunur; burada varsayılan kullanılır.
                plan = new FileApplyPlan(entry.File, CheckoutState.NotCheckedOut, CheckoutAction.CheckOut);
                plans[entry.File] = plan;
            }

            plan.Cells.Add(cell);
        }

        var planList = new List<FileApplyPlan>(plans.Count);
        foreach (var plan in plans.Values)
        {
            planList.Add(plan);
        }

        return new ChangeSet(
            _vault.Vault,
            preview.Operation.ExportSessionId,
            preview.Operation.SourceWorkbookPath,
            cells,
            planList,
            Array.Empty<ValidationIssue>());
    }

    private Dictionary<PdmFileIdentity, PdmFileSnapshot> ReadCurrentSnapshots(
        ApplyOperation operation,
        CancellationToken cancellationToken)
    {
        var files = new List<PdmFileIdentity>();
        var variables = new List<PdmVariableDefinition>();
        var seenFiles = new HashSet<PdmFileIdentity>();
        var seenVariables = new HashSet<int>();

        foreach (var entry in operation.Entries)
        {
            if (seenFiles.Add(entry.File))
            {
                files.Add(entry.File);
            }

            if (seenVariables.Add(entry.Variable.VariableId))
            {
                variables.Add(entry.Variable);
            }
        }

        var snapshots = new Dictionary<PdmFileIdentity, PdmFileSnapshot>();

        if (files.Count == 0)
        {
            return snapshots;
        }

        var read = _reader.ReadSnapshots(files, variables, null, cancellationToken);
        if (read.IsFailure)
        {
            // Toplu okuma başarısız: her dosya tek tek denenir ki tek bir silinmiş dosya
            // tüm önizlemeyi çökertmesin.
            foreach (var file in files)
            {
                var single = _reader.ReadSnapshot(file, variables);
                if (single.IsSuccess)
                {
                    snapshots[file] = single.Value;
                }
            }

            return snapshots;
        }

        foreach (var snapshot in read.Value)
        {
            snapshots[snapshot.Identity] = snapshot;
        }

        return snapshots;
    }
}
