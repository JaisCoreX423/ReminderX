using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ReminderX.Models;
using ReminderX.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ReminderX.Views;

public class AttachmentRow
{
    public string DisplayName { get; set; } = string.Empty;
    public BitmapImage? Thumbnail { get; set; }
    /// <summary>Set when this is an existing, already-saved attachment.</summary>
    public Attachment? Existing { get; set; }
    /// <summary>Set when this is a newly picked file not yet copied into the store.</summary>
    public string? PendingSourcePath { get; set; }
}

public partial class ReminderEditorWindow : Window
{
    private static readonly TimeOption[] TimeOptions = TimeOption.BuildQuarterHours();
    private static readonly UnitOption[] UnitOptions = UnitOption.All;

    private readonly Reminder? _editing;
    private readonly ObservableCollection<AttachmentRow> _attachments = new();

    public Reminder? Result { get; private set; }

    public ReminderEditorWindow(Reminder? editing)
    {
        Logger.Log("ReminderEditorWindow: ctor start");
        InitializeComponent();
        Logger.Log("ReminderEditorWindow: InitializeComponent done");
        _editing = editing;

        var displayTemplate = (DataTemplate)FindResource("DisplayTextItemTemplate");

        DueTimeCombo.ItemsSource = TimeOptions;
        DueTimeCombo.ItemTemplate = displayTemplate;
        DueTimeCombo.SelectedValuePath = "Value";

        RecurTimeCombo.ItemsSource = TimeOptions;
        RecurTimeCombo.ItemTemplate = displayTemplate;
        RecurTimeCombo.SelectedValuePath = "Value";

        UnitCombo.ItemsSource = UnitOptions;
        UnitCombo.ItemTemplate = displayTemplate;
        UnitCombo.SelectedValuePath = "Value";

        AttachmentsList.ItemsSource = _attachments;
        Logger.Log("ReminderEditorWindow: combos wired");

        DueDatePicker.SelectedDate = DateTime.Today;
        RecurStartDatePicker.SelectedDate = DateTime.Today;
        DueTimeCombo.SelectedValue = new TimeSpan(9, 0, 0);
        RecurTimeCombo.SelectedValue = new TimeSpan(9, 0, 0);
        UnitCombo.SelectedValue = RecurrenceUnit.Days;

        if (editing != null)
        {
            HeaderText.Text = "Edit Reminder";
            TitleBox.Text = editing.Title;
            DescriptionBox.Text = editing.Description;
            RenotifyBox.Text = editing.RenotifyMinutes.ToString();

            foreach (var a in editing.Attachments)
                _attachments.Add(ToRow(a));

            if (editing.Recurrence == null)
            {
                OneTimeRadio.IsChecked = true;
                DueDatePicker.SelectedDate = editing.DueAt.Date;
                DueTimeCombo.SelectedValue = RoundToNearestOption(editing.DueAt.TimeOfDay);
            }
            else
            {
                RecurringRadio.IsChecked = true;
                var rule = editing.Recurrence;
                IntervalBox.Text = rule.Interval.ToString();
                UnitCombo.SelectedValue = rule.Unit;
                RecurTimeCombo.SelectedValue = RoundToNearestOption(rule.TimeOfDay);
                RecurStartDatePicker.SelectedDate = editing.DueAt.Date;
                foreach (ToggleButton tb in WeekdaysPicker.Children)
                {
                    if (Enum.TryParse<DayOfWeek>((string)tb.Tag, out var dow))
                        tb.IsChecked = rule.DaysOfWeek.Contains(dow);
                }
                UpdateRecurringVisibility();
            }
        }
        else
        {
            HeaderText.Text = "New Reminder";
        }

        Logger.Log("ReminderEditorWindow: fields populated, calling TypeChanged");
        TypeChanged(this, new RoutedEventArgs());
        Logger.Log("ReminderEditorWindow: ctor end");

        Loaded += (_, _) => Logger.Log("ReminderEditorWindow: Loaded event fired");
        ContentRendered += (_, _) =>
        {
            Logger.Log("ReminderEditorWindow: ContentRendered event fired");
            if (DiagSnapshot.Dir != null) RunDiagSnapshots();
        };
        SourceInitialized += (_, _) => Logger.Log("ReminderEditorWindow: SourceInitialized event fired");
    }

    private static TimeSpan RoundToNearestOption(TimeSpan t) => TimeOption.RoundToNearest(TimeOptions, t);

