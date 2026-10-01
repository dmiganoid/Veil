using System.Drawing;
using Veil.Models;
using Forms = System.Windows.Forms;

namespace Veil.Ui;

/// <summary>
/// Notification-area icon with a status line and connect/disconnect/exit actions.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private static readonly Color MenuText = Color.FromArgb(0xE7, 0xEC, 0xF4);

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _connectItem;
    private bool _backgroundHintShown;

    public TrayIcon(Icon icon, Action open, Func<Task> toggleConnection, Func<Task> exit)
    {
        _statusItem = new Forms.ToolStripMenuItem("Disconnected") { Enabled = false };
        var openItem = new Forms.ToolStripMenuItem("Open Veil", null, (_, _) => open())
        {
            Font = new Font(Forms.Control.DefaultFont, FontStyle.Bold)
        };
        _connectItem = new Forms.ToolStripMenuItem("Connect", null, async (_, _) => await toggleConnection());
        var exitItem = new Forms.ToolStripMenuItem("Exit Veil", null, async (_, _) => await exit());

        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ForeColor = MenuText,
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false }
        };
        _menu.Items.AddRange([_statusItem, new Forms.ToolStripSeparator(), openItem, _connectItem, new Forms.ToolStripSeparator(), exitItem]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Veil",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    public void Update(VpnStatus status, string? server)
    {
        var statusText = status.DisplayText();
        _statusItem.Text = status == VpnStatus.Connected && !string.IsNullOrWhiteSpace(server)
            ? $"{statusText} to {server}"
            : statusText;
        _connectItem.Text = status == VpnStatus.Connected ? "Disconnect" : "Connect";
        _connectItem.Enabled = status is VpnStatus.Connected or VpnStatus.Disconnected;

        // NotifyIcon.Text is limited to 63 characters.
        var tooltip = $"Veil: {statusText}";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    /// <summary>Explains once per session that closing the window keeps Veil running.</summary>
    public void ShowBackgroundHint()
    {
        if (_backgroundHintShown)
        {
            return;
        }

        _backgroundHintShown = true;
        _notifyIcon.ShowBalloonTip(
            4000,
            "Veil is still running",
            "Click the tray icon to open Veil. Right-click it to connect, disconnect or exit.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(0x1A, 0x21, 0x30);
        private static readonly Color Hover = Color.FromArgb(0x2A, 0x35, 0x4A);
        private static readonly Color Border = Color.FromArgb(0x34, 0x40, 0x5A);

        public override Color ToolStripDropDownBackground => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
    }
}
