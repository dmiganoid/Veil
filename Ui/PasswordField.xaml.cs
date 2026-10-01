using System.Windows;
using System.Windows.Controls;

namespace Veil.Ui;

/// <summary>
/// Password input with a show/hide toggle.
/// </summary>
public partial class PasswordField : UserControl
{
    private bool _revealed;
    private bool _syncing;

    public PasswordField()
    {
        InitializeComponent();
    }

    public event EventHandler? PasswordChanged;

    public string Password
    {
        get => _revealed ? VisibleInput.Text : HiddenInput.Password;
        set
        {
            _syncing = true;
            HiddenInput.Password = value;
            VisibleInput.Text = value;
            _syncing = false;
        }
    }

    public void Reveal()
    {
        if (!_revealed)
        {
            Toggle();
        }
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e) => Toggle();

    private void Toggle()
    {
        var value = Password;
        _revealed = !_revealed;
        Password = value;
        HiddenInput.Visibility = (!_revealed).ToVisibility();
        VisibleInput.Visibility = _revealed.ToVisibility();
        ToggleButton.Content = _revealed ? "\uED1A" : "\uE7B3";
        ToggleButton.ToolTip = _revealed ? "Hide password" : "Show password";
    }

    private void HiddenInput_PasswordChanged(object sender, RoutedEventArgs e) => RaiseChanged();

    private void VisibleInput_TextChanged(object sender, TextChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        if (!_syncing)
        {
            PasswordChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
