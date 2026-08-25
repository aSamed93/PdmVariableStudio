using System;
using System.Collections.Generic;
using System.Threading;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Abstractions;

/// <summary>
/// PDM ile konuşan her şeyin arkasına saklandığı sınır.
/// </summary>
/// <remarks>
/// <para>
/// Bu arayüzlerin tamamı yalnızca POCO alır ve döner. <c>IEdmVault5</c>, <c>EdmCmd</c>,
/// <c>IEdmFile5</c> gibi COM tipleri buradan geçmez. Bu kural derleme zamanında zorlanıyor:
/// <c>PdmVariableStudio.Core</c> projesinin <c>EPDM.Interop.epdm</c> referansı yok, dolayısıyla
/// ihlal derlenmez.
/// </para>
/// <para>
/// <b>Çağrı bağlamı:</b> bu arayüzlerin uygulamaları PDM COM nesneleriyle konuşur ve yalnızca
/// adanmış STA worker thread'inden çağrılabilir. Arayüzler senkron: async yapmak, COM apartment
/// kuralını ihlal eden bir devam bağlamına düşme riskini davet ederdi. Eşzamanlılık,
/// çağıranın işi bir kuyruğa vermesiyle sağlanır.
/// </para>
/// </remarks>
public interface IPdmVaultContext
{
    VaultIdentity Vault { get; }

    /// <summary>Oturum açmış PDM kullanıcısının adı.</summary>
    string CurrentUserName { get; }

    /// <summary>Vault'ta tanımlı, kullanıcıya gösterilebilecek değişkenler.</summary>
    OperationOutcome<IReadOnlyList<PdmVariableDefinition>> GetVariables();

    /// <summary>Bir klasörün vault köküne göre yolu.</summary>
    OperationOutcome<string> GetFolderPath(int folderId);
}

/// <summary>Klasör içeriğini gezer.</summary>
public interface IPdmFolderScanner
{
    /// <summary>
    /// Klasördeki dosyaları listeler.
    /// </summary>
    /// <param name="folderId">Kaynak klasör.</param>
    /// <param name="includeSubfolders">Alt klasörlere inilsin mi.</param>
    /// <param name="extensionFilter">
    /// Küçük harfe çevrilmiş uzantılar (".sldprt" gibi). Boş ise tüm dosyalar.
    /// </param>
    /// <param name="progress">İşlenen dosya sayısı. UI'ya ilerleme bildirmek için.</param>
    OperationOutcome<IReadOnlyList<PdmFileIdentity>> ScanFolder(
        int folderId,
        bool includeSubfolders,
        IReadOnlyCollection<string> extensionFilter,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Bir dosyanın işlem listesine hangi yoldan girdiği. Yalnızca gösterim için.</summary>
public enum FileSourceKind
{
    Folder = 0,
    File = 1,
    Search = 2,
}

/// <summary>Arama ölçütleri.</summary>
/// <remarks>
/// PDM'in kendi arama motoruna (<c>IEdmSearch</c>) çevrilir. Boş bırakılan alanlar ölçüte
/// eklenmez; hepsi boşsa arama başlatılmaz — vault'un tamamını çekmek istenen bir şey değil.
/// </remarks>
public sealed class FileSearchCriteria
{
    /// <summary>Dosya adı deseni. PDM joker karakterlerini (<c>%</c>, <c>*</c>) kabul eder.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Aramanın başlayacağı klasör. 0 ise vault kökü.</summary>
    public int StartFolderId { get; set; }

    public bool Recursive { get; set; } = true;

    /// <summary>Değişken adına göre süzme. Boşsa kullanılmaz.</summary>
    public string VariableName { get; set; } = string.Empty;

    /// <summary>Değişken değeri. <see cref="VariableName"/> doluysa anlamlı.</summary>
    public string VariableValue { get; set; } = string.Empty;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FileName)
        && string.IsNullOrWhiteSpace(VariableName);
}

/// <summary>
/// Kullanıcının işlem listesine dosya eklemesini sağlayan kaynaklar.
/// </summary>
/// <remarks>
/// Klasör taraması <see cref="IPdmFolderScanner"/> içinde; burada PDM'in kendi dosya seçme
/// penceresi ve arama motoru var. Üçü birden aynı listeye eklenir, böylece kullanıcı farklı
/// klasörlerden ve arama sonuçlarından tek bir işlem kümesi kurabilir.
/// </remarks>
public interface IPdmFileBrowser
{
    /// <summary>
    /// PDM'in kendi dosya seçme penceresini açar (çoklu seçim).
    /// </summary>
    /// <remarks>
    /// Kendi ağaç denetimimizi yazmak yerine PDM'inki kullanılıyor: kullanıcı zaten bu
    /// pencereyi tanıyor ve yetki, paylaşım, gizli klasör kurallarını PDM doğru uyguluyor.
    /// </remarks>
    OperationOutcome<IReadOnlyList<PdmFileIdentity>> BrowseForFiles(IntPtr parentWindow);

