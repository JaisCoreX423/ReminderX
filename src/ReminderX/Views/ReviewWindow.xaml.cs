using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ReminderX.Services;

namespace ReminderX.Views;

/// <summary>
/// Shows a review run owned by <see cref="ReviewCoordinator"/>: closing the window doesn't stop it,
/// and a run that finishes after the window is closed ends up in a notification instead.
/// </summary>
public partial class ReviewWindow : Window
{
    public enum Mode { Today, Days, Resume }

    private static ReviewWindow? _current;

    private readonly Mode _mode;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<ReviewSuggestion> _suggestions = new();
    private int _skippedAtReview;

    /// <summary>One review window at a time; opening again brings the existing one forward.</summary>
    public static void Open(Window? owner, Mode mode)
    {
        if (_current != null)
        {
            _current.Activate();
            return;
        }

        _current = new ReviewWindow(mode);
        if (owner is { IsVisible: true }) _current.Owner = owner;
        else _current.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _current.Show();
        _current.Activate();
    }

    private ReviewWindow(Mode mode)
    {
        InitializeComponent();
        _mode = mode;

        AutoReviewCombo.ItemTemplate = (DataTemplate)FindResource("DisplayTextItemTemplate");
        AutoReviewCombo.ItemsSource = ReviewIntervalOption.All;
        var hours = AppHost.Reminders.GetSettings().AutoReviewHours;
        AutoReviewCombo.SelectedItem = ReviewIntervalOption.All.FirstOrDefault(o => o.Hours == hours) ?? ReviewIntervalOption.All[0];

        _clock.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - AppHost.Reviews.StartedAt;
            ElapsedText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        };
        Loaded += async (_, _) => await RunAsync();
        Closed += (_, _) =>
        {
            _clock.Stop();
            AppHost.Reviews.WindowOpen = false;
            _current = null;
        };
    }

    private void SetScope()
    {
        var reviews = AppHost.Reviews;
        ScopeText.Text = reviews.CurrentTodayOnly
            ? "Today's email, meetings and chats"
            : $"Email, meetings and chats from the last {reviews.CurrentDaysBack} day{Plural(reviews.CurrentDaysBack)}";
    }

    private async Task RunAsync()
    {
        var reviews = AppHost.Reviews;
        reviews.WindowOpen = true;

        ReviewResult result;
        if (_mode == Mode.Resume && !reviews.IsRunning && reviews.Pending is { } pending)
        {
            result = pending; // opened from the "found N things" notification
            reviews.ClearPending();
        }
        else if (_mode == Mode.Resume && !reviews.IsRunning)
        {
            SetScope();
            BusyPanel.Visibility = Visibility.Collapsed;
            ShowMessage("Nothing waiting for you", "Use Today or Last days on the Reminders screen to run a review.", "Close");
            return;
        }
        else
        {
            if (!reviews.IsRunning)
                _ = reviews.Start(_mode == Mode.Today, AppHost.Reminders.GetSettings().ReviewDaysBack);

            SetScope();
            StopButton.Visibility = Visibility.Visible;
            SecondaryButton.Content = "Run in background";
            _clock.Start();
            SaveBusySnapshot();
            result = await reviews.Current!;
            _clock.Stop();
            if (!IsLoaded) return; // closed meanwhile: the coordinator notified instead

            reviews.MarkSeen(result.Suggestions);
        }

        SetScope();
        StopButton.Visibility = Visibility.Collapsed;
        BusyPanel.Visibility = Visibility.Collapsed;

        if (result.Error != null)
        {
            ShowMessage("The review didn't finish", result.Error, "Close");
        }
        else if (result.Suggestions.Count == 0)
        {
            ShowMessage("Nothing new to remind you about",
                result.SkippedAsDuplicates > 0
                    ? $"Claude found {result.SkippedAsDuplicates} item{Plural(result.SkippedAsDuplicates)} you already have reminders for."
                    : "Claude didn't find pending items in this period.",
                "Close");
        }
        else
        {
            _suggestions = result.Suggestions;
            SuggestionsList.ItemsSource = _suggestions;
            ResultsPanel.Visibility = Visibility.Visible;
            CreateButton.Visibility = Visibility.Visible;
            SecondaryButton.Content = "Cancel";
            UpdateSummary(result.SkippedAsDuplicates);
        }

        SaveDiagSnapshot();
    }

    private void UpdateSummary(int? skippedAtReview = null)
    {
        if (skippedAtReview.HasValue) _skippedAtReview = skippedAtReview.Value;

        var selected = _suggestions.Count(s => s.Selected);
        var text = $"{_suggestions.Count} suggestion{Plural(_suggestions.Count)}, {selected} selected.";
        if (_skippedAtReview > 0)
            text += $" {_skippedAtReview} skipped because you already have them.";
        SummaryText.Text = text;
        CreateButton.IsEnabled = selected > 0;
    }

    private void ShowMessage(string title, string body, string closeLabel)
    {
        ResultsPanel.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
        MessageTitle.Text = title;
        MessageBody.Text = body;
        SecondaryButton.Content = closeLabel;
    }

    private void Selection_Changed(object sender, RoutedEventArgs e) => UpdateSummary();

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var (created, skipped) = AppHost.Review.CreateSelected(_suggestions);
        Logger.Log($"Review: created {created} reminders, {skipped} skipped as duplicates");

        ShowMessage(
            created == 0 ? "No reminders created" : $"Created {created} reminder{Plural(created)}",
            skipped > 0 ? $"{skipped} skipped because a reminder with the same title already exists." : "You'll find them on the Reminders screen.",
            "Done");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        AppHost.Reviews.Cancel();
    }

    private void AutoReviewCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AutoReviewCombo.SelectedItem is not ReviewIntervalOption option) return;
        if (option.Hours == AppHost.Reminders.GetSettings().AutoReviewHours) return;

        AppHost.Reminders.UpdateSettings(s => s.AutoReviewHours = option.Hours);
        Logger.Log($"Review: check automatically = {option.Display}");
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    private void SaveBusySnapshot()
    {
        if (DiagSnapshot.Dir == null) return;
        Dispatcher.BeginInvoke(() => DiagSnapshot.Save((FrameworkElement)Content, "review-busy", Background),
            DispatcherPriority.ApplicationIdle);
    }

    private void SaveDiagSnapshot()
    {
        if (DiagSnapshot.Dir == null) return;
        var name = _mode == Mode.Resume ? "review-pending" : "review";
        Dispatcher.BeginInvoke(() =>
        {
            DiagSnapshot.Save((FrameworkElement)Content, name, Background);
            if (CreateButton.Visibility == Visibility.Visible)
                Create_Click(this, new RoutedEventArgs());

            Dispatcher.BeginInvoke(() =>
            {
                DiagSnapshot.Save((FrameworkElement)Content, name + "-done", Background);
                Logger.Log("DiagSnapshot: done, exiting test instance");
                Environment.Exit(0);
            }, DispatcherPriority.ApplicationIdle);
        }, DispatcherPriority.ApplicationIdle);
    }
}
