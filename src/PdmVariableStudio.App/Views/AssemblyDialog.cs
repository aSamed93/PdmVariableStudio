using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.App.Views;

/// <summary>Montajdan eklemede kullanıcının seçimi.</summary>
internal sealed class AssemblyChoice
{
    public AssemblyChoice(ConfigurationKey configuration, bool includeSubassemblyContents, bool includeRoot)
    {
        Configuration = configuration;
        IncludeSubassemblyContents = includeSubassemblyContents;
        IncludeRoot = includeRoot;
    }

    public ConfigurationKey Configuration { get; }

    public bool IncludeSubassemblyContents { get; }

    public bool IncludeRoot { get; }
}

/// <summary>
/// Bir montajın hangi konfigürasyonuyla ve ne kadar derine açılacağını sorar.
/// </summary>
/// <remarks>
/// <para>
/// Konfigürasyon sorulmak ZORUNDA: montajın her konfigürasyonu farklı bileşenler ve farklı
/// parça konfigürasyonları kullanabilir. Varsayılan seçim ilk konfigürasyon; montajın tek
/// konfigürasyonu varsa seçim kutusu yine görünür ama tek seçeneklidir — kullanıcı neyin
/// açıldığını görsün.
/// </para>
/// <para>
/// Kodla kuruldu; gerekçe <see cref="SearchDialog"/> ile aynı.
/// </para>
/// </remarks>
internal static class AssemblyDialog
{
    public static AssemblyChoice? Show(
        IntPtr ownerHandle,
        string assemblyName,
        IReadOnlyList<ConfigurationKey> configurations)
    {
        var configuration = new ComboBox { Margin = new Thickness(0, 0, 0, 4), MinWidth = 220 };
        foreach (var key in configurations)
        {
            configuration.Items.Add(key.Name);
        }

        configuration.SelectedIndex = configurations.Count > 0 ? 0 : -1;

        var subassemblies = new CheckBox
        {
            Content = Loc.T("Alt montajların içindeki parçaları da ekle", "Also add the contents of subassemblies"),
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var includeRoot = new CheckBox
        {
            Content = Loc.T("Montajın kendisini de ekle", "Also add the assembly itself"),
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var layout = new StackPanel { Margin = new Thickness(16) };

        layout.Children.Add(new TextBlock
        {
            Text = assemblyName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        layout.Children.Add(Caption(Loc.T("Konfigürasyon", "Configuration")));
        layout.Children.Add(configuration);
        layout.Children.Add(Hint(Loc.T(
            "Montajın bu konfigürasyonunda kullanılan bileşenler eklenir. Her parça için yalnızca " +
            "montajın kullandığı konfigürasyon ve dosya düzeyi (@) satır olur.",
            "The components used in this configuration of the assembly are added. Each part gets " +
            "rows only for the configuration the assembly uses and for the file level (@).")));
        layout.Children.Add(subassemblies);
        layout.Children.Add(includeRoot);

        var ok = new Button
        {
            Content = Loc.T("Bileşenleri Ekle", "Add Components"),
            IsDefault = true,
            MinWidth = 130,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var cancel = new Button
        {
            Content = Loc.T("İptal", "Cancel"),
            IsCancel = true,
            MinWidth = 90,
            Padding = new Thickness(14, 6, 14, 6),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        var window = new Window
        {
            Title = Loc.T("Montajdan ekle", "Add from Assembly"),
            Content = layout,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        if (ownerHandle != IntPtr.Zero)
        {
            new WindowInteropHelper(window) { Owner = ownerHandle };
        }

        AssemblyChoice? result = null;

        ok.Click += (_, _) =>
        {
            if (configuration.SelectedIndex < 0)
            {
                return;
            }

            result = new AssemblyChoice(
                configurations[configuration.SelectedIndex],
                subassemblies.IsChecked == true,
                includeRoot.IsChecked == true);

            window.DialogResult = true;
        };

        configuration.Focus();

        return window.ShowDialog() == true ? result : null;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };
}
