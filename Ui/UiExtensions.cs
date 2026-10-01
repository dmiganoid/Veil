using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Veil.Ui;

internal static class UiExtensions
{
    /// <summary>
    /// Runs the action on the UI thread; service events are raised from background threads.
    /// </summary>
    public static void OnUi(this DispatcherObject target, Action action)
    {
        if (target.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = target.Dispatcher.InvokeAsync(action);
    }

    public static IDisposable BusyCursor()
    {
        Mouse.OverrideCursor = Cursors.Wait;
        return new CursorReset();
    }

    public static Visibility ToVisibility(this bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private sealed class CursorReset : IDisposable
    {
        public void Dispose() => Mouse.OverrideCursor = null;
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
