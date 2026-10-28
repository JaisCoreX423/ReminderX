namespace ReminderX.Models;

public class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTime? DueAt { get; set; }

    public bool IsDone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? CompletedAt { get; set; }

    /// <summary>The Reminder auto-created for this to-do, if any.</summary>
    public string? LinkedReminderId { get; set; }
}
