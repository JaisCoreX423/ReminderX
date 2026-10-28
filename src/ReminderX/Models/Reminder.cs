namespace ReminderX.Models;

public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The next (or only, for one-time) time this reminder should fire.</summary>
    public DateTime DueAt { get; set; }

    public RecurrenceRule? Recurrence { get; set; }

    public bool IsRecurring => Recurrence != null;

    public ReminderState State { get; set; } = ReminderState.Pending;

    public List<Attachment> Attachments { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Last time the notification was actually shown/re-shown to the user.</summary>
    public DateTime? LastShownAt { get; set; }

    /// <summary>Next time the "nag" re-notification should fire if still unacknowledged.</summary>
    public DateTime? NextRenotifyAt { get; set; }

    /// <summary>How often (minutes) to re-show the toast if the user ignores it. 0 = don't nag.</summary>
    public int RenotifyMinutes { get; set; } = 10;

    public bool IsPaused { get; set; }

    /// <summary>If this reminder was auto-created from a To-Do item.</summary>
    public string? SourceTodoId { get; set; }

    public bool IsActive => !IsPaused && State is ReminderState.Pending or ReminderState.Snoozed;
}
