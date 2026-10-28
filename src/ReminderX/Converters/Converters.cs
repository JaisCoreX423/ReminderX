using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ReminderX.Models;

namespace ReminderX.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => !(value is bool v && v);

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => !(value is bool v && v);
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (Invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a DateTime as a friendly relative string: "Today 3:00 PM", "Tomorrow 9:00 AM", "Nov 3, 9:00 AM".</summary>
public class FriendlyDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return string.Empty;

        var today = DateTime.Today;
        if (dt.Date == today) return $"Today, {dt:h:mm tt}";
        if (dt.Date == today.AddDays(1)) return $"Tomorrow, {dt:h:mm tt}";
        if (dt.Date == today.AddDays(-1)) return $"Yesterday, {dt:h:mm tt}";
        return dt.Year == today.Year ? $"{dt:MMM d, h:mm tt}" : $"{dt:MMM d yyyy, h:mm tt}";
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ReminderStateToBrushKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ReminderState.Completed => "Brush.Success",
            ReminderState.Snoozed => "Brush.Warning",
            ReminderState.Dismissed => "Brush.TextSecondary",
            _ => "Brush.Accent"
        };
        return System.Windows.Application.Current.TryFindResource(key) ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
