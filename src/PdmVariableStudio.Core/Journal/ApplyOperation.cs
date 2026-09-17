using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Journal;

public enum OperationType
{
    Apply = 0,
    Undo = 1,
}

/// <summary>Bir işlemin nasıl sonuçlandığı.</summary>
public enum OperationOutcomeKind
{
    /// <summary>
    /// Başlamış ama kapanış satırı yazılmamış. Süreç çökmüş ya da öldürülmüş demektir.
    /// Bu durumdaki bir işlem geri alınabilir, ama önce her kaydın PDM'deki güncel değerine
    /// bakılarak gerçekten yazılıp yazılmadığı belirlenir.
    /// </summary>
    Incomplete = 0,

    Success = 1,
    Partial = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>Tek bir hücrenin işlem sonucu.</summary>
public enum EntryResult
{
    /// <summary>
    /// Niyet yazıldı ama sonuç yazılmadı. Yazma sırasında çökme olmuş olabilir; hücrenin
    /// PDM'deki güncel değerine bakılmadan geri alınamaz.
    /// </summary>
    Unknown = 0,

    Applied = 1,
    Failed = 2,
    Skipped = 3,
}

/// <summary>Journal'daki tek bir hücre kaydı.</summary>
public sealed class ApplyOperationEntry
{
    public ApplyOperationEntry(
        PdmFileIdentity file,
        ConfigurationKey configuration,
        PdmVariableDefinition variable,
        VariableValue previousValue,
        VariableValue appliedValue,
        int fileVersion)
    {
        File = file;
        Configuration = configuration;
        Variable = variable;
        PreviousValue = previousValue;
        AppliedValue = appliedValue;
        FileVersion = fileVersion;
        Result = EntryResult.Unknown;
    }

    public PdmFileIdentity File { get; }

    public ConfigurationKey Configuration { get; }

    public PdmVariableDefinition Variable { get; }

    /// <summary>Bizim yazmamızdan ÖNCEKİ değer. Undo bunu geri koymayı hedefler.</summary>
    public VariableValue PreviousValue { get; }

    /// <summary>Bizim yazdığımız değer. Undo güvenliği bunun hâlâ yerinde olmasına bakar.</summary>
    public VariableValue AppliedValue { get; }

    public int FileVersion { get; }

    public EntryResult Result { get; set; }

    public IssueCode Code { get; set; }

    public CellCoordinate Coordinate =>
        new(File, Configuration, Variable.VariableId);
}

/// <summary>
/// Bir Apply ya da Undo işleminin tam kaydı.
/// </summary>
/// <remarks>
/// Journal'ın amacı sonradan güvenle geri alabilmek. Bu yüzden her kayıt hem ÖNCEKİ hem
/// YAZILAN değeri taşır: yalnızca öncekini saklamak, "bizim yazdığımız değer hâlâ duruyor mu"
/// sorusunu yanıtlanamaz hâle getirir ve Undo körleşir.
/// </remarks>
public sealed class ApplyOperation
{
    public ApplyOperation(
        Guid operationId,
        OperationType type,
        DateTime utcTimestamp,
        VaultIdentity vault,
        string windowsUser,
        string pdmUser)
    {
        OperationId = operationId;
        Type = type;
        UtcTimestamp = utcTimestamp;
        Vault = vault;
        WindowsUser = windowsUser ?? string.Empty;
        PdmUser = pdmUser ?? string.Empty;
        Entries = new List<ApplyOperationEntry>();
        Outcome = OperationOutcomeKind.Incomplete;
    }

    public Guid OperationId { get; }

    public OperationType Type { get; }

    public DateTime UtcTimestamp { get; }

    public VaultIdentity Vault { get; }

    public string WindowsUser { get; }

    public string PdmUser { get; }

    public string SourceWorkbookPath { get; set; } = string.Empty;

    public Guid ExportSessionId { get; set; }

    /// <summary>Bu bir Undo ise, geri aldığı işlemin kimliği.</summary>
    public Guid UndoesOperationId { get; set; }

    /// <summary>Bu işlem geri alındıysa, geri alan işlemin kimliği.</summary>
    public Guid UndoneByOperationId { get; set; }

    public bool CheckInRequested { get; set; }

    public OperationOutcomeKind Outcome { get; set; }

    public List<ApplyOperationEntry> Entries { get; }

    public int AppliedCount { get; set; }

    public int FailedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FileCount { get; set; }

    public bool IsUndone => UndoneByOperationId != Guid.Empty;

    /// <summary>
    /// Geri alınabilir mi. Kesin karar Undo önizlemesinde, güncel PDM değerlerine bakarak
    /// verilir; bu yalnızca listeyi süzmek için hızlı bir ön eleme.
    /// </summary>
    public bool IsUndoCandidate =>
        !IsUndone
        && AppliedCount > 0
        && Outcome is OperationOutcomeKind.Success
            or OperationOutcomeKind.Partial
            or OperationOutcomeKind.Cancelled
            or OperationOutcomeKind.Incomplete;

    public string OutcomeText => Outcome switch
    {
        OperationOutcomeKind.Success => "Başarılı",
        OperationOutcomeKind.Partial => "Kısmen başarılı",
        OperationOutcomeKind.Failed => "Başarısız",
        OperationOutcomeKind.Cancelled => "Durduruldu",
        _ => "Yarım kaldı",
    };

    public string TypeText => Type == OperationType.Undo ? "Geri alma" : "Uygulama";
}
