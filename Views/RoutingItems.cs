using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veil.Models;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

public enum RuleItemKind
{
    Rule,
    Exception,
    Group
}

/// <summary>
/// A row in the Domains &amp; IPs list: a standalone rule, an exception or a site group.
/// </summary>
public sealed class RuleItem : ObservableObject
{
    private bool _isExpanded;

    public required RuleItemKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required bool IsBypass { get; init; }
    public string Pattern { get; init; } = "";
    public DomainGroup? Group { get; init; }
    public bool HasWarning { get; init; }
    public bool CanSwitchRoute { get; init; }
    public IReadOnlyList<GroupDomainItem> GroupDomains { get; set; } = [];

    public bool IsGroup => Kind == RuleItemKind.Group;
    public string BadgeText => IsBypass ? "BYPASS" : "VPN";
    public string SwitchToolTip => IsBypass ? "Send through the VPN instead" : "Bypass the VPN instead";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public static string DescribePattern(string pattern) => SplitTunnelEntry.Classify(pattern) switch
    {
        SplitTunnelEntryKind.Domain => $"{pattern} and www.{pattern}",
        SplitTunnelEntryKind.WildcardDomain when !pattern[SplitTunnelEntry.WildcardPrefix.Length..].Contains('.') =>
            $"Every .{pattern[SplitTunnelEntry.WildcardPrefix.Length..]} domain",
        SplitTunnelEntryKind.WildcardDomain => $"Every subdomain of {pattern[SplitTunnelEntry.WildcardPrefix.Length..]}",
        SplitTunnelEntryKind.IpAddress => "IP address",
        SplitTunnelEntryKind.Cidr => "IP range",
        SplitTunnelEntryKind.Port => "Any address on this port",
        _ => "Unrecognized entry, ignored by the engine"
    };
}

public sealed class GroupDomainItem(RuleItem owner, string domain)
{
    public RuleItem Owner { get; } = owner;
    public string Domain { get; } = domain;
}

/// <summary>
/// A selectable row in the Apps or Countries list.
/// </summary>
public abstract class SelectableItem : ObservableObject
{
    private bool _isSelected;

    public event EventHandler? SelectionChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Sets the value without notifying the page (used when rebuilding lists).</summary>
    public void Load(bool selected)
    {
        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }
}

public sealed class AppItem(InstalledApp app, bool isManual) : SelectableItem
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _iconLoaded;
    private ImageSource? _icon;

    public string Name { get; } = string.IsNullOrWhiteSpace(app.DisplayName) ? app.ExecutableName : app.DisplayName;
    public string ExecutableName { get; } = app.ExecutableName;
    public string Location { get; } = isManual ? "Added manually" : app.Path;
    public bool IsManual { get; } = isManual;

    /// <summary>Loaded lazily so that only rows the list actually shows pay for icon extraction.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = LoadIcon(app.Path);
            }

            return _icon;
        }
    }

    public bool HasIcon => Icon != null;

    public bool Matches(string query) =>
        query.Length == 0 ||
        Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        ExecutableName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static ImageSource? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (IconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        ImageSource? icon = null;
        try
        {
            using var systemIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (systemIcon != null)
            {
                icon = Imaging.CreateBitmapSourceFromHIcon(
                    systemIcon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                icon.Freeze();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception)
        {
        }

        IconCache[path] = icon;
        return icon;
    }
}

public sealed class CountryItem(GeoIpCountry country) : SelectableItem
{
    public string Code { get; } = country.Code;
    public string Name { get; } = country.Name;

    public bool Matches(string query) =>
        query.Length == 0 ||
        Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Code.Contains(query, StringComparison.OrdinalIgnoreCase);
}
