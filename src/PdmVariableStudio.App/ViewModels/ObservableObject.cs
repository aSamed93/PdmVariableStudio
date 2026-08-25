using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PdmVariableStudio.App.ViewModels;

/// <summary>MVVM için asgari bildirim temeli.</summary>
internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Değer gerçekten değiştiyse alanı günceller ve bildirim yayar.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    /// <summary>Birden fazla türetilmiş özelliğin bildirimini tek çağrıda yayar.</summary>
    protected void RaiseAll(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            Raise(name);
        }
    }
}

/// <summary>Basit komut. Karmaşık bir komut altyapısına MVP'de ihtiyaç yok.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Komutun kullanılabilirliğini yeniden sorgulatır.
    /// </summary>
    /// <remarks>
    /// <c>CommandManager.RequerySuggested</c> kullanılmıyor: o, her fare hareketinde tüm
    /// komutları yeniden değerlendiriyor ve 60.000 satırlık bir tabloda gözle görülür
    /// takılmaya yol açıyor. Bildirim, durumu gerçekten değiştiren yerlerden açıkça yapılır.
    /// </remarks>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
