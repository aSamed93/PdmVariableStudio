using System;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Diff;

/// <summary>
/// Tek bir kart hücresi için three-way karşılaştırmanın girdisi ve sonucu.
/// </summary>
/// <remarks>
/// Üç değeri birden taşır — bu, ürünün tasarımındaki en önemli noktadır. İki değerle
/// (eski/yeni) çalışan bir tasarım "PDM tarafı da değişmiş" durumunu göremez ve başkasının
/// işini sessizce yok eder.
/// </remarks>
public sealed class CellChange
{
    public CellChange(
        CellCoordinate coordinate,
        PdmVariableDefinition variable,
        int exportRowId,
        VariableValue original,
        VariableValue current,
        VariableValue requested)
    {
        Coordinate = coordinate;
        Variable = variable ?? throw new ArgumentNullException(nameof(variable));
        ExportRowId = exportRowId;
        Original = original ?? VariableValue.Empty;
        Current = current ?? VariableValue.Empty;
        Requested = requested ?? VariableValue.Empty;
        Status = ChangeStatus.Unchanged;
    }

    public CellCoordinate Coordinate { get; }

    public PdmVariableDefinition Variable { get; }

    /// <summary>Çalışma kitabındaki satır numarası. Kullanıcıya hatayı gösterirken kullanılır.</summary>
    public int ExportRowId { get; }

    /// <summary>Dışa aktarım anındaki PDM değeri (<c>_Rows</c> sayfasından).</summary>
    public VariableValue Original { get; }

    /// <summary>İçe aktarım anındaki güncel PDM değeri. Taze okunur.</summary>
    public VariableValue Current { get; private set; }

    /// <summary>Kullanıcının Excel'de bıraktığı değer.</summary>
    public VariableValue Requested { get; }

    public ChangeStatus Status { get; private set; }

    /// <summary>
    /// Bu hücre PDM'ye yazılabilir mi. <see cref="Status"/> ile birlikte değerlendirilir:
    /// <see cref="ChangeStatus.SafeChange"/> olup <c>CanApply == false</c> olan hücreler
    /// vardır (salt okunur değişken, kilitli dosya, yetki yok).
    /// </summary>
    public bool CanApply { get; private set; }

    /// <summary>Uygulanamıyorsa ya da dikkat gerekiyorsa nedeni.</summary>
    public IssueCode Reason { get; private set; }

    /// <summary>Uygulama sonrası hata ayrıntısı; yalnızca günlüğe gider.</summary>
    public string TechnicalDetail { get; private set; } = string.Empty;

    /// <summary>Kullanıcı bu hücreyi uygulamak üzere seçti mi.</summary>
    public bool IsSelected { get; set; }

    /// <summary>Diff motoru sonucu buraya yazar. Başka hiçbir yerden çağrılmaz.</summary>
    internal void SetDiffResult(ChangeStatus status, bool canApply, IssueCode reason)
    {
        Status = status;
        CanApply = canApply;
        Reason = reason;
        IsSelected = canApply && status == ChangeStatus.SafeChange;
    }

    /// <summary>
    /// Uygulama öncesi yeniden doğrulamada güncel değer değişmişse çağrılır. Hücre çakışmaya
    /// döner ve bir daha uygulanamaz.
    /// </summary>
    public void DemoteToConflict(VariableValue freshCurrent, IssueCode reason)
    {
        Current = freshCurrent ?? VariableValue.Empty;
        Status = ChangeStatus.Conflict;
        CanApply = false;
        IsSelected = false;
        Reason = reason;
    }

    public void MarkApplied()
    {
        Status = ChangeStatus.Applied;
        CanApply = false;
        Reason = IssueCode.None;
        Current = Requested;
    }

    public void MarkFailed(IssueCode reason, string? technicalDetail = null)
    {
        Status = ChangeStatus.Failed;
        CanApply = false;
        Reason = reason;
        TechnicalDetail = technicalDetail ?? string.Empty;
    }

    public void MarkSkipped(IssueCode reason = IssueCode.None)
    {
        Status = ChangeStatus.Skipped;
        CanApply = false;
        Reason = reason;
    }

    /// <summary>Kullanıcının gördüğü tabloda değişiklik satırı mı, yoksa gürültü mü.</summary>
    public bool IsInteresting => Status != ChangeStatus.Unchanged;

    public override string ToString() =>
        $"{Coordinate} {Original}->{Requested} [{Status}{(CanApply ? "" : ", uygulanamaz")}]";
}
