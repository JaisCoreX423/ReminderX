namespace ReminderX.Models;

/// <summary>Describes how a reminder repeats. Null on a one-time reminder.</summary>
public class RecurrenceRule
{
    public RecurrenceUnit Unit { get; set; } = RecurrenceUnit.Days;

    /// <summary>Repeat every N units (e.g. every 3 days).</summary>
    public int Interval { get; set; } = 1;

    /// <summary>For Days/Weeks/Months: time of day the reminder fires.</summary>
    public TimeSpan TimeOfDay { get; set; } = new TimeSpan(9, 0, 0);

    /// <summary>For Weeks: which days of week it fires on. Empty = same weekday as start.</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Optional end date for the recurrence. Null = forever.</summary>
    public DateTime? EndDate { get; set; }

    public RecurrenceRule Clone() => new()
    {
        Unit = Unit,
        Interval = Interval,
        TimeOfDay = TimeOfDay,
        DaysOfWeek = new List<DayOfWeek>(DaysOfWeek),
        EndDate = EndDate
    };

    public string Describe()
    {
        var unitName = Unit switch
        {
            RecurrenceUnit.Seconds => "second",
            RecurrenceUnit.Minutes => "minute",
            RecurrenceUnit.Hours => "hour",
            RecurrenceUnit.Days => "day",
            RecurrenceUnit.Weeks => "week",
            RecurrenceUnit.Months => "month",
            _ => "interval"
        };
        var unitLabel = Interval == 1 ? unitName : $"{Interval} {unitName}s";

        var suffix = Unit is RecurrenceUnit.Days or RecurrenceUnit.Weeks or RecurrenceUnit.Months
            ? $" at {DateTime.Today.Add(TimeOfDay):h:mm tt}"
            : string.Empty;

        if (Unit == RecurrenceUnit.Weeks && DaysOfWeek.Count > 0)
        {
            var days = string.Join(", ", DaysOfWeek.Select(d => d.ToString()[..3]));
            return $"Every {days}{suffix}";
        }

        return $"Every {unitLabel}{suffix}";
    }
}
