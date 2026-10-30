<p align="center">
  <img src="src/ReminderX/Assets/reminderx-256.png" width="96" alt="ReminderX icon">
</p>

# ReminderX

A minimalist, Apple-style reminders app for Windows. It lives in the tray, dims the screen with a
reminder card when something is due (one-time or recurring), keeps a to-do list and a history log,
updates itself from GitHub Releases, and exposes an MCP server so an AI client (Claude Code,
Claude Desktop) can list and create reminders and to-dos directly.

Developed by Alfred Ibarra & Claude.

## Install

Download `ReminderXSetup.exe` from the [latest release](../../releases/latest) and run it. It
always asks for admin rights (UAC), then installs to `%LocalAppData%\Programs\ReminderX`, creates a
Desktop shortcut, registers a per-user logon task (`ReminderX`, runs with `--background` so it
starts straight into the tray), connects ReminderX to Claude Desktop and Claude Code (see below),
offers to restart Claude Desktop if it's open, launches the app and closes itself. No .NET runtime
is needed. Automatic updates run it without the UAC prompt.

**If the logon task doesn't get created** (some locked-down environments block `schtasks`), open
Settings inside the app and toggle "Start with Windows". It registers the same task.

## Updates

ReminderX checks this repo's latest release a minute after it starts and every 6 hours after that.
When there's a newer version it downloads `ReminderXSetup.exe`, verifies it against the SHA-256
checksum GitHub publishes for the file, and installs it silently while the app is idle in the tray
(window hidden, no alert on screen), then restarts into the tray. Settings > About shows the current
version, has **Check now** / **Install**, and a switch to turn automatic installs off.
Silent-install output goes to `%LocalAppData%\ReminderX\setup.log`.

### Publishing a release

Push a version tag. The [Release workflow](.github/workflows/release.yml) builds the installer
with that version stamped in and publishes it as a GitHub release:

```bash
git tag v1.0.2
```

```bash
git push origin v1.0.2
```

To build the installer locally instead (needs the .NET 10 SDK): `.\build-installer.ps1 -Version 1.0.2`
(output in `dist\`).

## Uninstall

```powershell
ReminderXSetup.exe --uninstall               # keeps your reminders/to-dos/history
ReminderXSetup.exe --uninstall --remove-data # also deletes %LocalAppData%\ReminderX
```

## Using it

- **Opening it**: single-click the tray icon, or use the taskbar pin / Start menu / Desktop shortcut
  (if it's already running in the tray, that brings the existing window up). Right-click the tray
  icon for New Reminder / New To-Do / Exit.
- **Reminders**: one-time (pick a date and time), recurring (every N seconds/minutes/hours, or
  every N days/weeks/months at a specific time; weekly can target specific weekdays), or a timer
  (goes off in N weeks/days/hours/minutes/seconds). Time fields take the 15-minute presets or any
  time you type: `9:37 AM`, `9:37p`, `21:37`, `2137`.
- **Review Outlook & Teams**: the **Today** and **Last N days** buttons on Reminders run Claude
  Code in the background (`claude -p`, Sonnet, read-only Microsoft 365 tools) to read your email,
  calendar and Teams chats and suggest reminders. It checks your existing reminders and to-dos first
  and drops duplicates; you tick which suggestions to create. N is set in Settings (1/3/7/14 days).
  Needs Claude Code installed and signed in, with the Microsoft 365 connector enabled on claude.ai.
- **Alerts**: when a reminder is due, a chime plays, every monitor dims and a card appears on the
  screen your mouse is on, with **Complete** (Enter), **Snooze 10 min** (Esc), **Open reminder** and
  **Dismiss**. Several due at once queue up ("1 more waiting"). If you only open it or ignore it, it
  comes back every N minutes (per reminder, default 10) until you complete it. Settings >
  "Full-screen alerts" switches to regular Windows notifications instead; "Preview" shows a sample.
- **Appearance**: Settings > Appearance: System (follows Windows), Light or Dark.
- **Attachments**: add files to any reminder (images get a thumbnail in the editor and show on the
  alert card).
- **To-Do**: a plain checklist. Give one a due date and it creates a matching reminder
  automatically (toggle off per item if you don't want that).
- **History**: every created/shown/completed/snoozed/dismissed event, most recent first.
- Closing the window (X) just hides it to the tray; the app keeps running. Use Exit from the tray
  menu to actually quit.

## Connecting an AI client (MCP)

While ReminderX is running it serves MCP tools over HTTP on `127.0.0.1` only (never the network).
Opening `http://127.0.0.1:8787/mcp` in a browser shows a status page.

The installer registers it for you:

- **Claude Desktop**: adds `reminderx` to `%APPDATA%\Claude\claude_desktop_config.json`
  (Claude Desktop only loads command-based servers). With Node.js installed it uses
  `npx -y mcp-remote http://127.0.0.1:8787/mcp`; without it, `ReminderX.exe --mcp-stdio`, a small
  stdio bridge to the running app. The previous file is backed up to
  `claude_desktop_config.json.reminderx-backup`. If Claude Desktop is open, setup asks whether to
  restart it so it loads the server.
- **Claude Code**: runs `claude mcp add --transport http --scope user reminderx http://127.0.0.1:8787/mcp`
  if the `claude` CLI is installed.

Setup creates `%APPDATA%\Claude` and the config file when they don't exist yet, reads the entry back
to confirm it's there, and warns if it couldn't write it. Automatic updates put the entry back if it
went missing.

Settings > AI access shows whether each one is connected, with a **Connect Claude** button (or
**Connect again** when everything looks connected but Claude doesn't see ReminderX) that writes both
entries, checks them and offers to restart Claude Desktop. If you change the port, it also shows
the Claude Code command. From a terminal: `ReminderXSetup.exe --claude-register` (or
`--claude-unregister`).

Tools: `list_reminders`, `get_reminder`, `create_reminder`, `complete_reminder`, `snooze_reminder`,
`delete_reminder`, `list_todos`, `create_todo`, `complete_todo`, `delete_todo`, `get_history`.
`create_reminder` and `create_todo` skip an item when an active one with the same title exists
(pass `skipIfDuplicate: false` to force it).

## Project layout

```
src/ReminderX/          The app (WPF, .NET 10)
  Models/               Reminder, TodoItem, RecurrenceRule, HistoryEntry, Attachment, AppData
  Services/             storage (data.json), recurrence, scheduler, full-screen alerts, chime,
                        toast notifications, tray icon, autostart, auto-updater, MCP host
  Mcp/                  MCP tool definitions
  Views/                Reminders / To-Do / History / Settings, editors, alert overlay
  Themes/               light/dark color tokens and control styles
src/ReminderX.Setup/    The installer: embeds the published app and installs/uninstalls it
build-installer.ps1     Builds dist\ReminderXSetup.exe
```

Data lives at `%LocalAppData%\ReminderX\data.json` (plus an `Attachments\<reminderId>\` folder per
reminder with attachments, and `reminderx.log` for troubleshooting). Settings has shortcuts to open
both.
