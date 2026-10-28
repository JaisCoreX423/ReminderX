using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace ReminderX.Services;

/// <summary>
/// `ReminderX.exe --mcp-stdio`: lets Claude Desktop (which only launches command-based MCP servers)
/// reach the running app. Reads newline-delimited JSON-RPC from stdin, POSTs each message to the
/// app's local MCP endpoint, and writes the replies to stdout.
/// </summary>
public static class McpStdioBridge
{
    public static async Task<int> RunAsync()
    {
        var port = ReadConfiguredPort();
        var url = $"http://127.0.0.1:{port}/mcp";

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        string? sessionId = null;

        while (await stdin.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? message;
            try { message = JsonNode.Parse(line); }
            catch { continue; }

            var id = message?["id"]?.DeepClone(); // null for notifications, which get no reply

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(line, Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (sessionId != null) request.Headers.Add("Mcp-Session-Id", sessionId);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids)) sessionId = ids.FirstOrDefault();
                if (id == null || response.StatusCode == HttpStatusCode.Accepted) continue;

                var body = await response.Content.ReadAsStringAsync();
                var wrote = false;
                if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                {
                    foreach (var raw in body.Split('\n'))
                    {
                        var dataLine = raw.TrimEnd('\r');
                        if (!dataLine.StartsWith("data:")) continue;
                        await stdout.WriteLineAsync(dataLine[5..].Trim());
                        wrote = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(body))
                {
                    await stdout.WriteLineAsync(JsonNode.Parse(body)!.ToJsonString());
                    wrote = true;
                }

                if (!wrote)
                    await WriteErrorAsync(stdout, id, $"ReminderX returned HTTP {(int)response.StatusCode} with no reply.");
            }
            catch (Exception ex)
            {
                if (id != null)
                    await WriteErrorAsync(stdout, id,
                        $"ReminderX isn't reachable at {url} — is the app running with its MCP server on? ({ex.Message})");
            }
        }

        return 0;
    }

    private static Task WriteErrorAsync(StreamWriter stdout, JsonNode id, string text)
    {
        var error = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject { ["code"] = -32000, ["message"] = text }
        };
        return stdout.WriteLineAsync(error.ToJsonString());
    }

    private static int ReadConfiguredPort()
    {
        try
        {
            if (File.Exists(PathService.DataFile) &&
                JsonNode.Parse(File.ReadAllText(PathService.DataFile))?["Settings"]?["McpServerPort"]?.GetValue<int>() is { } port and > 0)
                return port;
        }
        catch { /* fall back to the default */ }
        return 8787;
    }
}
