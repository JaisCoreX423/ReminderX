using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReminderX.Setup;

/// <summary>
/// Registers ReminderX's MCP server with Claude Desktop (claude_desktop_config.json, as a command
/// Desktop launches: ReminderX.exe --mcp-stdio) and Claude Code (claude mcp add, HTTP), and removes
/// both on uninstall. Only the "reminderx" entry is ever touched.
/// </summary>
internal static class ClaudeIntegration
{
    private const string ServerName = "reminderx";

    private static string DesktopConfigPath =>
        Environment.GetEnvironmentVariable("REMINDERX_TEST_CLAUDE_CONFIG") is { Length: > 0 } testPath
            ? testPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude",
                "claude_desktop_config.json");

    private static bool SkipClaudeCode => Environment.GetEnvironmentVariable("REMINDERX_TEST_SKIP_CLAUDE_CODE") == "1";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep paths readable, don't \u-escape them
    };

    public static void Register(string exePath, string dataDir, string? markerPath)
    {
        Console.WriteLine("Connecting ReminderX to Claude...");
        var desktop = RegisterDesktop(exePath);
        var code = RegisterCode(ReadPort(dataDir));
        if ((desktop || code) && markerPath != null)
        {
            try { File.WriteAllText(markerPath, DateTime.Now.ToString("O")); }
            catch { /* the marker is only an optimization for silent updates */ }
        }
    }

    public static void Unregister()
    {
        Console.WriteLine("Disconnecting ReminderX from Claude...");
        UnregisterDesktop();
        if (!SkipClaudeCode && FindClaudeCli())
        {
            var (code, _) = RunClaude($"mcp remove {ServerName} --scope user");
            Console.WriteLine(code == 0 ? "  Claude Code: removed." : "  Claude Code: nothing to remove.");
        }
    }

    // ---------------- Claude Desktop ----------------

    private static bool RegisterDesktop(string exePath)
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

        servers[ServerName] = new JsonObject
        {
            ["command"] = exePath,
            ["args"] = new JsonArray("--mcp-stdio")
        };

        SaveConfig(path, root);
        Console.WriteLine("  Claude Desktop: added (restart Claude Desktop to load it).");
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

    // ---------------- Claude Code ----------------

    private static bool RegisterCode(int port)
    {
        if (SkipClaudeCode) return false;
        if (!FindClaudeCli())
        {
            Console.WriteLine("  Claude Code: not installed, skipped.");
            return false;
        }

        if (RunClaude($"mcp get {ServerName}").ExitCode == 0)
        {
            Console.WriteLine("  Claude Code: already connected.");
            return true;
        }

        var (code, output) = RunClaude(
            $"mcp add --transport http --scope user {ServerName} http://127.0.0.1:{port}/mcp");
        Console.WriteLine(code == 0 ? "  Claude Code: added." : $"  Claude Code: couldn't add it ({output.Trim()}).");
        return code == 0;
    }

    private static bool FindClaudeCli() => RunCmd("where claude").ExitCode == 0;

    private static (int ExitCode, string Output) RunClaude(string arguments) => RunCmd($"claude {arguments}");

    /// <summary>Through cmd.exe because the npm-installed `claude` is a .cmd shim.</summary>
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

    private static int ReadPort(string dataDir)
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
