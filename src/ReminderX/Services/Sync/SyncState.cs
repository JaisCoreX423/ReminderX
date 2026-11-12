using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReminderX.Services.Sync;

/// <summary>What the last sync cycle knew about one record: its fingerprint and version.</summary>
public sealed class KnownRecord
{
    public string Kind { get; set; } = "";
    public string Hash { get; set; } = "";
    public long UpdatedAt { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Sync link of this PC, kept in sync.json next to data.json. The device token and the vault
/// encryption key are DPAPI-protected (current Windows user), so a copied file is useless elsewhere.
/// </summary>
public sealed class SyncState
{
    public string? ServerUrl { get; set; }
    public string? VaultId { get; set; }
    public string? DeviceId { get; set; }
    public string? TokenProtected { get; set; }
    public string? KeyProtected { get; set; }
    public long Cursor { get; set; }
    public Dictionary<string, KnownRecord> Known { get; set; } = new();
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }

    [JsonIgnore]
    public bool IsLinked =>VaultId != null && TokenProtected != null && KeyProtected != null;

    // Decrypted views of the protected fields: never written to sync.json.
    [JsonIgnore]
    public string Token
    {
        get => Unprotect(TokenProtected);
        set => TokenProtected = Protect(value);
    }

    [JsonIgnore]
    public byte[] Key
    {
        get => Base64Url.Decode(Unprotect(KeyProtected));
        set => KeyProtected = Protect(Base64Url.Encode(value));
    }

    private static string Protect(string value) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string? value) =>
        value == null ? throw new InvalidOperationException("Not linked")
            : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));

    private static string FilePath => Path.Combine(PathService.RootFolder, "sync.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static SyncState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(FilePath), JsonOptions) ?? new SyncState();
        }
        catch (Exception ex)
        {
            Logger.Log($"Sync: couldn't read sync.json ({ex.Message}); starting unlinked");
        }
        return new SyncState();
    }

    public void Save()
    {
        PathService.EnsureFolders();
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, FilePath, overwrite: true);
    }
}
