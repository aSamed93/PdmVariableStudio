using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace PdmVariableStudio.AddIn;

/// <summary>
/// Ayrı süreçte çalışan uygulamanın yerini bulur.
/// </summary>
/// <remarks>
/// <para>
/// Eklenti DLL'i PDM tarafından geçici bir klasöre açılıp oradan yüklendiği için uygulamayı
/// <b>kendi yanında arayamaz</b>. Bu yüzden bilinen konumlara sırayla bakılır:
/// </para>
/// <list type="number">
/// <item><c>HKLM\SOFTWARE\PdmVariableStudio\InstallPath</c> — kurulum tarafından yazılır,
/// ağ payına da işaret edebilir</item>
/// <item><c>HKCU\SOFTWARE\PdmVariableStudio\InstallPath</c> — yönetici hakkı olmayan
/// kullanıcı kurulumu</item>
/// <item><c>%ProgramFiles%\PDM Variable Studio\</c></item>
/// <item><c>%ProgramFiles(x86)%\PDM Variable Studio\</c></item>
/// </list>
/// <para>
/// Bulunamazsa kullanıcıya <b>nereye kurulması gerektiğini söyleyen</b> bir mesaj gösterilir;
/// "dosya bulunamadı" demek yeterli değil, çünkü kullanıcı neyi düzelteceğini bilemez.
/// </para>
/// </remarks>
internal static class AppLocator
{
    public const string ExecutableName = "PdmVariableStudio.exe";

    private const string RegistryKeyPath = @"SOFTWARE\PdmVariableStudio";
    private const string RegistryValueName = "InstallPath";
    private const string DefaultFolderName = "PDM Variable Studio";

    /// <summary>
    /// Uygulamanın yanında bulunması ZORUNLU dosyalar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neden kontrol ediliyor: yalnızca <c>.exe</c> kopyalandığında <c>Process.Start</c>
    /// BAŞARILI oluyor, süreç başlıyor ve CLR <c>Main</c>'i derlerken eksik derlemeyi
    /// arayıp bulamadan ölüyor — kullanıcıya hiçbir şey görünmüyor, günlüğe bile bir satır
    /// düşmüyor. Bu sessiz başarısızlığı açık bir iletiye çevirmek için elle kontrol
    /// ediliyor.
    /// </para>
    /// <para>
    /// Liste bilinçli olarak kısa: uygulamanın tüm bağımlılıklarını burada tekrarlamak,
    /// her yeni paketle eklentiyi de güncellemek demek olurdu. Bu ikisi kalıcı ve
    /// <c>Main</c>'in ilk satırında gerekiyorlar; eksik bir kurulumda ikisi de eksik olur.
    /// </para>
    /// </remarks>
    private static readonly string[] RequiredCompanions =
    {
        "PdmVariableStudio.Core.dll",
        "EPDM.Interop.epdm.dll",
    };

