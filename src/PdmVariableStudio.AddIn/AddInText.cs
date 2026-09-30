using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace PdmVariableStudio.AddIn;

/// <summary>
/// Eklentinin kullanıcıya gösterdiği metinlerin dili.
/// </summary>
/// <remarks>
/// <para>
/// <b>Core'daki <c>Loc</c> / <c>UiLanguages</c> kuralının kopyası.</b> Eklentiye Core
/// referansı verilmediği için (bkz. <c>AddInLog</c> gerekçesi) aynı kural burada küçük bir
/// kopya olarak duruyor. Öncelik ikisinde de aynı: <c>HKCU\SOFTWARE\PdmVariableStudio\Language</c>
/// (kullanıcının uygulamada seçtiği) &gt; <c>HKLM\...\Language</c> (kurulumda seçilen) &gt;
/// Windows arayüz dili (Türkçe değilse İngilizce). Kuralı değiştirirseniz ikisini birlikte
/// değiştirin.
/// </para>
/// <para>
/// Dil <see cref="Refresh"/> ile her giriş noktasında yeniden okunur: menü metni Explorer
/// açılırken bir kez kaydedilir, ama kullanıcı uygulamada dili değiştirdikten sonra gelen
/// bir hata iletisi yeni dilde çıksın.
/// </para>
/// </remarks>
internal static class AddInText
{
    private const string RegistryKeyPath = @"SOFTWARE\PdmVariableStudio";
    private const string RegistryValueName = "Language";

    public static bool IsEnglish { get; private set; }

    /// <summary>Dili kayıt defterinden yeniden çözer. Asla fırlatmaz.</summary>
    public static void Refresh()
    {
        var code = Parse(Read(RegistryHive.CurrentUser))
                   ?? Parse(Read(RegistryHive.LocalMachine))
                   ?? (Parse(CultureInfo.CurrentUICulture.Name) == "tr" ? "tr" : "en");

        IsEnglish = code == "en";
    }

    public static string T(string turkish, string english) => IsEnglish ? english : turkish;

    private static string? Parse(string value)
    {
        var text = value.Trim();
        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            text = text.Substring(0, dash);
        }

        if (string.Equals(text, "tr", StringComparison.OrdinalIgnoreCase))
        {
            return "tr";
        }

        if (string.Equals(text, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return null;
    }

    private static string Read(RegistryHive hive)
    {
        // AppLocator ile aynı gerekçe: eklenti AnyCPU, kurulum iki görünümden birine yazmış
        // olabilir.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(RegistryKeyPath);
                var value = Convert.ToString(key?.GetValue(RegistryValueName), CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value!;
                }
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return string.Empty;
    }
}
