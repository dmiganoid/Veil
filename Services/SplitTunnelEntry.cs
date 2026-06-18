using System.Net;
using System.Text.RegularExpressions;

namespace Veil.Services;

public static class SplitTunnelEntry
{
    private static readonly Regex Ipv4CidrRegex = new(
        @"^(?<ip>(?:\d{1,3}\.){3}\d{1,3})/(?<prefix>\d{1,2})$",
        RegexOptions.Compiled);

    private static readonly Regex Ipv6CidrRegex = new(
        @"^(?<ip>[0-9a-fA-F:]+)/(?<prefix>\d{1,3})$",
        RegexOptions.Compiled);

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.ToLowerInvariant();
        }

        value = value.TrimEnd('/');
        if (IsCidr(value))
        {
            return value.ToLowerInvariant();
        }

        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
        {
            value = value[..slashIndex];
        }

        return value.Trim().ToLowerInvariant();
    }

    public static bool ShouldDiscoverRelatedDomains(string value)
    {
        value = Normalize(value);
        return value.Length > 0 &&
               !IsIpAddress(value) &&
               !IsCidr(value) &&
               !value.Contains('/', StringComparison.Ordinal);
    }

    private static bool IsIpAddress(string value) => IPAddress.TryParse(value, out _);

    private static bool IsCidr(string value) => IsIpv4Cidr(value) || IsIpv6Cidr(value);

    private static bool IsIpv4Cidr(string value)
    {
        var match = Ipv4CidrRegex.Match(value);
        if (!match.Success ||
            !IPAddress.TryParse(match.Groups["ip"].Value, out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return int.TryParse(match.Groups["prefix"].Value, out var prefix) && prefix is >= 0 and <= 128;
    }
}
