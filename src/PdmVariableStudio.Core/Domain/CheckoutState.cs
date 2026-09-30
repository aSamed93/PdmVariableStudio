using System.Collections.Generic;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.Core.Domain;

public enum CheckoutStatus
{
    /// <summary>Çekili değil. Yazmak için önce check-out gerekir.</summary>
    NotCheckedOut = 0,

    /// <summary>Bu makinede, bu kullanıcı tarafından çekili. Doğrudan yazılabilir.</summary>
    CheckedOutByMe = 1,

    /// <summary>Başka kullanıcı ya da başka makine tarafından çekili. Yazılamaz.</summary>
    CheckedOutByOther = 2,

    /// <summary>Durum belirlenemedi (yetki yok ya da okuma hatası).</summary>
    Unknown = 3,
}

/// <summary>Bir dosyanın check-out durumu ve kimin tuttuğu.</summary>
public sealed class CheckoutState
{
    private CheckoutState(CheckoutStatus status, string user, string computer)
    {
        Status = status;
        User = user;
        Computer = computer;
    }

    public static readonly CheckoutState NotCheckedOut =
        new(CheckoutStatus.NotCheckedOut, string.Empty, string.Empty);

    public static readonly CheckoutState Unknown =
        new(CheckoutStatus.Unknown, string.Empty, string.Empty);

    public static CheckoutState ByMe(string? computer = null) =>
        new(CheckoutStatus.CheckedOutByMe, string.Empty, computer ?? string.Empty);

    public static CheckoutState ByOther(string? user, string? computer = null) =>
        new(CheckoutStatus.CheckedOutByOther, user ?? string.Empty, computer ?? string.Empty);

    public CheckoutStatus Status { get; }

    /// <summary>Başkası çekmişse kullanıcı adı; aksi hâlde boş.</summary>
    public string User { get; }

    public string Computer { get; }

    /// <summary>Bu dosyaya (gerekirse check-out ederek) yazabilir miyiz.</summary>
    public bool IsWritable =>
        Status is CheckoutStatus.CheckedOutByMe or CheckoutStatus.NotCheckedOut;

    public override string ToString() => Status switch
    {
        CheckoutStatus.NotCheckedOut => Loc.T("Çekili değil", "Not checked out"),
        CheckoutStatus.CheckedOutByMe => Loc.T("Sizin tarafınızdan çekili", "Checked out by you"),
        CheckoutStatus.CheckedOutByOther => Computer.Length > 0
            ? Loc.T($"{User} tarafından çekili ({Computer})", $"Checked out by {User} ({Computer})")
            : Loc.T($"{User} tarafından çekili", $"Checked out by {User}"),
        _ => Loc.T("Durum bilinmiyor", "Status unknown"),
    };
}

/// <summary>
/// Bir dosyanın dışa aktarım anındaki tam görüntüsü: kimlik, sürüm, kilit ve değerleri.
/// </summary>
public sealed class PdmFileSnapshot
{
    public PdmFileSnapshot(
        PdmFileIdentity identity,
        int currentVersion,
        CheckoutState checkout,
        string? stateName,
        IReadOnlyList<ConfigurationKey> configurations,
        IReadOnlyDictionary<CellCoordinate, VariableValue> values,
        bool hasWritePermission)
    {
        Identity = identity;
        CurrentVersion = currentVersion;
        Checkout = checkout;
        StateName = stateName ?? string.Empty;
        Configurations = configurations;
        Values = values;
        HasWritePermission = hasWritePermission;
    }

    public PdmFileIdentity Identity { get; }

    /// <summary>
    /// Dışa aktarım anındaki sürüm numarası. Journal'a yazılır; içe aktarımda değişmiş olması
    /// tek başına çakışma sayılmaz (değer bazlı karşılaştırma daha kesin) ama teşhise yarar.
    /// </summary>
    public int CurrentVersion { get; }

    public CheckoutState Checkout { get; }

    /// <summary>İş akışı durumu adı. Yalnızca gösterim ve teşhis için.</summary>
    public string StateName { get; }

    /// <summary>
    /// Dosyanın konfigürasyonları. Konfigürasyonsuz dosyalarda tek eleman:
    /// <see cref="ConfigurationKey.FileLevel"/>.
    /// </summary>
    public IReadOnlyList<ConfigurationKey> Configurations { get; }

    public IReadOnlyDictionary<CellCoordinate, VariableValue> Values { get; }

    public bool HasWritePermission { get; }

    public VariableValue ValueAt(CellCoordinate coordinate) =>
        Values.TryGetValue(coordinate, out var value) ? value : VariableValue.Empty;
}
