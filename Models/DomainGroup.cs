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

/// <summary>
/// Result of the related-domains dialog: either a group of the selected domains or the domain alone.
/// </summary>
public sealed record DomainDiscoveryChoice(bool CreateGroup, string GroupName, List<string> Domains);

/// <summary>
/// Domain routing rules edited on the Routing page.
/// Rules (groups and standalone entries) follow the routing mode: they bypass the VPN in General mode
/// and use it in Selective mode. Exceptions take the opposite route and override broader rules,
/// e.g. the rule <c>*.net</c> with the exception <c>example.net</c>.
/// </summary>
public sealed class DomainGroupsData
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<DomainGroup> Groups { get; set; } = [];
    public List<string> StandaloneDomains { get; set; } = [];
    public List<string> ExceptionDomains { get; set; } = [];

    public static DomainGroupsData FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Domain groups JSON root must be an object.");
        }

        return new DomainGroupsData
        {
            Version = JsonFields.StrictInt(json, "version", CurrentVersion),
            Groups = ReadGroups(json, "groups"),
            StandaloneDomains = JsonFields.StrictStringList(json, "standaloneDomains"),
            ExceptionDomains = JsonFields.StrictStringList(json, "exceptionDomains")
        };
    }

    public List<string> FlattenDomains()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new List<string>();

        foreach (var domain in Groups.SelectMany(group => group.Domains).Concat(StandaloneDomains).Select(CleanDomain))
        {
            if (domain.Length > 0 && seen.Add(domain))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    public List<string> FlattenExceptions()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ExceptionDomains
            .Select(CleanException)
            .Where(domain => domain.Length > 0 && seen.Add(domain))
            .ToList();
    }

    public DomainGroupsData NormalizeEntries()
    {
        var normalized = new DomainGroupsData
        {
            Version = CurrentVersion,
            ExceptionDomains = FlattenExceptions()
        };

        // A pattern cannot be a rule and an exception at once; the engine lets the exception win,
        // so drop the rule to keep the list truthful.
        var seen = new HashSet<string>(normalized.ExceptionDomains, StringComparer.OrdinalIgnoreCase);

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

    public bool ContainsException(string domain)
    {
        domain = CleanException(domain);
        return domain.Length > 0 &&
               ExceptionDomains.Any(item => CleanException(item).Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    public bool AddStandaloneDomain(string domain)
    {
        domain = CleanDomain(domain);
        if (domain.Length == 0 || ContainsDomain(domain))
        {
            return false;
        }

        RemoveException(domain);
        StandaloneDomains.Add(domain);
        return true;
    }

    public bool RemoveStandaloneDomain(string domain) =>
        StandaloneDomains.RemoveAll(item => CleanDomain(item).Equals(CleanDomain(domain), StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>
    /// Adds a domain exception. A standalone rule for the same pattern is replaced, because an exception
    /// and a rule for one pattern cannot both apply.
    /// </summary>
    public bool AddException(string domain)
    {
        domain = CleanException(domain);
        if (domain.Length == 0 || ContainsException(domain))
        {
            return false;
        }

        RemoveStandaloneDomain(domain);
        ExceptionDomains.Add(domain);
        return true;
    }

    public bool RemoveException(string domain) =>
        ExceptionDomains.RemoveAll(item => CleanException(item).Equals(CleanException(domain), StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>
    /// Returns the rules that an exception overrides. An exception without covering rules has no effect.
    /// </summary>
    public List<string> RulesOverriddenBy(string exception)
    {
        exception = CleanException(exception);
        return FlattenDomains()
            .Where(rule => !rule.Equals(exception, StringComparison.OrdinalIgnoreCase) &&
                           SplitTunnelEntry.Covers(rule, exception))
            .ToList();
    }

    public bool AddDomainToGroup(DomainGroup group, string domain)
    {
        var trackedGroup = FindTrackedGroup(group);
        domain = CleanDomain(domain);
        if (trackedGroup == null || domain.Length == 0 || ContainsDomain(domain))
        {
            return false;
        }

        RemoveException(domain);
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

    /// <summary>
    /// Applies the user's choice from the related-domains dialog. Returns false when the user cancelled.
    /// </summary>
    public bool ApplyDiscoveryChoice(string domain, DomainDiscoveryChoice? choice)
    {
        if (choice == null)
        {
            return false;
        }

        if (choice.CreateGroup)
        {
            AddDiscoveryResult(domain, true, choice.GroupName, choice.Domains);
        }
        else
        {
            AddStandaloneDomain(domain);
        }

        return true;
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

        foreach (var domain in groupDomains)
        {
            RemoveException(domain);
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

    public bool RemoveGroup(DomainGroup group)
    {
        var trackedGroup = FindTrackedGroup(group);
        return trackedGroup != null && Groups.Remove(trackedGroup);
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

    public DomainGroup? FindTrackedGroup(DomainGroup group) =>
        Groups.FirstOrDefault(candidate =>
            ReferenceEquals(candidate, group) ||
            (!string.IsNullOrWhiteSpace(candidate.Id) &&
             candidate.Id.Equals(group.Id, StringComparison.OrdinalIgnoreCase)));

    private static string CleanDomain(string domain) => SplitTunnelEntry.Normalize(domain);

    /// <summary>Normalized exception pattern, or "" for entries the engine cannot use as exceptions.</summary>
    private static string CleanException(string domain) =>
        SplitTunnelEntry.TryNormalizeException(domain, out var exception) ? exception : "";

    private static string PrimaryDomainFromDomains(string primaryDomain, IReadOnlyList<string> domains) =>
        primaryDomain.Length > 0 && domains.Contains(primaryDomain, StringComparer.OrdinalIgnoreCase)
            ? primaryDomain
            : domains[0];

    private static List<DomainGroup> ReadGroups(JsonElement json, string name)
    {
        if (!JsonFields.TryGet(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
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
            Id = JsonFields.RequiredString(json, "id"),
            Name = JsonFields.RequiredString(json, "name"),
            PrimaryDomain = JsonFields.RequiredString(json, "primaryDomain"),
            Domains = JsonFields.StrictStringList(json, "domains")
        };
    }
}
