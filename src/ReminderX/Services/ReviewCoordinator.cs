using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ReminderX.Services;

/// <summary>
/// Owns the "Review Outlook &amp; Teams" run so it keeps going after its window is closed, runs it on
/// a schedule (Settings > Check automatically), and notifies when it finds something while no
/// review window is open. Lives on the UI thread.
/// </summary>
public class ReviewCoordinator
{
    /// <summary>Automatic runs only start inside these hours, so they don't spend Claude usage overnight.</summary>
    private const int AutoStartHour = 7, AutoEndHour = 20;

    private const int MaxSeenTitles = 300;

    private readonly AiReviewService _review;
    private readonly ReminderService _reminders;
    private CancellationTokenSource? _cancel;
    private DispatcherTimer? _timer;

    public ReviewCoordinator(AiReviewService review, ReminderService reminders)
    {
        _review = review;
        _reminders = reminders;
    }

    public Task<ReviewResult>? Current { get; private set; }
    public bool IsRunning => Current is { IsCompleted: false };
    public bool CurrentTodayOnly { get; private set; }
    public int CurrentDaysBack { get; private set; }
    public bool CurrentIsAutomatic { get; private set; }
    public DateTime StartedAt { get; private set; }

    /// <summary>Finished suggestions nobody has looked at yet (opened from the notification).</summary>
    public ReviewResult? Pending { get; private set; }

    /// <summary>True while a ReviewWindow is showing; finished runs then go to the window, not a notification.</summary>
    public bool WindowOpen { get; set; }

    /// <summary>Running state or pending result changed.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when a notification can't be shown as a Windows toast (App shows a tray balloon).</summary>
    public event Action<string, string>? FallbackNotify;

