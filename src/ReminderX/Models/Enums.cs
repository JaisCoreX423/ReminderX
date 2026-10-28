namespace ReminderX.Models;

public enum RecurrenceUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months
}

public enum ReminderState
{
    Pending,
    Snoozed,
    Completed,
    Dismissed
}

public enum AlertStyle
{
    /// <summary>Dims every screen and shows the reminder card in the middle.</summary>
    FullScreen,
    /// <summary>Regular Windows toast notification.</summary>
    Toast
}

public enum HistoryAction
{
    Created,
    Shown,
    Completed,
    Snoozed,
    Dismissed,
    Edited,
    Deleted,
    Rescheduled
}
