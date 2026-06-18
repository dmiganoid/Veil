using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace Veil.Services;

public sealed record GeoIpResolutionResult(
    IReadOnlyList<string> Cidrs,
    IReadOnlyList<string> LoadedCountries,
    IReadOnlyList<string> FailedCountries,
    bool UsedCache);

public sealed class GeoIpService : IDisposable
{
    private const string Ipv4UrlTemplate = "https://www.ipdeny.com/ipblocks/data/aggregated/{0}-aggregated.zone";
    private const string Ipv6UrlTemplate = "https://www.ipdeny.com/ipv6/ipaddresses/aggregated/{0}-aggregated.zone";

    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public GeoIpService(string appDataDirectory)
        : this(
            Path.Combine(appDataDirectory, "geoip-cache"),
            new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
            disposeHttpClient: true)
    {
    }

    internal GeoIpService(string cacheDirectory, HttpClient httpClient)
        : this(cacheDirectory, httpClient, disposeHttpClient: false)
    {
    }

    private GeoIpService(string cacheDirectory, HttpClient httpClient, bool disposeHttpClient)
    {
        _cacheDirectory = cacheDirectory;
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Veil/1.0");
        }
    }

    public async Task<GeoIpResolutionResult> ResolveCountryCidrsAsync(
        IEnumerable<string> countryCodes,
        CancellationToken cancellationToken = default)
    {
        var normalizedCodes = ConfigService.NormalizeSplitTunnelCountries(countryCodes);
        if (normalizedCodes.Count == 0)
        {
            return new GeoIpResolutionResult([], [], [], UsedCache: false);
        }

        Directory.CreateDirectory(_cacheDirectory);

        var cidrs = new List<string>();
        var seenCidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedCountries = new List<string>();
        var failedCountries = new List<string>();
        var usedCache = false;

        foreach (var countryCode in normalizedCodes)
        {
            var countryCidrs = new List<string>();
            var ipv4 = await LoadCountryRangesAsync(countryCode, "ipv4", Ipv4UrlTemplate, cancellationToken);
            var ipv6 = await LoadCountryRangesAsync(countryCode, "ipv6", Ipv6UrlTemplate, cancellationToken);

            usedCache = usedCache || ipv4.UsedCache || ipv6.UsedCache;
            countryCidrs.AddRange(ipv4.Cidrs);
            countryCidrs.AddRange(ipv6.Cidrs);

            if (countryCidrs.Count == 0)
            {
                failedCountries.Add(countryCode);
                continue;
            }

            loadedCountries.Add(countryCode);
            foreach (var cidr in countryCidrs)
            {
                if (seenCidrs.Add(cidr))
                {
                    cidrs.Add(cidr);
                }
            }
        }

        return new GeoIpResolutionResult(cidrs, loadedCountries, failedCountries, usedCache);
    }

    private async Task<CountryRangeLoad> LoadCountryRangesAsync(
        string countryCode,
        string addressFamily,
        string urlTemplate,
        CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(_cacheDirectory, $"{countryCode.ToLowerInvariant()}-{addressFamily}.zone");
        var url = string.Format(urlTemplate, countryCode.ToLowerInvariant());

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var downloaded = NormalizeCidrLines(content);
                if (downloaded.Count > 0)
                {
                    await WriteCacheAsync(cachePath, downloaded, cancellationToken);
                    return new CountryRangeLoad(downloaded, UsedCache: false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
        }
        catch (IOException)
        {
        }

        var cached = await ReadCacheAsync(cachePath, cancellationToken);
        return new CountryRangeLoad(cached, UsedCache: cached.Count > 0);
    }

    private static List<string> NormalizeCidrLines(string content)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cidrs = new List<string>();
        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !IsIpOrCidr(line))
            {
                continue;
            }

            var normalized = SplitTunnelEntry.Normalize(line);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                cidrs.Add(normalized);
            }
        }

        return cidrs;
    }

    private static bool IsIpOrCidr(string value)
    {
        var slashIndex = value.IndexOf('/');
        if (slashIndex < 0)
        {
            return IPAddress.TryParse(value, out _);
        }

        var addressText = value[..slashIndex];
        var prefixText = value[(slashIndex + 1)..];
        if (!IPAddress.TryParse(addressText, out var address) ||
            !int.TryParse(prefixText, out var prefixLength))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => prefixLength is >= 0 and <= 32,
            AddressFamily.InterNetworkV6 => prefixLength is >= 0 and <= 128,
            _ => false
        };
    }

    private static async Task<List<string>> ReadCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return [];
            }

            var content = await File.ReadAllTextAsync(cachePath, cancellationToken);
            return NormalizeCidrLines(content);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static async Task WriteCacheAsync(
        string cachePath,
        IEnumerable<string> cidrs,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(cachePath, cidrs, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record CountryRangeLoad(IReadOnlyList<string> Cidrs, bool UsedCache);
}
