using System.Windows;
using Veil.Ui;

namespace Veil.Dialogs;

public partial class TextPromptDialog : Window
{
    private TextPromptDialog(string title, string label, string value, string confirmText)
    {
        InitializeComponent();
        WindowEffects.EnableDialogChrome(this);

        Title = title;
        LabelText.Text = label;
        ValueTextBox.Text = value;
        OkButton.Content = confirmText;
        Loaded += (_, _) => ValueTextBox.SelectAll();
    }

    /// <summary>
    /// Asks for a single line of text. Returns null when cancelled or when the text is blank.
    /// </summary>
    public static string? Ask(Window? owner, string title, string label, string value, string confirmText = "Save")
    {
        var dialog = new TextPromptDialog(title, label, value, confirmText);
        return AppMessageBox.ShowOwned(dialog, owner) == true && !string.IsNullOrWhiteSpace(dialog.ValueTextBox.Text)
            ? dialog.ValueTextBox.Text.Trim()
            : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
