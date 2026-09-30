using System;
using System.Threading;

namespace PdmVariableStudio.Core.Localization;

/// <summary>Arayüz dili.</summary>
public enum UiLanguage
{
    Turkish = 0,
    English = 1,
}

/// <summary>
/// Kullanıcıya görünen metinlerin dili.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden .resx değil.</b> Kaynak dosyaları her dil için ayrı bir uydu derleme
/// (<c>en\PdmVariableStudio.resources.dll</c>) üretir; kurulum klasörü alt klasör kazanır ve
/// "eksik kopyalanan dosya" hatası için yeni bir yol açılır — bu projenin dağıtım kuralı
/// dosya listesini kısa tutmak. İki dil için çeviri çiftini metnin kullanıldığı yerde
/// yazmak hem daha az dosya hem daha kolay gözden geçirme demek: Türkçe ile İngilizce aynı
/// satırda durur, biri değişince öbürünün de değişmesi gerektiği göze çarpar.
/// </para>
/// <para>
/// <b>Dil süreç başında bir kez seçilir</b> (<see cref="SetCurrent"/>) ve çalışırken
/// değişmez; arayüzde dil değiştirmek uygulamayı yeniden başlatır. Canlı geçiş, her
/// bağlamanın dil değişikliğini dinlemesini gerektirirdi — kazancı bu karmaşıklığa değmez.
/// </para>
/// <para>
/// <b>Yalnızca kullanıcıya görünen metin çevrilir.</b> <c>studio.log</c> satırları ve
/// <c>TechnicalDetail</c> alanları destek içindir ve Türkçe kalır; iki dilli bir günlük,
/// sorunun izini sürerken aramayı zorlaştırır.
/// </para>
/// <para>
/// <b>Sayı ve tarih biçimi dile bağlı DEĞİL.</b> <c>CurrentCulture</c>'a dokunulmaz:
/// değerler Windows'un bölge ayarıyla gösterilir ve ayrıştırılır. İngilizce arayüzü seçen
/// bir Türk kullanıcının <c>12,4</c> yazdığı değer yine <c>12,4</c> olarak okunmalı.
/// </para>
/// </remarks>
public static class Loc
{
    private static readonly AsyncLocal<UiLanguage?> ScopedLanguage = new();

    private static UiLanguage _current = UiLanguage.Turkish;

    /// <summary>Etkin dil. Test kapsamı (<see cref="Scope"/>) varsa o, yoksa süreç dili.</summary>
    public static UiLanguage Current => ScopedLanguage.Value ?? _current;

    public static bool IsEnglish => Current == UiLanguage.English;

    /// <summary>Süreç dilini belirler. Başlangıçta bir kez çağrılır.</summary>
    public static void SetCurrent(UiLanguage language) => _current = language;

    /// <summary>Etkin dile göre iki metinden birini seçer.</summary>
    public static string T(string turkish, string english) => IsEnglish ? english : turkish;

    /// <summary>
    /// Sayı + ad: <c>N(3, "dosya", "file", "files")</c> → "3 dosya" / "3 files".
    /// </summary>
    /// <remarks>Türkçe sayıdan sonra adı çoğul yapmaz; İngilizce yapar.</remarks>
    public static string N(int count, string turkish, string englishSingular, string englishPlural)
    {
        var number = count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!IsEnglish)
        {
            return number + " " + turkish;
        }

        return number + " " + (count == 1 ? englishSingular : englishPlural);
    }

    /// <summary>
    /// Yalnızca bu mantıksal akış (ve ondan başlayan görevler) için dili geçici olarak
    /// değiştirir. Testler paralel koştuğu için süreç dilini değiştirmek yerine bu kullanılır.
    /// </summary>
    public static IDisposable Scope(UiLanguage language)
    {
        var previous = ScopedLanguage.Value;
        ScopedLanguage.Value = language;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly UiLanguage? _previous;

        public Restore(UiLanguage? previous) => _previous = previous;

        public void Dispose() => ScopedLanguage.Value = _previous;
    }
}

/// <summary>
/// Dil tercihinin kaynağı ve çözümü. Kayıt defterini okumaz; okunan değerleri alır ki
/// kural PDM'siz ve kayıt defterisiz test edilebilsin.
/// </summary>
/// <remarks>
/// <para>
/// Öncelik: <b>kullanıcı seçimi (HKCU) &gt; makine varsayılanı (HKLM) &gt; Windows arayüz
/// dili.</b> Makine varsayılanını kurulum, kurulumda seçilen dile göre yazar. Dil bir ilke
/// değil tercih olduğu için — <c>JournalRoot</c>'un tersine — kullanıcının kendi seçimi
/// makineninkini geçer.
/// </para>
/// <para>
/// Kayıt Windows arayüz dilinden gelmişse yalnızca Türkçe Windows Türkçe açılır; diğer
/// her dil İngilizce'ye düşer.
/// </para>
/// <para>
/// <b>Eklenti bu kuralın bir kopyasını taşır</b> (<c>AddIn/AddInText.cs</c>): eklentiye
/// Core referansı verilmez. Kuralı değiştirirseniz ikisini birlikte değiştirin.
/// </para>
/// </remarks>
public static class UiLanguages
{
    /// <summary>Kayıt defteri anahtarı: <c>HKCU</c> ve <c>HKLM</c> altında aynı yol.</summary>
    public const string RegistryKeyPath = @"SOFTWARE\PdmVariableStudio";

    /// <summary>Kayıt defteri değeri; içeriği <c>tr</c> ya da <c>en</c>.</summary>
    public const string RegistryValueName = "Language";

    /// <summary><c>tr</c> / <c>en</c> kodunu dile çevirir. Tanınmayan kod <c>null</c> döner.</summary>
    public static UiLanguage? Parse(string? code)
    {
        var text = (code ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        // "tr-TR", "en-US" gibi tam kültür adları da kabul edilir; yalnızca dil kısmına bakılır.
        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            text = text.Substring(0, dash);
        }

        if (string.Equals(text, "tr", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguage.Turkish;
        }

        if (string.Equals(text, "en", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguage.English;
        }

        return null;
    }

    /// <summary>Kayıt defterine yazılan kod.</summary>
    public static string ToCode(UiLanguage language) => language == UiLanguage.English ? "en" : "tr";

    /// <summary>Dilin kendi dilindeki adı; dil seçicide görünür.</summary>
    public static string NativeName(UiLanguage language) =>
        language == UiLanguage.English ? "English" : "Türkçe";

    /// <summary>Dilin tercihlerden belirlenmesi.</summary>
    /// <param name="userChoice">HKCU değeri; yoksa boş.</param>
    /// <param name="machineDefault">HKLM değeri; yoksa boş.</param>
    /// <param name="windowsUiCulture">Windows arayüz kültürü, örn. <c>tr-TR</c>.</param>
    public static UiLanguage Resolve(string? userChoice, string? machineDefault, string? windowsUiCulture) =>
        Parse(userChoice)
        ?? Parse(machineDefault)
        ?? (Parse(windowsUiCulture) == UiLanguage.Turkish ? UiLanguage.Turkish : UiLanguage.English);
}
