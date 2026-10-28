using System.IO;

namespace ReminderX.Services;

public static class Logger
{
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                PathService.EnsureFolders();
                File.AppendAllText(PathService.LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
