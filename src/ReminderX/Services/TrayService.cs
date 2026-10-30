using System.Windows.Forms;

namespace ReminderX.Services;

/// <summary>Keeps the app resident in the notification area while it runs in the background.</summary>
public class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? OpenRequested;
    public event Action? NewReminderRequested;
    public event Action? NewTodoRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ReminderX", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("New Reminder…", null, (_, _) => NewReminderRequested?.Invoke());
        menu.Items.Add("New To-Do…", null, (_, _) => NewTodoRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "ReminderX",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    private Action? _balloonClick;

    /// <summary>Fallback when a Windows toast can't be shown; clicking it runs <paramref name="onClick"/>.</summary>
    public void ShowBalloon(string title, string text, Action onClick)
    {
        if (_balloonClick == null)
            _notifyIcon.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();
        _balloonClick = onClick;
        _notifyIcon.ShowBalloonTip(10_000, title, text, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
