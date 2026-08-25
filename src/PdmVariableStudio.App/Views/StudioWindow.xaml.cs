using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PdmVariableStudio.App.ViewModels;
using PdmVariableStudio.Core.Abstractions;

namespace PdmVariableStudio.App.Views;

/// <summary>Ana pencere. Üç sekme: dışa aktar, içe aktar, işlem geçmişi.</summary>
public partial class StudioWindow : Window
{
    private readonly StudioViewModel _viewModel;
    private readonly IStudioLog _log;

    /// <summary>
    /// Ana pencereyi kurar.
    /// </summary>
    /// <remarks>
    /// Explorer'ın pencere tanıtıcısı BİLEREK alınmıyor ve sahip (owner) olarak atanmıyor.
    /// Süreç dışına çıkmanın tüm amacı bu uygulamanın Explorer'ı etkileyememesi; süreçler
    /// arası bir owner/owned ilişkisi kurmak, z-order ve girdi kuyruğu üzerinden o bağı
    /// kısmen geri getirirdi. Pencere görev çubuğunda ayrı bir öğe olarak durur.
    ///
    /// Tanıtıcı yalnızca PDM'in klasör seçme iletişim kutusu için kullanılır
    /// (<c>StartupResolver</c>), o da bu pencere daha oluşmadan önce.
    /// </remarks>
    internal StudioWindow(string vaultName, int folderId, IStudioLog log, string databaseName = "")
    {
        InitializeComponent();

        _log = log;
        _viewModel = new StudioViewModel(vaultName, folderId, log, databaseName);
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            _viewModel.SetWindowHandle(handle);

            BringToFront();

            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _log.Error("Pencere başlatılamadı.", exception);
        }
    }

    /// <summary>
    /// Pencereyi öne getirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uygulama PDM Explorer tarafından başlatıldığı için Windows'un ön plan kilidi devreye
    /// giriyordu ve pencere Explorer'ın <b>arkasında</b> açılıyordu — kullanıcı komuta
    /// tıklıyor, hiçbir şey olmamış gibi görünüyordu.
    /// </para>
    /// <para>
    /// Çözüm iki taraflı: eklenti tarafında <c>AllowSetForegroundWindow</c> ile yeni sürece
    /// ön plan hakkı devrediliyor, burada da <c>Activate</c> çağrılıyor. <c>Topmost</c>
    /// kısa süreli açılıp kapatılması yedek yol: hak devri bir sebeple çalışmazsa pencere
    /// yine öne gelir. Kalıcı olarak <c>Topmost</c> bırakmak yanlış olurdu — kullanıcı
    /// Excel'e geçtiğinde pencerenin üstte takılı kalması can sıkıcı olur.
    /// </para>
    /// </remarks>
    private void BringToFront()
    {
        try
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();

            Topmost = true;
            Topmost = false;

            Focus();
        }
        catch (InvalidOperationException exception)
        {
            // Pencere bu noktada kapanmış olabilir; öne getirememek işlevsel bir kayıp değil.
            _log.Warn("Pencere öne getirilemedi: " + exception.Message);
        }
    }

    /// <summary>
    /// PDM'ye yazma sürerken pencerenin kapatılmasını engeller.
    /// </summary>
    /// <remarks>
    /// Commit ortasında pencereyi kapatmak, yarım kalmış ve durumu belirsiz bir işlem
    /// bırakırdı. İşlem günlüğü bu durumu "yarım kaldı" olarak yakalayabiliyor ama en iyisi
    /// hiç oluşmaması. Tarama ve önizleme aşamalarında kapatma serbest — orada PDM'ye
    /// yazılmıyor.
    /// </remarks>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_viewModel.IsCommitting)
        {
            return;
        }

        e.Cancel = true;

        MessageBox.Show(
            this,
            "PDM'ye yazma sürüyor. İşlem tamamlanmadan pencere kapatılamaz.\n\n" +
            "Durdurmak isterseniz 'Durdur' düğmesini kullanın; işlem içinde bulunduğu " +
            "dosyayı bitirip duracak ve tamamlananlar işlem geçmişine yazılacak.",
            "PDM Variable Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnClosed(object sender, EventArgs e)
    {
        // COM çalışma kuyruğu burada kapanır; kuyruktaki iş bitene kadar bekler.
        _viewModel.Dispose();
    }

    /// <summary>
    /// Onay kutusu değiştiğinde seçim sayacını ve Uygula düğmesini tazeler.
    /// </summary>
    /// <remarks>
    /// Satır görünüm modelinden ana modele bildirim geçirmek için olay kullanılıyor:
    /// 60.000 satırın her birine ana modele referans vermek gereksiz bir bağ ve bellek
    /// yükü olurdu.
    /// </remarks>
    private void OnRowCheckChanged(object sender, RoutedEventArgs e) =>
        _viewModel.NotifySelectionChanged();

    private void OnChangeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Satır seçimi (vurgu) ile uygulama seçimi (onay kutusu) farklı şeyler; burada
        // yalnızca sayaçlar tazeleniyor.
        _viewModel.NotifySelectionChanged();
    }
}
