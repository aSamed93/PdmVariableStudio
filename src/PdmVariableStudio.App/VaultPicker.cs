using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PdmVariableStudio.App;

/// <summary>
/// Birden fazla vault view'ı varken hangisine bağlanılacağını sorar.
/// </summary>
/// <remarks>
/// Yalnızca uygulama eklenti olmadan açıldığında görünür; eklentiden başlatıldığında vault
/// zaten bilinir. Ayrı bir XAML dosyası yerine kodla kuruldu: tek listeden ibaret bir
/// pencere için XAML + code-behind ikilisi gereksiz bir dosya çifti olurdu.
/// </remarks>
internal static class VaultPicker
{
    public static VaultEntry? Show(IReadOnlyList<VaultEntry> vaults)
    {
        VaultEntry? selected = null;

        var list = new ListBox
        {
            ItemsSource = vaults,
            Margin = new Thickness(0, 0, 0, 12),
            SelectedIndex = 0,
        };

        var ok = new Button
        {
            Content = "Aç",
            IsDefault = true,
            MinWidth = 90,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };

        var cancel = new Button
        {
            Content = "İptal",
            IsCancel = true,
            MinWidth = 90,
            Padding = new Thickness(14, 6, 14, 6),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new DockPanel { Margin = new Thickness(16) };

        var caption = new TextBlock
        {
            Text = "Hangi vault ile çalışmak istiyorsunuz?",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold,
        };

        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(caption);
        layout.Children.Add(buttons);
        layout.Children.Add(list);

        var window = new Window
        {
            Title = "PDM Variable Studio",
            Content = layout,
            Width = 460,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
        };

        ok.Click += (_, _) =>
        {
            selected = list.SelectedItem as VaultEntry;
            window.DialogResult = selected is not null;
        };

        // Çift tıklama da açsın: liste tek satırlıksa düğmeye gitmek gereksiz bir adım.
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is VaultEntry entry)
            {
                selected = entry;
                window.DialogResult = true;
            }
        };

        return window.ShowDialog() == true ? selected : null;
    }
}
