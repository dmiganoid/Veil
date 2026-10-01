using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Veil.Models;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

public partial class ServerSetupView : UserControl
{
    private readonly AppServices _services;
    private readonly Action<AppPage> _navigate;
    private bool _draftLoaded;

    // The furthest setup step reached in the current run, used to show where a failed run stopped.
    private int _furthestStep = -1;

    public ServerSetupView(AppServices services, Action<AppPage> navigate)
    {
        _services = services;
        _navigate = navigate;
        InitializeComponent();

        _services.ServerSetup.StateChanged += (_, _) => this.OnUi(RefreshState);
        Loaded += async (_, _) =>
        {
            if (!_draftLoaded)
            {
                Fill(await _services.Config.LoadServerSetupConfigAsync(), includePasswords: false);
                // Only now may SaveDraftAsync persist the form; saving earlier would store empty fields.
                _draftLoaded = true;
            }

            RefreshState();
        };
    }

    /// <summary>
    /// Persists the non-secret fields so the form is prefilled next time.
    /// </summary>
    public async Task SaveDraftAsync()
    {
        if (_draftLoaded)
        {
            await _services.Config.SaveServerSetupConfigAsync(ReadDraft());
        }
    }

    private void Fill(ServerSetupConfig config, bool includePasswords)
    {
        HostTextBox.Text = config.Host;
        SshPortTextBox.Text = config.SshPort.ToString();
        SshUserTextBox.Text = config.SshUsername;
        SshKeyPathTextBox.Text = config.SshKeyPath ?? "";
        KeyAuthOption.IsChecked = config.UseKeyAuth;
        PasswordAuthOption.IsChecked = !config.UseKeyAuth;
        DomainTextBox.Text = config.Domain;
        EmailTextBox.Text = config.Email;
        ListenPortTextBox.Text = config.ListenPort.ToString();
        VpnUsernameTextBox.Text = config.VpnUsername;
        SshPasswordField.Password = includePasswords ? config.SshPassword : "";
        VpnPasswordField.Password = includePasswords ? config.VpnPassword : "";
        UpdateAuthPanels();
    }

    private ServerSetupConfig ReadDraft()
    {
        _ = int.TryParse(SshPortTextBox.Text.Trim(), out var sshPort);
        _ = int.TryParse(ListenPortTextBox.Text.Trim(), out var listenPort);
        return new ServerSetupConfig
        {
            Host = HostTextBox.Text.Trim(),
            SshPort = ServerSetupConfig.IsValidPort(sshPort) ? sshPort : 22,
            SshUsername = string.IsNullOrWhiteSpace(SshUserTextBox.Text) ? "root" : SshUserTextBox.Text.Trim(),
            SshKeyPath = SshKeyPathTextBox.Text.Trim(),
            UseKeyAuth = KeyAuthOption.IsChecked == true,
            Domain = DomainTextBox.Text.Trim(),
            Email = EmailTextBox.Text.Trim(),
            ListenPort = ServerSetupConfig.IsValidPort(listenPort) ? listenPort : 443,
            VpnUsername = VpnUsernameTextBox.Text.Trim()
        };
    }

    private ServerSetupConfig ReadValidatedConfig()
    {
        if (!int.TryParse(SshPortTextBox.Text.Trim(), out var sshPort) || !ServerSetupConfig.IsValidPort(sshPort))
        {
            throw new InvalidOperationException("Enter an SSH port between 1 and 65535.");
        }

        if (!int.TryParse(ListenPortTextBox.Text.Trim(), out var listenPort) || !ServerSetupConfig.IsValidPort(listenPort))
        {
            throw new InvalidOperationException("Enter a listen port between 1 and 65535.");
        }

        var config = new ServerSetupConfig
        {
            Host = Required(HostTextBox.Text, "Server IP or hostname"),
            SshPort = sshPort,
            SshUsername = Required(SshUserTextBox.Text, "SSH user"),
            SshPassword = SshPasswordField.Password,
            SshKeyPath = SshKeyPathTextBox.Text.Trim(),
            UseKeyAuth = KeyAuthOption.IsChecked == true,
            Domain = Required(DomainTextBox.Text, "Domain"),
            Email = Required(EmailTextBox.Text, "Email"),
            ListenPort = listenPort,
            VpnUsername = Required(VpnUsernameTextBox.Text, "VPN username"),
            VpnPassword = Required(VpnPasswordField.Password, "VPN password")
        };

        if (config.UseKeyAuth)
        {
            if (string.IsNullOrWhiteSpace(config.SshKeyPath))
            {
                throw new InvalidOperationException("Choose the SSH private key file.");
            }

            if (!File.Exists(config.SshKeyPath))
            {
                throw new InvalidOperationException($"SSH key file not found: {config.SshKeyPath}");
            }
        }
        else if (string.IsNullOrWhiteSpace(config.SshPassword))
        {
            throw new InvalidOperationException("Enter the SSH password.");
        }

        return config;
    }

    private static string Required(string value, string label)
    {
        value = value.Trim();
        return value.Length > 0 ? value : throw new InvalidOperationException($"{label} is required.");
    }

