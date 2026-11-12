using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReminderX.Services.Sync;

public sealed record IssuedDevice(string VaultId, string DeviceId, string Token);
public sealed record Pairing(string Code, long ExpiresAt);
public sealed record LinkedDevice(string Id, string Name, string Platform, long CreatedAt, long LastSeenAt, bool Self);

public sealed class SyncChange
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public long UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public string? Data { get; set; }
    public long Seq { get; set; }
    public string? From { get; set; }
}

public sealed class SyncResponse
{
    public int Accepted { get; set; }
    public long Cursor { get; set; }
    public bool More { get; set; }
    public List<SyncChange> Changes { get; set; } = new();
}

/// <summary>HTTP client for the Netlify sync service (sync/lib/api.mjs).</summary>
public sealed class SyncClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _baseUrl;
    private readonly string? _token;

    public SyncClient(string baseUrl, string? token = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _token = token;
    }

    public Task<IssuedDevice> CreateVaultAsync(string name, CancellationToken ct = default) =>
        SendAsync<IssuedDevice>(HttpMethod.Post, "/api/vaults", new { name }, ct);

    public Task<Pairing> StartPairingAsync(CancellationToken ct = default) =>
        SendAsync<Pairing>(HttpMethod.Post, "/api/pairings", new { }, ct);

    public async Task<List<LinkedDevice>> ListDevicesAsync(CancellationToken ct = default) =>
        (await SendAsync<DevicesResponse>(HttpMethod.Get, "/api/devices", null, ct)).Devices;

    public Task UnlinkDeviceAsync(string deviceId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Delete, $"/api/devices/{deviceId}", null, ct);

    public Task DeleteVaultAsync(CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Delete, "/api/vaults/current", null, ct);

    public Task<SyncResponse> SyncAsync(long since, IReadOnlyList<SyncChange> changes, CancellationToken ct = default) =>
        SendAsync<SyncResponse>(HttpMethod.Post, "/api/sync", new
        {
            since,
            changes = changes.Select(c => new { c.Id, c.Kind, c.UpdatedAt, c.Deleted, c.Data })
        }, ct);

    private sealed record DevicesResponse(List<LinkedDevice> Devices);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);
        if (_token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message;
            try { message = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false)).GetProperty("error").GetString() ?? ""; }
            catch { message = response.ReasonPhrase ?? ""; }
            throw new SyncHttpException((int)response.StatusCode, message);
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false))!;
    }
}

public sealed class SyncHttpException(int status, string message) : Exception($"{message} (HTTP {status})")
{
    public int Status { get; } = status;
}
