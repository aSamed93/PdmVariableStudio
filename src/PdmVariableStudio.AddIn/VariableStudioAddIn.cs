using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;

namespace PdmVariableStudio.AddIn;

/// <summary>
/// SOLIDWORKS PDM eklenti giriş noktası. Tek işi ayrı süreçteki uygulamayı başlatmaktır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu eklenti bilinçli olarak neredeyse boş.</b> Ne Excel okur, ne kart değeri yazar, ne
/// arayüz açar. Menü komutunu kaydeder, seçili klasörü çözer ve <c>PdmVariableStudio.exe</c>
/// dosyasını başlatır. Vault'a yüklenen paket bu yüzden iki dosyadan ibaret: bu DLL ve PDM
/// interop'u.
/// </para>
/// <para>
/// Gerekçe: vault'a yüklenen bir bileşeni güncellemek her istemcide tüm Explorer
/// pencerelerinin kapatılmasını gerektiriyor. Sık değişen kısmı (arayüz, Excel, karşılaştırma
/// kuralları) vault'un dışında tutmak, güncellemeyi tek bir klasörü değiştirmeye indiriyor.
/// Ayrıca buradaki bir hata artık Explorer'ı düşüremez.
/// </para>
/// </remarks>
[Guid("7C4E1A63-9F28-4B5D-8E11-2A6F3D7C0B94")]
[ComVisible(true)]
public sealed class VariableStudioAddIn : IEdmAddIn5
{
    private const int OpenStudioCommandId = 3001;

    /// <summary>
    /// Ön plan hakkını başka bir sürece devreder.
    /// </summary>
    /// <remarks>
    /// Windows, ön planda olmayan bir sürecin pencere öne getirmesini engeller. Uygulamayı
    /// biz başlattığımız ve o an ön planda Explorer olduğu için hakkı devretmek bize düşüyor;
    /// aksi hâlde uygulama penceresi Explorer'ın arkasında açılıyor.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public void GetAddInInfo(ref EdmAddInInfo addInInfo, IEdmVault5 vault, IEdmCmdMgr5 commandManager)
    {
        try
        {
            AddInText.Refresh();

            addInInfo.mbsAddInName = "PDM Variable Studio";
            addInInfo.mbsCompany = "Abdussamed Tarlak";
            addInInfo.mbsDescription = AddInText.T(
                "Klasördeki dosyaların kart değişkenlerini Excel'e aktarır, düzenlenmiş dosyayı " +
                "geri alır, değişiklikleri önizleme ile uygular ve güvenle geri alır.",
                "Exports the data card variables of files to Excel, reads the edited workbook " +
                "back, applies the changes after a preview and undoes them safely.");

            // Vault'a yeni sürüm yüklerken bu sayı ARTIRILMALI, yoksa PDM yeni paketi almaz.
            // 2: eklenti inceltildi; uygulama ayrı sürece taşındı.
            // 3: menü bayrakları asgariye indirildi (bkz. RegisterCommand).
            // 4: eksik kurulum artık sessiz ölmek yerine açık ileti veriyor.
            // 5: uygulamaya ön plan hakkı devrediliyor (pencere arkada açılıyordu).
            // 6: gerekli PDM sürümü 33.5'ten 30.0'a indirildi (aşağıda).
            // 7: İngilizce desteği; metinlerin dili kayıt defterinden seçiliyor (AddInText).
            addInInfo.mlAddInVersion = 7;

            // En düşük desteklenen istemci: SOLIDWORKS PDM Professional 2022 (= 30.0).
            // Geliştirme ve doğrulama 2025 (33.5) üzerinde yapıldı; kullanılan API'lerin
            // hepsi (IEdmVault5/7/11, IEdmEnumeratorVariable5, IEdmSearch, BrowseForFile)
            // 30 ve sonrasında var. Daha eski istemciye izin vermenin anlamı yok: interop
            // tip kimlikleri değişiyor ve eklenti yüklenirken sessizce düşer.
            addInInfo.mlRequiredVersionMajor = 30;
            addInInfo.mlRequiredVersionMinor = 0;

            RegisterCommand(commandManager);

            // Assembly sürümü ve yüklendiği yol günlüğe yazılıyor. Eski bir sürüm vault'ta
            // kalmışsa iki farklı eklenti aynı süreçte yüklenebiliyor ve hangisinin
            // konuştuğu ancak buradan anlaşılıyor.
            var assembly = typeof(VariableStudioAddIn).Assembly;
            AddInLog.Info(
                $"Eklenti yüklendi, komut kaydedildi. " +
                $"Sürüm {assembly.GetName().Version}, AddInVersion {addInInfo.mlAddInVersion}, " +
                $"dil {(AddInText.IsEnglish ? "en" : "tr")}, " +
                $"süreç {System.Diagnostics.Process.GetCurrentProcess().ProcessName}.");
        }
        catch (Exception exception)
        {
            // GetAddInInfo'dan sızan bir istisna eklentinin hiç yüklenmemesine yol açar.
            // Yutmuyoruz — yüklenememesi sessizce çalışmamasından iyidir — ama günlüğe
            // yazmadan bırakmak teşhisi imkânsız kılardı.
            AddInLog.Error("GetAddInInfo başarısız.", exception);
            throw;
        }
    }