    /// <summary>Starts the timer that runs automatic reviews.</summary>
    public void StartSchedule()
    {
        var tick = int.TryParse(Environment.GetEnvironmentVariable("REMINDERX_TEST_REVIEW_TICK_SEC"), out var s) ? s : 60;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(tick) };
        _timer.Tick += (_, _) => CheckSchedule();
        _timer.Start();
    }

    private void CheckSchedule()
    {
        var settings = _reminders.GetSettings();
        if (settings.AutoReviewHours <= 0 || IsRunning) return;

        var now = DateTime.Now;
        var anyHour = Environment.GetEnvironmentVariable("REMINDERX_TEST_REVIEW_ANY_HOUR") == "1";
        if (!anyHour && (now.Hour < AutoStartHour || now.Hour >= AutoEndHour)) return;

        var interval = TimeSpan.FromHours(settings.AutoReviewHours);
        if (settings.LastReviewAt is { } last && now - last < interval) return;
        if (_lastAttemptAt is { } attempt && now - attempt < interval) return; // failed runs don't retry every minute

        // Only what's new since the previous review, with an overlap so nothing falls between two runs.
        // First run ever: today (daily: since yesterday). PC off for days: at most 3 days back.
        var daily = settings.AutoReviewHours >= 24;
        var since = settings.LastReviewAt is { } previous
            ? previous - SinceOverlap
            : (daily ? now.Date.AddDays(-1) : now.Date);
        if (since < now.AddDays(-3)) since = now.AddDays(-3);

        Logger.Log($"Review: automatic run (every {settings.AutoReviewHours} h), since {since:yyyy-MM-dd HH:mm}");
        _ = Start(todayOnly: !daily, daysBack: 1, automatic: true, since: since);
    }

    private static readonly TimeSpan SinceOverlap = TimeSpan.FromMinutes(15);

    private DateTime? _lastAttemptAt;

    /// <summary>Set for automatic runs: they only look at activity after this moment.</summary>
    public DateTime? CurrentSince { get; private set; }

    /// <summary>Starts a review, or returns the one already running.</summary>
    public Task<ReviewResult> Start(bool todayOnly, int daysBack, bool automatic = false, DateTime? since = null)
    {
        if (IsRunning) return Current!;

        CurrentTodayOnly = todayOnly;
        CurrentDaysBack = daysBack;
        CurrentIsAutomatic = automatic;
        CurrentSince = since;
        StartedAt = DateTime.Now;
        _cancel = new CancellationTokenSource();
        Current = RunAsync(todayOnly, daysBack, automatic, since, _cancel.Token);
        StateChanged?.Invoke();
        return Current;
    }

    public void Cancel() => _cancel?.Cancel();

    private async Task<ReviewResult> RunAsync(bool todayOnly, int daysBack, bool automatic, DateTime? since, CancellationToken cancel)
    {
        var startedAt = DateTime.Now;
        _lastAttemptAt = startedAt;
        var result = await _review.RunAsync(todayOnly, daysBack, cancel, since);

        // The next automatic run starts from when this one began (messages that arrived while it ran
        // are covered next time). A failed or cancelled run doesn't move it, so nothing is skipped.
        if (result.Error == null)
            _reminders.UpdateSettings(s => s.LastReviewAt = startedAt);
        else
            Logger.Log($"Review failed{(automatic ? " (automatic, will retry next interval)" : "")}: {result.Error.Split('\n')[0]}");

        if (automatic)
        {
            // Only tell the user about things they haven't been shown before.
            var seen = _reminders.GetSettings().ReviewSeenTitles.ToHashSet();
            var before = result.Suggestions.Count;
            result.Suggestions.RemoveAll(s => seen.Contains(ReminderService.NormalizeTitle(s.Title)));
            if (before != result.Suggestions.Count)
                Logger.Log($"Review: {before - result.Suggestions.Count} suggestion(s) already shown before, not repeated");
        }

        if (WindowOpen)
        {
            // The open window shows it; nothing to notify.
        }
        else if (result.Suggestions.Count > 0)
        {
            Pending = result;
            MarkSeen(result.Suggestions);
            NotifyFound(result);
        }
        else if (!automatic && result.Error != null)
        {
            Notify("The review didn't finish", result.Error.Split('\n')[0]);
        }
        else if (!automatic)
        {
            Notify("Review finished", "Nothing new to remind you about.");
        }

        _ = Dispatcher.CurrentDispatcher.BeginInvoke(() => StateChanged?.Invoke());
        return result;
    }

    /// <summary>The window shows these, so automatic runs won't notify about them again.</summary>
    public void MarkSeen(IEnumerable<ReviewSuggestion> suggestions)
    {
        var titles = suggestions.Select(s => ReminderService.NormalizeTitle(s.Title)).ToList();
        _reminders.UpdateSettings(s =>
        {
            foreach (var t in titles.Where(t => !s.ReviewSeenTitles.Contains(t)))
                s.ReviewSeenTitles.Add(t);
            if (s.ReviewSeenTitles.Count > MaxSeenTitles)
                s.ReviewSeenTitles.RemoveRange(0, s.ReviewSeenTitles.Count - MaxSeenTitles);
        });
    }

    /// <summary>The window took the pending result, or the user acted on it.</summary>
    public void ClearPending()
    {
        if (Pending == null) return;
        Pending = null;
        StateChanged?.Invoke();
    }

    /// <summary>"Add all" from the notification.</summary>
    public void AddAllPending()
    {
        if (Pending == null) return;
        var (created, skipped) = _review.CreateSelected(Pending.Suggestions);
        Logger.Log($"Review: added {created} from the notification, {skipped} skipped as duplicates");
        ClearPending();
        Notify(created == 1 ? "Added 1 reminder" : $"Added {created} reminders",
            skipped > 0 ? $"{skipped} skipped because you already have them." : "You'll find them on the Reminders screen.");
    }

    // ---------------- Notifications ----------------

    private void NotifyFound(ReviewResult result)
    {
        var count = result.Suggestions.Count;
        var title = count == 1 ? "Claude found 1 thing to remind you about" : $"Claude found {count} things to remind you about";
        var lines = string.Join("\n", result.Suggestions.Take(3).Select(s => "• " + s.Title));
        if (count > 3) lines += $"\n…and {count - 3} more";

        var builder = new ToastContentBuilder()
            .AddArgument(ToastArgsKeys.Action, ToastActions.Review)
            .AddText(title)
            .AddText(lines, hintMaxLines: 2)
            .AddButton(new ToastButton()
                .SetContent("Review")
                .AddArgument(ToastArgsKeys.Action, ToastActions.Review))
            .AddButton(new ToastButton()
                .SetContent("Add all")
                .AddArgument(ToastArgsKeys.Action, ToastActions.ReviewAddAll)
                .SetBackgroundActivation())
            .SetToastDuration(ToastDuration.Long);

        Show(builder, title, lines);
    }

    private void Notify(string title, string text) =>
        Show(new ToastContentBuilder()
            .AddArgument(ToastArgsKeys.Action, ToastActions.Review)
            .AddText(title)
            .AddText(text, hintMaxLines: 2), title, text);

    private void Show(ToastContentBuilder builder, string title, string text)
    {
        if (!_reminders.GetSettings().SoundEnabled)
            builder.AddAudio(new ToastAudio { Silent = true });

        if (AppHost.IsTestMode)
        {
            Logger.Log($"Review toast built: \"{title}\" / {text.Replace('\n', ' ')} (test mode, not shown)");
            return;
        }

        try
        {
            builder.Show();
        }
        catch (Exception ex)
        {
            Logger.Log($"Review toast failed, using the tray balloon: {ex.Message}");
            FallbackNotify?.Invoke(title, text);
        }
    }
}
