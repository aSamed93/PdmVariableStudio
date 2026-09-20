using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PdmVariableStudio.App.Views;
using PdmVariableStudio.Core.Diagnostics;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.App;

/// <summary>
/// Uygulamanın giriş noktası.
/// </summary>
/// <remarks>
/// <para>
/// Uygulama PDM Explorer'ın <b>içinde değil, ayrı bir süreçte</b> çalışır. Bunun üç gerekçesi
/// var:
/// </para>
/// <list type="number">
/// <item>Vault'a yüklenen eklenti paketi iki DLL'e iner (eklenti + interop) ve neredeyse hiç
/// değişmez. Sık değişen kısım — arayüz, Excel, karşılaştırma — vault'un dışında kalır ve
/// güncellemek için her istemcide Explorer kapatmak gerekmez.</item>
/// <item>Buradaki bir hata PDM Explorer'ı düşüremez.</item>
/// <item>Uygulama eklenti olmadan da açılabilir; vault ve klasör seçimi kendi içinde yapılır.</item>
/// </list>
/// <para>
/// Bu ayrım ucuz oldu çünkü kod baştan böyle kurulmuştu: Explorer'dan hiçbir COM nesnesi
/// taşınmıyor, yalnızca vault adı ve klasör numarası geçiyor.
/// </para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var log = new StudioLog();

        // Arayüz dışındaki çökmeler. DispatcherUnhandledException yalnızca UI thread'ini
        // görür; PDM çalışma kuyruğu, thread havuzu ya da sonlandırıcı thread'inden sızan
        // bir istisna buradan geçmeden süreci düşürür ve günlükte HİÇ iz bırakmazdı
        // (2026-09-20'deki dosya penceresi çökmesi böyle bulundu: yalnızca Windows olay
        // günlüğünde). Süreç yine düşer — bunu engelleyemeyiz — ama en azından ne olduğu
        // studio.log'a yazılır ve kullanıcı destek isterken tek bir dosya gönderir.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            log.Error(
                "İşlenmeyen istisna; süreç sonlanıyor. " +
                $"Sürüm {ProductInfo.Version}, sonlandırıyor mu: {e.IsTerminating}.",
                exception);
        };

        // Beklenmeyen bir Task istisnası: gözlenmemiş kalırsa .NET 4.x'te süreci düşürmez
        // ama sessizce kaybolur. Günlüğe yazıp gözlendi sayıyoruz.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("Gözlenmemiş Task istisnası.", e.Exception);
            e.SetObserved();
        };

        try
        {
            var options = StartupOptions.Parse(args);
            log.Info($"Uygulama başlatıldı. Sürüm {ProductInfo.Version}. {options}");

            // Başlangıçta OnExplicitShutdown: vault seçme penceresi ana pencereden ÖNCE
            // açılıyor ve kapandığında "son pencere kapandı" sayılıp uygulama daha
            // başlamadan sonlanırdı. Ana pencere kurulduktan sonra OnMainWindowClose'a
            // geçiliyor.
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };

            // Tema UYGULAMA kapsamında birleştiriliyor, yalnızca pencere kapsamında değil.
            // Gerekçe: StatusBrushConverter durum rengini çalışma anında
            // Application.Current.TryFindResource ile çözüyor ve o arama YALNIZCA
            // Application.Resources içine bakar. Tema sadece Window.Resources'ta olduğunda
            // arama boş dönüyor, fırçalar saydam kalıyor ve önizleme tablosundaki Durum
            // sütunu görünmez oluyordu — rozet çiziliyor ama hem zemini hem yazısı saydam.
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Themes/Theme.xaml", UriKind.Relative),
            });

            application.DispatcherUnhandledException += (_, e) =>
            {
                log.Error("Arayüzde işlenmemiş hata.", e.Exception);
                e.Handled = true;

                MessageBox.Show(
                    "Beklenmeyen bir hata oluştu; son işlem yarıda kalmış olabilir." +
                    Environment.NewLine + Environment.NewLine +
                    "Ayrıntılı günlük: " + log.FilePath,
                    "PDM Variable Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            var context = StartupResolver.Resolve(options, log);
            if (context is null)
            {
                // Kullanıcı vault ya da klasör seçiminden vazgeçti. Hata değil.
                log.Info("Başlatma kullanıcı tarafından iptal edildi.");
                return 0;
            }

            var window = new StudioWindow(
                context.VaultName,
                context.FolderId,
                log,
                context.DatabaseName);

            application.MainWindow = window;
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;

            return application.Run(window);
        }
        catch (Exception exception)
        {
            log.Error("Uygulama başlatılamadı.", exception);

            MessageBox.Show(
                "PDM Variable Studio başlatılamadı." + Environment.NewLine + Environment.NewLine +
                "Ayrıntılı günlük: " + log.FilePath,
                "PDM Variable Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return 1;
        }
    }
}

/// <summary>Komut satırı seçenekleri.</summary>
/// <remarks>
/// <para>
/// Eklenti bu uygulamayı <c>--vault "&lt;ad&gt;" --folder &lt;id&gt;</c> ile başlatır.
/// Hiçbiri zorunlu değil: vault verilmezse sorulur, klasör verilmezse dosya listesi boş
/// başlar ve kullanıcı kaynağı arayüzden seçer. Uygulama böylece Başlat menüsünden de
/// açılabiliyor.
/// </para>
/// <para>
/// Tanınmayan argümanlar sessizce atlanır. Eklentinin eski sürümleri ayrıca
/// <c>--parent &lt;hwnd&gt;</c> gönderiyor; o değer artık kullanılmıyor (pencere tanıtıcısı
/// doğrudan pencerenin kendisinden alınıyor) ve atlanması bir sorun değil — bu sayede
/// uygulamayı güncellemek için eklentiyi yeniden yüklemek gerekmiyor.
/// </para>
/// </remarks>
internal sealed class StartupOptions
{
    public string VaultName { get; private set; } = string.Empty;

    public int FolderId { get; private set; }

    public string FolderPath { get; private set; } = string.Empty;

    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : string.Empty;

            switch (key.ToLowerInvariant())
            {
                case "--vault":
                    options.VaultName = value;
                    i++;
                    break;

                case "--folder":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folderId))
                    {
                        options.FolderId = folderId;
                    }

                    i++;
                    break;

                case "--folderpath":
                    options.FolderPath = value;
                    i++;
                    break;

            }
        }

        return options;
    }

    public override string ToString() => $"vault='{VaultName}', klasör={FolderId}";
}