    /// <summary>
    /// Menü komutunu kaydeder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tek bayrak: <c>EdmMenu_ShowInMenuBarTools</c>.</b> Buraya iki yanlış denemeden
    /// sonra gelindi ve gerekçesi API'nin kendi tasarımında yazılı:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Bağlam menüsü varsayılandır.</b> <c>EdmMenu_NeverInContextMenu</c> (0x40)
    /// diye bir bayrağın var olması, komutların bağlam menüsüne kendiliğinden girdiğini
    /// gösteriyor — çıkarmak için ayrı bir bayrak gerekiyorsa, girmek için bayrak
    /// gerekmiyor demektir. <c>ShowInMenuBar*</c> bayrakları menü çubuğuna EK yerleşim
    /// ekler, bağlam menüsünü etkilemez.</item>
    /// <item><b>0x1–0x10 aralığı süzgeçtir, yerleşim değil.</b> İlk deneme
    /// <c>OnlyFolders | MustHaveSelection | OnlySingleSelection</c> veriyordu; bunlar dosya
    /// listesi seçimi semantiğine ait ve klasör ağacında komutu tamamen bastırdılar.</item>
    /// <item><b><c>ContextMenuItem</c> (0x400) ve <c>ContextMenuItemFolder</c> (0x800)
    /// kullanılmıyor.</b> İkinci deneme bunları veriyordu; <c>AddCmd</c> hata vermedi,
    /// eklenti <c>explorer.exe</c> içine yüklendi (günlükle doğrulandı) ama komut ne Araçlar
    /// menüsünde ne bağlam menüsünde göründü. Bu iki değer başka bir mekanizmaya ait
    /// görünüyor ve kaydı sessizce geçersiz kılıyor.</item>
    /// </list>
    /// <para>
    /// Kardeş proje <c>PDMetry</c> de yalnızca <c>ShowInMenuBarTools</c> kullanıyor ve bu
    /// makinede çalışıyor — yani bu, tahmin değil, kanıtlanmış yapılandırma.
    /// </para>
    /// <para>
    /// <b>Süzgeç eklemeyin.</b> Hangi bağlamdan gelindiği <see cref="OnCmd"/> içinde
    /// çözülüyor; klasör bulunamazsa uygulama kendi klasör seçme penceresini açıyor.
    /// Doğruluk kodda, görünürlük bayrakta — eklenen her kısıtlayıcı bayrak, komutun
    /// sessizce görünmez kalması için yeni bir yol demek.
    /// </para>
    /// <para>
    /// Menü adı ürün adıdır ve çevrilmez; yalnızca ipucu metni dile göre değişir. Metin
    /// Explorer açılırken kaydedildiği için dil değişikliği Explorer yeniden açılınca görünür.
    /// </para>
    /// </remarks>
    private static void RegisterCommand(IEdmCmdMgr5 commandManager)
    {
        const EdmMenuFlags Flags = EdmMenuFlags.EdmMenu_ShowInMenuBarTools;

        commandManager.AddCmd(
            OpenStudioCommandId,
            "PDM Variable Studio",
            (int)Flags,
            AddInText.T(
                "Seçili klasörün kart değişkenlerini Excel ile toplu düzenler",
                "Bulk-edit the data card variables of the selected folder in Excel"),
            "PDM Variable Studio",
            0,
            0);
    }

