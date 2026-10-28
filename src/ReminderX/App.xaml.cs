using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using ReminderX.Models;
using ReminderX.Services;
using ReminderX.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ReminderX;

public partial class App : Application
{
    // Instances on a test data folder always get their own identity, so they can never signal or
    // block the real running app.
    private static readonly string InstanceSuffix =
        "6C2B9B4E"
        + (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REMINDERX_DATA_DIR")) ? "" : ".test")
        + Environment.GetEnvironmentVariable("REMINDERX_TEST_SUFFIX");

    public static readonly string SingleInstanceMutexName = "Local\\ReminderX.SingleInstance." + InstanceSuffix;

    /// <summary>A second launch (taskbar pin, Start menu, desktop shortcut) sets this so the running copy shows itself.</summary>
    private static readonly string ShowSignalName = "Local\\ReminderX.ShowSignal." + InstanceSuffix;

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showSignalWait;
    private TrayService? _tray;
    private MainWindow? _mainWindow;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
    private const int AsfwAny = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Claude Desktop launches this mode as a command-based MCP server; it's only a relay to the
        // running app, so it skips everything else (no window, no tray, no single-instance check).
        if (e.Args.Contains("--mcp-stdio", StringComparer.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                int code;
                try { code = await McpStdioBridge.RunAsync(); }
                catch (Exception ex) { Logger.Log($"MCP stdio bridge failed: {ex.Message}"); code = 1; }
                await Dispatcher.InvokeAsync(() => Shutdown(code));
            });
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log($"UNHANDLED (UI thread): {args.Exception}");
            MessageBox.Show(
                $"ReminderX hit an unexpected error and needs to close this window.\n\n{args.Exception.Message}",
                "ReminderX", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; // keep the app (and tray icon) alive rather than taking the whole process down
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Log($"UNHANDLED (non-UI thread, fatal={args.IsTerminating}): {args.ExceptionObject}");

        var startHidden = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase);

        _mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            if (!startHidden) SignalRunningInstanceToShow();
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showSignalWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                Logger.Log("Show signal received from a second launch");
                ShowMainWindow();
            }), null, Timeout.Infinite, executeOnlyOnce: false);

        AppHost.Initialize();
        AppHost.Alerts.OpenReminderRequested += id => ShowMainWindow(focusReminderId: id);

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General) ApplyTheme();
        };

        AppHost.Updates.IsIdle = () => (_mainWindow == null || !_mainWindow.IsVisible) && !AppHost.Alerts.IsActive;
        AppHost.Updates.RequestShutdown = () => Dispatcher.BeginInvoke(ShutdownApp);
        if (!AppHost.IsTestMode || Environment.GetEnvironmentVariable("REMINDERX_TEST_UPDATER") == "1")
            AppHost.Updates.Start();

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        _tray = new TrayService();
        _tray.OpenRequested += () => Dispatcher.Invoke(() => ShowMainWindow());
        _tray.NewReminderRequested += () => Dispatcher.Invoke(() => ShowMainWindow(openNewReminder: true));
        _tray.NewTodoRequested += () => Dispatcher.Invoke(() => ShowMainWindow(openNewTodo: true));
        _tray.ExitRequested += () => Dispatcher.Invoke(ShutdownApp);

        // Test instances can hold a copy of real data; they must never pop real reminders on screen.
        if (!AppHost.IsTestMode || Environment.GetEnvironmentVariable("REMINDERX_TEST_SCHEDULER") == "1")
            AppHost.Scheduler.Start();

        var settings = AppHost.Reminders.GetSettings();
        if (settings.McpServerEnabled && !AppHost.IsTestMode)
        {
            _ = AppHost.Mcp.StartAsync(settings.McpServerPort);
        }
        else if (AppHost.IsTestMode &&
                 int.TryParse(Environment.GetEnvironmentVariable("REMINDERX_TEST_MCP_PORT"), out var testPort))
        {
            _ = AppHost.Mcp.StartAsync(testPort);
        }

        if (!startHidden)
        {
            ShowMainWindow();
        }

        RunTestHooks(e.Args);
    }

    private static void SignalRunningInstanceToShow()
    {
        try
        {
            // The new process owns the user's click, so it can hand foreground rights to the running copy.
            AllowSetForegroundWindow(AsfwAny);
            using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
            signal.Set();
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not signal the running instance: {ex.Message}");
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        var action = args.Contains(ToastArgsKeys.Action) ? args[ToastArgsKeys.Action] : null;
        var reminderId = args.Contains(ToastArgsKeys.ReminderId) ? args[ToastArgsKeys.ReminderId] : null;

        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrEmpty(reminderId)) return;

            switch (action)
            {
                case ToastActions.Complete:
                    AppHost.Reminders.CompleteReminder(reminderId);
                    break;
                case ToastActions.Snooze:
                    AppHost.Reminders.SnoozeReminder(reminderId, TimeSpan.FromMinutes(10));
                    break;
                case ToastActions.Dismiss:
                    AppHost.Reminders.DismissReminder(reminderId);
                    break;
                case ToastActions.Open:
                default:
                    ShowMainWindow(focusReminderId: reminderId);
                    break;
            }
        });
    }

    public void ShowMainWindow(bool openNewReminder = false, bool openNewTodo = false, string? focusReminderId = null)
    {
        _mainWindow ??= new MainWindow();

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        // Activate() alone can leave the window behind others; a Topmost flip reliably brings it forward.
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();

        if (openNewReminder) _mainWindow.RequestNewReminder();
        else if (openNewTodo) _mainWindow.RequestNewTodo();
        else if (focusReminderId != null) _mainWindow.FocusReminder(focusReminderId);
    }

    public void HideMainWindow()
    {
        _mainWindow?.Hide();
    }

    private void ShutdownApp()
    {
        AppHost.Scheduler.Dispose();
        _ = AppHost.Mcp.StopAsync();
        _tray?.Dispose();
        _showSignalWait?.Unregister(null);
        _showSignal?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    /// <summary>Test-only launch flags; inert in normal use.</summary>
    private void RunTestHooks(string[] args)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("REMINDERX_TEST_LIFETIME_SEC"), out var lifetime) && lifetime > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(lifetime) };
            timer.Tick += (_, _) =>
            {
                Logger.Log("Test lifetime reached, exiting");
                Environment.Exit(0);
            };
            timer.Start();
        }

        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

        if (Has("--test-new-reminder"))
        {
            Dispatcher.BeginInvoke(() =>
            {
                Logger.Log("DIAG: auto-triggering New Reminder dialog");
                ShowMainWindow(openNewReminder: true);
            });
        }
        else if (Has("--test-new-todo"))
        {
            Dispatcher.BeginInvoke(() =>
            {
                Logger.Log("DIAG: auto-triggering New To-Do dialog");
                ShowMainWindow(openNewTodo: true);
            });
        }
        else if (Has("--test-settings"))
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowMainWindow();
                _mainWindow!.ShowSettings();
                Dispatcher.BeginInvoke(() =>
                {
                    DiagSnapshot.Save((FrameworkElement)_mainWindow.Content, "settings", _mainWindow.Background);
                    DiagSnapshot.Save(_mainWindow.SettingsPanel.SettingsStack, "settings-full", _mainWindow.Background);
                    _mainWindow.NavReminders.IsChecked = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        DiagSnapshot.Save((FrameworkElement)_mainWindow.Content, "main", _mainWindow.Background);
                        Environment.Exit(0);
                    }, DispatcherPriority.ApplicationIdle);
                }, DispatcherPriority.ApplicationIdle);
            });
        }
        else if (Has("--test-review"))
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShowMainWindow();
                _mainWindow!.RemindersPanel.OpenReview(todayOnly: false);
            });
        }
        else if (Has("--test-alert"))
        {
            if (DiagSnapshot.Dir is { } snapshotDir)
            {
                System.IO.Directory.CreateDirectory(snapshotDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(snapshotDir, "chime.wav"), ChimePlayer.BuildWav());
            }

            Dispatcher.BeginInvoke(() =>
            {
                AppHost.Alerts.Enqueue(new Reminder
                {
                    Title = "Send the weekly metrics",
                    Description = "Export the Meraki usage report and send it to leadership before the 10:00 call.",
                    DueAt = DateTime.Today.AddHours(9),
                    Recurrence = new RecurrenceRule { Unit = RecurrenceUnit.Weeks, Interval = 1, TimeOfDay = new TimeSpan(9, 0, 0) }
                }, isPreview: true);
                AppHost.Alerts.Enqueue(new Reminder
                {
                    Title = "Call Rackspace about the P2P patch",
                    DueAt = DateTime.Today.AddHours(9).AddMinutes(30)
                }, isPreview: true);
            });
        }
    }

    /// <summary>Applies Settings > Appearance (System / Light / Dark). "System" follows Windows' app theme.</summary>
    public static void ApplyTheme()
    {
        var choice = Environment.GetEnvironmentVariable("REMINDERX_FORCE_THEME") is { Length: > 0 } forced
            ? forced
            : AppHost.Reminders.GetSettings().Theme;

        bool isLight;
        if (string.Equals(choice, "Dark", StringComparison.OrdinalIgnoreCase)) isLight = false;
        else if (string.Equals(choice, "Light", StringComparison.OrdinalIgnoreCase)) isLight = true;
        else isLight = WindowsUsesLightTheme();

        var app = Current;
        var dict = app.Resources.MergedDictionaries;
        var target = isLight ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";

        for (var i = 0; i < dict.Count; i++)
        {
            if (dict[i].Source != null && dict[i].Source!.OriginalString.Contains("Colors."))
            {
                if (dict[i].Source!.OriginalString != target)
                    dict[i] = new ResourceDictionary { Source = new Uri(target, UriKind.Relative) };
                break;
            }
        }
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int i) return i != 0;
        }
        catch { /* fall through to light */ }
        return true;
    }
}
