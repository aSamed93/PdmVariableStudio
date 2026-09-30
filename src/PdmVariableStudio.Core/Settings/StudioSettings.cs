using System;
using System.IO;
using System.Text;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.Core.Settings;

/// <summary>
/// Kullanıcı tercihleri. <c>%LOCALAPPDATA%\PdmVariableStudio\settings.json</c> içinde
/// tek satırlık düz bir JSON nesnesi olarak durur.
/// </summary>
/// <remarks>
/// <para>
/// <b>Kaybolabilir veri.</b> Buradaki hiçbir değer iş verisi değildir; dosya silinirse
/// uygulama varsayılanlarla açılır, hiçbir işlem geçmişi ya da vault verisi etkilenmez.
/// Bu yüzden okuma her koşulda toleranslıdır: dosya yok, bozuk, eksik alan — hepsi
/// sessizce varsayılana düşer. Yazma hatası da kullanıcıya gösterilmez, günlüğe bile
/// yazılmaz; tercihlerin kaydedilememesi bir işi durduracak bir şey değil.
/// </para>
/// <para>
/// <b>Neden FlatJson.</b> Journal ile aynı gerekçe: dış bağımlılık yok, düz nesne yeter.
/// Yeni bir alan eklerken <see cref="Load"/> içine varsayılanıyla okunmalı ve
/// <see cref="Save"/> içine yazılmalı; eski dosyalar alanı içermeyeceği için okuma zaten
/// varsayılana düşer, sürüm numarasına gerek yok.
/// </para>
/// </remarks>
public sealed class StudioSettings
{
    /// <summary>Klasör eklerken alt klasörlere inilsin mi.</summary>
    public bool IncludeSubfolders { get; set; }

    /// <summary>Uygulama sonrası bizim çektiğimiz dosyalar check-in edilsin mi.</summary>
    public bool CheckInAfterApply { get; set; } = true;

    /// <summary>Check-in yorumu.</summary>
    public string CheckInComment { get; set; } = DefaultCheckInComment();

    /// <summary>Son dışa aktarımın yapıldığı klasör; dosya iletişim kutuları buradan açılır.</summary>
    public string LastExportDirectory { get; set; } = string.Empty;

    /// <summary>
    /// İşlem günlüğü kök klasörü. Boşsa varsayılan (<c>%LOCALAPPDATA%</c>) kullanılır.
    /// Ekip için ağ paylaşımı verilebilir; öncelik sırası uygulama tarafında çözülür.
    /// </summary>
    public string JournalRoot { get; set; } = string.Empty;

    /// <summary>Etkin dildeki varsayılan check-in yorumu.</summary>
    public static string DefaultCheckInComment() =>
        Loc.T("PDM Variable Studio ile toplu kart güncellemesi", "Bulk data card update with PDM Variable Studio");

    /// <summary>
    /// Kayıtlı yorum, dillerden birinin varsayılanıysa etkin dilin varsayılanına çevrilir.
    /// </summary>
    /// <remarks>
    /// Varsayılan yorum her kayıtta <c>settings.json</c>'a yazılıyor. Bu olmasa, Türkçe
    /// kullanılmış bir kurulumda dil İngilizce'ye çevrildiğinde yorum Türkçe kalırdı —
    /// kullanıcı onu hiç yazmamışken. Kullanıcının kendi yazdığı yoruma dokunulmaz.
    /// </remarks>
    internal static string LocalizeIfDefault(string comment)
    {
        foreach (var language in new[] { UiLanguage.Turkish, UiLanguage.English })
        {
            string languageDefault;
            using (Loc.Scope(language))
            {
                languageDefault = DefaultCheckInComment();
            }

            if (string.Equals(comment, languageDefault, StringComparison.Ordinal))
            {
                return DefaultCheckInComment();
            }
        }

        return comment;
    }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdmVariableStudio",
            "settings.json");

    /// <summary>Dosyayı okur. Her hata varsayılanlara düşer; asla fırlatmaz.</summary>
    public static StudioSettings Load(string? path = null)
    {
        var settings = new StudioSettings();
        var filePath = path ?? DefaultPath();

        try
        {
            if (!File.Exists(filePath))
            {
                return settings;
            }

            var record = FlatJson.TryParse(File.ReadAllText(filePath, Encoding.UTF8));
            if (record is null)
            {
                return settings;
            }

            settings.IncludeSubfolders = record.Bool("includeSubfolders", settings.IncludeSubfolders);
            settings.CheckInAfterApply = record.Bool("checkInAfterApply", settings.CheckInAfterApply);
            settings.CheckInComment = LocalizeIfDefault(record.Text("checkInComment", settings.CheckInComment));
            settings.LastExportDirectory = record.Text("lastExportDirectory", settings.LastExportDirectory);
            settings.JournalRoot = record.Text("journalRoot", settings.JournalRoot);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return settings;
    }

    /// <summary>
    /// Dosyaya yazar. Önce geçici dosyaya yazıp yerine koyar: yazma ortasında kesilirse
    /// eski dosya bozulmamış kalır. Hata sessizce yutulur (bkz. sınıf açıklaması).
    /// </summary>
    public void Save(string? path = null)
    {
        var filePath = path ?? DefaultPath();

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var json = new FlatJson.Writer()
                .Bool("includeSubfolders", IncludeSubfolders)
                .Bool("checkInAfterApply", CheckInAfterApply)
                .Text("checkInComment", CheckInComment)
                .Text("lastExportDirectory", LastExportDirectory)
                .Text("journalRoot", JournalRoot)
                .Build();

            var temporary = filePath + ".tmp";
            File.WriteAllText(temporary, json + Environment.NewLine, Encoding.UTF8);

            if (File.Exists(filePath))
            {
                File.Replace(temporary, filePath, null);
            }
            else
            {
                File.Move(temporary, filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
