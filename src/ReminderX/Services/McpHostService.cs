using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReminderX.Services;

/// <summary>
/// Hosts a local-only MCP (Model Context Protocol) server inside the running app so an AI
/// client (Claude Code, Claude Desktop, etc.) can list and create reminders/to-dos.
/// Bound to 127.0.0.1 only — never reachable from the network.
/// </summary>
public class McpHostService
{
    private readonly ReminderService _reminders;
    private WebApplication? _app;

    public int Port { get; private set; } = 8787;
    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }

    public McpHostService(ReminderService reminders)
    {
        _reminders = reminders;
    }

    public async Task StartAsync(int port)
    {
        if (IsRunning) await StopAsync();

        Port = port;
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            builder.Services.AddSingleton(_reminders);
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly();

            _app = builder.Build();
            _app.Use(async (context, next) =>
            {
                // A person opening the endpoint in a browser gets a status page instead of "405".
                // MCP clients never ask for text/html, so they're unaffected.
                if (HttpMethods.IsGet(context.Request.Method) &&
                    context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(StatusPage(port));
                    return;
                }
                await next();
            });
            _app.Use(async (context, next) =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await next();
                if (watch.ElapsedMilliseconds > 1000)
                    Logger.Log($"Slow MCP request: {context.Request.Method} took {watch.ElapsedMilliseconds} ms");
            });
            _app.MapMcp("/mcp");

            await _app.StartAsync().ConfigureAwait(false);
            IsRunning = true;
            LastError = null;
            Logger.Log($"MCP server listening on http://127.0.0.1:{port}/mcp");
            _ = Task.Run(() => WarmUpAsync(port));
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastError = ex.Message;
            Logger.Log($"MCP server failed to start on port {port}: {ex.Message}");
        }
    }

    private static string StatusPage(int port) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>ReminderX MCP server</title>
        <style>
          :root { color-scheme: light dark; --bg:#f5f5f7; --card:#fff; --text:#1d1d1f; --muted:#6e6e73; --code:#f2f2f4; --ok:#30c85e; }
          @media (prefers-color-scheme: dark) { :root { --bg:#1e1e20; --card:#2a2a2d; --text:#f5f5f7; --muted:#9a9a9e; --code:#323235; } }
          body { margin:0; background:var(--bg); color:var(--text); font:15px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif; }
          main { max-width:620px; margin:64px auto; padding:0 16px; }
          .card { background:var(--card); border-radius:16px; padding:28px; box-shadow:0 8px 30px rgba(0,0,0,.12); }
          h1 { font-size:22px; margin:0 0 4px; } .muted { color:var(--muted); }
          .status { display:inline-flex; align-items:center; gap:8px; font-weight:600; margin:12px 0 18px; }
          .dot { width:10px; height:10px; border-radius:50%; background:var(--ok); }
          code, pre { font-family:Consolas,monospace; background:var(--code); border-radius:8px; }
          pre { padding:12px; white-space:pre-wrap; word-break:break-all; margin:6px 0 16px; }
        </style></head>
        <body><main><div class="card">
          <h1>ReminderX</h1><div class="muted">Version {{AppInfo.VersionText}} · {{AppInfo.Credits}}</div>
          <div class="status"><span class="dot"></span>MCP server is running</div>
          <p>This address is for AI clients, not for browsers. Claude connects to it to list and create your reminders and to-dos.</p>
          <div class="muted">Claude Code</div>
          <pre>claude mcp add --transport http --scope user reminderx http://127.0.0.1:{{port}}/mcp</pre>
          <div class="muted">Claude Desktop (claude_desktop_config.json, added automatically by the installer)</div>
          <pre>"reminderx": { "command": "{{EscapeJson(Environment.ProcessPath ?? "ReminderX.exe")}}", "args": ["--mcp-stdio"] }</pre>
        </div></main></body></html>
        """;

    private static string EscapeJson(string value) =>
        System.Net.WebUtility.HtmlEncode(value.Replace("\\", "\\\\"));

    /// <summary>
    /// Runs the MCP request path once at startup (read-only calls) so the first real request from an
    /// AI client doesn't pay for compiling it — after hours idle in the tray that could exceed the
    /// client's timeout.
    /// </summary>
    private static async Task WarmUpAsync(int port)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var url = $"http://127.0.0.1:{port}/mcp";
            string[] bodies =
            {
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"reminderx-warmup","version":"1"}}}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_reminders","arguments":{}}}"""
            };

            foreach (var body in bodies)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                using var response = await http.SendAsync(request).ConfigureAwait(false);
                await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            Logger.Log($"MCP warm-up done in {watch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Logger.Log($"MCP warm-up failed after {watch.ElapsedMilliseconds} ms: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"MCP server stop error: {ex.Message}");
        }
        finally
        {
            _app = null;
            IsRunning = false;
        }
    }
}
