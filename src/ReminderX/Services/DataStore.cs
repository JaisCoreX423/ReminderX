using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReminderX.Models;

namespace ReminderX.Services;

/// <summary>
/// Thread-safe JSON-backed store for all app data. Single writer at a time via lock,
/// atomic save (write to temp file then replace) so a crash never corrupts data.json.
/// </summary>
public class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();
    private AppData _data;

    public DataStore()
    {
        PathService.EnsureFolders();
        _data = Load();
    }

    public event Action? DataChanged;

    private AppData Load()
    {
        try
        {
            if (File.Exists(PathService.DataFile))
            {
                var json = File.ReadAllText(PathService.DataFile);
                var loaded = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
                if (loaded != null)
                    return loaded;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load data.json, starting fresh: {ex.Message}");
            TryBackupCorruptFile();
        }

        return new AppData();
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(PathService.DataFile))
            {
                var backup = PathService.DataFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(PathService.DataFile, backup, overwrite: true);
            }
        }
        catch { /* best effort */ }
    }

    public void Save()
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_data, JsonOptions);
            var tempFile = PathService.DataFile + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Copy(tempFile, PathService.DataFile, overwrite: true);
            File.Delete(tempFile);
        }

        DataChanged?.Invoke();
    }

    public T Read<T>(Func<AppData, T> selector)
    {
        lock (_lock)
        {
            return selector(_data);
        }
    }

    public void Mutate(Action<AppData> mutation)
    {
        lock (_lock)
        {
            mutation(_data);
        }
        Save();
    }

    public string SaveAttachment(string reminderId, string sourceFilePath)
    {
        var destDir = Path.Combine(PathService.AttachmentsFolder, reminderId);
        Directory.CreateDirectory(destDir);
        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(destDir, fileName);

        var i = 1;
        while (File.Exists(destPath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(destDir, $"{name} ({i}){ext}");
            i++;
        }

        File.Copy(sourceFilePath, destPath);
        return Path.GetRelativePath(PathService.AttachmentsFolder, destPath);
    }

    public string GetAttachmentFullPath(Attachment attachment) =>
        Path.Combine(PathService.AttachmentsFolder, attachment.RelativePath);

    public void DeleteAttachmentFolder(string reminderId)
    {
        var dir = Path.Combine(PathService.AttachmentsFolder, reminderId);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Logger.Log($"Could not delete attachments for {reminderId}: {ex.Message}"); }
        }
    }
}
