using System.Diagnostics;

namespace ReminderX.Services;

/// <summary>Manages the per-user "run at logon" scheduled task, the same mechanism install.ps1 sets up.</summary>
public static class AutostartService
{
    public const string TaskName = "ReminderX";

    public static bool IsEnabled()
    {
        try
        {
            var result = RunSchtasks($"/Query /TN \"{TaskName}\"");
            return result.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --background\" " +
                       $"/SC ONLOGON /RL LIMITED /RU \"{user}\" /IT /F";
            var result = RunSchtasks(args);
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Log($"Autostart enable failed: {ex.Message}");
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            var result = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Log($"Autostart disable failed: {ex.Message}");
            return false;
        }
    }

    private static Process RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var process = Process.Start(psi)!;
        process.WaitForExit(5000);
        return process;
    }
}