    private void RunDiagSnapshots()
    {
        DiagSnapshot.Save((FrameworkElement)Content, "editor", Background);

        DueTimeCombo.IsDropDownOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (DueTimeCombo.Template.FindName("Popup", DueTimeCombo) is Popup timePopup &&
                timePopup.Child is FrameworkElement timeList)
                DiagSnapshot.Save(timeList, "time-dropdown");
            DueTimeCombo.IsDropDownOpen = false;

            DueDatePicker.IsDropDownOpen = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (DueDatePicker.Template.FindName("PART_Popup", DueDatePicker) is not Popup datePopup ||
                    datePopup.Child is not System.Windows.Controls.Calendar calendar)
                {
                    Logger.Log("DiagSnapshot: date popup or its calendar not found");
                    return;
                }

                DiagSnapshot.Save(calendar, "date-calendar");
                calendar.DisplayMode = System.Windows.Controls.CalendarMode.Year;
                Dispatcher.BeginInvoke(() =>
                {
                    DiagSnapshot.Save(calendar, "date-calendar-year");
                    calendar.DisplayMode = System.Windows.Controls.CalendarMode.Month;
                    DueDatePicker.IsDropDownOpen = false;
                    DiagSnapshot.Save((FrameworkElement)Content, "editor-after", Background);

                    RecurringRadio.IsChecked = true;
                    UnitCombo.SelectedValue = RecurrenceUnit.Weeks;
                    Dispatcher.BeginInvoke(() =>
                    {
                        DiagSnapshot.Save((FrameworkElement)Content, "editor-recurring", Background);
                        Logger.Log("DiagSnapshot: done, exiting test instance");
                        Environment.Exit(0);
                    }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private AttachmentRow ToRow(Attachment a)
    {
        var row = new AttachmentRow { DisplayName = a.FileName, Existing = a };
        if (a.IsImage)
            row.Thumbnail = TryLoadThumbnail(AppHost.Reminders.GetAttachmentFullPath(a));
        return row;
    }

    private static BitmapImage? TryLoadThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void TypeChanged(object sender, RoutedEventArgs e)
    {
        if (OneTimePanel == null || RecurringPanel == null) return;
        OneTimePanel.Visibility = OneTimeRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RecurringPanel.Visibility = RecurringRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UnitCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateRecurringVisibility();

    private void UpdateRecurringVisibility()
    {
        if (TimeOfDayPanel == null) return;
        var unit = (RecurrenceUnit)(UnitCombo.SelectedValue ?? RecurrenceUnit.Days);
        var showTime = unit is RecurrenceUnit.Days or RecurrenceUnit.Weeks or RecurrenceUnit.Months;
        TimeOfDayPanel.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
        WeekdaysPanel.Visibility = unit == RecurrenceUnit.Weeks ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Add attachment",
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var isImage = IsImageExtension(Path.GetExtension(file));
                var row = new AttachmentRow
                {
                    DisplayName = Path.GetFileName(file),
                    PendingSourcePath = file,
                    Thumbnail = isImage ? TryLoadThumbnail(file) : null
                };
                _attachments.Add(row);
            }
        }
    }

    private static bool IsImageExtension(string ext) =>
        ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AttachmentRow row })
            _attachments.Remove(row);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show(this, "Give the reminder a title.", "ReminderX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(RenotifyBox.Text.Trim(), out var renotifyMinutes) || renotifyMinutes < 0)
            renotifyMinutes = 10;

        var reminder = _editing ?? new Reminder();
        reminder.Title = title;
        reminder.Description = DescriptionBox.Text.Trim();
        reminder.RenotifyMinutes = renotifyMinutes;

        if (OneTimeRadio.IsChecked == true)
        {
            var date = DueDatePicker.SelectedDate ?? DateTime.Today;
            var time = (TimeSpan)(DueTimeCombo.SelectedValue ?? TimeSpan.Zero);
            reminder.DueAt = date.Date.Add(time);
            reminder.Recurrence = null;
        }
        else
        {
            if (!int.TryParse(IntervalBox.Text.Trim(), out var interval) || interval < 1)
                interval = 1;

            var unit = (RecurrenceUnit)(UnitCombo.SelectedValue ?? RecurrenceUnit.Days);
            var time = (TimeSpan)(RecurTimeCombo.SelectedValue ?? TimeSpan.Zero);
            var startDate = RecurStartDatePicker.SelectedDate ?? DateTime.Today;

            var days = new List<DayOfWeek>();
            foreach (ToggleButton tb in WeekdaysPicker.Children)
            {
                if (tb.IsChecked == true && Enum.TryParse<DayOfWeek>((string)tb.Tag, out var dow))
                    days.Add(dow);
            }

            reminder.Recurrence = new RecurrenceRule
            {
                Unit = unit,
                Interval = interval,
                TimeOfDay = time,
                DaysOfWeek = days
            };

            reminder.DueAt = unit is RecurrenceUnit.Days or RecurrenceUnit.Weeks or RecurrenceUnit.Months
                ? startDate.Date.Add(time)
                : DateTime.Now;
        }

        // Reconcile attachments: keep existing ones still present, copy in new ones.
        var keptExisting = _attachments.Where(a => a.Existing != null).Select(a => a.Existing!).ToList();
        var removedExisting = reminder.Attachments.Where(a => !keptExisting.Contains(a)).ToList();

        reminder.Attachments = keptExisting;

        foreach (var row in _attachments.Where(a => a.PendingSourcePath != null))
        {
            var relative = AppHost.Reminders.AddAttachment(reminder.Id, row.PendingSourcePath!);
            reminder.Attachments.Add(new Attachment
            {
                FileName = Path.GetFileName(row.PendingSourcePath!),
                RelativePath = relative,
                IsImage = IsImageExtension(Path.GetExtension(row.PendingSourcePath!))
            });
        }

        foreach (var removed in removedExisting)
        {
            try
            {
                var full = AppHost.Reminders.GetAttachmentFullPath(removed);
                if (File.Exists(full)) File.Delete(full);
            }
            catch { /* best effort */ }
        }

        Result = reminder;
        DialogResult = true;
        Close();
    }
}
