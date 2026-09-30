using PdmVariableStudio.Core.Localization;

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
/// <remarks>
/// Her metin iki dilde, yan yana (<see cref="Loc.T"/>). Yeni bir kod eklerken iki dilin
/// ikisini de yazın; <c>IssueTextTests</c> eksik ya da Türkçeyle aynı kalmış bir İngilizce
/// metni yakalar.
/// </remarks>
public static class IssueText
{
    /// <summary>Kısa durum metni. Tabloda ve rozette görünür.</summary>
    public static string Summary(IssueCode code) => code switch
    {
        IssueCode.None => string.Empty,

        IssueCode.UnsupportedWorkbookVersion => Loc.T("Desteklenmeyen dosya sürümü", "Unsupported file version"),
        IssueCode.WorkbookCorrupted => Loc.T("Çalışma kitabı bozuk", "Workbook is corrupted"),
        IssueCode.MetadataChecksumMismatch => Loc.T("Teknik veri değiştirilmiş", "Technical data was modified"),
        IssueCode.WrongVault => Loc.T("Başka vault", "Different vault"),
        IssueCode.UnknownRow => Loc.T("Tanınmayan satır", "Unrecognized row"),
        IssueCode.DuplicateRow => Loc.T("Yinelenen satır", "Duplicate row"),
        IssueCode.RowTampered => Loc.T("Satır bütünlüğü bozuk", "Row integrity broken"),
        IssueCode.ColumnRelocated => Loc.T("Sütun taşınmış", "Column moved"),
        IssueCode.VariableColumnMissing => Loc.T("Sütun silinmiş", "Column deleted"),
        IssueCode.TypeMismatch => Loc.T("Değer tipi uygun değil", "Invalid value type"),

        IssueCode.FileNotFound => Loc.T("Dosya bulunamadı", "File not found"),
        IssueCode.FolderNotFound => Loc.T("Klasör bulunamadı", "Folder not found"),
        IssueCode.VariableNotFound => Loc.T("Değişken bulunamadı", "Variable not found"),
        IssueCode.ConfigurationNotFound => Loc.T("Konfigürasyon bulunamadı", "Configuration not found"),
        IssueCode.PermissionDenied => Loc.T("Yetki yok", "Permission denied"),
        IssueCode.LockedByOtherUser => Loc.T("Başkası tarafından çekili", "Checked out by another user"),
        IssueCode.CheckoutRequired => Loc.T("Check-out gerekli", "Check-out required"),
        IssueCode.CheckoutFailed => Loc.T("Check-out başarısız", "Check-out failed"),
        IssueCode.CheckInFailed => Loc.T("Check-in başarısız", "Check-in failed"),
        IssueCode.ReadOnlyVariable => Loc.T("Salt okunur değişken", "Read-only variable"),
        IssueCode.MandatoryEmpty => Loc.T("Zorunlu alan boş bırakılamaz", "Mandatory field cannot be empty"),
        IssueCode.FileExclusivelyOpen => Loc.T("Dosya başka uygulamada açık", "File is open in another application"),

        IssueCode.Conflict => Loc.T("Çakışma", "Conflict"),
        IssueCode.AlreadyApplied => Loc.T("Zaten uygulanmış", "Already applied"),
        IssueCode.ValueChangedSincePreview => Loc.T("Önizlemeden sonra değişti", "Changed since preview"),
        IssueCode.NotApplied => Loc.T("Uygulanmamıştı", "Was not applied"),
        IssueCode.NeverApplied => Loc.T("Hiç yazılmamış", "Never written"),
        IssueCode.AlreadyReverted => Loc.T("Zaten geri alınmış", "Already reverted"),

        IssueCode.PdmApiError => Loc.T("PDM işlemi tamamlanamadı", "PDM operation could not be completed"),
        IssueCode.JournalWriteFailed => Loc.T("İşlem günlüğü yazılamadı", "Operation journal could not be written"),
        IssueCode.Cancelled => Loc.T("İptal edildi", "Cancelled"),
        _ => Loc.T("Beklenmeyen hata", "Unexpected error"),
    };

