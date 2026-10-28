using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
