using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReminderX.Models;

namespace ReminderX.Services.Sync;

/// <summary>
/// What a reminder or to-do looks like inside an encrypted sync record (schema v1, camelCase JSON).
/// Shared with the Android app, so field names and formats are a contract:
/// - dates: ISO 8601 with offset ("2026-11-12T09:30:00-06:00")
/// - timeOfDay: "HH:mm:ss"; daysOfWeek: 0 = Sunday … 6 = Saturday (.NET DayOfWeek)
/// - unit / state: enum names ("Days", "Pending", …)
/// Device-local fields (attachments, LastShownAt) don't sync.
/// </summary>
public sealed class ReminderRecord
{
    public int V { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public RecurrenceRecord? Recurrence { get; set; }
    public ReminderState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextRenotifyAt { get; set; }
    public int RenotifyMinutes { get; set; }
    public bool IsPaused { get; set; }
    public string? SourceTodoId { get; set; }

    public static ReminderRecord From(Reminder r) => new()
    {
        Title = r.Title,
        Description = r.Description,
        DueAt = r.DueAt,
        Recurrence = r.Recurrence == null ? null : RecurrenceRecord.From(r.Recurrence),
        State = r.State,
        CreatedAt = r.CreatedAt,
        NextRenotifyAt = r.NextRenotifyAt,
        RenotifyMinutes = r.RenotifyMinutes,
        IsPaused = r.IsPaused,
        SourceTodoId = r.SourceTodoId
    };

    /// <summary>Copies the synced fields onto a local reminder, keeping its attachments and alert state.</summary>
    public void ApplyTo(Reminder r)
    {
        r.Title = Title;
        r.Description = Description;
        r.DueAt = DueAt.LocalDateTime;
        r.Recurrence = Recurrence?.ToModel();
        r.State = State;
        r.CreatedAt = CreatedAt.LocalDateTime;
        r.NextRenotifyAt = NextRenotifyAt?.LocalDateTime;
        r.RenotifyMinutes = RenotifyMinutes;
        r.IsPaused = IsPaused;
        r.SourceTodoId = SourceTodoId;
    }
}

public sealed class RecurrenceRecord
{
    public RecurrenceUnit Unit { get; set; }
    public int Interval { get; set; }
    public string TimeOfDay { get; set; } = "09:00:00";
    public List<int> DaysOfWeek { get; set; } = new();
    public DateTimeOffset? EndDate { get; set; }

    public static RecurrenceRecord From(RecurrenceRule rule) => new()
    {
        Unit = rule.Unit,
        Interval = rule.Interval,
        TimeOfDay = rule.TimeOfDay.ToString(@"hh\:mm\:ss"),
        DaysOfWeek = rule.DaysOfWeek.Select(d => (int)d).Order().ToList(),
        EndDate = rule.EndDate
    };

    public RecurrenceRule ToModel() => new()
    {
        Unit = Unit,
        Interval = Math.Max(1, Interval),
        TimeOfDay = TimeSpan.TryParse(TimeOfDay, out var t) ? t : new TimeSpan(9, 0, 0),
        DaysOfWeek = DaysOfWeek.Where(d => d is >= 0 and <= 6).Select(d => (DayOfWeek)d).ToList(),
        EndDate = EndDate?.LocalDateTime
    };
}

public sealed class TodoRecord
{
    public int V { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset? DueAt { get; set; }
    public bool IsDone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LinkedReminderId { get; set; }

    public static TodoRecord From(TodoItem t) => new()
    {
        Title = t.Title,
        Notes = t.Notes,
        DueAt = t.DueAt,
        IsDone = t.IsDone,
        CreatedAt = t.CreatedAt,
        CompletedAt = t.CompletedAt,
        LinkedReminderId = t.LinkedReminderId
    };

    public void ApplyTo(TodoItem t)
    {
        t.Title = Title;
        t.Notes = Notes;
        t.DueAt = DueAt?.LocalDateTime;
        t.IsDone = IsDone;
        t.CreatedAt = CreatedAt.LocalDateTime;
        t.CompletedAt = CompletedAt?.LocalDateTime;
        t.LinkedReminderId = LinkedReminderId;
    }
}

public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T record) => JsonSerializer.Serialize(record, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Empty record");

    /// <summary>Fingerprint used to notice local edits between sync cycles.</summary>
    public static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32];
}
