using System.Text.RegularExpressions;

namespace Veil.Services;

public sealed class SplitTunnelSuggestionService
{
    private readonly Regex _domainRegex = new(@"\b([a-zA-Z0-9][-a-zA-Z0-9]{0,62}\.)+[a-zA-Z]{2,}\b", RegexOptions.Compiled);

    public List<string> ExtractSuggestions(
        string line,
        IEnumerable<string> existingDomains,
        IEnumerable<string> currentSuggestions,
        IEnumerable<string> hiddenSuggestions,
        string endpointHostname,
        int maxSuggestions = 20)
    {
        var existing = new HashSet<string>(existingDomains.Select(NormalizeDomain), StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(currentSuggestions.Select(NormalizeDomain), StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(hiddenSuggestions.Select(NormalizeDomain), StringComparer.OrdinalIgnoreCase);
        var visibleCurrentCount = current.Count(domain => !hidden.Contains(domain));
        var result = new List<string>();

        foreach (Match match in _domainRegex.Matches(line))
        {
            if (visibleCurrentCount + result.Count >= maxSuggestions)
            {
                break;
            }

            var domain = NormalizeDomain(match.Value);
            if (domain.Length == 0 ||
                existing.Contains(domain) ||
                current.Contains(domain) ||
                hidden.Contains(domain) ||
                result.Contains(domain, StringComparer.OrdinalIgnoreCase) ||
                domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                domain.Equals("trusttunnel.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(domain);
        }

        return result;
    }

    public static string NormalizeDomain(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            value = uri.Host;
        }

        value = value.Trim().Trim('/');
        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
        {
            value = value[..slashIndex];
        }

        return value.ToLowerInvariant();
    }
}
