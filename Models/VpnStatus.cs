namespace Veil.Models;

public enum VpnStatus
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public static class VpnStatusExtensions
{
    public static bool IsActive(this VpnStatus status) =>
        status is VpnStatus.Connecting or VpnStatus.Connected or VpnStatus.Disconnecting;

    public static string DisplayText(this VpnStatus status) => status switch
    {
        VpnStatus.Disconnected => "Disconnected",
        VpnStatus.Connecting => "Connecting...",
        VpnStatus.Connected => "Connected",
        VpnStatus.Disconnecting => "Disconnecting...",
        VpnStatus.Error => "Error",
        _ => "Unknown"
    };
}
