using System.Windows;
using Veil.Models;
using Veil.Ui;

namespace Veil.Dialogs;

/// <summary>
/// Offers to route the domains a site loads content from together with the site itself.
/// </summary>
public partial class DomainDiscoveryDialog : Window
{
    private readonly List<DomainOption> _options;
    private DomainDiscoveryChoice? _choice;

    private DomainDiscoveryDialog(string domain, IReadOnlyList<string> discoveredDomains)
    {
        InitializeComponent();
        WindowEffects.EnableDialogChrome(this);

        _options = discoveredDomains.Select(item => new DomainOption(item)).ToList();
        DomainsList.ItemsSource = _options;
        IntroText.Text =
            $"{domain} loads content from {_options.Count} other domain{(_options.Count == 1 ? "" : "s")}. " +
            "Add them as a group so the whole site takes the same route.";
        GroupNameTextBox.Text = BuildDefaultGroupName(domain);
        WithoutGroupButton.Content = $"Add only {domain}";
        UpdateCount();
    }

    public static DomainDiscoveryChoice? Ask(Window? owner, string domain, IReadOnlyList<string> discoveredDomains)
    {
        var dialog = new DomainDiscoveryDialog(domain, discoveredDomains);
        return AppMessageBox.ShowOwned(dialog, owner) == true ? dialog._choice : null;
    }

    internal static string BuildDefaultGroupName(string domain)
    {
        var firstPart = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstPart)
            ? domain
            : char.ToUpperInvariant(firstPart[0]) + firstPart[1..];
    }

    private void UpdateCount()
    {
        var selected = _options.Count(option => option.IsSelected);
        CountText.Text = $"{selected} of {_options.Count} selected";
        AddGroupButton.IsEnabled = selected > 0;
    }

    private void Selection_Changed(object sender, RoutedEventArgs e) => UpdateCount();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (var option in _options)
        {
            option.IsSelected = selected;
        }

        UpdateCount();
    }

    private void WithoutGroup_Click(object sender, RoutedEventArgs e)
    {
        _choice = new DomainDiscoveryChoice(false, "", []);
        DialogResult = true;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        _choice = new DomainDiscoveryChoice(
            true,
            GroupNameTextBox.Text.Trim(),
            _options.Where(option => option.IsSelected).Select(option => option.Domain).ToList());
        DialogResult = true;
    }

    public sealed class DomainOption(string domain) : ObservableObject
    {
        private bool _isSelected = true;

        public string Domain { get; } = domain;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
