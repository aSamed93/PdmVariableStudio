using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Localization;
using PdmVariableStudio.Core.Settings;
using PdmVariableStudio.Core.Workbook;

namespace PdmVariableStudio.App.Views;

/// <summary>
/// Hakkında penceresi: sürüm, dosya konumları ve destek bağlantıları.
/// </summary>
/// <remarks>
/// <para>
/// Amacı süs değil, <b>destek</b>: kullanıcı bir sorun bildirirken "hangi sürüm, günlük
/// nerede" sorularının cevabını tek pencereden alıp göndersin. Bu yüzden her yolun yanında
/// onu açan bir düğme var ve "Bilgileri Kopyala" hepsini tek metin olarak panoya koyuyor.
/// </para>
/// <para>
/// Kodla kuruldu; gerekçe <see cref="SearchDialog"/> ile aynı.
/// </para>
/// </remarks>
internal static class AboutDialog
{
    public const string ProjectUrl = "https://github.com/aSamed93/PdmVariableStudio";
    public const string IssuesUrl = ProjectUrl + "/issues";
    public const string ReleasesUrl = ProjectUrl + "/releases";

    public static void Show(IntPtr ownerHandle, IStudioLog log, string journalRoot, string journalRootSource)
    {
        var executable = typeof(AboutDialog).Assembly.Location;
        var installDirectory = Path.GetDirectoryName(executable) ?? string.Empty;
        var settingsPath = StudioSettings.DefaultPath();
        var languageText = $"{UiLanguages.NativeName(Loc.Current)} — {LanguagePreference.DescribeSource(Program.LanguageSource)}";

        var layout = new StackPanel { Margin = new Thickness(18) };

        layout.Children.Add(new TextBlock
        {
            Text = ProductInfo.Name,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });

        layout.Children.Add(new TextBlock
        {
            Text = Loc.T($"Sürüm {ProductInfo.Version}", $"Version {ProductInfo.Version}"),
            Margin = new Thickness(0, 2, 0, 10),
        });

        layout.Children.Add(new TextBlock
        {
            Text = Loc.T(
                "SOLIDWORKS PDM Professional için kart değişkenlerini Excel ile toplu düzenleme " +
                "aracı — önizlemeli, çakışma korumalı ve geri alınabilir.",
                "Bulk-edit SOLIDWORKS PDM Professional data card variables in Excel — " +
                "with preview, conflict protection and undo."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        layout.Children.Add(InfoRow(Loc.T("Dil", "Language"), languageText));
        layout.Children.Add(PathRow(Loc.T("Uygulama", "Application"), installDirectory, () => Reveal(installDirectory, log)));
        layout.Children.Add(PathRow(Loc.T("Günlük", "Log"), log.FilePath, () => OpenLog(log)));
        layout.Children.Add(PathRow(
            Loc.T($"İşlem geçmişi ({journalRootSource})", $"Operation history ({journalRootSource})"),
            journalRoot,
            () => Reveal(journalRoot, log)));
        layout.Children.Add(PathRow(Loc.T("Ayarlar", "Settings"), settingsPath, () => Reveal(Path.GetDirectoryName(settingsPath) ?? string.Empty, log)));

        layout.Children.Add(new TextBlock
        {
            Text = Loc.T(
                "Bu uygulama hiçbir yere veri göndermez; tüm dosyalar yukarıdaki klasörlerde, bu " +
                "bilgisayarda durur. Ücretsizdir, MIT lisansıyla dağıtılır ve SOLIDWORKS ya da " +
                "Dassault Systèmes ile bağlantılı değildir.",
                "This application sends no data anywhere; all files stay in the folders above, on " +
                "this computer. It is free, distributed under the MIT license, and is not affiliated " +
                "with SOLIDWORKS or Dassault Systèmes."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12),
            Opacity = 0.8,
        });

        var links = new TextBlock { Margin = new Thickness(0, 0, 0, 4) };
        links.Inlines.Add(Link(Loc.T("Proje sayfası", "Project page"), ProjectUrl, log));
        links.Inlines.Add(new Run("   ·   "));
        links.Inlines.Add(Link(Loc.T("Sorun bildir", "Report an issue"), IssuesUrl, log));
        links.Inlines.Add(new Run("   ·   "));
        links.Inlines.Add(Link(Loc.T("Sürümler", "Releases"), ReleasesUrl, log));
        layout.Children.Add(links);

        var copy = new Button
        {
            Content = Loc.T("Bilgileri Kopyala", "Copy Info"),
            MinWidth = 130,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = Loc.T(
                "Sürüm ve dosya yollarını panoya kopyalar; sorun bildirirken yapıştırın",
                "Copies the version and file paths to the clipboard; paste them when reporting an issue"),
        };

        var close = new Button
        {
            Content = Loc.T("Kapat", "Close"),
            IsCancel = true,
            IsDefault = true,
            MinWidth = 90,
            Padding = new Thickness(14, 6, 14, 6),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        layout.Children.Add(buttons);

        var window = new Window
        {
            Title = Loc.T("Hakkında", "About"),
            Content = layout,
            Width = 680,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        if (ownerHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(window) { Owner = ownerHandle };
        }

        copy.Click += (_, _) =>
        {
            var text =
                $"{ProductInfo.Name} {ProductInfo.Version}" + Environment.NewLine +
                Loc.T($"Dil: {languageText}", $"Language: {languageText}") + Environment.NewLine +
                Loc.T($"Uygulama: {installDirectory}", $"Application: {installDirectory}") + Environment.NewLine +
                Loc.T($"Günlük: {log.FilePath}", $"Log: {log.FilePath}") + Environment.NewLine +
                Loc.T($"İşlem geçmişi: {journalRoot} ({journalRootSource})",
                      $"Operation history: {journalRoot} ({journalRootSource})") + Environment.NewLine +
                Loc.T($"Ayarlar: {settingsPath}", $"Settings: {settingsPath}") + Environment.NewLine +
                $"Windows: {Environment.OSVersion.VersionString}, .NET {Environment.Version}";

            try
            {
                Clipboard.SetText(text);
                copy.Content = Loc.T("Kopyalandı", "Copied");
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                // Pano başka bir uygulama tarafından kilitliyse olur; teşhis değeri düşük.
                log.Warn("Pano yazılamadı: " + exception.Message);
            }
        };

        window.ShowDialog();
    }

    private static UIElement PathRow(string caption, string path, Action open)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = caption,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var value = new TextBox
        {
            Text = path,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = null,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = path,
        };

        var button = new Button
        {
            Content = Loc.T("Aç", "Open"),
            MinWidth = 0,
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
        };

        button.Click += (_, _) => open();

        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        Grid.SetColumn(button, 2);
        grid.Children.Add(label);
        grid.Children.Add(value);
        grid.Children.Add(button);
        return grid;
    }

    /// <summary>Açma düğmesi olmayan bilgi satırı; hizası <see cref="PathRow"/> ile aynı.</summary>
    private static UIElement InfoRow(string caption, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = caption,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var value = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = null,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        grid.Children.Add(label);
        grid.Children.Add(value);
        return grid;
    }

    private static Hyperlink Link(string text, string url, IStudioLog log)
    {
        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
        link.RequestNavigate += (_, e) =>
        {
            Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }, log);
            e.Handled = true;
        };

        return link;
    }

    private static void OpenLog(IStudioLog log)
    {
        if (!File.Exists(log.FilePath))
        {
            Reveal(Path.GetDirectoryName(log.FilePath) ?? string.Empty, log);
            return;
        }

        // Varsayılan .log ilişkilendirmesi olmayan makine çok; Not Defteri her yerde var.
        Start(new ProcessStartInfo("notepad.exe", "\"" + log.FilePath + "\"") { UseShellExecute = true }, log);
    }

    private static void Reveal(string directory, IStudioLog log)
    {
        if (directory.Length == 0)
        {
            return;
        }

        if (!Directory.Exists(directory))
        {
            MessageBox.Show(
                Loc.T("Klasör henüz oluşturulmamış:", "The folder has not been created yet:") + Environment.NewLine + directory,
                Loc.T("Hakkında", "About"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Start(new ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true }, log);
    }

    private static void Start(ProcessStartInfo startInfo, IStudioLog log)
    {
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception exception)
        {
            log.Error("Açılamadı: " + startInfo.FileName + " " + startInfo.Arguments, exception);
        }
    }
}
