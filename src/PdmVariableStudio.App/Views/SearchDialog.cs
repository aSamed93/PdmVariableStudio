using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PdmVariableStudio.Core.Abstractions;

namespace PdmVariableStudio.App.Views;

/// <summary>
/// Arama ölçütlerini sorar. Sonuç PDM'in kendi arama motoruna (<c>IEdmSearch</c>) verilir.
/// </summary>
/// <remarks>
/// <para>
/// PDM'in tam arama penceresini açan bir API yok; bu yüzden ölçütler burada toplanıp
/// <c>IEdmSearch</c>'e aktarılıyor. Kazanç, aramanın PDM tarafında çalışması: yetki süzgeci,
/// paylaşılan dosyalar ve silinmiş öğe kuralları kendiliğinden doğru uygulanıyor.
/// </para>
/// <para>
/// Ayrı bir XAML dosyası yerine kodla kuruldu — beş alanlık bir iletişim kutusu için
/// XAML + code-behind ikilisi gereksiz bir dosya çifti olurdu (aynı gerekçe
/// <c>VaultPicker</c> için de geçerli).
/// </para>
/// </remarks>
internal static class SearchDialog
{
    public static FileSearchCriteria? Show(IntPtr ownerHandle, IReadOnlyList<string> variableNames)
    {
        var fileName = new TextBox { Margin = new Thickness(0, 0, 0, 4) };

        var recursive = new CheckBox
        {
            Content = "Alt klasörleri de ara",
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 10),
        };

        var variable = new ComboBox
        {
            IsEditable = true,
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 170,
        };

        variable.Items.Add(string.Empty);
        foreach (var name in variableNames)
        {
            variable.Items.Add(name);
        }

        variable.SelectedIndex = 0;

        var variableValue = new TextBox { MinWidth = 170 };

        var layout = new StackPanel { Margin = new Thickness(16) };

        layout.Children.Add(Caption("Dosya adı"));
        layout.Children.Add(fileName);
        layout.Children.Add(Hint("Joker karakter kullanabilirsiniz:  *.sldprt   ya da   MIL-*"));
        layout.Children.Add(recursive);

        layout.Children.Add(Caption("Değişken değerine göre süz (isteğe bağlı)"));

        var variableRow = new StackPanel { Orientation = Orientation.Horizontal };
        variableRow.Children.Add(variable);
        variableRow.Children.Add(variableValue);
        layout.Children.Add(variableRow);

        layout.Children.Add(Hint("Değişken seçilirse yalnızca o değişkeni bu değeri taşıyan dosyalar gelir."));

        var ok = new Button
        {
            Content = "Ara ve Ekle",
            IsDefault = true,
            MinWidth = 110,
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
            Margin = new Thickness(0, 16, 0, 0),
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        var window = new Window
        {
            Title = "Dosya ara",
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
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        FileSearchCriteria? result = null;

        ok.Click += (_, _) =>
        {
            var criteria = new FileSearchCriteria
            {
                FileName = fileName.Text ?? string.Empty,
                Recursive = recursive.IsChecked == true,
                VariableName = (variable.Text ?? string.Empty).Trim(),
                VariableValue = variableValue.Text ?? string.Empty,
            };

            // Boş ölçütle arama vault'un tamamını çeker; bu istenen bir şey değil ve
            // kullanıcıya bunu sessizce yaptırmak yerine söylemek doğru.
            if (criteria.IsEmpty)
            {
                MessageBox.Show(
                    window,
                    "Dosya adı ya da değişken ölçütlerinden en az birini doldurun." +
                    Environment.NewLine + Environment.NewLine +
                    "Boş bir aramayla vault'un tamamı listeye eklenirdi.",
                    "Dosya ara",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            result = criteria;
            window.DialogResult = true;
        };

        fileName.Focus();

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
