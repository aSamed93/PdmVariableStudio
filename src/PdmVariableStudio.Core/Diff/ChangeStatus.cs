namespace PdmVariableStudio.Core.Diff;

/// <summary>
/// Bir hücrenin three-way karşılaştırma sonucundaki durumu.
/// </summary>
/// <remarks>
/// Durum ile <c>CanApply</c> ayrı kavramlar. Durum "ne oldu" sorusunu, <c>CanApply</c>
/// "yazabilir miyiz" sorusunu yanıtlar. Salt okunur bir değişkende yapılan güvenli bir
/// değişiklik hâlâ <see cref="SafeChange"/>'dir ama <c>CanApply</c> false'tur; ikisini tek
/// enum'a sıkıştırmak kullanıcıdan "değişiklik var ama yazılamıyor" bilgisini gizlerdi.
/// </remarks>
public enum ChangeStatus
{
    /// <summary>Excel değeri dışa aktarım anındaki değerle aynı. Kullanıcı dokunmamış.</summary>
    Unchanged = 0,

    /// <summary>
    /// Excel'de değişmiş, PDM tarafı dışa aktarımdan beri değişmemiş. Tek güvenli yazma durumu.
    /// </summary>
    SafeChange = 1,

    /// <summary>İstenen değer PDM'de zaten mevcut. Yazma gerekmez.</summary>
    AlreadyApplied = 2,

    /// <summary>Hem Excel hem PDM değişmiş ve sonuçlar farklı. Otomatik yazma YASAK.</summary>
    Conflict = 3,

    /// <summary>Değer beyan edilen tipe çevrilemiyor ya da satır bütünlüğü bozuk.</summary>
    ValidationError = 4,

    /// <summary>Durum ne olursa olsun bu hücreye yazamayız (yetki, kilit, salt okunur).</summary>
    NotWritable = 5,

    // ---- uygulama sonrası durumlar ----

    /// <summary>PDM'ye başarıyla yazıldı.</summary>
    Applied = 6,

    /// <summary>Yazma denendi ve başarısız oldu.</summary>
    Failed = 7,

    /// <summary>Kullanıcı seçmedi ya da işlem yarıda durdu.</summary>
    Skipped = 8,
}

/// <summary>Undo önizlemesinde bir kaydın durumu.</summary>
public enum UndoStatus
{
    /// <summary>Yazdığımız değer hâlâ yerinde; geri alma güvenli.</summary>
    SafeUndo = 0,

    /// <summary>Değer zaten eski hâline dönmüş. Yapılacak bir şey yok.</summary>
    AlreadyReverted = 1,

    /// <summary>Bizden sonra başka bir değişiklik olmuş. Geri almak onu yok ederdi.</summary>
    Conflict = 2,

    /// <summary>Dosya/değişken/konfigürasyon artık yok ya da yetki kalmamış.</summary>
    Unavailable = 3,

    /// <summary>Geri alındı.</summary>
    Reverted = 4,

    /// <summary>Geri alma denendi, başarısız oldu.</summary>
    Failed = 5,

    /// <summary>Kullanıcı seçmedi.</summary>
    Skipped = 6,
}
