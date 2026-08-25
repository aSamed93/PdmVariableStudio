using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PdmVariableStudio.App.Views;

/// <summary>
/// Durum sınıfı adını (<c>Safe</c>, <c>Conflict</c>, ...) palet fırçasına çevirir.
/// </summary>
/// <remarks>
/// Renkler XAML'e gömülmez; anahtar <c>Status.&lt;kind&gt;.&lt;Background|Foreground&gt;</c>
/// biçiminde üretilip paletten çözülür. Yeni bir durum eklemek yalnızca palete iki anahtar
/// eklemek demek — burada ya da tablo tanımında değişiklik gerekmiyor.
/// </remarks>
internal sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var kind = value as string;
        var part = parameter as string ?? "Background";

        if (string.IsNullOrEmpty(kind))
        {
            kind = "Default";
        }

        var brush = Application.Current?.TryFindResource($"Status.{kind}.{part}") as Brush;

        // Palette anahtarı yoksa sessizce nötr renge düşülür: eksik bir anahtar yüzünden
        // tüm tablonun çizilmemesi kabul edilemez.
        return brush ?? Application.Current?.TryFindResource($"Status.Default.{part}") as Brush
            ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sayı sıfırdan büyükse görünür yapar.</summary>
internal sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int number ? number : 0;
        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Metin doluysa görünür yapar.</summary>
internal sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Boolean değeri tersleyerek görünürlüğe çevirir.</summary>
internal sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