    /// <summary>Nedeni ve kullanıcının ne yapabileceğini anlatan uzun metin (tooltip).</summary>
    public static string Detail(IssueCode code) => code switch
    {
        IssueCode.None => string.Empty,

        IssueCode.UnsupportedWorkbookVersion => Loc.T(
            "Bu çalışma kitabı, eklentinin bu sürümünün tanımadığı bir şema sürümüyle üretilmiş. " +
            "Eklentiyi güncelleyin ya da dosyayı üreten sürümle açın. Hiçbir değişiklik uygulanmadı.",
            "This workbook was created with a schema version this release does not recognize. " +
            "Update the application or open the file with the version that created it. No changes were applied."),
        IssueCode.WorkbookCorrupted => Loc.T(
            "Çalışma kitabının teknik sayfaları eksik ya da okunamıyor. Klasörü yeniden dışa aktarın.",
            "The workbook's technical sheets are missing or unreadable. Export the folder again."),
        IssueCode.MetadataChecksumMismatch => Loc.T(
            "Çalışma kitabının teknik verisi dışarıdan değiştirilmiş. Güvenlik gereği hiçbir değer " +
            "uygulanmayacak. Klasörü yeniden dışa aktarın.",
            "The workbook's technical data was modified outside the application. As a safety measure no " +
            "values will be applied. Export the folder again."),
        IssueCode.WrongVault => Loc.T(
            "Bu çalışma kitabı başka bir vault'tan dışa aktarılmış. Dosya kimlikleri bu vault'ta " +
            "başka dosyalara denk gelebileceği için işlem tamamen engellendi.",
            "This workbook was exported from a different vault. File IDs could match other files in " +
            "this vault, so the operation was blocked entirely."),
        IssueCode.UnknownRow => Loc.T(
            "Bu satırın numarası çalışma kitabının teknik sayfasında yok. Satır sonradan eklenmiş " +
            "ya da numarası bozulmuş olabilir. Eklenti PDM'ye yeni dosya eklemez; satır atlandı.",
            "This row's number is not in the workbook's technical sheet. The row may have been added " +
            "later or its number damaged. The application never adds new files to PDM; the row was skipped."),
        IssueCode.DuplicateRow => Loc.T(
            "Aynı satır numarası birden fazla kez geçiyor. Hangisinin doğru olduğu belirlenemediği " +
            "için ikisi de uygulanmayacak.",
            "The same row number appears more than once. Since the correct one cannot be determined, " +
            "neither will be applied."),
        IssueCode.RowTampered => Loc.T(
            "Satırın bütünlük damgası tutmuyor: teknik sayfadaki kimlik ya da orijinal değerler " +
            "değişmiş. Yanlış dosyaya yazma riski nedeniyle satır uygulanmayacak.",
            "The row's integrity stamp does not match: its identity or original values in the technical " +
            "sheet have changed. Because of the risk of writing to the wrong file, the row will not be applied."),
        IssueCode.ColumnRelocated => Loc.T(
            "Sütun dışa aktarımdaki yerinden taşınmış; başlığa bakılarak yeniden bulundu. " +
            "Değerleri uygulamadan önce kontrol edin.",
            "The column was moved from its exported position and was found again by its header. " +
            "Check the values before applying."),
        IssueCode.VariableColumnMissing => Loc.T(
            "Bu değişkenin sütunu çalışma kitabından silinmiş. Değişken atlandı; mevcut PDM " +
            "değerine dokunulmayacak.",
            "This variable's column was deleted from the workbook. The variable was skipped; the current " +
            "PDM value will not be touched."),
        IssueCode.TypeMismatch => Loc.T(
            "Girilen değer bu değişkenin veri tipine çevrilemiyor. Değeri düzeltip dosyayı " +
            "yeniden yükleyin.",
            "The entered value cannot be converted to this variable's data type. Correct the value and " +
            "load the file again."),

        IssueCode.FileNotFound => Loc.T(
            "Dosya vault'ta, dışa aktarımdaki klasöründe bulunamadı. Dışa aktarımdan sonra " +
            "silinmiş, taşınmış ya da silinip yeniden eklenmiş olabilir — yeniden eklenen dosya " +
            "yeni bir kimlik alır ve eski çalışma kitabı onu tanımaz. Yanlış dosyaya yazmamak " +
            "için satır atlandı; klasörü yeniden dışa aktarın.",
            "The file was not found in the vault, in the folder it was exported from. It may have been " +
            "deleted, moved, or deleted and re-added since the export — a re-added file gets a new ID " +
            "and the old workbook does not recognize it. To avoid writing to the wrong file the row was " +
            "skipped; export the folder again."),
        IssueCode.FolderNotFound => Loc.T(
            "Klasör vault'ta bulunamadı; dışa aktarımdan sonra silinmiş ya da taşınmış olabilir.",
            "The folder was not found in the vault; it may have been deleted or moved since the export."),
        IssueCode.VariableNotFound => Loc.T(
            "Değişken vault tanımından kaldırılmış. Yönetici ile görüşün.",
            "The variable was removed from the vault definition. Contact your administrator."),
        IssueCode.ConfigurationNotFound => Loc.T(
            "Konfigürasyon dosyada artık yok; dışa aktarımdan sonra kaldırılmış olabilir.",
            "The configuration no longer exists in the file; it may have been removed since the export."),
        IssueCode.PermissionDenied => Loc.T(
            "Bu klasörde dosya kartı değiştirme yetkiniz yok. Yönetici ile görüşün.",
            "You do not have permission to modify data cards in this folder. Contact your administrator."),
        IssueCode.LockedByOtherUser => Loc.T(
            "Dosya başka bir kullanıcı tarafından çekili. Değer yazılamaz; dosya iade edilince " +
            "yeniden deneyin.",
            "The file is checked out by another user. The value cannot be written; try again after it " +
            "is checked in."),
        IssueCode.CheckoutRequired => Loc.T(
            "Değer yazabilmek için dosyanın check-out edilmesi gerekiyor.",
            "The file must be checked out before the value can be written."),
        IssueCode.CheckoutFailed => Loc.T(
            "Dosya check-out edilemedi. Diğer dosyalar etkilenmedi; bu dosya atlandı.",
            "The file could not be checked out. Other files were not affected; this file was skipped."),
        IssueCode.CheckInFailed => Loc.T(
            "Değerler yazıldı ama dosya check-in edilemedi. Dosya sizde çekili kaldı; " +
            "PDM Explorer'dan elle check-in edebilirsiniz.",
            "The values were written but the file could not be checked in. It remains checked out to you; " +
            "you can check it in manually from PDM Explorer."),
        IssueCode.ReadOnlyVariable => Loc.T(
            "Bu değişken bu eklenti tarafından yazılamaz.",
            "This variable cannot be written by this application."),
        IssueCode.MandatoryEmpty => Loc.T(
            "Bu değişken vault'ta zorunlu olarak işaretli; boş bırakılamaz.",
            "This variable is marked as mandatory in the vault; it cannot be left empty."),
        IssueCode.FileExclusivelyOpen => Loc.T(
            "Dosya SOLIDWORKS'te ya da başka bir uygulamada açık olduğu için yazılamadı. " +
            "Dosyayı kapatıp yeniden deneyin.",
            "The file could not be written because it is open in SOLIDWORKS or another application. " +
            "Close the file and try again."),

        IssueCode.Conflict => Loc.T(
            "Hem Excel'de hem PDM'de dışa aktarımdan sonra değişiklik yapılmış. Otomatik yazmak " +
            "diğer değişikliği yok ederdi; karar sizin.",
            "The value was changed both in Excel and in PDM since the export. Writing automatically " +
            "would destroy the other change; the decision is yours."),
        IssueCode.AlreadyApplied => Loc.T(
            "İstenen değer PDM'de zaten mevcut. Yazmaya gerek yok.",
            "The requested value is already in PDM. No write is needed."),
        IssueCode.ValueChangedSincePreview => Loc.T(
            "Önizleme oluşturulduktan sonra PDM'deki değer değişti. Sessizce üzerine yazmamak " +
            "için bu hücre çakışmaya dönüştürüldü.",
            "The value in PDM changed after the preview was created. To avoid silently overwriting it, " +
            "this cell was turned into a conflict."),
        IssueCode.NotApplied => Loc.T(
            "Bu değer aslında hiç uygulanmamıştı; geri alınacak bir şey yok.",
            "This value was never actually applied; there is nothing to undo."),
        IssueCode.NeverApplied => Loc.T(
            "İşlem yarıda kalmış ve bu değer PDM'ye hiç yazılmamış; geri alınacak bir şey yok.",
            "The operation was interrupted and this value was never written to PDM; there is nothing to undo."),
        IssueCode.AlreadyReverted => Loc.T(
            "Değer zaten geri alınmış durumda.",
            "The value has already been reverted."),

        IssueCode.PdmApiError => Loc.T(
            "PDM API işlemi tamamlanamadı. Ayrıntı günlük dosyasında.",
            "The PDM API operation could not be completed. Details are in the log file."),
        IssueCode.JournalWriteFailed => Loc.T(
            "İşlem günlüğü yazılamadığı için işlem başlatılmadı. Günlük olmadan yapılan bir " +
            "değişiklik geri alınamaz; bu yüzden bilerek durduruldu.",
            "The operation was not started because the operation journal could not be written. A change " +
            "made without the journal cannot be undone, so it was stopped deliberately."),
        IssueCode.Cancelled => Loc.T(
            "İşlem kullanıcı tarafından durduruldu. Tamamlanan dosyalar günlüğe yazıldı.",
            "The operation was stopped by the user. Completed files were recorded in the journal."),
        _ => Loc.T(
            "Beklenmeyen bir hata oluştu. Ayrıntı günlük dosyasında.",
            "An unexpected error occurred. Details are in the log file."),
    };
}
