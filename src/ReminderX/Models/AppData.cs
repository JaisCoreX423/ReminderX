namespace ReminderX.Models;

/// <summary>Root object persisted to disk as a single JSON document.</summary>
public class AppData
{
    public int SchemaVersion { get; set; } = 1;

    public List<Reminder> Reminders { get; set; } = new();

    public List<TodoItem> Todos { get; set; } = new();

    public List<HistoryEntry> History { get; set; } = new();

    public AppSettings Settings { get; set; } = new();
}

public class AppSettings
{
    public bool StartWithWindows { get; set; } = true;

    public int DefaultRenotifyMinutes { get; set; } = 10;

    public bool SoundEnabled { get; set; } = true;

    public AlertStyle AlertStyle { get; set; } = AlertStyle.FullScreen;

    public string Theme { get; set; } = "System"; // System | Light | Dark

    public bool AutoUpdate { get; set; } = true;

    /// <summary>How far back "Review Outlook & Teams" looks.</summary>
    public int ReviewDaysBack { get; set; } = 3;

    /// <summary>Run today's review in the background every N hours (0 = off, 24 = once a day).</summary>
    public int AutoReviewHours { get; set; }

    public DateTime? LastReviewAt { get; set; }

    /// <summary>Normalized titles already shown to the user, so automatic runs only notify about new ones.</summary>
    public List<string> ReviewSeenTitles { get; set; } = new();

    public bool McpServerEnabled { get; set; } = true;

    public int McpServerPort { get; set; } = 8787;
}
