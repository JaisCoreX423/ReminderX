using ReminderX.Models;

namespace ReminderX.Services;

public static class RecurrenceCalculator
{
    /// <summary>Computes the next occurrence strictly after <paramref name="from"/>.</summary>
    public static DateTime? GetNext(RecurrenceRule rule, DateTime from)
    {
        DateTime next = rule.Unit switch
        {
            RecurrenceUnit.Seconds => from.AddSeconds(Math.Max(1, rule.Interval)),
            RecurrenceUnit.Minutes => from.AddMinutes(Math.Max(1, rule.Interval)),
            RecurrenceUnit.Hours => from.AddHours(Math.Max(1, rule.Interval)),
            RecurrenceUnit.Days => NextDaily(rule, from),
            RecurrenceUnit.Weeks => NextWeekly(rule, from),
            RecurrenceUnit.Months => NextMonthly(rule, from),
            _ => from.AddDays(1)
        };

        if (rule.EndDate.HasValue && next > rule.EndDate.Value)
            return null;

        return next;
    }

    private static DateTime NextDaily(RecurrenceRule rule, DateTime from)
    {
        var candidate = from.Date.AddDays(Math.Max(1, rule.Interval)).Add(rule.TimeOfDay);
        return candidate;
    }

    private static DateTime NextWeekly(RecurrenceRule rule, DateTime from)
    {
        var intervalWeeks = Math.Max(1, rule.Interval);

        if (rule.DaysOfWeek.Count == 0)
        {
            return from.Date.AddDays(7 * intervalWeeks).Add(rule.TimeOfDay);
        }

        // Look for the next matching weekday, walking forward day by day.
        var orderedDays = rule.DaysOfWeek.Distinct().ToList();
        var cursor = from.Date.AddDays(1);
        for (var i = 0; i < 7 * intervalWeeks + 7; i++)
        {
            if (orderedDays.Contains(cursor.DayOfWeek))
            {
                var candidate = cursor.Add(rule.TimeOfDay);
                if (candidate > from)
                    return candidate;
            }
            cursor = cursor.AddDays(1);
        }

        return from.Date.AddDays(7 * intervalWeeks).Add(rule.TimeOfDay);
    }

    private static DateTime NextMonthly(RecurrenceRule rule, DateTime from)
    {
        var intervalMonths = Math.Max(1, rule.Interval);
        var day = from.Day;
        var candidateMonth = from.AddMonths(intervalMonths);
        var daysInMonth = DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month);
        var safeDay = Math.Min(day, daysInMonth);
        return new DateTime(candidateMonth.Year, candidateMonth.Month, safeDay).Add(rule.TimeOfDay);
    }
}
