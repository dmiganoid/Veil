using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Veil.Services;

public sealed class DomainDiscoveryResult
{
    public List<string> DiscoveredDomains { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class DomainDiscoveryService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const int MaxBodyBytes = 512 * 1024;
    private readonly Func<string, Task<string?>> _fetchPageAsync;

    private static readonly HashSet<string> IgnoredDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "w3.org",
        "schema.org",
        "xmlns.com",
        "purl.org",
        "ogp.me",
        "creativecommons.org",
        "google-analytics.com",
        "googletagmanager.com",
        "doubleclick.net",
        "facebook.com",
        "facebook.net",
        "twitter.com",
        "x.com",
        "linkedin.com",
        "reddit.com",
        "pinterest.com",
        "instagram.com",
        "googlesyndication.com",
        "googleadservices.com",
        "adroll.com",
        "taboola.com",
        "outbrain.com",
        "cloudflare.com",
        "cloudflareinsights.com",
        "akamaihd.net",
        "fastly.net",
        "fonts.googleapis.com",
        "fonts.gstatic.com",
        "typekit.net"
    };

    public DomainDiscoveryService()
        : this(FetchPageAsync)
    {
    }

    internal DomainDiscoveryService(Func<string, Task<string?>> fetchPageAsync)
    {
        _fetchPageAsync = fetchPageAsync;
    }

    public async Task<DomainDiscoveryResult> DiscoverRelatedDomainsAsync(string domain)
    {
        try
        {
            domain = SplitTunnelEntry.Normalize(domain);
            if (string.IsNullOrWhiteSpace(domain))
            {
                return new DomainDiscoveryResult
                {
                    Error = "Domain is required."
                };
            }

            string? html;
            try
            {
                html = await _fetchPageAsync(domain);
            }
            catch
            {
                html = null;
            }

            if (html == null)
            {
                return new DomainDiscoveryResult
                {
                    Error = "Failed to load page."
                };
            }

            return new DomainDiscoveryResult
            {
                DiscoveredDomains = ExtractRelatedDomainsFromHtml(html, domain)
            };
        }
        catch (Exception ex)
        {
            return new DomainDiscoveryResult
            {
                Error = ex.Message
            };
        }
    }

    private static async Task<string?> FetchPageAsync(string domain)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var client = new HttpClient(handler)
        {
            Timeout = Timeout,
            MaxResponseContentBufferSize = MaxBodyBytes
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        using var response = await client.GetAsync($"https://{domain}", HttpCompletionOption.ResponseHeadersRead);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType != null && !contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            var remaining = MaxBodyBytes - (int)ms.Length;
            if (remaining <= 0)
            {
                break;
            }

            await ms.WriteAsync(buffer.AsMemory(0, Math.Min(read, remaining)));
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static List<string> ExtractRelatedDomainsFromHtml(string html, string rootDomainOrHost)
    {
        var normalizedRoot = SplitTunnelEntry.Normalize(rootDomainOrHost);
        var rootDomain = ExtractRootDomain(normalizedRoot) ?? normalizedRoot;
        var discovered = ExtractAllRelatedDomains(html, rootDomain).ToList();
        discovered.Sort(StringComparer.OrdinalIgnoreCase);
        return discovered;
    }

    private static IEnumerable<string> ExtractUrls(string html)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMatches(urls, html, "(?:src|href)\\s*=\\s*[\"']([^\"']+)[\"']");
        AddSrcSetMatches(urls, html);
        AddMatches(urls, html, "[\"'`](https?://[^\"'`\\s]+)[\"'`]");
        AddMatches(urls, html, "[\"'`](https?:(?:\\\\/|\\\\u002F){2}[^\"'`\\s]+)[\"'`]");
        AddMatches(urls, html, "[\"'`](//[a-zA-Z0-9][a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}[^\"'`\\s]*)[\"'`]");
        AddMatches(urls, html, "(?:fetch|axios(?:\\.[a-z]+)?|XMLHttpRequest)\\s*\\(\\s*[\"'`]([^\"'`]+)[\"'`]");

        var domainPattern = new Regex("(?:[\"'`:/]|^)([a-zA-Z0-9][-a-zA-Z0-9]{0,62}(?:\\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62})+\\.[a-zA-Z]{2,})(?:[\"'`:/\\s]|$)");
        foreach (Match match in domainPattern.Matches(html))
        {
            var domain = match.Groups[1].Value;
            if (!urls.Any(url => url.Contains(domain, StringComparison.OrdinalIgnoreCase)))
            {
                AddUrlCandidate(urls, $"https://{domain}");
            }
        }

        return urls;
    }

    private static void AddMatches(HashSet<string> urls, string html, string pattern)
    {
        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            var url = match.Groups[1].Value;
            AddUrlCandidate(urls, url);
        }
    }

    private static void AddSrcSetMatches(HashSet<string> urls, string html)
    {
        foreach (Match match in Regex.Matches(html, "srcset\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            foreach (var candidate in match.Groups[1].Value.Split(','))
            {
                var url = candidate.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    AddUrlCandidate(urls, url);
                }
            }
        }
    }

    private static void AddUrlCandidate(HashSet<string> urls, string url)
    {
        url = CleanUrlCandidate(url);
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("//", StringComparison.Ordinal))
        {
            urls.Add(url);
        }
    }

    private static string CleanUrlCandidate(string value)
    {
        return WebUtility.HtmlDecode(value.Trim())
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
            .TrimEnd(',', ';', ')', ']', '}');
    }

    private static string? ExtractHostFromUrl(string url)
    {
        try
        {
            var normalized = CleanUrlCandidate(url);
            normalized = normalized.StartsWith("//", StringComparison.Ordinal) ? $"https:{normalized}" : normalized;
            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                ? uri.Host.ToLowerInvariant()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractRootDomain(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var twoPartTlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "co.uk",
            "co.jp",
            "co.kr",
            "co.nz",
            "co.za",
            "com.au",
            "com.br",
            "com.cn",
            "com.tw",
            "com.ua",
            "org.uk",
            "net.au",
            "ac.uk"
        };

        if (parts.Length >= 3)
        {
            var lastTwo = $"{parts[^2]}.{parts[^1]}";
            if (twoPartTlds.Contains(lastTwo))
            {
                return parts.Length >= 4 ? $"{parts[^3]}.{lastTwo}" : host;
            }
        }

        return $"{parts[^2]}.{parts[^1]}";
    }

    private static HashSet<string> ExtractAllRelatedDomains(string html, string rootDomain)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in ExtractUrls(html))
        {
            var host = ExtractHostFromUrl(url);
            if (host == null)
            {
                continue;
            }

            var root = ExtractRootDomain(host);
            if (root == null ||
                root.Equals(rootDomain, StringComparison.OrdinalIgnoreCase) ||
                IsIgnoredDomain(host, root))
            {
                continue;
            }

            domains.Add(root);
            if (!host.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                host.EndsWith($".{root}", StringComparison.OrdinalIgnoreCase))
            {
                domains.Add(host);
            }
        }

        return domains;
    }

    private static bool IsIgnoredDomain(string host, string root)
    {
        if (IgnoredDomains.Contains(root))
        {
            return true;
        }

        return IgnoredDomains.Any(ignored =>
            host.Equals(ignored, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{ignored}", StringComparison.OrdinalIgnoreCase));
    }
}
