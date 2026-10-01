using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Veil.Models;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

public partial class SettingsView : UserControl
{
    private readonly AppServices _services;
    private readonly ConnectionController _connection;
    private bool _filling;
    private bool _dirty;
    private bool _saving;
    private bool _loadingPreferences;

    public SettingsView(AppServices services, ConnectionController connection)
    {
        _services = services;
        _connection = connection;
        InitializeComponent();

        _services.Vpn.StateChanged += (_, _) => this.OnUi(RefreshConnectionNotice);
        _services.Config.ConfigChanged += (_, _) => this.OnUi(async () =>
        {
            // Another page saved the configuration (routing, import, server setup): refresh unless the
            // user is editing here.
            if (!_saving && !_dirty)
            {
                await ReloadAsync();
            }
        });
        Loaded += async (_, _) =>
        {
            if (!_dirty)
            {
                await ReloadAsync();
            }

            RefreshConnectionNotice();
        };
    }

    public bool HasUnsavedChanges => _dirty;

    public async Task ReloadAsync()
    {
        Fill(await _services.Config.LoadConfigAsync());

        _loadingPreferences = true;
        var preferences = await _services.Config.LoadPreferencesAsync();
        SelectByTag(CloseActionComboBox, preferences.CloseAction.ToString());
        _loadingPreferences = false;
    }

    private async void CloseActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences || !IsLoaded ||
            !Enum.TryParse<CloseAction>(SelectedTag(CloseActionComboBox, nameof(CloseAction.Ask)), out var action))
        {
            return;
        }

        var preferences = await _services.Config.LoadPreferencesAsync();
        preferences.CloseAction = action;
        await _services.Config.SavePreferencesAsync(preferences);
    }

    /// <summary>
    /// Saves pending edits. Returns false when validation failed and the user should stay on the page.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        try
        {
            _saving = true;
            var saved = await _services.Config.LoadConfigAsync();
            var config = ReadForm().ApplyTo(saved);
            config.ValidateRequiredClientFields();
            await _services.Config.SaveConfigAsync(config);
            SetDirty(false);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Check the connection settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _saving = false;
        }
    }

    public async Task DiscardChangesAsync()
    {
        SetDirty(false);
        await ReloadAsync();
    }

    private void Fill(ServerConfig config)
    {
        _filling = true;
        try
        {
            HostnameTextBox.Text = config.Hostname;
            AddressTextBox.Text = config.Address;
            PortTextBox.Text = config.Port.ToString();
            UsernameTextBox.Text = config.Username;
            PasswordField.Password = config.Password;
            DnsTextBox.Text = config.Dns;
            CustomSniTextBox.Text = config.CustomSni;
            SelectByTag(ProtocolComboBox, config.UpstreamProtocol);
            SelectByTag(LogLevelComboBox, config.LogLevel);
            FullTunnelOption.IsChecked = config.ConnectionMode == VpnConnectionMode.FullTunnel;
            SystemProxyOption.IsChecked = config.ConnectionMode == VpnConnectionMode.SystemProxy;
            Ipv6Switch.IsChecked = config.HasIpv6;
            AntiDpiSwitch.IsChecked = config.AntiDpi;
            PostQuantumSwitch.IsChecked = config.PostQuantumGroupEnabled;
            SkipVerificationSwitch.IsChecked = config.SkipVerification;
        }
        finally
        {
            _filling = false;
        }

        SetDirty(false);
    }

    private ConnectionForm ReadForm() => new(
        HostnameTextBox.Text,
        AddressTextBox.Text,
        PortTextBox.Text,
        UsernameTextBox.Text,
        PasswordField.Password,
        DnsTextBox.Text,
        SelectedTag(ProtocolComboBox, "http2"),
        SelectedTag(LogLevelComboBox, "info"),
        CustomSniTextBox.Text,
        Ipv6Switch.IsChecked == true,
        SkipVerificationSwitch.IsChecked == true,
        AntiDpiSwitch.IsChecked == true,
        PostQuantumSwitch.IsChecked == true,
        SystemProxyOption.IsChecked == true ? VpnConnectionMode.SystemProxy : VpnConnectionMode.FullTunnel);

    private void RefreshConnectionNotice()
    {
        var status = _services.Vpn.Status;
        ReconnectNotice.Visibility = status.IsActive().ToVisibility();
        ReconnectButton.IsEnabled = status == VpnStatus.Connected;
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await SaveAsync())
        {
            return;
        }

        await _connection.ReconnectAsync();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyText.Visibility = dirty.ToVisibility();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (!_filling && IsLoaded)
        {
            SetDirty(true);
        }
    }

    private void PasswordField_Changed(object? sender, EventArgs e) => Field_Changed(this, new RoutedEventArgs());

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SaveAsync())
        {
            AppMessageBox.Show(Window.GetWindow(this), "Settings saved. They will be used the next time you connect.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Veil settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "veil-config.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var saved = await _services.Config.LoadConfigAsync();
            ServerConfig config;
            try
            {
                config = ReadForm().ApplyTo(saved);
            }
            catch (InvalidOperationException)
            {
                // An unfinished form is still worth exporting; keep the saved values instead.
                config = saved;
            }

            await _services.Config.ExportConfigAsync(config, dialog.FileName);
            AppMessageBox.Show(Window.GetWindow(this),
                "Settings exported. The file contains your VPN password, so keep it private.",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty &&
            AppMessageBox.Show(Window.GetWindow(this), "Importing replaces your unsaved changes. Continue?", "Import settings",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import Veil settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            _saving = true;
            var imported = await _services.Config.ImportConfigAndPersistAsync(dialog.FileName);
            Fill(imported);
            AppMessageBox.Show(Window.GetWindow(this),
                "Settings and routing rules were imported and saved. Review them before connecting.",
                "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _saving = false;
        }
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private static void SelectByTag(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }
}
