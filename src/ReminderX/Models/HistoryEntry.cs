namespace ReminderX.Models;

public class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ReminderId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public HistoryAction Action { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string Note { get; set; } = string.Empty;
}
