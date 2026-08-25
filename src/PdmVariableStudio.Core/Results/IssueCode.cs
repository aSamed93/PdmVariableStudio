namespace PdmVariableStudio.Core.Results;

/// <summary>
/// Bir hücrenin, satırın ya da işlemin neden beklenen sonucu vermediğini anlatan kod.
/// </summary>
/// <remarks>
/// Kullanıcıya asla ham istisna metni gösterilmez. Her kodun karşılığı
/// <see cref="IssueText"/> içinde: kısa metin + önerilen eylem. <c>catch (Exception) { }</c>
/// bu projede yasaktır; beklenen her durum burada bir koda sahiptir.
/// </remarks>
public enum IssueCode
{
    None = 0,

    // ---- çalışma kitabı ----
    UnsupportedWorkbookVersion,
    WorkbookCorrupted,
    MetadataChecksumMismatch,
    WrongVault,
    UnknownRow,
    DuplicateRow,
    RowTampered,
    ColumnRelocated,
    VariableColumnMissing,
    TypeMismatch,

    // ---- PDM ----
    FileNotFound,
    FolderNotFound,
    VariableNotFound,
    ConfigurationNotFound,
    PermissionDenied,
    LockedByOtherUser,
    CheckoutRequired,
    CheckoutFailed,
    CheckInFailed,
    ReadOnlyVariable,
    MandatoryEmpty,
    FileExclusivelyOpen,

    // ---- akış ----
    Conflict,
    AlreadyApplied,
    ValueChangedSincePreview,
    NotApplied,
    NeverApplied,
    AlreadyReverted,

    // ---- altyapı ----
    PdmApiError,
    JournalWriteFailed,
    Cancelled,
    Unexpected,
}

/// <summary>Bir <see cref="IssueCode"/> için kullanıcıya gösterilecek metinler.</summary>
public static class IssueText
{
    /// <summary>Kısa durum metni. Tabloda ve rozette görünür.</summary>
    public static string Summary(IssueCode code) => code switch
    {
        IssueCode.None => string.Empty,

        IssueCode.UnsupportedWorkbookVersion => "Desteklenmeyen dosya sürümü",
        IssueCode.WorkbookCorrupted => "Çalışma kitabı bozuk",
        IssueCode.MetadataChecksumMismatch => "Teknik veri değiştirilmiş",
        IssueCode.WrongVault => "Başka vault",
        IssueCode.UnknownRow => "Tanınmayan satır",
        IssueCode.DuplicateRow => "Yinelenen satır",
        IssueCode.RowTampered => "Satır bütünlüğü bozuk",
        IssueCode.ColumnRelocated => "Sütun taşınmış",
        IssueCode.VariableColumnMissing => "Sütun silinmiş",
        IssueCode.TypeMismatch => "Değer tipi uygun değil",

        IssueCode.FileNotFound => "Dosya bulunamadı",
        IssueCode.FolderNotFound => "Klasör bulunamadı",
        IssueCode.VariableNotFound => "Değişken bulunamadı",
        IssueCode.ConfigurationNotFound => "Konfigürasyon bulunamadı",
        IssueCode.PermissionDenied => "Yetki yok",
        IssueCode.LockedByOtherUser => "Başkası tarafından çekili",
        IssueCode.CheckoutRequired => "Check-out gerekli",
        IssueCode.CheckoutFailed => "Check-out başarısız",
        IssueCode.CheckInFailed => "Check-in başarısız",
        IssueCode.ReadOnlyVariable => "Salt okunur değişken",
        IssueCode.MandatoryEmpty => "Zorunlu alan boş bırakılamaz",
        IssueCode.FileExclusivelyOpen => "Dosya başka uygulamada açık",

        IssueCode.Conflict => "Çakışma",
        IssueCode.AlreadyApplied => "Zaten uygulanmış",
        IssueCode.ValueChangedSincePreview => "Önizlemeden sonra değişti",
        IssueCode.NotApplied => "Uygulanmamıştı",
        IssueCode.NeverApplied => "Hiç yazılmamış",
        IssueCode.AlreadyReverted => "Zaten geri alınmış",

        IssueCode.PdmApiError => "PDM işlemi tamamlanamadı",
        IssueCode.JournalWriteFailed => "İşlem günlüğü yazılamadı",
        IssueCode.Cancelled => "İptal edildi",
        _ => "Beklenmeyen hata",
    };

