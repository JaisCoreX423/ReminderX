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
            list.Add(new TimeOption { Display = Format(t), Value = t });
        }
        return list.ToArray();
    }

    public static string Format(TimeSpan t) =>
        DateTime.Today.Add(t).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Makes a time ComboBox accept any typed time while keeping the 15-minute presets.</summary>
    public static void ConfigureEditable(System.Windows.Controls.ComboBox combo, TimeOption[] options,
        System.Windows.DataTemplate itemTemplate)
    {
        combo.ItemsSource = options;
        combo.ItemTemplate = itemTemplate;
        combo.SelectedValuePath = nameof(Value);
        combo.IsEditable = true;
        combo.IsTextSearchEnabled = false; // autocomplete would fight free typing ("9:3" → "9:30 AM")
        System.Windows.Controls.TextSearch.SetTextPath(combo, nameof(Display));
        // Whatever was typed ("9 p. m.", "21:37", "9:37pm") is rewritten as "9:37 PM" on leaving the box.
        combo.LostKeyboardFocus += (_, _) =>
        {
            if (!combo.IsKeyboardFocusWithin && TryParse(combo.Text, out var typed))
            {
                var formatted = Format(typed);
                if (combo.Text != formatted) SetTime(combo, typed);
            }
        };
    }

    public static void SetTime(System.Windows.Controls.ComboBox combo, TimeSpan time)
    {
        var match = ((TimeOption[])combo.ItemsSource).FirstOrDefault(o => o.Value == time);
        combo.SelectedItem = match;
        combo.Text = Format(time);
    }

    /// <summary>
    /// Accepts "9:37 AM", "9:37am", "9:37 p", "9:37 a. m.", "9:37 p.m.", "21:37", "9 am", "2137", "9.37 pm".
    /// Always parsed with AM/PM, never the Windows regional designators.
    /// </summary>
    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToLowerInvariant();

        // "a. m." / "p.m." / "a m" / "pm" / "p" at the end → " AM" / " PM".
        string? designator = null;
        var suffix = System.Text.RegularExpressions.Regex.Match(s, @"\s*([ap])\s*\.?\s*(m\s*\.?)?\s*$");
        if (suffix.Success)
        {
            designator = suffix.Groups[1].Value == "a" ? "AM" : "PM";
            s = s[..suffix.Index].Trim();
        }
        s = s.Replace('.', ':'); // "9.37" → "9:37"
        if (designator != null) s += " " + designator;

        string[] formats = { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "h tt", "hh tt", "Hmm", "HHmm", "hmm tt", "hhmm tt",
            "h:mm:ss tt", "H:mm:ss" };
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (!DateTime.TryParseExact(s, formats, invariant, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return false;
        time = parsed.TimeOfDay;
        return true;
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

/// <summary>"Check automatically" choices for the Outlook/Teams review.</summary>
public class ReviewIntervalOption
{
    public string Display { get; init; } = string.Empty;
    public int Hours { get; init; }

    public static readonly ReviewIntervalOption[] All =
    {
        new() { Display = "Off", Hours = 0 },
        new() { Display = "Every hour", Hours = 1 },
        new() { Display = "Every 2 hours", Hours = 2 },
        new() { Display = "Every 4 hours", Hours = 4 },
        new() { Display = "Once a day", Hours = 24 }
    };
}
