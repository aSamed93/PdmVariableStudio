using System;
using System.IO;
using Microsoft.Win32;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Settings;

namespace PdmVariableStudio.App;

/// <summary>İşlem günlüğü kök klasörünün nereden geldiği; Hakkında penceresinde gösterilir.</summary>
internal enum JournalRootSource
{
    /// <summary>Varsayılan: <c>%LOCALAPPDATA%\PdmVariableStudio\journal</c>.</summary>
    Default,

    /// <summary>Kullanıcının kendi ayarı (<c>settings.json</c> → <c>journalRoot</c>).</summary>
    UserSettings,

    /// <summary>Yönetici dağıtımı (<c>HKLM\SOFTWARE\PdmVariableStudio\JournalRoot</c>).</summary>
    MachinePolicy,
}

internal sealed class JournalRoot
{
    public JournalRoot(string path, JournalRootSource source)
    {
        Path = path;
        Source = source;
    }

    public string Path { get; }

    public JournalRootSource Source { get; }

    public string SourceText => Source switch
    {
        JournalRootSource.MachinePolicy => Loc.T("makine ilkesi (HKLM)", "machine policy (HKLM)"),
        JournalRootSource.UserSettings => Loc.T("kullanıcı ayarı (settings.json)", "user setting (settings.json)"),
        _ => Loc.T("varsayılan", "default"),
    };
}

/// <summary>
/// İşlem günlüğünün nereye yazılacağını belirler.
/// </summary>
/// <remarks>
/// <para>
/// Öncelik: <b>makine ilkesi &gt; kullanıcı ayarı &gt; varsayılan.</b> Gerekçe: bir ekip
/// günlüğü ağ paylaşımında toplamaya karar verdiyse bunu yönetici tek bir kayıt defteri
/// değeriyle dağıtır ve kullanıcının kendi ayarı onu geçersiz kılamaz — aksi hâlde bir
/// kişinin yerel ayarı, ekibin geri alma zincirini iki yere böler.
/// </para>
/// <para>
/// <b>Erişilemeyen kök varsayılana düşmez.</b> Paylaşım o an ulaşılamıyorsa yerel klasöre
/// sessizce yazmak, işlemi ekibin göremediği bir yere kaydetmek demek; ürünün "günlük
/// açılamıyorsa işlem başlamaz" kuralıyla çelişir. Bu yüzden yalnızca <b>hangi</b> kökün
/// seçildiği burada çözülür; klasörün açılıp açılamadığını işlem anında
/// <see cref="JsonlOperationJournal"/> denetler ve açamazsa işlem hiç başlamaz.
/// </para>
/// </remarks>
internal static class JournalRootResolver
{
    private const string RegistryKeyPath = @"SOFTWARE\PdmVariableStudio";
    private const string RegistryValueName = "JournalRoot";

    public static JournalRoot Resolve(StudioSettings settings, IStudioLog log)
    {
        var machine = ReadMachinePolicy(log);
        if (machine.Length > 0)
        {
            return new JournalRoot(machine, JournalRootSource.MachinePolicy);
        }

        var user = settings.JournalRoot.Trim();
        if (user.Length > 0)
        {
            if (Path.IsPathRooted(user))
            {
                return new JournalRoot(user, JournalRootSource.UserSettings);
            }

            log.Warn($"settings.json içindeki journalRoot mutlak yol değil, yok sayıldı: '{user}'.");
        }

        return new JournalRoot(JsonlOperationJournal.DefaultRootDirectory(), JournalRootSource.Default);
    }

    private static string ReadMachinePolicy(IStudioLog log)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(RegistryValueName) as string;
            var path = value?.Trim() ?? string.Empty;

            if (path.Length > 0 && !Path.IsPathRooted(path))
            {
                log.Warn($"HKLM JournalRoot mutlak yol değil, yok sayıldı: '{path}'.");
                return string.Empty;
            }

            return path;
        }
        catch (System.Security.SecurityException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
