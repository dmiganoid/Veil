using System.Globalization;

namespace Veil.Services;

public sealed record GeoIpCountry(string Code, string Name)
{
    public string DisplayName => $"{Name} ({Code})";
}

public static class GeoIpCountryCatalog
{
    private static readonly IReadOnlyList<GeoIpCountry> Countries = BuildCountries();
    private static readonly HashSet<string> KnownCodes = Countries
        .Select(country => country.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<GeoIpCountry> GetCountries() => Countries;

    public static bool ContainsCode(string code) => KnownCodes.Contains(code);

    public static string DisplayNameForCode(string code)
    {
        var country = Countries.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        return country == null ? code.ToUpperInvariant() : country.DisplayName;
    }

    private static IReadOnlyList<GeoIpCountry> BuildCountries()
    {
        var byCode = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName.ToUpperInvariant();
                if (code.Length == 2 && code.All(char.IsAsciiLetter) && !byCode.ContainsKey(code))
                {
                    byCode[code] = region.EnglishName;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return byCode
            .Select(item => new GeoIpCountry(item.Key, item.Value))
            .OrderBy(country => country.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(country => country.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