    /// <summary>PDM arama motoruyla dosya arar.</summary>
    OperationOutcome<IReadOnlyList<PdmFileIdentity>> Search(
        FileSearchCriteria criteria,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Kart değerlerini okur.</summary>
public interface IPdmVariableReader
{
    /// <summary>
    /// Verilen dosyaların, verilen değişkenlerdeki tüm konfigürasyon değerlerini okur.
    /// </summary>
    /// <remarks>
    /// Dosya başına TEK bir değişken numaralandırıcı açılması beklenir; hücre başına ayrı bir
    /// COM turu, birkaç bin dosyada arayüzü kullanılamaz hâle getirir.
    /// </remarks>
    OperationOutcome<IReadOnlyList<PdmFileSnapshot>> ReadSnapshots(
        IReadOnlyList<PdmFileIdentity> files,
        IReadOnlyList<PdmVariableDefinition> variables,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tek bir dosyanın taze değerlerini okur. Uygulama öncesi yeniden doğrulama ve Undo
    /// önizlemesi bunu kullanır.
    /// </summary>
    OperationOutcome<PdmFileSnapshot> ReadSnapshot(
        PdmFileIdentity file,
        IReadOnlyList<PdmVariableDefinition> variables);
}

/// <summary>Bir dosyaya yazılacak tek bir değer.</summary>
public sealed class VariableWrite
{
    public VariableWrite(ConfigurationKey configuration, PdmVariableDefinition variable, VariableValue value)
    {
        Configuration = configuration;
        Variable = variable;
        Value = value;
    }

    public ConfigurationKey Configuration { get; }

    public PdmVariableDefinition Variable { get; }

    public VariableValue Value { get; }
}

/// <summary>Tek bir yazma girişiminin sonucu.</summary>
public sealed class VariableWriteResult
{
    public VariableWriteResult(ConfigurationKey configuration, int variableId, bool succeeded, IssueCode code = IssueCode.None, string? detail = null)
    {
        Configuration = configuration;
        VariableId = variableId;
        Succeeded = succeeded;
        Code = code;
        Detail = detail ?? string.Empty;
    }

    public ConfigurationKey Configuration { get; }

    public int VariableId { get; }

    public bool Succeeded { get; }

    public IssueCode Code { get; }

    public string Detail { get; }
}

/// <summary>Kart değerlerini yazar.</summary>
public interface IPdmVariableWriter
{
    /// <summary>
    /// Bir dosyanın değerlerini yazar ve diske indirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uygulama, dosya başına tek bir değişken numaralandırıcı açar, tüm değerleri yazar,
    /// <c>Flush()</c> çağırır ve numaralandırıcıyı AÇIKÇA serbest bırakır. Serbest bırakmamak
    /// yerel dosyayı açık tutuyor ve hemen ardından gelen check-in
    /// <c>"exclusively opened by another application"</c> ile düşüyor; referansı çöp
    /// toplayıcıya bırakmak yetmiyor.
    /// </para>
    /// <para>
    /// Çağıran taraf dosyanın çekili olmasını sağlamış olmalıdır; bu metot check-out yapmaz.
    /// </para>
    /// </remarks>
    OperationOutcome<IReadOnlyList<VariableWriteResult>> WriteValues(
        PdmFileIdentity file,
        IReadOnlyList<VariableWrite> writes);
}

/// <summary>Bir check-out girişiminin sonucu.</summary>
public sealed class CheckoutResult
{
    private CheckoutResult(bool succeeded, bool weCheckedOut, IssueCode code, string detail)
    {
        Succeeded = succeeded;
        WeCheckedOut = weCheckedOut;
        Code = code;
        Detail = detail;
    }

    /// <summary>Dosya bizim tarafımızdan çekildi; işlem sonunda iade edilebilir.</summary>
    public static CheckoutResult CheckedOut() => new(true, true, IssueCode.None, string.Empty);

    /// <summary>
    /// Dosya zaten kullanıcıda çekiliydi. Yazılabilir, ama işlem sonunda check-in EDİLMEZ:
    /// kullanıcı onu başka bir iş için çekmiş olabilir.
    /// </summary>
    public static CheckoutResult AlreadyMine() => new(true, false, IssueCode.None, string.Empty);

    public static CheckoutResult Failed(IssueCode code, string? detail = null) =>
        new(false, false, code, detail ?? string.Empty);

    public bool Succeeded { get; }

    public bool WeCheckedOut { get; }

    public IssueCode Code { get; }

    public string Detail { get; }
}

/// <summary>Check-out / check-in işlemleri.</summary>
public interface IPdmCheckoutService
{
    /// <summary>Dosyanın güncel kilit durumunu okur.</summary>
    OperationOutcome<CheckoutState> GetCheckoutState(PdmFileIdentity file);

    /// <summary>Gerekiyorsa check-out eder. Zaten kullanıcıda çekiliyse dokunmaz.</summary>
    CheckoutResult EnsureCheckedOut(PdmFileIdentity file);

    /// <summary>Check-in eder. Yalnızca BİZİM çektiğimiz dosyalar için çağrılmalıdır.</summary>
    OperationOutcome CheckIn(PdmFileIdentity file, string comment);

    /// <summary>
    /// Check-out'u geri alır. Yazma başarısız olduğunda, dosyada iz bırakmamak için çağrılır.
    /// </summary>
    OperationOutcome UndoCheckout(PdmFileIdentity file);
}

/// <summary>Günlük yazma. Uygulama katmanının teşhis kanalı.</summary>
public interface IStudioLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);

    /// <summary>Günlük dosyasının yolu. Hata iletişim kutularında kullanıcıya gösterilir.</summary>
    string FilePath { get; }
}
