using System.Text.Json;
using Veil.Services;

namespace Veil.Models;

public sealed class DomainGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string PrimaryDomain { get; set; } = "";
    public List<string> Domains { get; set; } = [];

    public override string ToString() => $"{Name} ({Domains.Count})";
}

public sealed class DomainGroupsData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<DomainGroup> Groups { get; set; } = [];
    public List<string> StandaloneDomains { get; set; } = [];

    public static DomainGroupsData FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Domain groups JSON root must be an object.");
        }

        return new DomainGroupsData
        {
            Version = GetInt(json, "version", CurrentVersion),
            Groups = GetGroups(json, "groups"),
            StandaloneDomains = GetStringList(json, "standaloneDomains")
        };
    }

    public List<string> FlattenDomains()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new List<string>();

        foreach (var group in Groups)
        {
            foreach (var domain in group.Domains.Select(CleanDomain))
            {
                if (domain.Length > 0 && seen.Add(domain))
                {
                    domains.Add(domain);
                }
            }
        }

        foreach (var domain in StandaloneDomains.Select(CleanDomain))
        {
            if (domain.Length > 0 && seen.Add(domain))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    public DomainGroupsData NormalizeEntries()
    {
        var normalized = new DomainGroupsData
        {
            Version = Version
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in Groups)
        {
            var primaryDomain = CleanDomain(group.PrimaryDomain);
            var domains = new[] { primaryDomain }
                .Concat(group.Domains)
                .Select(CleanDomain)
                .Where(domain => domain.Length > 0 && seen.Add(domain))
                .ToList();

            if (domains.Count == 0)
            {
                continue;
            }

            var effectivePrimaryDomain = PrimaryDomainFromDomains(primaryDomain, domains);
            normalized.Groups.Add(new DomainGroup
            {
                Id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id,
                Name = string.IsNullOrWhiteSpace(group.Name) ? effectivePrimaryDomain : group.Name.Trim(),
                PrimaryDomain = effectivePrimaryDomain,
                Domains = domains
            });
        }

        foreach (var domain in StandaloneDomains.Select(CleanDomain))
        {
            if (domain.Length > 0 && seen.Add(domain))
            {
                normalized.StandaloneDomains.Add(domain);
            }
        }

        return normalized;
    }

    public bool ContainsDomain(string domain)
    {
        domain = CleanDomain(domain);
        return domain.Length > 0 &&
               FlattenDomains().Any(item => item.Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    public bool AddStandaloneDomain(string domain)
    {
        domain = CleanDomain(domain);
        if (domain.Length == 0 || ContainsDomain(domain))
        {
            return false;
        }

        StandaloneDomains.Add(domain);
        return true;
    }

    public bool AddDomainToGroup(DomainGroup group, string domain)
    {
        var trackedGroup = FindTrackedGroup(group);
        domain = CleanDomain(domain);
        if (trackedGroup == null || domain.Length == 0 || ContainsDomain(domain))
        {
            return false;
        }

        trackedGroup.Domains.Add(domain);
        return true;
    }

    public bool AddDiscoveryResult(
        string primaryDomain,
        bool createGroup,
        string groupName,
        IEnumerable<string> selectedDomains)
    {
        var normalizedPrimary = CleanDomain(primaryDomain);
        var selected = selectedDomains
            .Select(CleanDomain)
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (createGroup && selected.Count > 0)
        {
            return AddGroup(
                string.IsNullOrWhiteSpace(groupName) ? normalizedPrimary : groupName,
                normalizedPrimary,
                selected) != null;
        }

        return AddStandaloneDomain(normalizedPrimary);
    }

    public DomainGroup? AddGroup(string name, string primaryDomain, IEnumerable<string> domains)
    {
        var seen = new HashSet<string>(FlattenDomains(), StringComparer.OrdinalIgnoreCase);
        var normalizedPrimaryDomain = CleanDomain(primaryDomain);
        var groupDomains = new List<string>();
        foreach (var domain in new[] { normalizedPrimaryDomain }.Concat(domains).Select(CleanDomain))
        {
            if (domain.Length > 0 && seen.Add(domain))
            {
                groupDomains.Add(domain);
            }
        }

        if (groupDomains.Count == 0)
        {
            return null;
        }

        var effectivePrimaryDomain = PrimaryDomainFromDomains(normalizedPrimaryDomain, groupDomains);
        var group = new DomainGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? effectivePrimaryDomain : name.Trim(),
            PrimaryDomain = effectivePrimaryDomain,
            Domains = groupDomains
        };
        Groups.Add(group);
        return group;
    }

    public bool RemoveDomainFromGroup(DomainGroup group, string domain)
    {
        var trackedGroup = FindTrackedGroup(group);
        if (trackedGroup == null)
        {
            return false;
        }

        var removed = trackedGroup.Domains.RemoveAll(item =>
            item.Equals(domain, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed && trackedGroup.Domains.Count == 0)
        {
            Groups.Remove(trackedGroup);
        }

        return removed;
    }

    private DomainGroup? FindTrackedGroup(DomainGroup group) =>
        Groups.FirstOrDefault(candidate =>
            ReferenceEquals(candidate, group) ||
            (!string.IsNullOrWhiteSpace(candidate.Id) &&
             candidate.Id.Equals(group.Id, StringComparison.OrdinalIgnoreCase)));

    private static string CleanDomain(string domain) => SplitTunnelEntry.Normalize(domain);

    private static string PrimaryDomainFromDomains(string primaryDomain, IReadOnlyList<string> domains) =>
        primaryDomain.Length > 0 && domains.Contains(primaryDomain, StringComparer.OrdinalIgnoreCase)
            ? primaryDomain
            : domains[0];

    private static List<DomainGroup> GetGroups(JsonElement json, string name)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Domain groups field must be an array.");
        }

        return value.EnumerateArray()
            .Select(ReadGroup)
            .ToList();
    }

    private static DomainGroup ReadGroup(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Domain group must be an object.");
        }

        return new DomainGroup
        {
            Id = GetRequiredString(json, "id"),
            Name = GetRequiredString(json, "name"),
            PrimaryDomain = GetRequiredString(json, "primaryDomain"),
            Domains = GetStringList(json, "domains")
        };
    }

    private static int GetInt(JsonElement json, string name, int fallback)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{name} must be a number.");
    }

    private static string GetRequiredString(JsonElement json, string name)
    {
        return TryGetProperty(json, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new InvalidDataException($"{name} must be a string.");
    }

    private static List<string> GetStringList(JsonElement json, string name)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{name} must be an array.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{name} entries must be strings.");
            }

            result.Add(item.GetString() ?? "");
        }

        return result;
    }

    private static bool TryGetProperty(JsonElement json, string name, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
