using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Veil.Ui;

namespace Veil.Dialogs;

public sealed record DialogButton(
    string Label,
    MessageBoxResult Result,
    bool IsDefault = false,
    bool IsCancel = false,
    bool IsPrimary = false);

public partial class MessageDialog : Window
{
    private readonly MessageBoxResult _closeResult;

    public MessageDialog(
        string message,
        string caption,
        MessageBoxImage image,
        IReadOnlyList<DialogButton> buttons,
        MessageBoxResult closeResult,
        string? checkBoxText = null)
    {
        InitializeComponent();
        WindowEffects.EnableDialogChrome(this);

        _closeResult = closeResult;
        Result = closeResult;
        Title = string.IsNullOrWhiteSpace(caption) ? "Veil" : caption;
        MessageText.Text = message;
        ApplyImage(image);

        if (!string.IsNullOrWhiteSpace(checkBoxText))
        {
            OptionCheckBox.Content = checkBoxText;
            OptionCheckBox.Visibility = Visibility.Visible;
        }

        foreach (var button in buttons)
        {
            AddButton(button);
        }
    }

    public MessageBoxResult Result { get; private set; }

    public bool IsOptionChecked => OptionCheckBox.IsChecked == true;

    private void ApplyImage(MessageBoxImage image)
    {
        var (glyph, foreground, background) = image switch
        {
            MessageBoxImage.Error => ("\uE783", "DangerBrush", "DangerSoftBrush"),
            MessageBoxImage.Warning => ("\uE7BA", "WarningBrush", "WarningSoftBrush"),
            MessageBoxImage.Question => ("\uE9CE", "AccentBrush", "AccentSoftBrush"),
            _ => ("\uE946", "AccentBrush", "AccentSoftBrush")
        };

        IconGlyph.Text = glyph;
        IconGlyph.Foreground = (Brush)FindResource(foreground);
        IconBadge.Background = (Brush)FindResource(background);
    }

    private void AddButton(DialogButton definition)
    {
        var button = new Button
        {
            Content = definition.Label,
            IsDefault = definition.IsDefault,
            IsCancel = definition.IsCancel,
            MinWidth = 96,
            Margin = new Thickness(8, 0, 0, 0)
        };

        if (definition.IsPrimary)
        {
            button.Style = (Style)FindResource("PrimaryButton");
        }

        button.Click += (_, _) =>
        {
            Result = definition.Result;
            DialogResult = true;
        };

        ButtonsPanel.Children.Add(button);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true)
        {
            Result = _closeResult;
        }

        base.OnClosed(e);
    }
}
