using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using ReminderX.Models;

namespace ReminderX.Services;

public static class ToastArgsKeys
{
    public const string Action = "action";
    public const string ReminderId = "reminderId";
}

public static class ToastActions
{
    public const string Open = "open";
    public const string Complete = "complete";
    public const string Snooze = "snooze";
    public const string Dismiss = "dismiss";
}

/// <summary>Shows a due reminder, either as the full-screen alert or as a native Windows toast.</summary>
public class NotificationService
{
    private readonly ReminderService _reminders;
    private readonly AlertService _alerts;

    public NotificationService(ReminderService reminders, AlertService alerts)
    {
        _reminders = reminders;
        _alerts = alerts;
    }

    public void Initialize()
    {
        ShortcutHelper.EnsureShortcutInstalled();
    }

    /// <summary>Called from the scheduler's background thread.</summary>
    public void ShowReminder(Reminder reminder)
    {
        if (_reminders.GetSettings().AlertStyle == AlertStyle.Toast)
        {
            ShowToast(reminder);
            return;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _alerts.Enqueue(reminder));
    }

    private void ShowToast(Reminder reminder)
    {
        var builder = new ToastContentBuilder()
            .AddArgument(ToastArgsKeys.Action, ToastActions.Open)
            .AddArgument(ToastArgsKeys.ReminderId, reminder.Id)
            .AddText(reminder.Title, hintMaxLines: 2);

        if (!string.IsNullOrWhiteSpace(reminder.Description))
            builder.AddText(reminder.Description, hintMaxLines: 3);

        var image = reminder.Attachments.FirstOrDefault(a => a.IsImage);
        if (image != null)
        {
            var fullPath = _reminders.GetAttachmentFullPath(image);
            if (File.Exists(fullPath))
                builder.AddHeroImage(new Uri(fullPath));
        }

        builder.AddButton(new ToastButton()
            .SetContent("Complete")
            .AddArgument(ToastArgsKeys.Action, ToastActions.Complete)
            .AddArgument(ToastArgsKeys.ReminderId, reminder.Id)
            .SetBackgroundActivation());

        builder.AddButton(new ToastButton()
            .SetContent("Snooze 10 min")
            .AddArgument(ToastArgsKeys.Action, ToastActions.Snooze)
            .AddArgument(ToastArgsKeys.ReminderId, reminder.Id)
            .SetBackgroundActivation());

        builder.AddButton(new ToastButton()
            .SetContent("Dismiss")
            .AddArgument(ToastArgsKeys.Action, ToastActions.Dismiss)
            .AddArgument(ToastArgsKeys.ReminderId, reminder.Id)
            .SetBackgroundActivation());

        builder.SetToastDuration(ToastDuration.Long);

        if (!_reminders.GetSettings().SoundEnabled)
            builder.AddAudio(new ToastAudio { Silent = true });

        try
        {
            builder.Show();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to show toast for {reminder.Id}: {ex.Message}");
        }
    }

    public static void ClearAll()
    {
        try { ToastNotificationManagerCompat.History.Clear(); }
        catch { /* ignore */ }
    }
}
