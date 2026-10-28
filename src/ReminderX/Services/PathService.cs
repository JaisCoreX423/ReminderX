using System.IO;

namespace ReminderX.Services;

public static class PathService
{
    // REMINDERX_DATA_DIR lets test runs use a throwaway folder so they never read, write or
    // delete the real user's data.
    public static string RootFolder { get; } =
        Environment.GetEnvironmentVariable("REMINDERX_DATA_DIR") is { Length: > 0 } overrideDir
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReminderX");

    public static string DataFile => Path.Combine(RootFolder, "data.json");

    public static string AttachmentsFolder => Path.Combine(RootFolder, "Attachments");

    public static string LogFile => Path.Combine(RootFolder, "reminderx.log");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(AttachmentsFolder);
    }
}
