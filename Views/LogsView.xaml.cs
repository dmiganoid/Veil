using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

public partial class LogsView : UserControl
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _refreshTimer;

    public LogsView(AppServices services)
    {
        _services = services;
        InitializeComponent();

        // The engine can log hundreds of lines per second; redraw at most a few times per second.
        _refreshTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _refreshTimer!.Stop();
            Refresh();
        }, Dispatcher);
        _refreshTimer.Stop();

        _services.Vpn.StateChanged += (_, _) => this.OnUi(ScheduleRefresh);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
            }
        };
    }

    private void ScheduleRefresh()
    {
        if (IsVisible && !_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private void Refresh()
    {
        var logs = _services.Vpn.Logs;
        LogTextBox.Text = string.Join(Environment.NewLine, logs);
        SubtitleText.Text = $"VPN engine output for this session · {DisplayText.Count(logs.Count, "entry", "entries")}";
        var hasLogs = logs.Count > 0;
        EmptyText.Visibility = (!hasLogs).ToVisibility();
        CopyButton.IsEnabled = hasLogs;
        SaveButton.IsEnabled = hasLogs;
        ClearButton.IsEnabled = hasLogs;
        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogTextBox.ScrollToEnd();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _services.Vpn.Logs));
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Could not copy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Veil log",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"veil-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            await File.WriteAllLinesAsync(dialog.FileName, _services.Vpn.Logs);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), ex.Message, "Could not save the log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppMessageBox.Show(Window.GetWindow(this), "Delete all log entries of this session?", "Clear logs",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _services.Vpn.ClearLogs();
            Refresh();
        }
    }
}