    public void OnCmd(ref EdmCmd command, ref EdmCmdData[] commandData)
    {
        // Buradan sızan bir istisna COM sınırını geçer ve kötü durumda Explorer'ı düşürür.
        try
        {
            if (command.meCmdType != EdmCmdType.EdmCmd_Menu || command.mlCmdID != OpenStudioCommandId)
            {
                return;
            }

            AddInText.Refresh();

            AddInLog.Info($"Komut tetiklendi (id {command.mlCmdID}, veri sayısı: {commandData?.Length ?? 0}).");

            if (command.mpoVault is not IEdmVault5 vault)
            {
                AddInLog.Warn("Menü komutunda vault nesnesi yok.");
                return;
            }

            var vaultName = vault.Name;
            var folderId = ResolveSelectedFolderId(commandData, vault);
            var parentHandle = command.mlParentWnd;

            Launch(vault, command, vaultName, folderId, parentHandle);
        }
        catch (COMException exception)
        {
            AddInLog.Error("PDM API işlemi tamamlanamadı.", exception);
            ShowMessage(command,
                AddInText.T("PDM API işlemi tamamlanamadı.", "The PDM API operation could not be completed.") +
                "\n\n" + AddInText.T("Ayrıntılı günlük: ", "Detailed log: ") + AddInLog.FilePath);
        }
        catch (Exception exception)
        {
            AddInLog.Error("OnCmd beklenmeyen bir hatayla düştü.", exception);
            ShowMessage(command,
                AddInText.T("PDM Variable Studio başlatılamadı.", "PDM Variable Studio could not be started.") +
                "\n\n" + AddInText.T("Ayrıntılı günlük: ", "Detailed log: ") + AddInLog.FilePath);
        }
    }

