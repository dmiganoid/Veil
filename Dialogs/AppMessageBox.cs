using System.Windows;
using Veil.Dialogs;

namespace Veil;

/// <summary>
/// Themed replacement for <see cref="MessageBox"/>.
/// </summary>
public static class AppMessageBox
{
    public static MessageBoxResult Show(
        string message,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None) =>
        Show(Application.Current?.MainWindow, message, caption, button, image);

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var (buttons, closeResult) = button switch
        {
            MessageBoxButton.OKCancel => (new[]
            {
                new DialogButton("OK", MessageBoxResult.OK, IsDefault: true, IsPrimary: true),
                new DialogButton("Cancel", MessageBoxResult.Cancel, IsCancel: true)
            }, MessageBoxResult.Cancel),
            MessageBoxButton.YesNo => (new[]
            {
                new DialogButton("Yes", MessageBoxResult.Yes, IsDefault: true, IsPrimary: true),
                new DialogButton("No", MessageBoxResult.No, IsCancel: true)
            }, MessageBoxResult.No),
            MessageBoxButton.YesNoCancel => (new[]
            {
                new DialogButton("Yes", MessageBoxResult.Yes, IsDefault: true, IsPrimary: true),
                new DialogButton("No", MessageBoxResult.No),
                new DialogButton("Cancel", MessageBoxResult.Cancel, IsCancel: true)
            }, MessageBoxResult.Cancel),
            _ => (new[]
            {
                new DialogButton("OK", MessageBoxResult.OK, IsDefault: true, IsCancel: true, IsPrimary: true)
            }, MessageBoxResult.OK)
        };

        return ShowChoice(owner, message, caption, image, closeResult, buttons);
    }

    public static MessageBoxResult ShowChoice(
        Window? owner,
        string message,
        string caption,
        MessageBoxImage image,
        MessageBoxResult closeResult,
        params DialogButton[] buttons)
    {
        var dialog = new MessageDialog(message, caption, image, buttons, closeResult);
        ShowOwned(dialog, owner);
        return dialog.Result;
    }

    /// <summary>
    /// Shows a dialog with custom buttons and a checkbox such as "Don't ask again".
    /// </summary>
    public static (MessageBoxResult Result, bool IsChecked) ShowChoiceWithOption(
        Window? owner,
        string message,
        string caption,
        MessageBoxImage image,
        MessageBoxResult closeResult,
        string checkBoxText,
        params DialogButton[] buttons)
    {
        var dialog = new MessageDialog(message, caption, image, buttons, closeResult, checkBoxText);
        ShowOwned(dialog, owner);
        return (dialog.Result, dialog.IsOptionChecked);
    }

    internal static bool? ShowOwned(Window dialog, Window? owner)
    {
        if (owner is { IsLoaded: true, IsVisible: true } && owner.WindowState != WindowState.Minimized)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }

        return dialog.ShowDialog();
    }
}
