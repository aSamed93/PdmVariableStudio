using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PdmVariableStudio.App.ViewModels;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.App.Views;

/// <summary>Ana pencere. Üç sekme: dışa aktar, içe aktar, işlem geçmişi.</summary>
public partial class StudioWindow : Window
{
    private readonly StudioViewModel _viewModel;
    private readonly IStudioLog _log;
    private readonly string _vaultName;
    private readonly int _folderId;

    /// <summary>Dil seçicinin ilk değeri atanırken değişiklik olayı yok sayılsın diye.</summary>
    private bool _languagePickerReady;

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
        _vaultName = vaultName;
        _folderId = folderId;
        SelectCurrentLanguage();

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
            Loc.T(
                "PDM'ye yazma sürüyor. İşlem tamamlanmadan pencere kapatılamaz.\n\n" +
                "Durdurmak isterseniz 'Durdur' düğmesini kullanın; işlem içinde bulunduğu " +
                "dosyayı bitirip duracak ve tamamlananlar işlem geçmişine yazılacak.",
                "Writing to PDM is in progress. The window cannot be closed until the operation completes.\n\n" +
                "To stop, use the 'Stop' button; the operation will finish the current " +
                "file, then stop, and the completed changes will be recorded in the operation history."),
            "PDM Variable Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void SelectCurrentLanguage()
    {
        var current = UiLanguages.ToCode(Loc.Current);

        foreach (ComboBoxItem item in LanguagePicker.Items)
        {
            if (string.Equals(item.Tag as string, current, StringComparison.Ordinal))
            {
                LanguagePicker.SelectedItem = item;
            }
        }

        _languagePickerReady = true;
    }

    /// <summary>
    /// Dil değişince tercihi kaydeder ve yeniden başlatmayı önerir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dil canlı değişmez (bkz. <see cref="Loc"/>): metinler pencere yüklenirken bir kez
    /// çözülüyor. Yeniden başlatma bu yüzden gerekli; dosya listesi ve önizleme kaybolacağı
    /// için kullanıcıya sorulur, zorlanmaz. Başka bir iş sürerken hiç önerilmez — tercih
    /// kaydedilir, sonraki açılışta geçerli olur.
    /// </para>
    /// <para>
    /// Soru <b>seçilen</b> dilde sorulur: dili değiştiren kişi büyük olasılıkla o dili okuyor.
    /// </para>
    /// </remarks>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languagePickerReady
            || (LanguagePicker.SelectedItem as ComboBoxItem)?.Tag is not string code
            || UiLanguages.Parse(code) is not UiLanguage chosen
            || chosen == Loc.Current)
        {
            return;
        }

        if (!LanguagePreference.SaveUserChoice(chosen, _log))
        {
            MessageBox.Show(
                this,
                Loc.T("Dil tercihi kaydedilemedi. Ayrıntılı günlük: ",
                      "The language preference could not be saved. Detailed log: ") + _log.FilePath,
                "PDM Variable Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _languagePickerReady = false;
            SelectCurrentLanguage();
            return;
        }

        _log.Info($"Dil tercihi kaydedildi: {code}.");

        string question;
        string later;
        using (Loc.Scope(chosen))
        {
            question = Loc.T(
                "Dil, uygulama yeniden başlatılınca değişir. Şimdi yeniden başlatılsın mı?\n\n" +
                "Dosya listesi ve önizleme sıfırlanır; işlem geçmişi etkilenmez.",
                "The language changes when the application restarts. Restart now?\n\n" +
                "The file list and preview will be reset; the operation history is not affected.");
            later = Loc.T(
                "Dil, uygulama bir sonraki açılışta değişecek.",
                "The language will change the next time the application starts.");
        }

        if (_viewModel.IsBusy)
        {
            MessageBox.Show(this, later, "PDM Variable Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this, question, "PDM Variable Studio", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            Restart();
        }
    }

    /// <summary>
    /// Uygulamayı aynı vault ve başlangıç klasörüyle yeniden başlatır.
    /// </summary>
    /// <remarks>
    /// Yeni süreç bu pencere kapanmadan başlatılır: başlatma başarısız olursa kullanıcı
    /// elinde çalışan bir pencereyle kalır. Argümanlar eklentinin verdiğiyle aynı sözleşme
    /// (<see cref="StartupOptions"/>).
    /// </remarks>
    private void Restart()
    {
        var executable = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        var arguments = "--vault \"" + _vaultName + "\"";
        if (_folderId > 0)
        {
            arguments += " --folder " + _folderId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(executable) ?? string.Empty,
            });

            _log.Info($"Dil değişikliği için yeniden başlatılıyor: {executable} {arguments}");
            Close();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or System.IO.IOException)
        {
            _log.Error("Uygulama yeniden başlatılamadı.", exception);
            MessageBox.Show(
                this,
                Loc.T("Uygulama yeniden başlatılamadı; lütfen kapatıp yeniden açın.",
                      "The application could not be restarted; please close and reopen it."),
                "PDM Variable Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
