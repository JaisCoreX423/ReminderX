using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReminderX;

public enum ClaudeClientState { NotInstalled, Missing, WrongPort, Connected }

/// <summary>
/// Connects ReminderX's MCP server to Claude Desktop (claude_desktop_config.json) and Claude Code
/// (claude mcp add, HTTP), checks whether both still point at the current port, and restarts Claude
/// Desktop so it picks the entry up. Only the "reminderx" entry is ever touched. Shared by the
/// installer and the app (Settings > Reconnect Claude).
/// </summary>
internal static class ClaudeIntegration
{
    private const string ServerName = "reminderx";

    private static string DesktopConfigPath =>
        Environment.GetEnvironmentVariable("REMINDERX_TEST_CLAUDE_CONFIG") is { Length: > 0 } testPath
            ? testPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude",
                "claude_desktop_config.json");

    private static string CodeConfigPath =>
        Environment.GetEnvironmentVariable("REMINDERX_TEST_CLAUDE_CODE_CONFIG") is { Length: > 0 } testPath
            ? testPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    private static bool SkipClaudeCode => Environment.GetEnvironmentVariable("REMINDERX_TEST_SKIP_CLAUDE_CODE") == "1";

    /// <summary>Test hook: log what a Claude Desktop restart would do instead of doing it.</summary>
    private static bool RestartDryRun => Environment.GetEnvironmentVariable("REMINDERX_TEST_CLAUDE_RESTART_DRYRUN") == "1";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep paths readable, don't \u-escape them
    };

    public static string McpUrl(int port) => $"http://127.0.0.1:{port}/mcp";

    public static string ClaudeCodeCommand(int port) =>
        $"claude mcp add --transport http --scope user {ServerName} {McpUrl(port)}";

    /// <summary>Adds or updates both entries. Returns true when the Claude Desktop config changed.</summary>
    public static bool Register(string exePath, int port)
    {
        Console.WriteLine("Connecting ReminderX to Claude...");
        var desktopChanged = RegisterDesktop(exePath, port);
        RegisterCode(port);
        return desktopChanged;
    }

    public static void Unregister()
    {
        Console.WriteLine("Disconnecting ReminderX from Claude...");
        UnregisterDesktop();
        if (!SkipClaudeCode && FindOnPath("claude"))
        {
            var (code, _) = RunClaude($"mcp remove {ServerName} --scope user");
            Console.WriteLine(code == 0 ? "  Claude Code: removed." : "  Claude Code: nothing to remove.");
        }
    }

    public static (ClaudeClientState Desktop, ClaudeClientState Code) GetStatus(int port) =>
        (GetDesktopState(port), GetCodeState(port));

    // ---------------- Claude Desktop ----------------

    /// <summary>
    /// npx mcp-remote when Node.js is installed (what Claude Desktop is known to load on this setup);
    /// otherwise ReminderX's own stdio bridge, which needs nothing else installed.
    /// </summary>
    private static JsonObject DesktopEntry(string exePath, int port) =>
        FindOnPath("npx")
            ? new JsonObject { ["command"] = "npx", ["args"] = new JsonArray("-y", "mcp-remote", McpUrl(port)) }
            : new JsonObject { ["command"] = exePath, ["args"] = new JsonArray("--mcp-stdio") };

    private static bool RegisterDesktop(string exePath, int port)
    {
        var path = DesktopConfigPath;
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            Console.WriteLine("  Claude Desktop: not installed, skipped.");
            return false;
        }

        var root = LoadConfig(path);
        if (root == null) return false;

        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }

        var entry = DesktopEntry(exePath, port);
        if (JsonNode.DeepEquals(servers[ServerName], entry))
        {
            Console.WriteLine("  Claude Desktop: already connected.");
            return false;
        }

        servers[ServerName] = entry;
        SaveConfig(path, root);
        Console.WriteLine($"  Claude Desktop: connected ({entry["command"]}).");
        return true;
    }

    private static void UnregisterDesktop()
    {
        var path = DesktopConfigPath;
        if (!File.Exists(path)) return;

        var root = LoadConfig(path);
        if (root?["mcpServers"] is JsonObject servers && servers.Remove(ServerName))
        {
            SaveConfig(path, root);
            Console.WriteLine("  Claude Desktop: removed.");
        }
    }

    private static ClaudeClientState GetDesktopState(int port)
    {
        var path = DesktopConfigPath;
        if (!Directory.Exists(Path.GetDirectoryName(path))) return ClaudeClientState.NotInstalled;

        JsonNode? entry;
        try { entry = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?["mcpServers"]?[ServerName] : null; }
        catch { return ClaudeClientState.Missing; }
        if (entry?["args"] is not JsonArray args) return ClaudeClientState.Missing;

        var values = args.Select(a => a?.ToString() ?? "").ToList();
        // The stdio bridge reads the port from ReminderX's own settings, so it never goes stale.
        if (values.Contains("--mcp-stdio") || values.Contains(McpUrl(port))) return ClaudeClientState.Connected;
        return ClaudeClientState.WrongPort;
    }

    private static JsonObject? LoadConfig(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root) return root;
        }
        catch (JsonException) { }

        Console.WriteLine($"  Claude Desktop: couldn't read {path}; left it untouched.");
        return null;
    }

    private static void SaveConfig(string path, JsonObject root)
    {
        if (File.Exists(path)) File.Copy(path, path + ".reminderx-backup", overwrite: true);
        File.WriteAllText(path, root.ToJsonString(WriteOptions));
    }

    // ---------------- Claude Desktop restart ----------------

    public static bool IsDesktopRunning() => DesktopProcesses().Count > 0;

    /// <summary>
    /// Closes Claude Desktop and starts it again through explorer.exe (normal user token, even when
    /// called from the elevated installer). Only touches Claude Desktop's own install folders, never
    /// the Claude Code CLI, which is also named claude.exe.
    /// </summary>
    public static bool RestartDesktop()
    {
        var processes = DesktopProcesses();
        if (processes.Count == 0) return false;

        var exe = processes[0].Path;
        var launch = PackagedAppId(exe) is { } appId ? $"shell:AppsFolder\\{appId}" : exe;

        if (RestartDryRun)
        {
            Console.WriteLine($"  (dry run) would stop {processes.Count} Claude Desktop process(es) and start {launch}");
            return true;
        }

        foreach (var (process, _) in processes)
        {
            try { process.Kill(); } catch { /* already gone */ }
        }
        foreach (var (process, _) in processes)
        {
            try { process.WaitForExit(5000); } catch { }
        }
        Thread.Sleep(1000); // let Electron release its single-instance lock

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{launch}\"") { UseShellExecute = false });
        Console.WriteLine("  Claude Desktop restarted.");
        return true;
    }

    private static List<(Process Process, string Path)> DesktopProcesses()
    {
        var found = new List<(Process, string)>();
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            string? path;
            try { path = process.MainModule?.FileName; }
            catch { continue; }

            if (path != null &&
                (path.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) ||
                 path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase)))
                found.Add((process, path));
        }
        return found;
    }

    /// <summary>
    /// Store (MSIX) installs can't be started by exe path. Turns
    /// ...\WindowsApps\Claude_4.29.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe into Claude_pzs8sxrjxfjjc!Claude.
    /// </summary>
    private static string? PackagedAppId(string exePath)
    {
        var match = Regex.Match(exePath, @"\\WindowsApps\\(?<name>[^_\\]+)_[^\\]*__(?<publisher>[^\\]+)\\", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var appId = "Claude";
        try
        {
            var packageDir = exePath[..(match.Index + match.Length)];
            var manifest = File.ReadAllText(Path.Combine(packageDir, "AppxManifest.xml"));
            if (Regex.Match(manifest, "<Application Id=\"([^\"]+)\"") is { Success: true } app)
                appId = app.Groups[1].Value;
        }
        catch { /* "Claude" is the id in every build seen so far */ }

        return $"{match.Groups["name"].Value}_{match.Groups["publisher"].Value}!{appId}";
    }

    // ---------------- Claude Code ----------------

    private static void RegisterCode(int port)
    {
        if (SkipClaudeCode) return;
        if (!FindOnPath("claude"))
        {
            Console.WriteLine("  Claude Code: not installed, skipped.");
            return;
        }

        switch (GetCodeState(port))
        {
            case ClaudeClientState.Connected:
                Console.WriteLine("  Claude Code: already connected.");
                return;
            case ClaudeClientState.WrongPort:
                RunClaude($"mcp remove {ServerName} --scope user");
                break;
        }

        var (code, output) = RunClaude($"mcp add --transport http --scope user {ServerName} {McpUrl(port)}");
        Console.WriteLine(code == 0 ? "  Claude Code: connected." : $"  Claude Code: couldn't connect ({output.Trim()}).");
    }

    /// <summary>User-scope MCP servers live in the top-level "mcpServers" of ~/.claude.json.</summary>
    private static ClaudeClientState GetCodeState(int port)
    {
        var path = CodeConfigPath;
        if (!File.Exists(path)) return ClaudeClientState.NotInstalled;

        try
        {
            var entry = JsonNode.Parse(File.ReadAllText(path))?["mcpServers"]?[ServerName];
            if (entry == null) return ClaudeClientState.Missing;
            return entry["url"]?.ToString() == McpUrl(port) ? ClaudeClientState.Connected : ClaudeClientState.WrongPort;
        }
        catch
        {
            return ClaudeClientState.Missing;
        }
    }

    private static bool FindOnPath(string command) => RunCmd($"where {command}").ExitCode == 0;

    private static (int ExitCode, string Output) RunClaude(string arguments) => RunCmd($"claude {arguments}");

    /// <summary>Through cmd.exe because the npm-installed `claude` and `npx` are .cmd shims.</summary>
    private static (int ExitCode, string Output) RunCmd(string commandLine)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/d /c {commandLine}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (-1, "timed out");
            }
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public static int ReadPort(string dataDir)
    {
        try
        {
            var dataFile = Path.Combine(dataDir, "data.json");
            if (File.Exists(dataFile) &&
                JsonNode.Parse(File.ReadAllText(dataFile))?["Settings"]?["McpServerPort"]?.GetValue<int>() is { } port and > 0)
                return port;
        }
        catch { /* default below */ }
        return 8787;
    }
}
