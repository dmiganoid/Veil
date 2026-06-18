using System.IO;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using Veil.Models;

namespace Veil.Services;

public sealed class ServerSetupService : IDisposable
{
    private readonly List<string> _logs = [];
    private readonly IServerSetupSshSessionFactory _sshSessionFactory;
    private IServerSetupSshSession? _sshSession;
    private ServerSetupConfig? _lastConfig;

    public ServerSetupService()
        : this(new RenciServerSetupSshSessionFactory())
    {
    }

    internal ServerSetupService(IServerSetupSshSessionFactory sshSessionFactory)
    {
        _sshSessionFactory = sshSessionFactory;
    }

    public event EventHandler? StateChanged;

    public SetupStep CurrentStep { get; private set; } = SetupStep.Idle;
    public IReadOnlyList<string> Logs => _logs.AsReadOnly();
    public string? ErrorMessage { get; private set; }
    public bool AlreadyInstalled { get; private set; }
    public bool IsRunning => CurrentStep is not (SetupStep.Idle or SetupStep.Completed or SetupStep.Failed);

    public void ClearLogs()
    {
        var wasRunning = IsRunning;
        _logs.Clear();
        ErrorMessage = null;
        if (!wasRunning)
        {
            CurrentStep = SetupStep.Idle;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task InstallAndRememberAsync(ServerSetupConfig config)
    {
        if (IsRunning)
        {
            AddLog("Server installation is already running.");
            return;
        }

        _lastConfig = null;
        await InstallServerAsync(config);
        if (CurrentStep == SetupStep.Completed)
        {
            _lastConfig = config;
        }
    }

    public async Task InstallServerAsync(ServerSetupConfig config)
    {
        if (IsRunning)
        {
            AddLog("Server installation is already running.");
            return;
        }

        _logs.Clear();
        ErrorMessage = null;
        AlreadyInstalled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await StepConnectAsync(config);
            await StepCheckSystemAsync(config);
            await StepInstallAsync();
            await StepConfigureAsync(config);
            await StepCertificateAsync(config);
            await StepStartServiceAsync();
            await StepVerifyAsync();

            SetStep(SetupStep.Completed);
            AddLog("Installation completed successfully.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetStep(SetupStep.Failed);
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    public async Task ApplyToClientConfigAsync(ConfigService configService)
    {
        if (_lastConfig == null || CurrentStep != SetupStep.Completed)
        {
            throw new InvalidOperationException("No completed server setup configuration is available.");
        }

        var existing = await configService.LoadConfigAsync();
        existing.Hostname = _lastConfig.Domain;
        existing.Address = _lastConfig.Host;
        existing.Port = _lastConfig.ListenPort;
        existing.Username = _lastConfig.VpnUsername;
        existing.Password = _lastConfig.VpnPassword;
        await configService.SaveConfigAsync(existing);
    }

    private async Task StepConnectAsync(ServerSetupConfig config)
    {
        SetStep(SetupStep.Connecting);
        AddLog($"Connecting to {config.Host}:{config.SshPort}...");

        await Task.Run(() =>
        {
            Disconnect();

            _sshSession = _sshSessionFactory.Connect(config);
        });

        AddLog("SSH connection established.");
    }

    private async Task StepCheckSystemAsync(ServerSetupConfig config)
    {
        SetStep(SetupStep.CheckingSystem);

        var arch = await RunCommandAsync("uname -m");
        AddLog($"Architecture: {arch}");
        if (arch != "x86_64" && arch != "aarch64")
        {
            throw new InvalidOperationException($"Unsupported architecture: {arch}. Requires x86_64 or aarch64.");
        }

        var osInfo = await RunCommandAsync("cat /etc/os-release | head -5");
        AddLog($"OS: {osInfo.Split('\n').FirstOrDefault() ?? "unknown"}");

        try
        {
            await RunCommandAsync("test -f /opt/trusttunnel/trusttunnel_endpoint");
            AlreadyInstalled = true;
            AddLog("Veil server is already installed on this server.");
        }
        catch
        {
            AlreadyInstalled = false;
            AddLog("Veil server is not installed; it will be installed.");
        }

        try
        {
            await RunCommandAsync("which curl");
        }
        catch
        {
            AddLog("Installing curl...");
            await RunCommandAsync("apt-get update -qq && apt-get install -y -qq curl");
        }

        AddLog($"Checking if port {config.ListenPort} is available...");
        if (AlreadyInstalled)
        {
            await RunCommandAsync("systemctl stop trusttunnel || true");
        }

        var portFound = false;
        var currentPort = config.ListenPort;
        var attempts = 0;

        while (!portFound && attempts < 10)
        {
            string portCheck;
            try
            {
                portCheck = await RunCommandAsync($"ss -tuln | grep \":{currentPort} \" || true");
                if (string.IsNullOrWhiteSpace(portCheck))
                {
                    portFound = true;
                    if (currentPort != config.ListenPort)
                    {
                        AddLog($"Port {config.ListenPort} is busy. Automatically selected port {currentPort}.");
                        config.ListenPort = currentPort;
                    }
                    else
                    {
                        AddLog($"Port {currentPort} is available.");
                    }
                }
                else
                {
                    currentPort = currentPort == 443 ? 8443 : currentPort + 1;
                    attempts++;
                }
            }
            catch
            {
                portFound = true;
            }
        }

        if (!portFound)
        {
            throw new InvalidOperationException($"Could not find an available port. Original requested port: {config.ListenPort}");
        }
    }

    private async Task StepInstallAsync()
    {
        SetStep(SetupStep.Installing);
        AddLog("Downloading and installing Veil server (latest)...");

        await RunCommandAsync("curl -fsSL https://raw.githubusercontent.com/TrustTunnel/TrustTunnel/refs/heads/master/scripts/install.sh | sh -s -- -a y");
        await RunCommandAsync("test -f /opt/trusttunnel/trusttunnel_endpoint");
        AddLog("Veil server installed to /opt/trusttunnel/.");
    }

    private async Task StepConfigureAsync(ServerSetupConfig config)
    {
        SetStep(SetupStep.ConfiguringServer);

        if (AlreadyInstalled)
        {
            AddLog("Stopping existing service...");
            await RunCommandAsync("systemctl stop trusttunnel 2>/dev/null || true");
        }

        await UploadFileAsync("/opt/trusttunnel/vpn.toml", config.GenerateVpnToml());
        AddLog("vpn.toml uploaded.");

        await UploadFileAsync("/opt/trusttunnel/credentials.toml", config.GenerateCredentialsToml());
        AddLog("credentials.toml uploaded.");

        await UploadFileAsync("/opt/trusttunnel/hosts.toml", config.GenerateHostsToml());
        AddLog("hosts.toml uploaded.");

        await RunCommandAsync("chmod 600 /opt/trusttunnel/credentials.toml");
        AddLog("Server configuration ready.");
    }

    private async Task StepCertificateAsync(ServerSetupConfig config)
    {
        SetStep(SetupStep.ObtainingCertificate);

        try
        {
            await RunCommandAsync($"test -f /etc/letsencrypt/live/{ShellQuote(config.Domain)}/fullchain.pem");
            AddLog($"Certificate for {config.Domain} already exists.");
            return;
        }
        catch
        {
            AddLog("Certificate not found, obtaining via Let's Encrypt...");
        }

        try
        {
            await RunCommandAsync("which certbot");
        }
        catch
        {
            AddLog("Installing certbot...");
            await RunCommandAsync("apt-get update -qq && apt-get install -y -qq certbot");
        }

        AddLog($"Requesting certificate for {config.Domain}...");
        try
        {
            await RunCommandAsync(
                "certbot certonly --non-interactive --standalone " +
                $"--agree-tos -m {ShellQuote(config.Email)} -d {ShellQuote(config.Domain)}");
        }
        catch (Exception ex) when (ex.Message.Contains("TCP port 80", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
        {
            AddLog("Port 80 is busy. Trying Nginx/Apache certbot plugins...");
            try
            {
                await RunCommandAsync("apt-get install -y -qq python3-certbot-nginx python3-certbot-apache || true");
                await RunCommandAsync(
                    "certbot certonly --non-interactive --nginx " +
                    $"--agree-tos -m {ShellQuote(config.Email)} -d {ShellQuote(config.Domain)} || " +
                    "certbot certonly --non-interactive --apache " +
                    $"--agree-tos -m {ShellQuote(config.Email)} -d {ShellQuote(config.Domain)}");
            }
            catch
            {
                throw new InvalidOperationException(
                    "Failed to obtain certificate because port 80 is busy and Nginx/Apache auto-config failed. " +
                    $"If you already have a certificate, copy it to /etc/letsencrypt/live/{config.Domain}/ and rerun installation.");
            }
        }

        await RunCommandAsync($"test -f /etc/letsencrypt/live/{ShellQuote(config.Domain)}/fullchain.pem");
        AddLog("Certificate obtained.");
    }

    private async Task StepStartServiceAsync()
    {
        SetStep(SetupStep.StartingService);
        AddLog("Configuring systemd service...");

        await RunCommandAsync("cp /opt/trusttunnel/trusttunnel.service.template /etc/systemd/system/trusttunnel.service");
        await RunCommandAsync("systemctl daemon-reload");
        await RunCommandAsync("systemctl enable trusttunnel");
        await RunCommandAsync("systemctl start trusttunnel");
        AddLog("Service started.");
    }

    private async Task StepVerifyAsync()
    {
        SetStep(SetupStep.Verifying);
        AddLog("Waiting for service to start...");
        await Task.Delay(TimeSpan.FromSeconds(3));

        try
        {
            var status = await RunCommandAsync("systemctl is-active trusttunnel");
            if (status.Trim() == "active")
            {
                AddLog("Veil service is running.");
            }
        }
        catch (Exception ex)
        {
            var journal = await RunCommandAsync("journalctl -u trusttunnel --no-pager -n 20 2>/dev/null || true");
            throw new InvalidOperationException($"Service failed to start.\n\nLogs:\n{journal}\n\nOriginal error: {ex.Message}");
        }
    }

    private async Task<string> RunCommandAsync(string command)
    {
        if (_sshSession == null || !_sshSession.IsConnected)
        {
            throw new InvalidOperationException("SSH not connected.");
        }

        AddLog($"$ {command}");
        return await Task.Run(() =>
        {
            var result = _sshSession.RunCommand(command);
            LogMultiline(result.Output, "  ");
            LogMultiline(result.Error, "  [stderr] ");

            if (result.ExitStatus != 0)
            {
                throw new SshException($"Command failed (exit code {result.ExitStatus}): {command}\n{result.Error}");
            }

            return result.Output.Trim();
        });
    }

    private async Task UploadFileAsync(string remotePath, string content)
    {
        if (_sshSession == null || !_sshSession.IsConnected)
        {
            throw new InvalidOperationException("SSH not connected.");
        }

        AddLog($"Uploading file: {remotePath}");
        await Task.Run(() => _sshSession.UploadFile(remotePath, content));
    }

    private void LogMultiline(string? text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var line in text.Trim().Split('\n'))
        {
            AddLogRaw($"{prefix}{line.TrimEnd('\r')}");
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private void SetStep(SetupStep step)
    {
        CurrentStep = step;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddLog(string message)
    {
        _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        TrimLogs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddLogRaw(string message)
    {
        _logs.Add(message);
        TrimLogs();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimLogs()
    {
        if (_logs.Count > 1000)
        {
            _logs.RemoveRange(0, _logs.Count - 1000);
        }
    }

    public void Disconnect()
    {
        if (_sshSession != null)
        {
            try
            {
                if (_sshSession.IsConnected)
                {
                    _sshSession.Disconnect();
                }
            }
            finally
            {
                _sshSession.Dispose();
                _sshSession = null;
            }
        }
    }

    public void Dispose() => Disconnect();
}

internal readonly record struct ServerSetupCommandResult(string Output, string Error, int ExitStatus);

internal interface IServerSetupSshSession : IDisposable
{
    bool IsConnected { get; }
    ServerSetupCommandResult RunCommand(string command);
    void UploadFile(string remotePath, string content);
    void Disconnect();
}

internal interface IServerSetupSshSessionFactory
{
    IServerSetupSshSession Connect(ServerSetupConfig config);
}

internal sealed class RenciServerSetupSshSessionFactory : IServerSetupSshSessionFactory
{
    public IServerSetupSshSession Connect(ServerSetupConfig config)
    {
        AuthenticationMethod authMethod;
        if (config.UseKeyAuth)
        {
            if (string.IsNullOrWhiteSpace(config.SshKeyPath) || !File.Exists(config.SshKeyPath))
            {
                throw new FileNotFoundException("SSH key not found.", config.SshKeyPath);
            }

            authMethod = new PrivateKeyAuthenticationMethod(
                config.SshUsername,
                new PrivateKeyFile(config.SshKeyPath));
        }
        else
        {
            authMethod = new PasswordAuthenticationMethod(config.SshUsername, config.SshPassword);
        }

        var connectionInfo = new ConnectionInfo(config.Host, config.SshPort, config.SshUsername, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var sshClient = new SshClient(connectionInfo);
        try
        {
            sshClient.Connect();
            return new RenciServerSetupSshSession(sshClient, connectionInfo);
        }
        catch
        {
            sshClient.Dispose();
            throw;
        }
    }
}

internal sealed class RenciServerSetupSshSession : IServerSetupSshSession
{
    private readonly SshClient _sshClient;
    private readonly ConnectionInfo _connectionInfo;

    public RenciServerSetupSshSession(SshClient sshClient, ConnectionInfo connectionInfo)
    {
        _sshClient = sshClient;
        _connectionInfo = connectionInfo;
    }

    public bool IsConnected => _sshClient.IsConnected;

    public ServerSetupCommandResult RunCommand(string command)
    {
        var result = _sshClient.RunCommand(command);
        return new ServerSetupCommandResult(result.Result, result.Error, result.ExitStatus ?? -1);
    }

    public void UploadFile(string remotePath, string content)
    {
        using var sftp = new SftpClient(_connectionInfo);
        sftp.Connect();
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            sftp.UploadFile(stream, remotePath, canOverride: true);
        }
        finally
        {
            if (sftp.IsConnected)
            {
                sftp.Disconnect();
            }
        }
    }

    public void Disconnect()
    {
        _sshClient.Disconnect();
    }

    public void Dispose()
    {
        _sshClient.Dispose();
    }
}
