using System.Windows.Threading;
using ReminderX.Models;
using ReminderX.Views;
using Screen = System.Windows.Forms.Screen;

namespace ReminderX.Services;

public enum AlertAction
{
    Complete,
    Snooze,
    Open,
    Dismiss
}

public sealed class AlertItem
{
    public required Reminder Reminder { get; init; }

    /// <summary>Preview alerts (Settings > Preview, tests) only close; they never touch stored data.</summary>
    public bool IsPreview { get; init; }
}

/// <summary>
/// Full-screen reminder alerts: dims every monitor and shows one reminder card at a time on the
/// monitor under the mouse. Reminders that come due while a card is up wait in a queue.
/// All members must be called on the UI thread.
/// </summary>
public class AlertService
{
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromMinutes(10);

    private readonly ReminderService _reminders;
    private readonly Queue<AlertItem> _queue = new();
    private readonly List<AlertOverlayWindow> _windows = new();
    private AlertItem? _current;

    public event Action<string>? OpenReminderRequested;

    public AlertService(ReminderService reminders)
    {
        _reminders = reminders;
    }

    public void Enqueue(Reminder reminder, bool isPreview = false)
    {
        if (!isPreview && IsQueuedOrShowing(reminder.Id)) return; // re-notify fired while it's still on screen

        _queue.Enqueue(new AlertItem { Reminder = reminder, IsPreview = isPreview });

        if (_current == null) ShowNext();
        else foreach (var w in _windows) w.SetWaitingCount(_queue.Count);
    }

    public bool IsActive => _current != null;

    private bool IsQueuedOrShowing(string reminderId) =>
        _current?.Reminder.Id == reminderId || _queue.Any(i => i.Reminder.Id == reminderId);

    private void ShowNext()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            return;
        }

        var item = _queue.Dequeue();
        _current = item;
        if (_reminders.GetSettings().SoundEnabled) ChimePlayer.Play();

        var cardScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        AlertOverlayWindow? cardWindow = null;

        // Create every window before showing any: Show() can pump messages, so an action may arrive
        // mid-loop, and it must be able to close the whole set.
        foreach (var screen in Screen.AllScreens)
        {
            var hasCard = screen.DeviceName == cardScreen.DeviceName;
            var window = new AlertOverlayWindow(screen, hasCard ? item : null, _queue.Count,
                item.IsPreview ? null : _reminders);
            if (hasCard)
            {
                window.ActionChosen += OnActionChosen;
                cardWindow = window;
            }
            _windows.Add(window);
        }

        foreach (var window in _windows.ToList()) window.Show();

        if (_current == item) cardWindow?.Activate();
        Logger.Log($"Alert shown: {item.Reminder.Title}{(item.IsPreview ? " (preview)" : "")}");
    }

    private void OnActionChosen(AlertAction action)
    {
        var item = _current;
        if (item == null) return;

        foreach (var w in _windows) w.FadeOutAndClose();
        _windows.Clear();
        _current = null;

        if (!item.IsPreview)
        {
            var id = item.Reminder.Id;
            switch (action)
            {
                case AlertAction.Complete:
                    _reminders.CompleteReminder(id);
                    break;
                case AlertAction.Snooze:
                    _reminders.SnoozeReminder(id, SnoozeDuration);
                    break;
                case AlertAction.Dismiss:
                    _reminders.DismissReminder(id);
                    break;
                case AlertAction.Open:
                    // Stays pending: if it isn't completed it will come back at the next re-notify.
                    OpenReminderRequested?.Invoke(id);
                    break;
            }
        }

        Logger.Log($"Alert action: {action} on {item.Reminder.Title}");

        // Let the fade-out finish before the next queued alert appears.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_current == null) ShowNext();
        };
        timer.Start();
    }
}
