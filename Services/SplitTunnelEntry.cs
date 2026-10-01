using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Veil.Services;

public enum SplitTunnelEntryKind
{
    Invalid,
    Domain,
    WildcardDomain,
    IpAddress,
    Cidr,
    Port
}

/// <summary>
/// Normalizes and validates the split-tunnel entries that end up in the engine's `exclusions` list.
/// </summary>
public static class SplitTunnelEntry
{
    public const string WildcardPrefix = "*.";
    public const string ExceptionPrefix = "!";

    private static readonly IdnMapping Idn = new();

    private static readonly Regex Ipv4CidrRegex = new(
        @"^(?<ip>(?:\d{1,3}\.){3}\d{1,3})/(?<prefix>\d{1,2})$",
        RegexOptions.Compiled);

    private static readonly Regex Ipv6CidrRegex = new(
        @"^(?<ip>[0-9a-fA-F:]+)/(?<prefix>\d{1,3})$",
        RegexOptions.Compiled);

    private static readonly Regex DomainRegex = new(
        @"^(?:[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?\.)*[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Best-effort cleanup of user text: extracts URL hosts, strips paths and lowercases.
    /// It never rejects input; use <see cref="TryNormalizeRule"/> to validate.
    /// </summary>
    public static string Normalize(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (!value.StartsWith(WildcardPrefix, StringComparison.Ordinal) &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            !uri.IsFile)
        {
            return CanonicalDomain(ToAsciiDomain(uri.Host.ToLowerInvariant()));
        }

        value = value.TrimEnd('/');
        if (IsCidr(value))
        {
            return value.ToLowerInvariant();
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
        {
            // "203.0.113.0/33" is a broken CIDR, not an address followed by a URL path; keep it so it fails validation.
            if (IPAddress.TryParse(value[..slashIndex], out _) && value[(slashIndex + 1)..].All(char.IsAsciiDigit))
            {
                return value.ToLowerInvariant();
            }

            value = value[..slashIndex];
        }

        return CanonicalDomain(ToAsciiDomain(value.Trim().TrimEnd('.').ToLowerInvariant()));
    }

    /// <summary>
    /// The engine treats <c>www.example.com</c> exactly like <c>example.com</c> (both match the domain and
    /// its www. name), so Veil stores the shorter form to detect duplicates and overlaps correctly.
    /// </summary>
    private static string CanonicalDomain(string value)
    {
        const string www = "www.";
        return value.StartsWith(www, StringComparison.Ordinal) &&
               value.IndexOf('.', www.Length) > 0 &&
               !IPAddress.TryParse(value, out _)
            ? value[www.Length..]
            : value;
    }

    /// <summary>
    /// Validates a routing rule: a domain, a <c>*.domain</c> wildcard, an IP address (optionally with port),
    /// a CIDR range or a <c>*:port</c> entry.
    /// </summary>
    public static bool TryNormalizeRule(string input, out string normalized, out SplitTunnelEntryKind kind)
    {
        normalized = Normalize(input);
        kind = Classify(normalized);
        if (kind == SplitTunnelEntryKind.Invalid)
        {
            normalized = "";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates a routing exception. The engine supports exceptions for domain patterns only.
    /// </summary>
    public static bool TryNormalizeException(string input, out string normalized)
    {
        var value = input.Trim();
        if (value.StartsWith(ExceptionPrefix, StringComparison.Ordinal))
        {
            value = value[ExceptionPrefix.Length..];
        }

        if (TryNormalizeRule(value, out normalized, out var kind) && IsDomainKind(kind))
        {
            return true;
        }

        normalized = "";
        return false;
    }

    public static SplitTunnelEntryKind Classify(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(char.IsWhiteSpace))
        {
            return SplitTunnelEntryKind.Invalid;
        }

        if (IsCidr(normalized))
        {
            return SplitTunnelEntryKind.Cidr;
        }

        if (IsIpAddressWithOptionalPort(normalized))
        {
            return SplitTunnelEntryKind.IpAddress;
        }

        if (normalized.StartsWith("*:", StringComparison.Ordinal))
        {
            return int.TryParse(normalized[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                   port is >= 1 and <= 65535
                ? SplitTunnelEntryKind.Port
                : SplitTunnelEntryKind.Invalid;
        }

        if (normalized.StartsWith(WildcardPrefix, StringComparison.Ordinal))
        {
            return IsDomainName(normalized[WildcardPrefix.Length..])
                ? SplitTunnelEntryKind.WildcardDomain
                : SplitTunnelEntryKind.Invalid;
        }

        return IsDomainName(normalized) ? SplitTunnelEntryKind.Domain : SplitTunnelEntryKind.Invalid;
    }

    public static bool IsDomainKind(SplitTunnelEntryKind kind) =>
        kind is SplitTunnelEntryKind.Domain or SplitTunnelEntryKind.WildcardDomain;

    public static bool ShouldDiscoverRelatedDomains(string value) =>
        Classify(Normalize(value)) == SplitTunnelEntryKind.Domain;

    /// <summary>
    /// Returns true when <paramref name="rule"/> matches every name matched by <paramref name="pattern"/>,
    /// using the engine semantics: <c>example.com</c> matches the domain and <c>www.</c> + domain,
    /// <c>*.example.com</c> matches subdomains only.
    /// </summary>
    public static bool Covers(string rule, string pattern)
    {
        if (rule.Equals(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!rule.StartsWith(WildcardPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = "." + rule[WildcardPrefix.Length..];
        var name = pattern.StartsWith(WildcardPrefix, StringComparison.Ordinal)
            ? pattern[WildcardPrefix.Length..]
            : pattern;
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a user-entered executable name, path or command line into the process name the engine matches.
    /// </summary>
    public static string NormalizeAppProcessName(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return "";
        }

        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                value = value[1..closingQuote];
            }
        }

        value = value.Trim().Trim('"');
        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            value = value[..(exeIndex + 4)];
        }

        try
        {
            var fileName = Path.GetFileName(value);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                value = fileName;
            }
        }
        catch
        {
            // Keep the raw text if it is not a valid path.
        }

        value = value.Trim();
        if (value.Length == 0 || value.Any(char.IsControl))
        {
            return "";
        }

        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}.exe";
    }

    // Top-level domains are never numeric, so "10" or "300.1.1.1" is a mistyped address, not a domain.
    private static bool IsDomainName(string value) =>
        value.Length is > 0 and <= 253 &&
        DomainRegex.IsMatch(value) &&
        !value[(value.LastIndexOf('.') + 1)..].All(char.IsAsciiDigit);

    private static string ToAsciiDomain(string value)
    {
        if (value.All(char.IsAscii))
        {
            return value;
        }

        var wildcard = value.StartsWith(WildcardPrefix, StringComparison.Ordinal);
        var name = wildcard ? value[WildcardPrefix.Length..] : value;
        try
        {
            var ascii = Idn.GetAscii(name);
            return wildcard ? WildcardPrefix + ascii : ascii;
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    private static bool IsIpAddressWithOptionalPort(string value)
    {
        if (IPAddress.TryParse(value, out var address))
        {
            return IsCanonicalAddressText(value, address);
        }

        if (!IPEndPoint.TryParse(value, out var endpoint) || endpoint.Port is < 1 or > 65535)
        {
            return false;
        }

        if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPEndPoint also accepts bare IPv6 text where the last group looks like a port; require brackets.
            return value.StartsWith('[');
        }

        return IsCanonicalAddressText(value[..value.LastIndexOf(':')], endpoint.Address);
    }

    // IPAddress.TryParse also accepts "10", "1.2", "010.0.0.1" or "0x7f.0.0.1" and silently reinterprets them;
    // only the canonical dotted quad means the same thing to the user and to the engine.
    private static bool IsCanonicalAddressText(string text, IPAddress address) =>
        address.AddressFamily != AddressFamily.InterNetwork || address.ToString() == text;

    private static bool IsCidr(string value) => IsIpv4Cidr(value) || IsIpv6Cidr(value);

    private static bool IsIpv4Cidr(string value)
    {
        var match = Ipv4CidrRegex.Match(value);
        if (!match.Success ||
            !IPAddress.TryParse(match.Groups["ip"].Value, out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return int.TryParse(match.Groups["prefix"].Value, out var prefix) && prefix is >= 0 and <= 32;
    }

    private static bool IsIpv6Cidr(string value)
    {
        var match = Ipv6CidrRegex.Match(value);
        if (!match.Success ||
            !IPAddress.TryParse(match.Groups["ip"].Value, out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return int.TryParse(match.Groups["prefix"].Value, out var prefix) && prefix is >= 0 and <= 128;
    }
}
