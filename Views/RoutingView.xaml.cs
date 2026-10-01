using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Veil.Dialogs;
using Veil.Models;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

/// <summary>
/// Split-tunnel editor. Rules follow the routing mode (they bypass the VPN in "Everything through VPN"
/// and use it in "Only selected through VPN"); exceptions take the opposite route.
/// </summary>
public partial class RoutingView : UserControl
{
    private readonly AppServices _services;
    private readonly ConnectionController _connection;
    private readonly Action<AppPage> _navigate;
    private readonly HashSet<string> _selectedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedCountries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suggestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenSuggestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedGroupIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CountryItem> _countryItems;
    private List<AppItem> _appItems = [];
    private List<InstalledApp> _installedApps = [];
    private bool _appsLoaded;
    private DomainGroupsData _rules = new();
    private ServerConfig _config = ServerConfig.DefaultConfig();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _stateLoaded;
    private bool _loading;
    private int _pendingSaves;
    private bool _changedWhileConnected;

    public RoutingView(AppServices services, ConnectionController connection, Action<AppPage> navigate)
    {
        _services = services;
        _connection = connection;
        _navigate = navigate;
        InitializeComponent();

        _countryItems = GeoIpCountryCatalog.GetCountries()
            .Select(country =>
            {
                var item = new CountryItem(country);
                item.SelectionChanged += CountryItem_SelectionChanged;
                return item;
            })
            .ToList();

        _services.Vpn.StateChanged += (_, _) => this.OnUi(RefreshNotices);
        _services.Vpn.LogAdded += line => this.OnUi(() => CollectSuggestions(line));
        _services.Config.ConfigChanged += (_, _) => this.OnUi(async () =>
        {
            // Our own saves raise ConfigChanged too; only reload for changes made elsewhere.
            if (_pendingSaves == 0)
            {
                await ReloadAsync();
            }
        });
        Loaded += async (_, _) =>
        {
            if (!_stateLoaded)
            {
                await ReloadAsync();
            }

            if (!_appsLoaded)
            {
                _ = LoadInstalledAppsAsync();
            }

            RefreshNotices();
        };
    }

    private bool IsGeneralMode => _config.VpnMode == VpnMode.General;