    /// <summary>Nedeni ve kullanıcının ne yapabileceğini anlatan uzun metin (tooltip).</summary>
    public static string Detail(IssueCode code) => code switch
    {
        IssueCode.None => string.Empty,

        IssueCode.UnsupportedWorkbookVersion =>
            "Bu çalışma kitabı, eklentinin bu sürümünün tanımadığı bir şema sürümüyle üretilmiş. " +
            "Eklentiyi güncelleyin ya da dosyayı üreten sürümle açın. Hiçbir değişiklik uygulanmadı.",
        IssueCode.WorkbookCorrupted =>
            "Çalışma kitabının teknik sayfaları eksik ya da okunamıyor. Klasörü yeniden dışa aktarın.",
        IssueCode.MetadataChecksumMismatch =>
            "Çalışma kitabının teknik verisi dışarıdan değiştirilmiş. Güvenlik gereği hiçbir değer " +
            "uygulanmayacak. Klasörü yeniden dışa aktarın.",
        IssueCode.WrongVault =>
            "Bu çalışma kitabı başka bir vault'tan dışa aktarılmış. Dosya kimlikleri bu vault'ta " +
            "başka dosyalara denk gelebileceği için işlem tamamen engellendi.",
        IssueCode.UnknownRow =>
            "Bu satırın numarası çalışma kitabının teknik sayfasında yok. Satır sonradan eklenmiş " +
            "ya da numarası bozulmuş olabilir. Eklenti PDM'ye yeni dosya eklemez; satır atlandı.",
        IssueCode.DuplicateRow =>
            "Aynı satır numarası birden fazla kez geçiyor. Hangisinin doğru olduğu belirlenemediği " +
            "için ikisi de uygulanmayacak.",
        IssueCode.RowTampered =>
            "Satırın bütünlük damgası tutmuyor: teknik sayfadaki kimlik ya da orijinal değerler " +
            "değişmiş. Yanlış dosyaya yazma riski nedeniyle satır uygulanmayacak.",
        IssueCode.ColumnRelocated =>
            "Sütun dışa aktarımdaki yerinden taşınmış; başlığa bakılarak yeniden bulundu. " +
            "Değerleri uygulamadan önce kontrol edin.",
        IssueCode.VariableColumnMissing =>
            "Bu değişkenin sütunu çalışma kitabından silinmiş. Değişken atlandı; mevcut PDM " +
            "değerine dokunulmayacak.",
        IssueCode.TypeMismatch =>
            "Girilen değer bu değişkenin veri tipine çevrilemiyor. Değeri düzeltip dosyayı " +
            "yeniden yükleyin.",

        IssueCode.FileNotFound =>
            "Dosya vault'ta bulunamadı; dışa aktarımdan sonra silinmiş olabilir.",
        IssueCode.FolderNotFound =>
            "Klasör vault'ta bulunamadı; dışa aktarımdan sonra silinmiş ya da taşınmış olabilir.",
        IssueCode.VariableNotFound =>
            "Değişken vault tanımından kaldırılmış. Yönetici ile görüşün.",
        IssueCode.ConfigurationNotFound =>
            "Konfigürasyon dosyada artık yok; dışa aktarımdan sonra kaldırılmış olabilir.",
        IssueCode.PermissionDenied =>
            "Bu klasörde dosya kartı değiştirme yetkiniz yok. Yönetici ile görüşün.",
        IssueCode.LockedByOtherUser =>
            "Dosya başka bir kullanıcı tarafından çekili. Değer yazılamaz; dosya iade edilince " +
            "yeniden deneyin.",
        IssueCode.CheckoutRequired =>
            "Değer yazabilmek için dosyanın check-out edilmesi gerekiyor.",
        IssueCode.CheckoutFailed =>
            "Dosya check-out edilemedi. Diğer dosyalar etkilenmedi; bu dosya atlandı.",
        IssueCode.CheckInFailed =>
            "Değerler yazıldı ama dosya check-in edilemedi. Dosya sizde çekili kaldı; " +
            "PDM Explorer'dan elle check-in edebilirsiniz.",
        IssueCode.ReadOnlyVariable =>
            "Bu değişken bu eklenti tarafından yazılamaz.",
        IssueCode.MandatoryEmpty =>
            "Bu değişken vault'ta zorunlu olarak işaretli; boş bırakılamaz.",
        IssueCode.FileExclusivelyOpen =>
            "Dosya SOLIDWORKS'te ya da başka bir uygulamada açık olduğu için yazılamadı. " +
            "Dosyayı kapatıp yeniden deneyin.",

        IssueCode.Conflict =>
            "Hem Excel'de hem PDM'de dışa aktarımdan sonra değişiklik yapılmış. Otomatik yazmak " +
            "diğer değişikliği yok ederdi; karar sizin.",
        IssueCode.AlreadyApplied =>
            "İstenen değer PDM'de zaten mevcut. Yazmaya gerek yok.",
        IssueCode.ValueChangedSincePreview =>
            "Önizleme oluşturulduktan sonra PDM'deki değer değişti. Sessizce üzerine yazmamak " +
            "için bu hücre çakışmaya dönüştürüldü.",
        IssueCode.NotApplied =>
            "Bu değer aslında hiç uygulanmamıştı; geri alınacak bir şey yok.",
        IssueCode.NeverApplied =>
            "İşlem yarıda kalmış ve bu değer PDM'ye hiç yazılmamış; geri alınacak bir şey yok.",
        IssueCode.AlreadyReverted =>
            "Değer zaten geri alınmış durumda.",

        IssueCode.PdmApiError =>
            "PDM API işlemi tamamlanamadı. Ayrıntı günlük dosyasında.",
        IssueCode.JournalWriteFailed =>
            "İşlem günlüğü yazılamadığı için işlem başlatılmadı. Günlük olmadan yapılan bir " +
            "değişiklik geri alınamaz; bu yüzden bilerek durduruldu.",
        IssueCode.Cancelled =>
            "İşlem kullanıcı tarafından durduruldu. Tamamlanan dosyalar günlüğe yazıldı.",
        _ =>
            "Beklenmeyen bir hata oluştu. Ayrıntı günlük dosyasında.",
    };
}
