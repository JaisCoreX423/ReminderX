namespace ReminderX.Models;

public class Attachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Original file name shown to the user.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Path relative to the app's Attachments folder.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public bool IsImage { get; set; }
}