    /// <summary>Uygulamanın tam yolu; bulunamazsa <c>null</c>.</summary>
    public static string? Find()
    {
        foreach (var candidate in Candidates())
        {
            if (candidate.Length > 0 && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Kurulumun eksiksiz olup olmadığını denetler.
    /// </summary>
    /// <returns>Eksik dosya adları; kurulum tamsa boş dizi.</returns>
    public static string[] FindMissingCompanions(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(directory))
        {
            return new string[0];
        }

        var missing = new List<string>();

        foreach (var name in RequiredCompanions)
        {
            if (!File.Exists(Path.Combine(directory!, name)))
            {
                missing.Add(name);
            }
        }

        return missing.ToArray();
    }

    /// <summary>Eksik dosyalar için kullanıcıya gösterilecek metin.</summary>
    public static string DescribeIncompleteInstall(string executablePath, string[] missing)
    {
        var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;

        return
            AddInText.T(
                "PDM Variable Studio eksik kurulmuş; uygulama başlatılamıyor.",
                "PDM Variable Studio is not installed completely; the application cannot start.") +
            Environment.NewLine + Environment.NewLine +
            AddInText.T(
                "Uygulama tek bir dosyadan ibaret değildir — .exe yanındaki DLL'lerle birlikte " +
                "kopyalanmalıdır.",
                "The application is more than a single file — the .exe must be copied together " +
                "with the DLLs next to it.") +
            Environment.NewLine + Environment.NewLine +
            AddInText.T("Klasör:", "Folder:") + Environment.NewLine +
            "    " + directory + Environment.NewLine + Environment.NewLine +
            AddInText.T("Eksik dosyalar:", "Missing files:") + Environment.NewLine +
            "    " + string.Join(Environment.NewLine + "    ", missing) + Environment.NewLine +
            Environment.NewLine +
            AddInText.T(
                "Doğru kurulum için derleme çıktısındaki TÜM dosyaları kopyalayın:",
                "For a correct installation copy ALL files from the build output:") +
            Environment.NewLine +
            @"    src\PdmVariableStudio.App\bin\Release\net481\*" + Environment.NewLine +
            Environment.NewLine +
            AddInText.T("ya da kurulum betiğini çalıştırın:", "or run the installation script:") +
            Environment.NewLine +
            @"    powershell -File docs\install-app.ps1" + Environment.NewLine +
            Environment.NewLine +
            AddInText.T("Ayrıntılı günlük: ", "Detailed log: ") + AddInLog.FilePath;
    }

    /// <summary>Kullanıcıya gösterilecek "nereye kurulmalı" metni.</summary>
    public static string DescribeExpectedLocations()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var expected = Path.Combine(programFiles, DefaultFolderName, ExecutableName);

        return
            AddInText.T(
                "PDM Variable Studio uygulaması bu bilgisayarda bulunamadı.",
                "The PDM Variable Studio application was not found on this computer.") +
            Environment.NewLine + Environment.NewLine +
            AddInText.T(
                "Eklenti yalnızca uygulamayı başlatır; asıl uygulama ayrı olarak kurulur.",
                "The add-in only launches the application; the application itself is installed separately.") +
            Environment.NewLine + Environment.NewLine +
            AddInText.T("Beklenen konum:", "Expected location:") + Environment.NewLine +
            "    " + expected + Environment.NewLine + Environment.NewLine +
            AddInText.T(
                "Uygulama başka bir yerde (örneğin bir ağ paylaşımında) duruyorsa, yolunu şu " +
                "kayıt defteri değerine yazın:",
                "If the application is somewhere else (for example on a network share), write its " +
                "path to this registry value:") +
            Environment.NewLine +
            @"    HKLM\SOFTWARE\PdmVariableStudio\InstallPath" + Environment.NewLine +
            Environment.NewLine +
            AddInText.T("Ayrıntılı günlük: ", "Detailed log: ") + AddInLog.FilePath;
    }

    private static IEnumerable<string> Candidates()
    {
        foreach (var fromRegistry in RegistryPaths())
        {
            yield return fromRegistry;
        }

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (folder.Length > 0)
            {
                yield return Path.Combine(folder, DefaultFolderName, ExecutableName);
            }
        }
    }

    private static IEnumerable<string> RegistryPaths()
    {
        // 64 ve 32 bit görünümlerin ikisi de denenir: eklenti AnyCPU ve host sürecin
        // bitness'ına göre çalışıyor, kurulum ise ikisinden birine yazmış olabilir.
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
        var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                var value = TryReadRegistry(hive, view);
                if (value.Length == 0)
                {
                    continue;
                }

                // Değer hem klasör hem doğrudan exe yolu olabilir; ikisini de kabul et.
                yield return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : Path.Combine(value, ExecutableName);
            }
        }
    }

    private static string TryReadRegistry(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(RegistryKeyPath);

            return Convert.ToString(key?.GetValue(RegistryValueName)) ?? string.Empty;
        }
        catch (System.Security.SecurityException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
