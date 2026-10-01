namespace Veil.Models;

public enum CloseAction
{
    Ask,
    MinimizeToTray,
    Exit
}

/// <summary>
/// Window behaviour preferences, stored separately from the VPN configuration.
/// </summary>
public sealed class AppPreferences
{
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;

    /// <summary>Set once the leftovers of the removed TV Gateway feature have been checked for.</summary>
    public bool LegacyTvGatewayChecked { get; set; }
}
