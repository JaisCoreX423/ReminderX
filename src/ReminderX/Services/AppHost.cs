namespace ReminderX.Services;

/// <summary>Holds the app's singleton services. Simple service locator — this app is small
/// enough that a full DI container would add ceremony without real benefit.</summary>
public static class AppHost
{
    /// <summary>True when REMINDERX_DATA_DIR points at a throwaway folder: no MCP server, no shortcut changes.</summary>
    public static bool IsTestMode { get; } =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REMINDERX_DATA_DIR"));

    public static DataStore Store { get; private set; } = null!;
    public static ReminderService Reminders { get; private set; } = null!;
    public static AlertService Alerts { get; private set; } = null!;
    public static NotificationService Notifications { get; private set; } = null!;
    public static SchedulerService Scheduler { get; private set; } = null!;
    public static McpHostService Mcp { get; private set; } = null!;
    public static UpdateService Updates { get; private set; } = null!;
    public static AiReviewService Review { get; private set; } = null!;

    public static void Initialize()
    {
        Store = new DataStore();
        Reminders = new ReminderService(Store);
        Alerts = new AlertService(Reminders);
        Notifications = new NotificationService(Reminders, Alerts);
        if (!IsTestMode) Notifications.Initialize();
        Scheduler = new SchedulerService(Reminders, Notifications);
        Mcp = new McpHostService(Reminders);
        Updates = new UpdateService(Reminders);
        Review = new AiReviewService(Reminders);
    }
}
