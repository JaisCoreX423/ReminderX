using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;

namespace ReminderX.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Installing,
    Error
}

/// <summary>
/// Keeps ReminderX current from the GitHub repo's latest release: checks periodically, downloads the
/// ReminderXSetup.exe asset, verifies it against the SHA-256 digest GitHub publishes for it, and runs
/// it silently. Automatic installs only happen while the app is idle in the tray.
/// All members are used on the UI thread.
/// </summary>
public class UpdateService
{
    private const string InstallerAssetName = "ReminderXSetup.exe";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan IdleRetryInterval = TimeSpan.FromMinutes(5);

    private readonly ReminderService _reminders;
    private readonly HttpClient _http;
    private DispatcherTimer? _timer;
    private ReleaseInfo? _latest;
    private string? _downloadedInstaller;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string StatusText { get; private set; } = "";
    public string? LatestVersionText => _latest?.Version.ToString(3);

    public event Action? StateChanged;

    /// <summary>True when installing now wouldn't interrupt the user (main window hidden, no alert up).</summary>
    public Func<bool>? IsIdle { get; set; }

    /// <summary>Closes the app so the installer can replace its files.</summary>
    public Action? RequestShutdown { get; set; }

    private static bool IsDryRun =>
        AppHost.IsTestMode || Environment.GetEnvironmentVariable("REMINDERX_UPDATE_DRYRUN") == "1";

    public UpdateService(ReminderService reminders)
    {
        _reminders = reminders;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ReminderX/{AppInfo.VersionText}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        StatusText = $"You're on version {AppInfo.VersionText}.";
    }

    public void Start()
    {
        var firstDelay = int.TryParse(Environment.GetEnvironmentVariable("REMINDERX_TEST_UPDATE_DELAY_SEC"), out var s)
            ? TimeSpan.FromSeconds(s)
            : FirstCheckDelay;

        _timer = new DispatcherTimer { Interval = firstDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await AutoCycleAsync();
        };
        _timer.Start();
    }

    private async Task AutoCycleAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return;

        if (State != UpdateState.ReadyToInstall)
        {
            await CheckAsync();
            if (State != UpdateState.Available || !_reminders.GetSettings().AutoUpdate) return;
            await DownloadAsync();
            if (State != UpdateState.ReadyToInstall) return;
        }

        if (!_reminders.GetSettings().AutoUpdate) return;

        if (IsIdle?.Invoke() ?? true)
        {
            Install();
        }
        else
        {
            Logger.Log($"Update {LatestVersionText} ready; waiting until the app is idle");
            if (_timer != null) _timer.Interval = IdleRetryInterval;
        }
    }

    /// <summary>Settings > "Check now".</summary>
    public Task CheckNowAsync() => CheckAsync();

    /// <summary>Settings > "Install now": downloads if needed, then installs right away.</summary>
    public async Task InstallNowAsync()
    {
        if (State == UpdateState.Available) await DownloadAsync();
        if (State == UpdateState.ReadyToInstall) Install();
    }

    private async Task CheckAsync()
    {
        SetState(UpdateState.Checking, "Checking for updates…");
        try
        {
            var api = Environment.GetEnvironmentVariable("REMINDERX_UPDATE_API")
                      ?? $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest";

            using var response = await _http.GetAsync(api);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                SetState(UpdateState.UpToDate, $"No releases published yet. You're on version {AppInfo.VersionText}.");
                return;
            }
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var release = ReleaseInfo.Parse(doc.RootElement, InstallerAssetName);
            var current = Normalize(AppInfo.Version);

            if (release == null || release.Version <= current)
            {
                _latest = release;
                SetState(UpdateState.UpToDate, $"You're on the latest version ({AppInfo.VersionText}).");
                return;
            }

            _latest = release;
            if (release.DownloadUrl == null)
            {
                SetState(UpdateState.Error,
                    $"Version {LatestVersionText} is published but its installer isn't attached yet.");
                return;
            }

            SetState(UpdateState.Available, $"Version {LatestVersionText} is available.");
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"Couldn't check for updates: {ex.Message}");
        }
    }

    private async Task DownloadAsync()
    {
        if (_latest?.DownloadUrl == null) return;

        SetState(UpdateState.Downloading, $"Downloading version {LatestVersionText}…");
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ReminderX-update");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"ReminderXSetup-{LatestVersionText}.exe");

            using (var response = await _http.GetAsync(_latest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path);
                await response.Content.CopyToAsync(file);
            }

            var size = new FileInfo(path).Length;
            if (_latest.Size > 0 && size != _latest.Size)
            {
                File.Delete(path);
                SetState(UpdateState.Error, $"Download was incomplete ({size:N0} of {_latest.Size:N0} bytes).");
                return;
            }

            if (_latest.Sha256 != null)
            {
                string actual;
                await using (var stream = File.OpenRead(path))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

                if (actual != _latest.Sha256)
                {
                    File.Delete(path);
                    SetState(UpdateState.Error, "The downloaded installer didn't match GitHub's checksum; it was discarded.");
                    return;
                }
                Logger.Log($"Update {LatestVersionText} downloaded and SHA-256 verified ({size:N0} bytes)");
            }
            else
            {
                Logger.Log($"Update {LatestVersionText} downloaded ({size:N0} bytes); GitHub published no checksum for it");
            }

            _downloadedInstaller = path;
            SetState(UpdateState.ReadyToInstall, $"Version {LatestVersionText} is ready to install.");
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"Couldn't download the update: {ex.Message}");
        }
    }

    private void Install()
    {
        if (_downloadedInstaller == null || !File.Exists(_downloadedInstaller)) return;

        if (IsDryRun)
        {
            Logger.Log($"Update dry run: would install {LatestVersionText} from {_downloadedInstaller}");
            SetState(UpdateState.ReadyToInstall, $"Version {LatestVersionText} verified (dry run, not installed).");
            return;
        }

        SetState(UpdateState.Installing, $"Installing version {LatestVersionText}…");
        Logger.Log($"Installing update {LatestVersionText} from {_downloadedInstaller}");
        try
        {
            Process.Start(new ProcessStartInfo(_downloadedInstaller, "--silent")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            RequestShutdown?.Invoke();
        }
        catch (Exception ex)
        {
            SetState(UpdateState.Error, $"Couldn't start the installer: {ex.Message}");
        }
    }

    private void SetState(UpdateState state, string text)
    {
        State = state;
        StatusText = text;
        if (state is not (UpdateState.Checking or UpdateState.Downloading)) Logger.Log($"Updater: {text}");
        StateChanged?.Invoke();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed record ReleaseInfo(Version Version, string? DownloadUrl, long Size, string? Sha256)
    {
        public static ReleaseInfo? Parse(JsonElement root, string assetName)
        {
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

            string? url = null, sha = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    url = asset.GetProperty("browser_download_url").GetString();
                    size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest["sha256:".Length..].ToLowerInvariant();
                    break;
                }
            }

            return new ReleaseInfo(Normalize(version), url, size, sha);
        }
    }
}
