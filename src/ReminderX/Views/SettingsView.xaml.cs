using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ReminderX.Models;
using ReminderX.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using RadioButton = System.Windows.Controls.RadioButton;

namespace ReminderX.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        AppHost.Updates.StateChanged += () => Dispatcher.Invoke(UpdateUpdaterUi);
    }

    public void Refresh()
    {
        var settings = AppHost.Reminders.GetSettings();
        ThemeSystem.IsChecked = settings.Theme is not ("Light" or "Dark");
        ThemeLight.IsChecked = settings.Theme == "Light";
        ThemeDark.IsChecked = settings.Theme == "Dark";
        VersionRun.Text = AppInfo.VersionText;
        CreditsText.Text = AppInfo.Credits;
        RepoLinkButton.Content = AppInfo.RepoUrl.Replace("https://", "");
        AutoUpdateCheck.IsChecked = settings.AutoUpdate;
        UpdateUpdaterUi();
        StartWithWindowsCheck.IsChecked = AutostartService.IsEnabled();
        SoundCheck.IsChecked = settings.SoundEnabled;
        FullScreenAlertsCheck.IsChecked = settings.AlertStyle == AlertStyle.FullScreen;
        foreach (var segment in ReviewDaysSegments.Children.OfType<RadioButton>())
            segment.IsChecked = (string)segment.Tag == settings.ReviewDaysBack.ToString();
        McpEnabledCheck.IsChecked = settings.McpServerEnabled;
        PortBox.Text = settings.McpServerPort.ToString();
        UpdateMcpStatus();
    }

    private void UpdateMcpStatus()
    {
        var mcp = AppHost.Mcp;
        McpStatusText.Text = mcp.IsRunning
            ? $"Running at http://127.0.0.1:{mcp.Port}/mcp"
            : (mcp.LastError != null ? $"Not running: {mcp.LastError}" : "Not running");
        UpdateClaudeStatus();
    }

    private void UpdateClaudeStatus()
    {
        var port = AppHost.Reminders.GetSettings().McpServerPort;
        var (desktop, code) = ClaudeIntegration.GetStatus(port);

        static string Describe(string name, ClaudeClientState state) => state switch
        {
            ClaudeClientState.Connected => $"{name}: connected",
            ClaudeClientState.WrongPort => $"{name}: points to another port",
            ClaudeClientState.Missing => $"{name}: not connected",
            _ => $"{name}: not installed"
        };

        ClaudeStatusText.Text = $"{Describe("Claude Desktop", desktop)}  ·  {Describe("Claude Code", code)}";
        McpCommandText.Text = ClaudeIntegration.ClaudeCodeCommand(port);

        // The button is always there ("run it again" when Claude doesn't see ReminderX even though the
        // entry looks right), but it's the primary action only when something is missing.
        var needsAction = desktop is ClaudeClientState.Missing or ClaudeClientState.WrongPort ||
                          code is ClaudeClientState.Missing or ClaudeClientState.WrongPort;
        ReconnectButton.Content = needsAction ? "Connect Claude" : "Connect again";
        ReconnectButton.Style = (Style)FindResource(needsAction ? "Button.Primary" : "Button.Secondary");

        // The Claude Code command only when Claude Code doesn't point at this port (e.g. the port changed).
        ClaudeConnectPanel.Visibility = code is ClaudeClientState.Missing or ClaudeClientState.WrongPort
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ReconnectClaude_Click(object sender, RoutedEventArgs e)
    {
        if (AppHost.IsTestMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REMINDERX_TEST_CLAUDE_CONFIG")))
        {
            Logger.Log("Reconnect Claude skipped in test mode (would edit the real Claude configs)");
            return;
        }

        var port = AppHost.Reminders.GetSettings().McpServerPort;
        var exePath = Environment.ProcessPath ?? "";
        var owner = Window.GetWindow(this);
        ReconnectButton.IsEnabled = false;
        ReconnectButton.Content = "Connecting…";
        try
        {
            var result = await Task.Run(() => ClaudeIntegration.Register(exePath, port));
            Logger.Log($"Connect Claude on port {port}: desktop {result.Desktop} (changed {result.DesktopChanged}), " +
                       $"code {result.Code}{(result.DesktopError != null ? ", error: " + result.DesktopError : "")}");

            if (result.DesktopError != null)
            {
                MessageBox.Show(owner, $"ReminderX couldn't be added to Claude Desktop: {result.DesktopError}.",
                    "ReminderX", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (ClaudeIntegration.IsDesktopRunning())
            {
                // Claude Desktop only reads its config at startup, so a restart is what makes it show up.
                var question = result.DesktopChanged
                    ? "ReminderX was added to Claude Desktop. Restart Claude Desktop now so it loads it?"
                    : "Claude Desktop already has ReminderX in its config. Restart Claude Desktop now to load it again?";
                if (MessageBox.Show(owner, question, "ReminderX", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes)
                {
                    await Task.Run(ClaudeIntegration.RestartDesktop);
                }
            }
            else
            {
                MessageBox.Show(owner, "ReminderX is connected. Claude Desktop will load it the next time it starts.",
                    "ReminderX", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Connect Claude failed: {ex.Message}");
            MessageBox.Show(owner, $"Couldn't update Claude: {ex.Message}", "ReminderX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ReconnectButton.IsEnabled = true;
            UpdateClaudeStatus();
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string theme }) return;
        AppHost.Reminders.UpdateSettings(s => s.Theme = theme);
        App.ApplyTheme();
    }

    private void ReviewDays_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var days))
            AppHost.Reminders.UpdateSettings(s => s.ReviewDaysBack = days);
    }

    private void UpdateUpdaterUi()
    {
        var updates = AppHost.Updates;
        UpdateStatusText.Text = updates.StatusText;

        var busy = updates.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
        CheckUpdatesButton.IsEnabled = !busy;

        var canInstall = updates.State is UpdateState.Available or UpdateState.ReadyToInstall;
        InstallUpdateButton.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Content = $"Install {updates.LatestVersionText}";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await AppHost.Updates.CheckNowAsync();

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await AppHost.Updates.InstallNowAsync();

    private void AutoUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        var enabled = AutoUpdateCheck.IsChecked == true;
        AppHost.Reminders.UpdateSettings(s => s.AutoUpdate = enabled);
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppInfo.RepoUrl) { UseShellExecute = true });

    private void FullScreenAlertsCheck_Click(object sender, RoutedEventArgs e)
    {
        var style = FullScreenAlertsCheck.IsChecked == true ? AlertStyle.FullScreen : AlertStyle.Toast;
        AppHost.Reminders.UpdateSettings(s => s.AlertStyle = style);
    }

    private void PreviewAlert_Click(object sender, RoutedEventArgs e)
    {
        AppHost.Alerts.Enqueue(new Reminder
        {
            Title = "This is how a reminder looks",
            Description = "Complete, snooze or open it from here. Enter completes, Esc snoozes.",
            DueAt = DateTime.Now
        }, isPreview: true);
    }

    private void StartWithWindowsCheck_Click(object sender, RoutedEventArgs e)
    {
        var enabled = StartWithWindowsCheck.IsChecked == true;
        var ok = enabled ? AutostartService.Enable() : AutostartService.Disable();
        if (!ok)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Couldn't update the Windows logon task. Try running ReminderXSetup.exe again.",
                "ReminderX", MessageBoxButton.OK, MessageBoxImage.Warning);
            StartWithWindowsCheck.IsChecked = AutostartService.IsEnabled();
        }
        AppHost.Reminders.UpdateSettings(s => s.StartWithWindows = enabled);
    }

    private void SoundCheck_Click(object sender, RoutedEventArgs e)
    {
        var enabled = SoundCheck.IsChecked == true;
        AppHost.Reminders.UpdateSettings(s => s.SoundEnabled = enabled);
    }

    private async void McpEnabledCheck_Click(object sender, RoutedEventArgs e)
    {
        var enabled = McpEnabledCheck.IsChecked == true;
        AppHost.Reminders.UpdateSettings(s => s.McpServerEnabled = enabled);

        if (enabled)
            await AppHost.Mcp.StartAsync(AppHost.Reminders.GetSettings().McpServerPort);
        else
            await AppHost.Mcp.StopAsync();

        UpdateMcpStatus();
    }

    private async void ApplyPort_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a port number between 1024 and 65535.",
                "ReminderX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AppHost.Reminders.UpdateSettings(s => s.McpServerPort = port);
        if (AppHost.Reminders.GetSettings().McpServerEnabled)
            await AppHost.Mcp.StartAsync(port);

        UpdateMcpStatus();
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(McpCommandText.Text); }
        catch { /* clipboard can be locked by another app; not critical */ }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        PathService.EnsureFolders();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PathService.RootFolder}\"") { UseShellExecute = true });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!System.IO.File.Exists(PathService.LogFile))
        {
            MessageBox.Show(Window.GetWindow(this), "No log file yet.", "ReminderX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(PathService.LogFile) { UseShellExecute = true });
    }
}
