using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ReminderX.Services;

namespace ReminderX.Views;

public partial class ReviewWindow : Window
{
    private readonly bool _todayOnly;
    private readonly int _daysBack;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<ReviewSuggestion> _suggestions = new();
    private bool _running;
    private int _skippedAtReview;

    public ReviewWindow(bool todayOnly, int daysBack)
    {
        InitializeComponent();
        _todayOnly = todayOnly;
        _daysBack = daysBack;

        ScopeText.Text = todayOnly
            ? "Today's email, meetings and chats"
            : $"Email, meetings and chats from the last {daysBack} day{(daysBack == 1 ? "" : "s")}";

        _clock.Tick += (_, _) => ElapsedText.Text = $"{(int)_elapsed.Elapsed.TotalMinutes}:{_elapsed.Elapsed.Seconds:00}";
        Loaded += async (_, _) => await RunAsync();
        Closed += (_, _) =>
        {
            _clock.Stop();
            if (_running) _cancel.Cancel();
        };
    }

    private async Task RunAsync()
    {
        _running = true;
        _elapsed.Start();
        _clock.Start();

        var result = await AppHost.Review.RunAsync(_todayOnly, _daysBack, _cancel.Token);

        _running = false;
        _clock.Stop();
        if (!IsLoaded) return;

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

    private static string Plural(int n) => n == 1 ? "" : "s";

    private void SaveDiagSnapshot()
    {
        if (DiagSnapshot.Dir == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            DiagSnapshot.Save((FrameworkElement)Content, "review", Background);
            if (CreateButton.Visibility == Visibility.Visible)
                Create_Click(this, new RoutedEventArgs());

            Dispatcher.BeginInvoke(() =>
            {
                DiagSnapshot.Save((FrameworkElement)Content, "review-done", Background);
                Logger.Log("DiagSnapshot: done, exiting test instance");
                Environment.Exit(0);
            }, DispatcherPriority.ApplicationIdle);
        }, DispatcherPriority.ApplicationIdle);
    }
}
