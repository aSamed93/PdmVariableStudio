using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.App;

/// <summary>Arayüz dilinin nereden geldiği; Hakkında penceresinde gösterilir.</summary>
internal enum LanguageSource
{
    /// <summary>Windows arayüz dili.</summary>
    Windows,

    /// <summary>Kurulumda seçilen dil (<c>HKLM\SOFTWARE\PdmVariableStudio\Language</c>).</summary>
    MachineDefault,

    /// <summary>Kullanıcının uygulamada seçtiği dil (<c>HKCU\...\Language</c>).</summary>
    UserChoice,
}

/// <summary>
/// Dil tercihini kayıt defterinden okur ve kullanıcının seçimini yazar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden <c>settings.json</c> değil de kayıt defteri.</b> Dili iki süreç okuyor: bu
/// uygulama ve PDM Explorer içindeki eklenti (menü ipucu ve hata iletileri). Eklentiye Core
/// referansı verilmediği için <c>FlatJson</c> orada yok; kayıt defterini ise eklenti zaten
/// okuyor (<c>AppLocator</c>). Makine varsayılanını da kurulum aynı yere yazıyor — böylece
/// kural tek: HKCU &gt; HKLM &gt; Windows. Çözüm kuralı <see cref="UiLanguages.Resolve"/>.
/// </para>
/// <para>
/// Okuma her hatada bir sonraki kaynağa düşer; dil bir işi durduracak bir şey değil.
/// </para>
/// </remarks>
internal static class LanguagePreference
{
    public static UiLanguage Resolve(IStudioLog log, out LanguageSource source)
    {
        var user = Read(Registry.CurrentUser, log);
        var machine = Read(Registry.LocalMachine, log);
        var windows = CultureInfo.CurrentUICulture.Name;

        source = UiLanguages.Parse(user) is not null
            ? LanguageSource.UserChoice
            : UiLanguages.Parse(machine) is not null
                ? LanguageSource.MachineDefault
                : LanguageSource.Windows;

        return UiLanguages.Resolve(user, machine, windows);
    }

    /// <summary>Kullanıcının seçimini <c>HKCU</c>'ya yazar. Başarısızsa <c>false</c>.</summary>
    public static bool SaveUserChoice(UiLanguage language, IStudioLog log)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UiLanguages.RegistryKeyPath);
            key?.SetValue(UiLanguages.RegistryValueName, UiLanguages.ToCode(language), RegistryValueKind.String);
            return key is not null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            log.Warn($"Dil tercihi kaydedilemedi: {exception.Message}");
            return false;
        }
    }

    /// <summary>Hakkında penceresindeki "kaynak" metni.</summary>
    public static string DescribeSource(LanguageSource source) => source switch
    {
        LanguageSource.UserChoice => Loc.T("kullanıcı seçimi (HKCU)", "user choice (HKCU)"),
        LanguageSource.MachineDefault => Loc.T("kurulumda seçilen (HKLM)", "chosen at setup (HKLM)"),
        _ => Loc.T("Windows dili", "Windows language"),
    };

    private static string Read(RegistryKey hive, IStudioLog log)
    {
        try
        {
            using var key = hive.OpenSubKey(UiLanguages.RegistryKeyPath);
            var value = Convert.ToString(key?.GetValue(UiLanguages.RegistryValueName), CultureInfo.InvariantCulture)
                        ?? string.Empty;

            if (value.Trim().Length > 0 && UiLanguages.Parse(value) is null)
            {
                log.Warn($"{hive.Name}\\{UiLanguages.RegistryKeyPath}\\{UiLanguages.RegistryValueName} " +
                         $"tanınmıyor, yok sayıldı: '{value}'.");
            }

            return value;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or UnauthorizedAccessException
                                              or IOException)
        {
            return string.Empty;
        }
    }
}