    private Window? Owner => Window.GetWindow(this);

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            _config = await _services.Config.LoadConfigAsync();
            _rules = await _services.Config.MigrateFlatDomainsToGroupsAsync();
            _selectedApps.Clear();
            _selectedApps.UnionWith(_config.SplitTunnelApps);
            _selectedCountries.Clear();
            _selectedCountries.UnionWith(_config.SplitTunnelCountries);
            GeneralOption.IsChecked = IsGeneralMode;
            SelectiveOption.IsChecked = !IsGeneralMode;
            SelectDefaultRuleAction();
            _stateLoaded = true;
        }
        finally
        {
            _loading = false;
        }

        RebuildAppItems();
        foreach (var country in _countryItems)
        {
            country.Load(_selectedCountries.Contains(country.Code));
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshModeTexts();
        RefreshRules();
        RefreshApps();
        RefreshCountries();
        RefreshSuggestions();
        RefreshNotices();
    }

    private async Task SaveAsync()
    {
        // Saves run one at a time (quick checkbox clicks can overlap) and each writes the latest state.
        _pendingSaves++;
        await _saveLock.WaitAsync();
        try
        {
            _rules = _rules.NormalizeEntries();
            _config = await _services.Config.SaveSplitTunnelStateAsync(_rules, _config.VpnMode, _selectedApps, _selectedCountries);
            if (_services.Vpn.Status == VpnStatus.Connected)
            {
                _changedWhileConnected = true;
            }
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Owner, ex.Message, "Could not save routing", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _saveLock.Release();
            _pendingSaves--;
        }

        RefreshRules();
        RefreshSuggestions();
        RefreshNotices();
        AppsCountText.Text = _selectedApps.Count.ToString();
        CountriesCountText.Text = _selectedCountries.Count.ToString();
    }

    private void RefreshNotices()
    {
        var status = _services.Vpn.Status;
        if (status == VpnStatus.Disconnected)
        {
            _changedWhileConnected = false;
        }

        ProxyNotice.Visibility = (_config.ConnectionMode == VpnConnectionMode.SystemProxy).ToVisibility();
        ReconnectNotice.Visibility = (_changedWhileConnected && status == VpnStatus.Connected).ToVisibility();
    }

    private void RefreshModeTexts()
    {
        var ruleAction = IsGeneralMode ? "Bypass VPN" : "Through VPN";
        var exceptionAction = IsGeneralMode ? "Through VPN" : "Bypass VPN";
        RuleHelpText.Text =
            "example.com also covers www.example.com; *.example.com covers every subdomain. " +
            $"The most specific rule wins: *.net → {ruleAction} with example.net → {exceptionAction} " +
            $"{(IsGeneralMode ? "sends only example.net through the VPN" : "keeps example.net off the VPN")}.";

        AppsHintText.Text = IsGeneralMode
            ? "Checked apps bypass the VPN. Steam games also route the Steam client and its servers the same way."
            : "Only checked apps use the VPN. Steam games also route the Steam client and its servers the same way.";

        CountriesHintText.Text = (IsGeneralMode
            ? "Traffic to IP addresses in checked countries bypasses the VPN."
            : "Only traffic to IP addresses in checked countries uses the VPN.") +
            " Address lists are downloaded when you connect and cached for offline use.";
    }

    private void SelectDefaultRuleAction()
    {
        BypassAction.IsChecked = IsGeneralMode;
        VpnAction.IsChecked = !IsGeneralMode;
    }

    // Domains & IPs

    private void RefreshRules()
    {
        var ruleBypasses = IsGeneralMode;
        var items = new List<RuleItem>();

        var entries = _rules.StandaloneDomains
            .Select(pattern => (Pattern: pattern, IsException: false))
            .Concat(_rules.ExceptionDomains.Select(pattern => (Pattern: pattern, IsException: true)))
            .OrderBy(entry => HierarchyKey(entry.Pattern), StringComparer.Ordinal)
            .ThenBy(entry => entry.IsException);

        foreach (var (pattern, isException) in entries)
        {
            var kind = SplitTunnelEntry.Classify(pattern);
            if (isException)
            {
                var overridden = _rules.RulesOverriddenBy(pattern);
                items.Add(new RuleItem
                {
                    Kind = RuleItemKind.Exception,
                    Title = pattern,
                    Subtitle = overridden.Count > 0
                        ? $"Exception to {FormatList(overridden)}"
                        : "Exception without a broader rule to override, so it has no effect yet",
                    HasWarning = overridden.Count == 0,
                    IsBypass = !ruleBypasses,
                    Pattern = pattern,
                    CanSwitchRoute = true
                });
            }
            else
            {
                items.Add(new RuleItem
                {
                    Kind = RuleItemKind.Rule,
                    Title = pattern,
                    Subtitle = RuleItem.DescribePattern(pattern),
                    HasWarning = kind == SplitTunnelEntryKind.Invalid,
                    IsBypass = ruleBypasses,
                    Pattern = pattern,
                    CanSwitchRoute = SplitTunnelEntry.IsDomainKind(kind)
                });
            }
        }

        foreach (var group in _rules.Groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new RuleItem
            {
                Kind = RuleItemKind.Group,
                Title = group.Name,
                Subtitle = $"Site group · {DisplayText.Count(group.Domains.Count, "domain")}: {FormatList(group.Domains)}",
                IsBypass = ruleBypasses,
                Group = group,
                IsExpanded = _expandedGroupIds.Contains(group.Id)
            };
            item.GroupDomains = group.Domains
                .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
                .Select(domain => new GroupDomainItem(item, domain))
                .ToList();
            items.Add(item);
        }

        RulesList.ItemsSource = items;
        DomainsCountText.Text = items.Count.ToString();
        RulesEmptyText.Visibility = (items.Count == 0).ToVisibility();
        RulesEmptyText.Text = IsGeneralMode
            ? "No rules yet, so everything goes through the VPN.\nAdd sites, wildcards like *.example.com or IP ranges that should bypass it."
            : "No rules yet, so nothing goes through the VPN.\nAdd the sites, wildcards or IP ranges that should use it.";
    }

    private async Task<bool> AddRuleAsync(string text, bool bypass)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var isException = IsGeneralMode ? !bypass : bypass;
        return isException ? await AddExceptionAsync(text) : await AddStandaloneRuleAsync(text);
    }

    private async Task<bool> AddExceptionAsync(string text)
    {
        if (!SplitTunnelEntry.TryNormalizeException(text, out var pattern))
        {
            AppMessageBox.Show(Owner,
                $"\"{text.Trim()}\" can't be an exception. Exceptions work for domains such as example.com or *.example.com; IP addresses and ranges can only be rules.",
                "Invalid exception", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_rules.ContainsException(pattern))
        {
            AppMessageBox.Show(Owner, $"{pattern} is already in the list.", "Routing", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        foreach (var group in _rules.Groups.Where(group => group.Domains.Contains(pattern, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _rules.RemoveDomainFromGroup(group, pattern);
        }

        _rules.AddException(pattern);
        await SaveAsync();
        return true;
    }

    private async Task<bool> AddStandaloneRuleAsync(string text)
    {
        if (!SplitTunnelEntry.TryNormalizeRule(text, out var pattern, out var kind))
        {
            AppMessageBox.Show(Owner,
                $"\"{text.Trim()}\" is not a domain, wildcard, IP address or IP range.\n\nExamples: example.com, *.example.com, 203.0.113.7, 203.0.113.0/24.",
                "Invalid rule", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_rules.ContainsDomain(pattern))
        {
            AppMessageBox.Show(Owner, $"{pattern} is already in the list.", "Routing", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (kind == SplitTunnelEntryKind.Domain && DiscoverCheckBox.IsChecked == true && !_rules.ContainsException(pattern))
        {
            DomainDiscoveryResult discovery;
            using (UiExtensions.BusyCursor())
            {
                AddRuleButton.IsEnabled = false;
                try
                {
                    discovery = await _services.DomainDiscovery.DiscoverRelatedDomainsAsync(pattern);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Related domains are a convenience; add the site on its own if the lookup fails.
                    discovery = new DomainDiscoveryResult { Error = ex.Message };
                }
                finally
                {
                    AddRuleButton.IsEnabled = true;
                }
            }

            var related = discovery.DiscoveredDomains
                .Where(domain => !domain.Equals(pattern, StringComparison.OrdinalIgnoreCase) && !_rules.ContainsDomain(domain))
                .ToList();
            if (related.Count > 0)
            {
                if (!_rules.ApplyDiscoveryChoice(pattern, DomainDiscoveryDialog.Ask(Owner, pattern, related)))
                {
                    return false;
                }

                await SaveAsync();
                return true;
            }
        }

        _rules.AddStandaloneDomain(pattern);
        await SaveAsync();
        return true;
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs e) => await AddFromInputAsync();

    private async void RuleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await AddFromInputAsync();
        }
    }

    private async Task AddFromInputAsync()
    {
        if (await AddRuleAsync(RuleInput.Text, BypassAction.IsChecked == true))
        {
            RuleInput.Clear();
        }

        RuleInput.Focus();
    }

    private async void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleItem item)
        {
            return;
        }

        switch (item.Kind)
        {
            case RuleItemKind.Rule:
                _rules.RemoveStandaloneDomain(item.Pattern);
                break;
            case RuleItemKind.Exception:
                _rules.RemoveException(item.Pattern);
                break;
            case RuleItemKind.Group when item.Group != null:
                if (AppMessageBox.Show(Owner, $"Remove the group \"{item.Title}\" and its {DisplayText.Count(item.GroupDomains.Count, "domain")}?",
                        "Remove group", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                _rules.RemoveGroup(item.Group);
                break;
        }

        await SaveAsync();
    }

    private async void SwitchRoute_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleItem item)
        {
            return;
        }

        if (item.Kind == RuleItemKind.Rule)
        {
            _rules.AddException(item.Pattern);
        }
        else if (item.Kind == RuleItemKind.Exception)
        {
            _rules.AddStandaloneDomain(item.Pattern);
        }

        await SaveAsync();
    }

    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleItem { Group: { } group } ||
            _rules.FindTrackedGroup(group) is not { } tracked)
        {
            return;
        }

        var name = TextPromptDialog.Ask(Owner, "Rename group", "Group name", tracked.Name);
        if (name == null || name == tracked.Name)
        {
            return;
        }

        tracked.Name = name;
        await SaveAsync();
    }

    private void ToggleGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleItem { Group: { } group } item)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded)
        {
            _expandedGroupIds.Add(group.Id);
        }
        else
        {
            _expandedGroupIds.Remove(group.Id);
        }
    }

    private async void RemoveGroupDomain_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupDomainItem { Owner.Group: { } group } item)
        {
            return;
        }

        _rules.RemoveDomainFromGroup(group, item.Domain);
        await SaveAsync();
    }

    private async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || !_stateLoaded)
        {
            return;
        }

        var mode = SelectiveOption.IsChecked == true ? VpnMode.Selective : VpnMode.General;
        if (mode == _config.VpnMode)
        {
            return;
        }

        _config.VpnMode = mode;
        SelectDefaultRuleAction();
        RefreshModeTexts();
        await SaveAsync();
    }

    // Suggestions from the engine log

    private void CollectSuggestions(string line)
    {
        var known = _rules.FlattenDomains().Concat(_rules.FlattenExceptions());
        var found = _services.Suggestions.ExtractSuggestions(line, known, _suggestions, _hiddenSuggestions, _config.Hostname);
        if (found.Count == 0)
        {
            return;
        }

        _suggestions.UnionWith(found);
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        var rules = _rules.FlattenDomains().Concat(_rules.FlattenExceptions()).ToList();
        var visible = _suggestions
            .Where(domain => !_hiddenSuggestions.Contains(domain) && !rules.Any(rule => SplitTunnelEntry.Covers(rule, SplitTunnelEntry.Normalize(domain))))
            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SuggestionsList.ItemsSource = visible;
        SuggestionsHeader.Text = $"Seen in recent connections ({visible.Count})";
        SuggestionsExpander.Visibility = (visible.Count > 0).ToVisibility();
    }

    private async void AddSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string domain)
        {
            _suggestions.Remove(domain);
            await AddRuleAsync(domain, bypass: IsGeneralMode);
            RefreshSuggestions();
        }
    }

    private void HideSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string domain)
        {
            _hiddenSuggestions.Add(domain);
            _suggestions.Remove(domain);
            RefreshSuggestions();
        }
    }

    private void HideAllSuggestions_Click(object sender, RoutedEventArgs e)
    {
        _hiddenSuggestions.UnionWith(_suggestions);
        _suggestions.Clear();
        RefreshSuggestions();
    }

    // Apps

    private async Task LoadInstalledAppsAsync()
    {
        RefreshAppsButton.IsEnabled = false;
        AppsEmptyText.Text = "Looking for installed apps…";
        AppsEmptyText.Visibility = (_appItems.Count == 0).ToVisibility();
        try
        {
            _installedApps = await _services.InstalledApps.GetInstalledAppsAsync();
            _appsLoaded = true;
        }
        catch
        {
            // App discovery is best effort; manual entries still work.
        }
        finally
        {
            RefreshAppsButton.IsEnabled = true;
        }

        RebuildAppItems();
        RefreshApps();
    }

    private void RebuildAppItems()
    {
        foreach (var item in _appItems)
        {
            item.SelectionChanged -= AppItem_SelectionChanged;
        }

        var installedNames = new HashSet<string>(_installedApps.Select(app => app.ExecutableName), StringComparer.OrdinalIgnoreCase);
        var manual = _selectedApps
            .Where(app => !installedNames.Contains(app))
            .Select(app => new AppItem(new InstalledApp
            {
                DisplayName = Path.GetFileNameWithoutExtension(app),
                ExecutableName = app,
                Path = ""
            }, isManual: true));
        var installed = _installedApps.Select(app => new AppItem(app, isManual: false));

        _appItems = manual.Concat(installed).ToList();
        foreach (var item in _appItems)
        {
            item.Load(_selectedApps.Contains(item.ExecutableName));
            item.SelectionChanged += AppItem_SelectionChanged;
        }
    }

    private void RefreshApps()
    {
        var query = AppSearchBox.Text.Trim();
        var selectedOnly = AppsSelectedOnly.IsChecked == true;
        var visible = _appItems
            .Where(item => item.Matches(query) && (!selectedOnly || item.IsSelected))
            .OrderByDescending(item => item.IsSelected)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppsList.ItemsSource = visible;
        AppsCountText.Text = _selectedApps.Count.ToString();
        if (visible.Count == 0 && RefreshAppsButton.IsEnabled)
        {
            AppsEmptyText.Text = _appItems.Count == 0
                ? "No apps found. Add a process by name below."
                : "No apps match the filter.";
        }

        AppsEmptyText.Visibility = (visible.Count == 0).ToVisibility();
    }

    private async void AppItem_SelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not AppItem item)
        {
            return;
        }

        if (item.IsSelected)
        {
            _selectedApps.Add(item.ExecutableName);
        }
        else
        {
            _selectedApps.Remove(item.ExecutableName);
        }

        await SaveAsync();
    }

    private void AppSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshApps();

    private void AppsFilter_Changed(object sender, RoutedEventArgs e) => RefreshApps();

    private void RefreshAppsButton_Click(object sender, RoutedEventArgs e) => _ = LoadInstalledAppsAsync();

    private async void AddManualAppButton_Click(object sender, RoutedEventArgs e) => await AddManualAppAsync();

    private async void ManualAppBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await AddManualAppAsync();
        }
    }

    private async Task AddManualAppAsync()
    {
        var executable = SplitTunnelEntry.NormalizeAppProcessName(ManualAppBox.Text);
        if (executable.Length == 0)
        {
            return;
        }

        ManualAppBox.Clear();
        if (!_selectedApps.Add(executable))
        {
            AppMessageBox.Show(Owner, $"{executable} is already selected.", "Routing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RebuildAppItems();
        AppSearchBox.Clear();
        RefreshApps();
        await SaveAsync();
    }

    // Countries

    private void RefreshCountries()
    {
        var query = CountrySearchBox.Text.Trim();
        var selectedOnly = CountriesSelectedOnly.IsChecked == true;
        var visible = _countryItems
            .Where(item => item.Matches(query) && (!selectedOnly || item.IsSelected))
            .OrderByDescending(item => item.IsSelected)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CountriesList.ItemsSource = visible;
        CountriesCountText.Text = _selectedCountries.Count.ToString();
        CountriesEmptyText.Visibility = (visible.Count == 0).ToVisibility();
        ClearCountriesButton.IsEnabled = _selectedCountries.Count > 0;
    }

    private async void CountryItem_SelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not CountryItem item)
        {
            return;
        }

        if (item.IsSelected)
        {
            _selectedCountries.Add(item.Code);
        }
        else
        {
            _selectedCountries.Remove(item.Code);
        }

        ClearCountriesButton.IsEnabled = _selectedCountries.Count > 0;
        await SaveAsync();
    }

    private void CountrySearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCountries();

    private void CountriesFilter_Changed(object sender, RoutedEventArgs e) => RefreshCountries();

    private async void ClearCountriesButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _countryItems)
        {
            item.Load(false);
        }

        _selectedCountries.Clear();
        RefreshCountries();
        await SaveAsync();
    }

    // Notices

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => _navigate(AppPage.Settings);

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _changedWhileConnected = false;
        RefreshNotices();
        await _connection.ReconnectAsync();
    }

    /// <summary>
    /// Sort key that keeps a domain next to its wildcard parents: "*.net" → "net", "example.net" → "net.example".
    /// </summary>
    private static string HierarchyKey(string pattern)
    {
        var name = pattern.StartsWith(SplitTunnelEntry.WildcardPrefix, StringComparison.Ordinal)
            ? pattern[SplitTunnelEntry.WildcardPrefix.Length..]
            : pattern;
        return SplitTunnelEntry.IsDomainKind(SplitTunnelEntry.Classify(pattern))
            ? string.Join('.', name.Split('.').Reverse())
            : "~" + name;
    }

    private static string FormatList(IReadOnlyList<string> items) =>
        items.Count <= 2
            ? string.Join(", ", items)
            : $"{string.Join(", ", items.Take(2))} and {items.Count - 2} more";
}
