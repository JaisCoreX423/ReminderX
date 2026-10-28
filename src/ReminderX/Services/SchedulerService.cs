using Timer = System.Threading.Timer;

namespace ReminderX.Services;

/// <summary>Ticks on a background timer and fires notifications for due reminders.</summary>
public class SchedulerService : IDisposable
{
    private readonly ReminderService _reminders;
    private readonly NotificationService _notifications;
    private Timer? _timer;
    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(2); // short timers should land on time
    private bool _ticking;

    public SchedulerService(ReminderService reminders, NotificationService notifications)
    {
        _reminders = reminders;
        _notifications = notifications;
    }

    public void Start()
    {
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, _tickInterval);
    }

    private void Tick()
    {
        if (_ticking) return; // avoid overlap if a save is slow
        _ticking = true;
        try
        {
            var now = DateTime.Now;
            var due = _reminders.GetDueReminders(now);
            foreach (var reminder in due)
            {
                // Isolated per reminder so one failure can't block every other due reminder.
                try
                {
                    _notifications.ShowReminder(reminder);
                    _reminders.MarkShown(reminder.Id, now);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Couldn't show \"{reminder.Title}\": {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Scheduler tick failed: {ex.Message}");
        }
        finally
        {
            _ticking = false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
