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
        McpCommandText.Text = $"claude mcp add --transport http --scope user reminderx http://127.0.0.1:{mcp.Port}/mcp";
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
