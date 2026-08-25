using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Diff;

/// <summary>
/// Bir hücrenin yazılabilirliğini belirleyen, diff dışı bağlam.
/// </summary>
/// <remarks>
/// Diff motoru PDM'i tanımaz; yetki ve kilit bilgisini buradan alır. Bu ayrım motoru
/// PDM istemcisi olmadan test edilebilir tutuyor.
/// </remarks>
public sealed class WriteContext
{
    public static readonly WriteContext Writable =
        new(CheckoutState.NotCheckedOut, hasWritePermission: true, fileExists: true);

    public WriteContext(CheckoutState checkout, bool hasWritePermission, bool fileExists = true)
    {
        Checkout = checkout ?? CheckoutState.Unknown;
        HasWritePermission = hasWritePermission;
        FileExists = fileExists;
    }

    public CheckoutState Checkout { get; }

    public bool HasWritePermission { get; }

    public bool FileExists { get; }
}

/// <summary>
/// Three-way karşılaştırma. Ürünün veri bütünlüğü garantisi buradan çıkar.
/// </summary>
/// <remarks>
/// <para>
/// Üç değer: <c>Original</c> dışa aktarım anındaki PDM değeri, <c>Requested</c> kullanıcının
/// Excel'de bıraktığı değer, <c>Current</c> içe aktarım anındaki taze PDM değeri.
/// </para>
/// <para>
/// <b>Kritik sıra:</b> <see cref="ChangeStatus.AlreadyApplied"/> kontrolü
/// <see cref="ChangeStatus.SafeChange"/>'den ÖNCE gelir. Aksi hâlde "başkası zaten aynı değeri
/// yazmış" durumu gereksiz bir yazma olarak işaretlenirdi — PDM'ye dokunmak, dokunmamaktan
/// her zaman risklidir.
/// </para>
/// </remarks>
public static class ThreeWayDiffEngine
{
    /// <summary>Tek bir hücreyi karşılaştırır ve sonucu hücrenin üzerine yazar.</summary>
    public static void Evaluate(CellChange cell, WriteContext context)
    {
        if (cell is null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        context ??= WriteContext.Writable;

        var status = Classify(cell.Original, cell.Current, cell.Requested);
        var (canApply, reason) = EvaluateWritability(status, cell, context);

        cell.SetDiffResult(status, canApply, reason);
    }

    /// <summary>Bir koleksiyonu, dosya başına yazma bağlamıyla birlikte karşılaştırır.</summary>
    public static void EvaluateAll(
        IEnumerable<CellChange> cells,
        Func<PdmFileIdentity, WriteContext> contextForFile)
    {
        if (cells is null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        if (contextForFile is null)
        {
            throw new ArgumentNullException(nameof(contextForFile));
        }

        foreach (var cell in cells)
        {
            Evaluate(cell, contextForFile(cell.Coordinate.File));
        }
    }

    /// <summary>
    /// Saf sınıflandırma: yalnızca üç değere bakar, yetki/kilit tanımaz.
    /// </summary>
    public static ChangeStatus Classify(VariableValue original, VariableValue current, VariableValue requested)
    {
        original ??= VariableValue.Empty;
        current ??= VariableValue.Empty;
        requested ??= VariableValue.Empty;

        // Ayrıştırılamayan istek asla yazılmaz; diğer kontrollerin hiçbirine gerek yok.
        if (requested.IsUnparseable)
        {
            return ChangeStatus.ValidationError;
        }

        // Case A - kullanıcı dokunmamış.
        if (requested.Equals(original))
        {
            return ChangeStatus.Unchanged;
        }

        // Case D - istenen değer PDM'de zaten var. SafeChange kontrolünden ÖNCE gelmeli.
        if (requested.Equals(current))
        {
            return ChangeStatus.AlreadyApplied;
        }

        // Case B - PDM tarafı dışa aktarımdan beri değişmemiş; tek güvenli yazma durumu.
        if (current.Equals(original))
        {
            return ChangeStatus.SafeChange;
        }

        // Case C - ikisi de değişmiş ve sonuçlar farklı.
        return ChangeStatus.Conflict;
    }

    private static (bool CanApply, IssueCode Reason) EvaluateWritability(
        ChangeStatus status,
        CellChange cell,
        WriteContext context)
    {
        // Yazılabilirlik yalnızca güvenli değişiklikler için sorulur. Diğer durumlarda
        // zaten yazma yapılmayacak; nedeni durumun kendisidir.
        if (status != ChangeStatus.SafeChange)
        {
            return (false, status switch
            {
                ChangeStatus.Conflict => IssueCode.Conflict,
                ChangeStatus.AlreadyApplied => IssueCode.AlreadyApplied,
                ChangeStatus.ValidationError => IssueCode.TypeMismatch,
                _ => IssueCode.None,
            });
        }

        if (!context.FileExists)
        {
            return (false, IssueCode.FileNotFound);
        }

        if (cell.Variable.IsReadOnly)
        {
            return (false, IssueCode.ReadOnlyVariable);
        }

        if (context.Checkout.Status == CheckoutStatus.CheckedOutByOther)
        {
            return (false, IssueCode.LockedByOtherUser);
        }

        if (!context.HasWritePermission)
        {
            return (false, IssueCode.PermissionDenied);
        }

        // Zorunlu bir değişkeni boşaltmak PDM tarafında da reddedilirdi; kullanıcıya
        // yazma denemesinden önce söylemek daha anlaşılır.
        if (cell.Variable.IsMandatory && cell.Requested.IsEmpty)
        {
            return (false, IssueCode.MandatoryEmpty);
        }

        return (true, IssueCode.None);
    }
}