    private void RefreshState()
    {
        var setup = _services.ServerSetup;
        var step = setup.CurrentStep;
        var index = step.StepIndex();
        var running = setup.IsRunning;

        StepText.Text = step == SetupStep.Failed && !string.IsNullOrWhiteSpace(setup.ErrorMessage)
            ? $"Installation failed: {setup.ErrorMessage}"
            : step.DisplayText();
        StepText.Foreground = (Brush)FindResource(step switch
        {
            SetupStep.Failed => "DangerBrush",
            SetupStep.Completed => "SuccessBrush",
            _ => "MutedBrush"
        });
        SetupProgress.Value = index < 0 ? 0 : Math.Min(7, index);
        SetupProgress.Foreground = (Brush)FindResource(step == SetupStep.Failed ? "DangerBrush" : step == SetupStep.Completed ? "SuccessBrush" : "AccentBrush");

        FormPanel.IsEnabled = !running;
        InstallButton.IsEnabled = !running;
        InstallButtonText.Text = running ? "Installing…" : step == SetupStep.Completed ? "Install again" : "Install server";
        ApplyButton.IsEnabled = step == SetupStep.Completed;

        if (index >= 0)
        {
            _furthestStep = Math.Max(_furthestStep, index);
        }

        var failedAt = step == SetupStep.Failed ? Math.Max(_furthestStep, 0) : -1;
        SetStep(SshStepIcon, SshStepGlyph, SshStepStatus, "1", StateFor(index, failedAt, 0, 1));
        SetStep(InstallStepIcon, InstallStepGlyph, InstallStepStatus, "2", StateFor(index, failedAt, 2, 3));
        SetStep(CertificateStepIcon, CertificateStepGlyph, CertificateStepStatus, "3", StateFor(index, failedAt, 4, 4));
        SetStep(ServiceStepIcon, ServiceStepGlyph, ServiceStepStatus, "4", StateFor(index, failedAt, 5, 7));

        var logs = setup.Logs;
        SetupLogTextBox.Text = string.Join(Environment.NewLine, logs);
        SetupLogTextBox.ScrollToEnd();
        if (running && logs.Count > 0)
        {
            LogExpander.IsExpanded = true;
        }
    }

    private static string StateFor(int index, int failedAt, int start, int end)
    {
        if (failedAt >= 0)
        {
            return failedAt < start ? "queued" : failedAt <= end ? "failed" : "done";
        }

        if (index < start)
        {
            return "queued";
        }

        return index <= end && index < 7 ? "active" : "done";
    }

    private void SetStep(Border icon, TextBlock glyph, TextBlock status, string number, string state)
    {
        switch (state)
        {
            case "done":
                icon.Background = (Brush)FindResource("SuccessSoftBrush");
                glyph.Text = "\uE73E";
                glyph.FontFamily = (FontFamily)FindResource("IconFont");
                glyph.Foreground = (Brush)FindResource("SuccessBrush");
                status.Text = "Done";
                break;
            case "active":
                icon.Background = (Brush)FindResource("AccentSoftBrush");
                glyph.Text = number;
                glyph.FontFamily = (FontFamily)FindResource("UiFont");
                glyph.Foreground = (Brush)FindResource("AccentBrush");
                status.Text = "In progress";
                break;
            case "failed":
                icon.Background = (Brush)FindResource("DangerSoftBrush");
                glyph.Text = "\uE711";
                glyph.FontFamily = (FontFamily)FindResource("IconFont");
                glyph.Foreground = (Brush)FindResource("DangerBrush");
                status.Text = "Failed";
                break;
            default:
                icon.Background = (Brush)FindResource("SurfaceRaisedBrush");
                glyph.Text = number;
                glyph.FontFamily = (FontFamily)FindResource("UiFont");
                glyph.Foreground = (Brush)FindResource("MutedBrush");
                status.Text = "";
                break;
        }
    }

    private void UpdateAuthPanels()
    {
        var useKey = KeyAuthOption.IsChecked == true;
        SshPasswordPanel.Visibility = (!useKey).ToVisibility();
        SshKeyPanel.Visibility = useKey.ToVisibility();
    }

    private void AuthOption_Checked(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateAuthPanels();
        }
    }

    private void BrowseKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the SSH private key",
            Filter = "All files (*.*)|*.*"
        };
        var sshFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        if (Directory.Exists(sshFolder))
        {
            dialog.InitialDirectory = sshFolder;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            SshKeyPathTextBox.Text = dialog.FileName;
        }
    }

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        VpnPasswordField.Password = PasswordGenerator.Generate();
        VpnPasswordField.Reveal();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        ServerSetupConfig config;
        try
        {
            config = ReadValidatedConfig();
        }
        catch (InvalidOperationException ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Check the server details", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _furthestStep = -1;
            await _services.Config.SaveServerSetupConfigAsync(config);
            await _services.ServerSetup.InstallAndRememberAsync(config);

            // The service may pick another free listen port; show the values that were actually used.
            Fill(config, includePasswords: true);
            await _services.Config.SaveServerSetupConfigAsync(config);

            if (_services.ServerSetup.CurrentStep == SetupStep.Completed)
            {
                if (AppMessageBox.Show(Window.GetWindow(this),
                        "The server is installed and running.\n\nUse it for this Veil client now? This replaces the server address and credentials on the Connection page.",
                        "Server ready", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await ApplyToClientAsync();
                }
            }
            else if (_services.ServerSetup.ErrorMessage != null)
            {
                AppMessageBox.Show(Window.GetWindow(this), _services.ServerSetup.ErrorMessage, "Installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshState();
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e) => await ApplyToClientAsync();

    private async Task ApplyToClientAsync()
    {
        try
        {
            await _services.ServerSetup.ApplyToClientConfigAsync(_services.Config);
            _navigate(AppPage.Settings);
            AppMessageBox.Show(Window.GetWindow(this), "The connection settings now point to your new server.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Could not apply the settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _services.ServerSetup.ClearLogs();
        RefreshState();
    }
}