    /// <summary>Uygulamayı ayrı süreçte başlatır.</summary>
    private static void Launch(IEdmVault5 vault, EdmCmd command, string vaultName, int folderId, int parentHandle)
    {
        var executable = AppLocator.Find();

        if (executable is null)
        {
            AddInLog.Warn("Uygulama bulunamadı; beklenen konumlar kullanıcıya bildirildi.");
            ShowMessage(command, AppLocator.DescribeExpectedLocations());
            return;
        }

        // Yalnızca .exe kopyalanmışsa Process.Start BAŞARILI olur ama süreç, eksik derlemeyi
        // ararken hiçbir şey loglayamadan ölür ve kullanıcıya hiçbir şey görünmez.
        // O sessiz başarısızlığı burada açık bir iletiye çeviriyoruz.
        var missing = AppLocator.FindMissingCompanions(executable);
        if (missing.Length > 0)
        {
            AddInLog.Error(
                $"Uygulama eksik kurulmuş. Eksik dosyalar: {string.Join(", ", missing)}. Yol: {executable}");

            ShowMessage(command, AppLocator.DescribeIncompleteInstall(executable, missing));
            return;
        }

        // Klasör çözülemediyse argüman GÖNDERİLMEZ; uygulama kendi klasör seçme penceresini
        // açar. Yanlış bir klasörü sessizce dışa aktarmaktansa kullanıcıya sormak doğrusu.
        var arguments = new System.Text.StringBuilder();
        arguments.Append("--vault \"").Append(vaultName).Append('"');

        if (folderId > 0)
        {
            arguments.Append(" --folder ").Append(folderId.ToString(CultureInfo.InvariantCulture));
        }

        if (parentHandle != 0)
        {
            arguments.Append(" --parent ").Append(parentHandle.ToString(CultureInfo.InvariantCulture));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments.ToString(),

            // UseShellExecute = false: süreç doğrudan başlatılır ve çalışma klasörü
            // uygulamanın kendi klasörü olur. Kabuk üzerinden başlatmak, ağ paylaşımındaki
            // bir exe için ek güvenlik istemlerine yol açabiliyor.
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(executable) ?? string.Empty,
        };

        AddInLog.Info($"Uygulama başlatılıyor: {executable} {startInfo.Arguments}");

        try
        {
            using var process = Process.Start(startInfo);

            // Sürece BAĞLANMIYORUZ: Explorer'ı bekletmek arayüzü kilitler. Uygulama kendi
            // vault oturumunu açar ve kendi yaşam döngüsünü yönetir.
            if (process is null)
            {
                AddInLog.Warn("Process.Start null döndü.");
                return;
            }

            // Ön plan hakkını yeni sürece devret. Windows'un ön plan kilidi olmadan
            // uygulamanın penceresi Explorer'ın ARKASINDA açılıyor ve kullanıcı komuta
            // tıkladığında hiçbir şey olmamış gibi görünüyor. Bu çağrı, Explorer'ın
            // (şu an ön planda olan sürecin) hakkını devretmesini sağlıyor.
            //
            // Başarısız olması ölümcül değil: uygulama tarafında da Activate + kısa süreli
            // Topmost yedek yolu var.
            if (!AllowSetForegroundWindow(process.Id))
            {
                AddInLog.Info("Ön plan hakkı devredilemedi; uygulama kendi yolunu deneyecek.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AddInLog.Error("Uygulama başlatılamadı.", exception);
            ShowMessage(command,
                AddInText.T("PDM Variable Studio başlatılamadı.", "PDM Variable Studio could not be started.") +
                Environment.NewLine + Environment.NewLine +
                executable + Environment.NewLine + Environment.NewLine +
                AddInText.T("Ayrıntılı günlük: ", "Detailed log: ") + AddInLog.FilePath);
        }
    }

    /// <summary>
    /// Seçili klasörün numarasını belirler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Komut hem klasör ağacından hem dosya listesinden hem de Araçlar menüsünden gelebilir;
    /// üçünde de <c>EdmCmdData</c> farklı doluyor. Bu yüzden gelen her kimlik önce klasör
    /// olarak, olmazsa dosya olarak çözülüyor; dosyaysa <b>bulunduğu klasör</b> kullanılıyor
    /// (kullanıcı bir dosyaya sağ tıkladıysa niyeti o klasörle çalışmaktır).
    /// </para>
    /// <para>
    /// Hiçbiri çözülemezse 0 döner ve uygulama klasör seçme penceresini açar. Explorer'ın o
    /// an gezindiği klasöre <b>düşülmez</b>: yanlış klasörü sessizce dışa aktarmak, bir soru
    /// sormaktan çok daha pahalı.
    /// </para>
    /// </remarks>
    private static int ResolveSelectedFolderId(EdmCmdData[]? commandData, IEdmVault5 vault)
    {
        if (commandData is null || commandData.Length == 0)
        {
            AddInLog.Info("Komut verisi boş; klasör uygulamada seçilecek.");
            return 0;
        }

        foreach (var data in commandData)
        {
            var folderId = TryResolveFolder(vault, data.mlObjectID1);
            if (folderId > 0)
            {
                return folderId;
            }

            // Bazı bağlamlarda klasör kimliği ikinci alanda geliyor.
            folderId = TryResolveFolder(vault, data.mlObjectID2);
            if (folderId > 0)
            {
                return folderId;
            }

            folderId = TryResolveParentFolderOfFile(vault, data.mlObjectID1);
            if (folderId > 0)
            {
                return folderId;
            }
        }

        AddInLog.Info("Seçimden klasör çözülemedi; klasör uygulamada seçilecek.");
        return 0;
    }

    private static int TryResolveFolder(IEdmVault5 vault, int candidate)
    {
        if (candidate <= 0)
        {
            return 0;
        }

        try
        {
            if (vault.GetObject(EdmObjectType.EdmObject_Folder, candidate) is IEdmFolder5 folder)
            {
                AddInLog.Info($"Seçili klasör: {folder.Name} (#{candidate}).");
                return candidate;
            }
        }
        catch (COMException exception)
        {
            AddInLog.Info($"#{candidate} klasör olarak çözülemedi (0x{exception.ErrorCode:X8}).");
        }

        return 0;
    }

    private static int TryResolveParentFolderOfFile(IEdmVault5 vault, int candidate)
    {
        if (candidate <= 0)
        {
            return 0;
        }

        try
        {
            if (vault.GetObject(EdmObjectType.EdmObject_File, candidate) is not IEdmFile5 file)
            {
                return 0;
            }

            var position = file.GetFirstFolderPosition();
            if (position is null || position.IsNull)
            {
                return 0;
            }

            var folder = file.GetNextFolder(position);
            if (folder is null)
            {
                return 0;
            }

            AddInLog.Info($"Seçili dosyanın klasörü kullanılıyor: {folder.Name} (#{folder.ID}).");
            return folder.ID;
        }
        catch (COMException exception)
        {
            AddInLog.Info($"#{candidate} dosya olarak çözülemedi (0x{exception.ErrorCode:X8}).");
            return 0;
        }
    }

    private static void ShowMessage(EdmCmd command, string message)
    {
        try
        {
            if (command.mpoVault is IEdmVault5 vault)
            {
                vault.MsgBox(command.mlParentWnd, message, EdmMBoxType.EdmMbt_OKOnly, "PDM Variable Studio");
            }
        }
        catch (COMException exception)
        {
            AddInLog.Warn("Mesaj kutusu gösterilemedi: " + exception.Message);
        }
    }
}
