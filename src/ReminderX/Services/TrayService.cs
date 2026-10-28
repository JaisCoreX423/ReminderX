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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
