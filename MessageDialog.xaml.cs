using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Veil;

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
        var dialog = new MessageDialog(message, caption, button, image);
        if (owner is { IsLoaded: true, IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }
}

public partial class MessageDialog : Window
{
    private readonly MessageBoxButton _buttons;

    public MessageDialog(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        _buttons = buttons;
        InitializeComponent();

        CaptionText.Text = string.IsNullOrWhiteSpace(caption) ? "Veil" : caption;
        Title = CaptionText.Text;
        MessageText.Text = message;
        Result = DefaultCloseResult(buttons);

        ApplyImage(image);
        BuildButtons(buttons);
    }

    public MessageBoxResult Result { get; private set; }

    private void ApplyImage(MessageBoxImage image)
    {
        var (glyph, foreground, background) = image switch
        {
            MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => ("\uE783", "#FF7B92", "#24FF7B92"),
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => ("\uE7BA", "#FFD46B", "#24FFD46B"),
            MessageBoxImage.Question => ("\uE897", "#B75CFF", "#24B75CFF"),
            MessageBoxImage.Information or MessageBoxImage.Asterisk => ("\uE946", "#56D9FF", "#2456D9FF"),
            _ => ("\uE946", "#56D9FF", "#2456D9FF")
        };

        HeaderIconText.Text = glyph;
        HeaderIconText.Foreground = BrushFrom(foreground);
        HeaderIconBadge.Background = BrushFrom(background);
        HeaderIconBadge.BorderBrush = BrushFrom(foreground, alpha: 0x66);
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        ButtonsPanel.Children.Clear();
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }
    }

    private void AddButton(
        string text,
        MessageBoxResult result,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("DialogButton"),
            IsDefault = isDefault,
            IsCancel = isCancel
        };
        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = true;
            Close();
        };

        ButtonsPanel.Children.Add(button);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = DefaultCloseResult(_buttons);
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static MessageBoxResult DefaultCloseResult(MessageBoxButton buttons) =>
        buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };

    private static SolidColorBrush BrushFrom(string hex, byte? alpha = null)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (alpha.HasValue)
        {
            color.A = alpha.Value;
        }

        return new SolidColorBrush(color);
    }
}
