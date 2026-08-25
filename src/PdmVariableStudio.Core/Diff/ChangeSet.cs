using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Diff;

/// <summary>Bir dosyanın uygulama sırasında hangi check-out işlemine ihtiyaç duyduğu.</summary>
public enum CheckoutAction
{
    /// <summary>Bu dosyaya yazılacak bir şey yok.</summary>
    None = 0,

    /// <summary>Zaten bu kullanıcıda çekili. Olduğu gibi kullanılır, sonunda check-in EDİLMEZ.</summary>
    UseExisting = 1,

    /// <summary>Check-out edilecek. Kullanıcı onayı olmadan yapılmaz.</summary>
    CheckOut = 2,

    /// <summary>Başkasında çekili ya da yetki yok. Dosya tamamen atlanır.</summary>
    Blocked = 3,
}

/// <summary>Tek bir dosya için uygulama planı.</summary>
public sealed class FileApplyPlan
{
    public FileApplyPlan(PdmFileIdentity file, CheckoutState checkout, CheckoutAction action, IssueCode blockReason = IssueCode.None)
    {
        File = file;
        Checkout = checkout;
        Action = action;
        BlockReason = blockReason;
        Cells = new List<CellChange>();
    }

    public PdmFileIdentity File { get; }

    public CheckoutState Checkout { get; }

    public CheckoutAction Action { get; }

    public IssueCode BlockReason { get; }

    public List<CellChange> Cells { get; }

    /// <summary>
    /// Bu dosyayı biz mi çektik. Uygulama sırasında doldurulur ve check-in kararını belirler:
    /// yalnızca BİZİM çektiğimiz dosyalar check-in edilir. Kullanıcının kendi işi için çekili
    /// tuttuğu bir dosyayı iade etmek onun işini bozar.
    /// </summary>
    public bool WeCheckedOut { get; set; }
}

/// <summary>
/// Bir içe aktarımın tam sonucu: hücre değişiklikleri, dosya planları, bulgular ve özet.
/// </summary>
public sealed class ChangeSet
{
    public ChangeSet(
        VaultIdentity vault,
        Guid exportSessionId,
        string sourceWorkbookPath,
        IReadOnlyList<CellChange> cells,
        IReadOnlyList<FileApplyPlan> filePlans,
        IReadOnlyList<ValidationIssue> issues)
    {
        Vault = vault;
        ExportSessionId = exportSessionId;
        SourceWorkbookPath = sourceWorkbookPath ?? string.Empty;
        Cells = cells ?? Array.Empty<CellChange>();
        FilePlans = filePlans ?? Array.Empty<FileApplyPlan>();
        Issues = issues ?? Array.Empty<ValidationIssue>();
    }

    public VaultIdentity Vault { get; }

    public Guid ExportSessionId { get; }

    public string SourceWorkbookPath { get; }

    public IReadOnlyList<CellChange> Cells { get; }

    public IReadOnlyList<FileApplyPlan> FilePlans { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>
    /// Çalışma kitabının tamamı reddedildi mi. True ise hiçbir hücre uygulanamaz — çağıran
    /// taraf <see cref="Cells"/> içeriğine bakmadan durmalıdır.
    /// </summary>
    public bool IsRejected
    {
        get
        {
            foreach (var issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Fatal)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public int CountByStatus(ChangeStatus status)
    {
        var count = 0;
        foreach (var cell in Cells)
        {
            if (cell.Status == status)
            {
                count++;
            }
        }

        return count;
    }

    public int SafeChangeCount => CountByStatus(ChangeStatus.SafeChange);

    public int ConflictCount => CountByStatus(ChangeStatus.Conflict);

    public int ErrorCount => CountByStatus(ChangeStatus.ValidationError);

    public int UnchangedCount => CountByStatus(ChangeStatus.Unchanged);

    public int SelectedCount
    {
        get
        {
            var count = 0;
            foreach (var cell in Cells)
            {
                if (cell.IsSelected)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Uygulama için check-out edilmesi gereken dosya sayısı.</summary>
    public int PendingCheckoutCount
    {
        get
        {
            var count = 0;
            foreach (var plan in FilePlans)
            {
                if (plan.Action == CheckoutAction.CheckOut && HasSelectedCell(plan))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Başkasında çekili ya da yetkisiz olduğu için atlanacak dosya sayısı.</summary>
    public int BlockedFileCount
    {
        get
        {
            var count = 0;
            foreach (var plan in FilePlans)
            {
                if (plan.Action == CheckoutAction.Blocked)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private static bool HasSelectedCell(FileApplyPlan plan)
    {
        foreach (var cell in plan.Cells)
        {
            if (cell.IsSelected)
            {
                return true;
            }
        }

        return false;
    }
}
