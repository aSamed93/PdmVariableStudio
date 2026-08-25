using System;

namespace PdmVariableStudio.Core.Domain;

/// <summary>
/// Bir kart değerinin hangi konfigürasyona ait olduğunu taşır.
/// </summary>
/// <remarks>
/// <para>
/// Ham <c>string</c> yerine bu tip kullanılıyor, çünkü PDM'de "dosya düzeyi"
/// (konfigürasyondan bağımsız) değer için iki farklı gösterim dolaşımda: SOLIDWORKS
/// dosyalarında <c>"@"</c>, generic dosyalarda boş dize. Ayrımı tek bir yerde tutmak, yanlış
/// konfigürasyona yazma riskini ortadan kaldırıyor.
/// </para>
/// <para>
/// <b>Anlamı (gerçek vault'ta doğrulandı, 2026-08-25):</b> <c>"@"</c> konfigürasyonu
/// SOLIDWORKS'teki <b>Custom</b> sekmesine karşılık gelir; adlandırılmış konfigürasyonlar ise
/// <b>Configuration Specific</b> sekmelerine. PDM veri kartında ikisi <b>ayrı ayrı</b>
/// tutulur — yani <c>"@"</c> satırına yazmak adlandırılmış konfigürasyonları etkilemez,
/// tersi de geçerli. Bu yüzden her konfigürasyon çalışma kitabında ayrı bir satırdır ve
/// "tüm konfigürasyonlar" davranışı kendiliğinden kullanılmaz.
/// </para>
/// </remarks>
public readonly struct ConfigurationKey : IEquatable<ConfigurationKey>
{
    /// <summary>PDM'de SOLIDWORKS dosyalarının konfigürasyondan bağımsız değeri.</summary>
    public const string FileLevelToken = "@";

    private readonly string? _name;

    private ConfigurationKey(string? name) => _name = name;

    /// <summary>Konfigürasyondan bağımsız, dosya düzeyindeki değer.</summary>
    public static ConfigurationKey FileLevel => new(null);

    /// <summary>Adlandırılmış bir konfigürasyon. Boş ya da <c>"@"</c> ad dosya düzeyi sayılır.</summary>
    public static ConfigurationKey Named(string? name) =>
        string.IsNullOrWhiteSpace(name) || name!.Trim() == FileLevelToken
            ? FileLevel
            : new ConfigurationKey(name.Trim());

    public bool IsFileLevel => _name is null;

    /// <summary>Adlandırılmış konfigürasyonun adı; dosya düzeyinde boş dize.</summary>
    public string Name => _name ?? string.Empty;

    /// <summary>
    /// PDM API'sine gönderilecek konfigürasyon dizesi.
    /// </summary>
    /// <param name="isSolidWorksFile">
    /// Dosya bir SOLIDWORKS dosyası mı. Dosya düzeyi gösterimi buna göre seçilir.
    /// </param>
    public string ToPdmString(bool isSolidWorksFile) =>
        _name ?? (isSolidWorksFile ? FileLevelToken : string.Empty);

    /// <summary>Çalışma kitabında ve journal'da saklanan kararlı gösterim.</summary>
    public string ToStorageString() => _name ?? FileLevelToken;

    /// <summary>Saklanan gösterimden geri okur.</summary>
    public static ConfigurationKey FromStorageString(string? stored) => Named(stored);

    /// <summary>
    /// Kullanıcıya gösterilecek metin.
    /// </summary>
    /// <remarks>
    /// Dosya düzeyi için <c>"@"</c> — tire ya da "(dosya düzeyi)" gibi bir ifade değil.
    /// Gerekçe: <c>"@"</c> PDM ve SOLIDWORKS kullanıcılarının zaten tanıdığı gösterim
    /// (SOLIDWORKS'teki <b>Custom</b> sekmesi). Tire, kullanıcıya "burada bir değer yok"
    /// izlenimi veriyordu; oysa <c>"@"</c> satırı gerçek ve ayrı bir veri kartı alanıdır.
    /// </remarks>
    public string ToDisplayString() => _name ?? FileLevelToken;

    public bool Equals(ConfigurationKey other) =>
        string.Equals(_name, other._name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ConfigurationKey key && Equals(key);

    public override int GetHashCode() =>
        _name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_name);

    public override string ToString() => ToDisplayString();
}

/// <summary>
/// PDM değişken veri tipi. <c>EPDM.Interop.epdm.EdmVariableType</c> enum'unun Core kopyası.
/// </summary>
/// <remarks>
/// Kopya olması bilinçli: Core projesi interop'a referans vermiyor, aksi hâlde testler PDM
/// istemcisi olmayan bir makinede derlenemezdi. Adaptör katmanı (<c>PdmVariableStudio.AddIn</c>)
/// iki enum arasında çevirir. Değer sırası interop ile aynı tutuldu ki çeviri hatası gözle
/// yakalanabilsin.
/// </remarks>
public enum PdmVariableType
{
    None = 0,
    Text = 1,
    Int = 2,
    Float = 3,
    Bool = 4,
    Date = 5,
}

/// <summary>
/// Bir vault değişkeninin tanımı. Çalışma kitabı sütunlarının ve tip dönüşümünün kaynağı.
/// </summary>
public sealed class PdmVariableDefinition : IEquatable<PdmVariableDefinition>
{
    public PdmVariableDefinition(
        int variableId,
        string name,
        PdmVariableType dataType,
        bool isMandatory = false,
        bool isUnique = false,
        bool isReadOnly = false,
        string? displayName = null)
    {
        VariableId = variableId;
        Name = name ?? string.Empty;
        DataType = dataType;
        IsMandatory = isMandatory;
        IsUnique = isUnique;
        IsReadOnly = isReadOnly;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName!;
    }

    /// <summary>
    /// Vault'taki kararlı kimlik. Sütun eşlemesi buna göre yapılır, sütun BAŞLIĞINA göre değil:
    /// kullanıcı başlığı değiştirebilir ya da sütunları taşıyabilir.
    /// </summary>
    public int VariableId { get; }

    /// <summary>PDM'deki değişken adı. <c>SetVar</c> / <c>GetVar</c> bunu ister.</summary>
    public string Name { get; }

    /// <summary>Kullanıcıya gösterilen ad. Varsayılan olarak <see cref="Name"/> ile aynı.</summary>
    public string DisplayName { get; }

    public PdmVariableType DataType { get; }

    /// <summary><c>EdmVar_Mandatory</c>. Boş değer yazılması engellenir.</summary>
    public bool IsMandatory { get; }

    /// <summary><c>EdmVar_Unique</c>. Toplu yazmada çarpışma riski taşır.</summary>
    public bool IsUnique { get; }

    /// <summary>
    /// Bu değişken bizim tarafımızdan yazılamaz. Çalışma kitabına girer ama sütunu kilitlidir:
    /// kullanıcı değeri görebilmeli, ama değiştirememeli.
    /// </summary>
    public bool IsReadOnly { get; }

    public bool Equals(PdmVariableDefinition? other) =>
        other is not null && VariableId == other.VariableId;

    public override bool Equals(object? obj) => Equals(obj as PdmVariableDefinition);

    public override int GetHashCode() => VariableId;

    public override string ToString() => $"{Name} (#{VariableId}, {DataType})";
}
