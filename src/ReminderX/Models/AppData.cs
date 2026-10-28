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

    public bool McpServerEnabled { get; set; } = true;

    public int McpServerPort { get; set; } = 8787;
}
