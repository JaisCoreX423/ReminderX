using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using ReminderX.Models;
using ReminderX.Services;

namespace ReminderX.Mcp;

/// <summary>
/// Tools exposed to AI clients (Claude Code, Claude Desktop, etc.) over the local MCP server.
/// Every tool reads/writes through ReminderService, the same code path the UI uses, so nothing
/// here can leave the data store in a state the app itself couldn't reach.
/// </summary>
[McpServerToolType]
public static class ReminderMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    [McpServerTool, Description(
        "List reminders in ReminderX. Set includeCompleted=true to also see completed/dismissed one-time reminders.")]
    public static string ListReminders(ReminderService service, bool includeCompleted = false)
    {
        var reminders = service.GetAllReminders();
        if (!includeCompleted)
            reminders = reminders.Where(r => r.State is ReminderState.Pending or ReminderState.Snoozed).ToList();

        return ToJson(reminders.Select(ToSummary));
    }

    [McpServerTool, Description("Get full details (including description and attachments) for one reminder by id.")]
    public static string GetReminder(ReminderService service, string reminderId)
    {
        var r = service.GetReminder(reminderId);
        return r == null ? ToJson(new { error = "not_found" }) : ToJson(ToDetail(r));
    }

    [McpServerTool, Description(
        "Create a new reminder. dueAt must be an ISO-8601 local date/time, e.g. 2026-11-02T09:00:00. " +
        "For a recurring reminder, also set recurrenceUnit (Seconds|Minutes|Hours|Days|Weeks|Months) and recurrenceInterval " +
        "(e.g. unit=Days, interval=3 means every 3 days). recurrenceTimeOfDay (HH:mm) sets the fire time for Days/Weeks/Months " +
        "recurrences; it defaults to the time in dueAt. Skips (and says so) when an active reminder or open " +
        "to-do with the same title already exists, unless skipIfDuplicate=false.")]
    public static string CreateReminder(
        ReminderService service,
        string title,
        string dueAt,
        string description = "",
        string? recurrenceUnit = null,
        int recurrenceInterval = 1,
        string? recurrenceTimeOfDay = null,
        bool skipIfDuplicate = true)
    {
        if (skipIfDuplicate && service.FindActiveDuplicateTitle(title) is { } existing)
            return ToJson(new { skipped = "duplicate", existingTitle = existing });

        if (!DateTime.TryParse(dueAt, null, System.Globalization.DateTimeStyles.None, out var due))
            return ToJson(new { error = "invalid_dueAt", detail = "Could not parse dueAt as a date/time." });

        RecurrenceRule? rule = null;
        if (!string.IsNullOrWhiteSpace(recurrenceUnit))
        {
            if (!Enum.TryParse<RecurrenceUnit>(recurrenceUnit, ignoreCase: true, out var unit))
                return ToJson(new { error = "invalid_recurrenceUnit", detail = "Use Seconds, Minutes, Hours, Days, Weeks or Months." });

            var timeOfDay = due.TimeOfDay;
            if (!string.IsNullOrWhiteSpace(recurrenceTimeOfDay) && TimeSpan.TryParse(recurrenceTimeOfDay, out var parsedTime))
                timeOfDay = parsedTime;

            rule = new RecurrenceRule
            {
                Unit = unit,
                Interval = Math.Max(1, recurrenceInterval),
                TimeOfDay = timeOfDay
            };
        }

        var reminder = new Reminder
        {
            Title = title,
            Description = description,
            DueAt = due,
            Recurrence = rule
        };

        service.CreateReminder(reminder);
        return ToJson(ToSummary(reminder));
    }

    [McpServerTool, Description("Mark a reminder's current occurrence as complete. Recurring reminders advance to their next occurrence.")]
    public static string CompleteReminder(ReminderService service, string reminderId)
    {
        if (service.GetReminder(reminderId) == null) return ToJson(new { error = "not_found" });
        service.CompleteReminder(reminderId);
        return ToJson(new { ok = true });
    }

    [McpServerTool, Description("Snooze a reminder for the given number of minutes.")]
    public static string SnoozeReminder(ReminderService service, string reminderId, int minutes = 10)
    {
        if (service.GetReminder(reminderId) == null) return ToJson(new { error = "not_found" });
        service.SnoozeReminder(reminderId, TimeSpan.FromMinutes(Math.Max(1, minutes)));
        return ToJson(new { ok = true });
    }

    [McpServerTool, Description("Delete a reminder permanently, including its attachments.")]
    public static string DeleteReminder(ReminderService service, string reminderId)
    {
        if (service.GetReminder(reminderId) == null) return ToJson(new { error = "not_found" });
        service.DeleteReminder(reminderId);
        return ToJson(new { ok = true });
    }

    [McpServerTool, Description("List to-do items. Set includeDone=true to also see completed ones.")]
    public static string ListTodos(ReminderService service, bool includeDone = false)
    {
        var todos = service.GetAllTodos();
        if (!includeDone) todos = todos.Where(t => !t.IsDone).ToList();
        return ToJson(todos);
    }

    [McpServerTool, Description(
        "Create a to-do item. dueAt is optional ISO-8601 local date/time. When dueAt is set and createReminder " +
        "is true (the default), a matching reminder is created automatically. Skips (and says so) when an " +
        "active reminder or open to-do with the same title already exists, unless skipIfDuplicate=false.")]
    public static string CreateTodo(ReminderService service, string title, string notes = "", string? dueAt = null,
        bool createReminder = true, bool skipIfDuplicate = true)
    {
        if (skipIfDuplicate && service.FindActiveDuplicateTitle(title) is { } existing)
            return ToJson(new { skipped = "duplicate", existingTitle = existing });

        DateTime? due = null;
        if (!string.IsNullOrWhiteSpace(dueAt))
        {
            if (!DateTime.TryParse(dueAt, null, System.Globalization.DateTimeStyles.None, out var parsed))
                return ToJson(new { error = "invalid_dueAt" });
            due = parsed;
        }

        var todo = new TodoItem { Title = title, Notes = notes, DueAt = due };
        service.CreateTodo(todo, createReminder);
        return ToJson(todo);
    }

    [McpServerTool, Description("Mark a to-do item done or not done. Completing it also completes its linked reminder, if any.")]
    public static string CompleteTodo(ReminderService service, string todoId, bool done = true)
    {
        service.CompleteTodo(todoId, done);
        return ToJson(new { ok = true });
    }

    [McpServerTool, Description("Delete a to-do item, and its linked reminder if it has one.")]
    public static string DeleteTodo(ReminderService service, string todoId)
    {
        service.DeleteTodo(todoId);
        return ToJson(new { ok = true });
    }

    [McpServerTool, Description("Read recent reminder history (created/shown/completed/snoozed/dismissed events), most recent first.")]
    public static string GetHistory(ReminderService service, int limit = 50)
    {
        var history = service.GetHistory().Take(Math.Clamp(limit, 1, 500));
        return ToJson(history);
    }

    private static object ToSummary(Reminder r) => new
    {
        r.Id,
        r.Title,
        r.DueAt,
        r.State,
        r.IsRecurring,
        Recurrence = r.Recurrence?.Describe(),
        r.IsPaused
    };

    private static object ToDetail(Reminder r) => new
    {
        r.Id,
        r.Title,
        r.Description,
        r.DueAt,
        r.State,
        r.IsRecurring,
        Recurrence = r.Recurrence?.Describe(),
        r.IsPaused,
        r.CreatedAt,
        r.LastShownAt,
        Attachments = r.Attachments.Select(a => a.FileName)
    };
}
