using System.Windows;
using System.Windows.Controls;
using ReminderX.Models;
using ReminderX.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace ReminderX.Views;

public partial class RemindersView : UserControl
{
    public RemindersView()
    {
        InitializeComponent();
        AppHost.Reminders.Changed += () => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var items = AppHost.Reminders.GetAllReminders()
            .Where(r => r.State is ReminderState.Pending or ReminderState.Snoozed)
            .OrderBy(r => r.DueAt)
            .ToList();

        ItemsHost.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var days = AppHost.Reminders.GetSettings().ReviewDaysBack;
        ReviewDaysText.Text = $"Last {days} day{(days == 1 ? "" : "s")}";
    }

    /// <summary>Opens "Review Outlook &amp; Teams" for today only or for the configured number of days.</summary>
    public void OpenReview(bool todayOnly)
    {
        var dialog = new ReviewWindow(todayOnly, AppHost.Reminders.GetSettings().ReviewDaysBack) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        Refresh();
    }

    private void ReviewToday_Click(object sender, RoutedEventArgs e) => OpenReview(todayOnly: true);

    private void ReviewDays_Click(object sender, RoutedEventArgs e) => OpenReview(todayOnly: false);

    public void OpenNewReminderDialog()
    {
        Logger.Log("OpenNewReminderDialog: start");
        var owner = Window.GetWindow(this);
        Logger.Log($"OpenNewReminderDialog: owner resolved = {(owner != null ? owner.GetType().Name : "null")}");

        var dialog = new ReminderEditorWindow(null);
        Logger.Log("OpenNewReminderDialog: dialog constructed");
        dialog.Owner = owner;
        Logger.Log("OpenNewReminderDialog: owner assigned, calling ShowDialog");

        var result = dialog.ShowDialog();
        Logger.Log($"OpenNewReminderDialog: ShowDialog returned {result}");

        if (result == true && dialog.Result != null)
        {
            AppHost.Reminders.CreateReminder(dialog.Result);
            Refresh();
        }
    }

    public void OpenEditDialogFor(string reminderId)
    {
        var reminder = AppHost.Reminders.GetReminder(reminderId);
        if (reminder == null) return;
        OpenEditDialog(reminder);
    }

    private void OpenEditDialog(Reminder reminder)
    {
        var dialog = new ReminderEditorWindow(reminder) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            AppHost.Reminders.UpdateReminder(dialog.Result);
            Refresh();
        }
    }

    private static Reminder? RowOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as Reminder;

    private void NewReminder_Click(object sender, RoutedEventArgs e)
    {
        Logger.Log("NewReminder_Click: fired");
        OpenNewReminderDialog();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } r) AppHost.Reminders.CompleteReminder(r.Id);
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } r) AppHost.Reminders.SnoozeReminder(r.Id, TimeSpan.FromMinutes(10));
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } r) OpenEditDialog(r);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } r) AppHost.Reminders.TogglePause(r.Id, !r.IsPaused);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r) return;
        var result = MessageBox.Show(Window.GetWindow(this), $"Delete \"{r.Title}\"?", "ReminderX",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            AppHost.Reminders.DeleteReminder(r.Id);
    }
}
