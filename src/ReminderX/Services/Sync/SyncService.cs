using System.Security.Cryptography;
using Timer = System.Threading.Timer;

namespace ReminderX.Services.Sync;

public sealed record PairingInfo(string QrPayload, DateTime ExpiresAt);

/// <summary>
/// Keeps this PC's reminders and to-dos in sync with paired phones through the Netlify service.
/// Local edits are found by fingerprint (any path that changes data.json — window, MCP, alerts,
/// review — gets synced without hooks); each cycle pushes them and pulls what other devices changed.
/// Runs every 30 s, and 3 s after a local change.
/// </summary>
public sealed class SyncService
{
    public const string DefaultServerUrl = "https://reminderx-sync.netlify.app";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);
    private const int PushBatch = 200;

    private readonly ReminderService _reminders;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SyncState _state = new();
    private Timer? _loop;
    private Timer? _debounce;
    private volatile bool _applyingRemote;

    public SyncService(ReminderService reminders)
    {
        _reminders = reminders;
    }

    public static string ServerUrl =>
        Environment.GetEnvironmentVariable("REMINDERX_SYNC_URL") is { Length: > 0 } url ? url : DefaultServerUrl;

    public bool IsLinked => _state.IsLinked;
    public DateTime? LastSyncAt => _state.LastSyncAt;
    public string? LastError => _state.LastError;

    /// <summary>Raised (on a worker thread) after each cycle or link change.</summary>
    public event Action? StateChanged;

    public void Start()
    {
        _state = SyncState.Load();
        _reminders.Changed += () =>
        {
            if (!_applyingRemote && IsLinked) _debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);
        };
        _debounce = new Timer(_ => _ = SyncNowAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _loop = new Timer(_ => _ = SyncNowAsync(), null, TimeSpan.FromSeconds(5), Interval);
    }

    private SyncClient Client() => new(_state.ServerUrl ?? ServerUrl, _state.IsLinked ? _state.Token : null);

    // ---------------- Linking ----------------

    /// <summary>Creates this PC's vault on first use, then a one-time code for the QR.</summary>
    public async Task<PairingInfo> StartPairingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.IsLinked)
            {
                var issued = await new SyncClient(ServerUrl).CreateVaultAsync(Environment.MachineName);
                _state = new SyncState
                {
                    ServerUrl = ServerUrl,
                    VaultId = issued.VaultId,
                    DeviceId = issued.DeviceId,
                    Token = issued.Token,
                    Key = SyncCrypto.NewKey()
                };
                _state.Save();
                Logger.Log($"Sync: created vault {issued.VaultId}");
            }

            var pairing = await Client().StartPairingAsync();
            var payload = $"reminderx://pair?u={Uri.EscapeDataString(_state.ServerUrl!)}&v={_state.VaultId}" +
                          $"&c={pairing.Code}&k={Base64Url.Encode(_state.Key)}";
            return new PairingInfo(payload, DateTimeOffset.FromUnixTimeMilliseconds(pairing.ExpiresAt).LocalDateTime);
        }
        finally
        {
            _gate.Release();
        }
        // First push happens on the next cycle, so the phone finds everything when it pulls.
    }

    public Task<List<LinkedDevice>> ListDevicesAsync() => Client().ListDevicesAsync();

    public async Task UnlinkDeviceAsync(string deviceId)
    {
        await Client().UnlinkDeviceAsync(deviceId);
        Logger.Log($"Sync: unlinked device {deviceId}");
        StateChanged?.Invoke();
    }

    /// <summary>Deletes the vault (every phone loses access and the server copy is erased); local data stays.</summary>
    public async Task StopSyncingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_state.IsLinked)
            {
                try { await Client().DeleteVaultAsync(); }
                catch (SyncHttpException ex) when (ex.Status == 401) { /* already gone */ }
            }
            _state = new SyncState();
            _state.Save();
            Logger.Log("Sync: stopped, vault deleted");
        }
        finally
        {
            _gate.Release();
        }
        StateChanged?.Invoke();
    }

    // ---------------- Sync cycle ----------------

    public async Task SyncNowAsync()
    {
        if (!_state.IsLinked) return;
        if (!await _gate.WaitAsync(0)) return; // a cycle is already running
        try
        {
            await RunCycleAsync();
            _state.LastError = null;
        }
        catch (SyncHttpException ex) when (ex.Status == 401)
        {
            // The vault was deleted or this PC unlinked from elsewhere.
            Logger.Log("Sync: this PC is no longer linked; stopping");
            _state = new SyncState { LastError = "This PC was unlinked." };
        }
        catch (Exception ex)
        {
            _state.LastError = ex.Message;
            Logger.Log($"Sync failed: {ex.Message}");
        }
        finally
        {
            try { _state.Save(); } catch (Exception ex) { Logger.Log($"Sync: couldn't save sync.json: {ex.Message}"); }
            _gate.Release();
        }
        StateChanged?.Invoke();
    }

    private async Task RunCycleAsync()
    {
        var key = _state.Key;
        var client = Client();
        var outgoing = CollectLocalChanges(key);

        var applied = 0;
        var pushed = 0;
        var batches = outgoing.Chunk(PushBatch).ToList();
        if (batches.Count == 0) batches.Add(Array.Empty<SyncChange>());

        foreach (var batch in batches)
        {
            var response = await client.SyncAsync(_state.Cursor, batch);
            foreach (var change in batch)
            {
                _state.Known[change.Id] = new KnownRecord
                {
                    Kind = change.Kind, Hash = change.Data == null ? "" : _pendingHashes[change.Id],
                    UpdatedAt = change.UpdatedAt, Deleted = change.Deleted
                };
            }
            pushed += batch.Length;
            applied += ApplyRemote(response, key);
            _state.Cursor = response.Cursor;

            while (response.More)
            {
                response = await client.SyncAsync(_state.Cursor, Array.Empty<SyncChange>());
                applied += ApplyRemote(response, key);
                _state.Cursor = response.Cursor;
            }
        }
        _pendingHashes.Clear();

        _state.LastSyncAt = DateTime.Now;
        if (pushed > 0 || applied > 0)
            Logger.Log($"Sync: sent {pushed}, received {applied}");
        if (applied > 0) _reminders.NotifySyncApplied();
    }

    private readonly Dictionary<string, string> _pendingHashes = new();

    private List<SyncChange> CollectLocalChanges(byte[] key)
    {
        var (reminders, todos) = _reminders.GetSyncSnapshot();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changes = new List<SyncChange>();
        var present = new HashSet<string>();

        void Collect(string kind, string id, string json)
        {
            present.Add(id);
            var hash = SyncJson.Hash(json);
            if (_state.Known.TryGetValue(id, out var known) && !known.Deleted && known.Hash == hash) return;

            // Keep versions increasing even if this PC's clock is behind the last version seen.
            var updatedAt = Math.Max(now, (known?.UpdatedAt ?? 0) + 1);
            _pendingHashes[id] = hash;
            changes.Add(new SyncChange { Id = id, Kind = kind, UpdatedAt = updatedAt, Data = SyncCrypto.Encrypt(key, kind, id, json) });
        }

        foreach (var (id, json) in reminders) Collect("reminder", id, json);
        foreach (var (id, json) in todos) Collect("todo", id, json);

        foreach (var (id, known) in _state.Known)
        {
            if (known.Deleted || present.Contains(id)) continue;
            changes.Add(new SyncChange { Id = id, Kind = known.Kind, UpdatedAt = Math.Max(now, known.UpdatedAt + 1), Deleted = true });
        }
        return changes;
    }

    private int ApplyRemote(SyncResponse response, byte[] key)
    {
        var applied = 0;
        _applyingRemote = true;
        try
        {
            foreach (var change in response.Changes)
            {
                if (change.From == _state.DeviceId) continue; // our own push coming back
                if (_state.Known.TryGetValue(change.Id, out var known) && known.UpdatedAt >= change.UpdatedAt) continue;

                try
                {
                    string? localJson;
                    if (change.Kind == "reminder")
                    {
                        var record = change.Deleted ? null
                            : SyncJson.Deserialize<ReminderRecord>(SyncCrypto.Decrypt(key, change.Kind, change.Id, change.Data!));
                        localJson = _reminders.ApplySyncedReminder(change.Id, record);
                    }
                    else if (change.Kind == "todo")
                    {
                        var record = change.Deleted ? null
                            : SyncJson.Deserialize<TodoRecord>(SyncCrypto.Decrypt(key, change.Kind, change.Id, change.Data!));
                        localJson = _reminders.ApplySyncedTodo(change.Id, record);
                    }
                    else continue;

                    _state.Known[change.Id] = new KnownRecord
                    {
                        Kind = change.Kind,
                        Hash = localJson == null ? "" : SyncJson.Hash(localJson),
                        UpdatedAt = change.UpdatedAt,
                        Deleted = localJson == null
                    };
                    applied++;
                }
                catch (CryptographicException)
                {
                    Logger.Log($"Sync: couldn't decrypt {change.Kind} {change.Id} (different key?); skipped");
                }
            }
        }
        finally
        {
            _applyingRemote = false;
        }
        return applied;
    }
}
