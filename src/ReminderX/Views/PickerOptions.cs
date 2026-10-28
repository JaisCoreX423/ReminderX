using ReminderX.Models;

namespace ReminderX.Views;

/// <summary>
/// Backing items for the time-of-day and recurrence-unit ComboBoxes.
/// Real classes, not C# tuples: tuple element names ("Display"/"Value") only exist in source
/// via compiler metadata — at runtime a ValueTuple's real members are Item1/Item2, so WPF's
/// reflection-based DisplayMemberPath/SelectedValuePath binding against "Display"/"Value" would
/// silently fail and fall back to ToString() on the raw tuple.
/// </summary>
public class TimeOption
{
    public string Display { get; init; } = string.Empty;
    public TimeSpan Value { get; init; }

    public static TimeOption[] BuildQuarterHours()
    {
        var list = new List<TimeOption>();
        for (var minutes = 0; minutes < 24 * 60; minutes += 15)
        {
            var t = TimeSpan.FromMinutes(minutes);
            list.Add(new TimeOption { Display = DateTime.Today.Add(t).ToString("h:mm tt"), Value = t });
        }
        return list.ToArray();
    }

    public static TimeSpan RoundToNearest(TimeOption[] options, TimeSpan t)
    {
        var closest = options[0].Value;
        var bestDiff = double.MaxValue;
        foreach (var opt in options)
        {
            var diff = Math.Abs((opt.Value - t).TotalMinutes);
            if (diff < bestDiff) { bestDiff = diff; closest = opt.Value; }
        }
        return closest;
    }
}

public class UnitOption
{
    public string Display { get; init; } = string.Empty;
    public RecurrenceUnit Value { get; init; }

    public static readonly UnitOption[] All =
    {
        new() { Display = "second(s)", Value = RecurrenceUnit.Seconds },
        new() { Display = "minute(s)", Value = RecurrenceUnit.Minutes },
        new() { Display = "hour(s)", Value = RecurrenceUnit.Hours },
        new() { Display = "day(s)", Value = RecurrenceUnit.Days },
        new() { Display = "week(s)", Value = RecurrenceUnit.Weeks },
        new() { Display = "month(s)", Value = RecurrenceUnit.Months },
    };
}
