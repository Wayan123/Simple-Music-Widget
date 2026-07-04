using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicWidget;

/// <summary>Notification-area icon: hide/unhide the widget on demand and exit cleanly.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onShow, Action onHide, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Tampilkan / Unhide widget", null, (_, _) => onShow());
        menu.Items.Add("Sembunyikan widget", null, (_, _) => onHide());
        menu.Items.Add("Keluar", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Music Widget",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) onShow(); };
    }

    /// <summary>Show a tray balloon; clicking it runs <paramref name="onClick"/>.</summary>
    public void Notify(string title, string text, Action onClick)
    {
        void Handler(object? s, EventArgs e) { _icon.BalloonTipClicked -= Handler; onClick(); }
        _icon.BalloonTipClicked += Handler;
        _icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
    }

    // Use the app's own embedded icon; fall back to a system icon if unavailable.
    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico is not null) return ico;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
