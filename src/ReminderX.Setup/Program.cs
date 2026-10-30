using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ReminderX.Setup;

internal static class Program
{
    private const string TaskName = "ReminderX";
    private const string PayloadResourceSuffix = "payload.zip";

    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ReminderX");

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReminderX");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ReminderX.lnk");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ReminderX.lnk");

    /// <summary>
    /// --silent: run by the app's auto-updater. No window, no prompts; output goes to setup.log; keeps
    /// the user's Desktop shortcut and Start-with-Windows choices as they are; restarts into the tray.
    /// </summary>
    private static bool Silent;

    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--version", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
            return 0;
        }

        // Maintenance: (re)do or undo only the Claude Desktop / Claude Code hookup.
        if (args.Any(a => a.Equals("--claude-register", StringComparison.OrdinalIgnoreCase)))
        {
            ConnectClaude(Path.Combine(InstallDir, "ReminderX.exe"), interactive: true);
            return 0;
        }
        if (args.Any(a => a.Equals("--claude-unregister", StringComparison.OrdinalIgnoreCase)))
        {
            ClaudeIntegration.Unregister();
            return 0;
        }

        Silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        StreamWriter? log = null;
        if (Silent)
        {
            Directory.CreateDirectory(DataDir);
            log = new StreamWriter(Path.Combine(DataDir, "setup.log"), append: true) { AutoFlush = true };
            Console.SetOut(log);
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Silent update install");
        }

        Console.WriteLine("ReminderX Setup");
        Console.WriteLine("===============");
        Console.WriteLine();

        try
        {
            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                var removeData = args.Any(a => a.Equals("--remove-data", StringComparison.OrdinalIgnoreCase));
                Uninstall(removeData);
            }
            else
            {
                Install();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"FAILED: {ex.Message}");

            // A failed auto-update must not leave the user without ReminderX: start what's installed.
            var installedExe = Path.Combine(InstallDir, "ReminderX.exe");
            if (Silent && File.Exists(installedExe) && Process.GetProcessesByName("ReminderX").Length == 0)
            {
                Console.WriteLine("Restarting the installed version...");
                try { LaunchApp(installedExe); }
                catch (Exception launchEx) { Console.WriteLine($"  (couldn't start it: {launchEx.Message})"); }
            }

            if (!Silent) Pause(); // keep the window open on failure so the error is actually readable
            log?.Dispose();
            return 1;
        }

        if (!Silent) CloseShortly();
        log?.Dispose();
        return 0;
    }

    private static void Pause()
    {
        if (!Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to close...");
            Console.ReadKey(true);
        }
    }

    /// <summary>Closes on its own a moment after finishing, instead of waiting for a keypress.</summary>
    private static void CloseShortly()
    {
        Console.WriteLine();
        Console.WriteLine("Closing this window...");
        Thread.Sleep(1500);
    }

    // ---------------- Install ----------------

    private static void Install()
    {
        Console.WriteLine("Stopping any running ReminderX instance...");
        StopRunningApp();

        Console.WriteLine($"Installing to {InstallDir} ...");
        Directory.CreateDirectory(InstallDir);
        ExtractPayload(InstallDir);

        var exePath = Path.Combine(InstallDir, "ReminderX.exe");
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Setup payload did not contain ReminderX.exe.", exePath);

        if (!Silent)
        {
            Console.WriteLine("Registering Windows logon task (runs in the tray at sign-in)...");
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var taskArgs = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --background\" " +
                           $"/SC ONLOGON /RL LIMITED /RU \"{user}\" /IT /F";
            var (code, output) = RunProcess("schtasks.exe", taskArgs);
            if (code != 0)
                Console.WriteLine($"  (could not register the logon task automatically: {output.Trim()} — " +
                                   "open ReminderX > Settings and toggle \"Start with Windows\" instead)");

            Console.WriteLine("Creating Desktop shortcut...");
            try { CreateShortcut(DesktopShortcut, exePath, InstallDir); }
            catch (Exception ex) { Console.WriteLine($"  (shortcut not created: {ex.Message})"); }
        }

        // Interactive installs always connect; silent updates put back an entry that went missing
        // (e.g. Claude Desktop reinstalled or its config reset) without touching a correct one.
        if (!Silent)
        {
            ConnectClaude(exePath, interactive: true);
        }
        else
        {
            var (desktop, code) = ClaudeIntegration.GetStatus(ClaudeIntegration.ReadPort(DataDir));
            if (desktop is ClaudeClientState.Missing or ClaudeClientState.WrongPort ||
                code is ClaudeClientState.Missing or ClaudeClientState.WrongPort)
                ConnectClaude(exePath, interactive: false);
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
        Console.WriteLine($"  Installed to: {InstallDir}");
        Console.WriteLine($"  Data stored at: {DataDir}");
        Console.WriteLine();
        Console.WriteLine("Launching ReminderX...");
        LaunchApp(exePath);
    }

    /// <summary>
    /// Connects Claude Desktop and Claude Code and confirms the Desktop entry is really in the
    /// config. Interactive: says so when it isn't, and offers to restart Claude Desktop so it loads it.
    /// </summary>
    private static void ConnectClaude(string exePath, bool interactive)
    {
        var result = ClaudeIntegration.Register(exePath, ClaudeIntegration.ReadPort(DataDir));
        if (!interactive) return;

        if (result.DesktopError != null)
        {
            ShowWarning($"ReminderX couldn't be added to Claude Desktop: {result.DesktopError}.\n\n" +
                        "You can try again from ReminderX > Settings > AI access > Connect Claude.");
        }
        else if (result.DesktopChanged && ClaudeIntegration.IsDesktopRunning() &&
                 AskYesNo("ReminderX was added to Claude Desktop. Restart Claude Desktop now so it loads it?"))
        {
            ClaudeIntegration.RestartDesktop();
        }
    }

    private static void LaunchApp(string exePath)
    {
        if (Environment.IsPrivilegedProcess)
        {
            // Setup runs as admin, the app must not: explorer.exe starts it with the normal user token.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"") { UseShellExecute = false });
        }
        else
        {
            Process.Start(new ProcessStartInfo(exePath, Silent ? "--background" : "")
            {
                UseShellExecute = true,
                WorkingDirectory = InstallDir
            });
        }
    }

    private static string ClaudeRegisteredMarker => Path.Combine(DataDir, "claude-registered");

    private static void ExtractPayload(string destinationDir)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(PayloadResourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException(
                "This setup executable has no embedded payload. Run build-installer.ps1 to produce a real installer.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var destPath = Path.Combine(destinationDir, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    entry.ExtractToFile(destPath, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    // The exe can stay locked for a few seconds after the app exits (antivirus scan,
                    // a straggler instance). 1.0.3's first silent update failed here with 3 x 0.5 s.
                    if (attempt == 1) Console.WriteLine($"  {entry.Name} is in use, waiting...");
                    StopRunningApp();
                    Thread.Sleep(1000);
                }
            }
        }
    }

    private static void StopRunningApp()
    {
        foreach (var process in Process.GetProcessesByName("ReminderX"))
        {
            try
            {
                // An auto-update starts this right as the app shuts itself down; give it a moment to exit cleanly.
                if (Silent && process.WaitForExit(8000)) continue;
                process.Kill();
                process.WaitForExit(5000);
            }
            catch { /* best effort */ }
        }
    }

    // ---------------- Uninstall ----------------

    private static void Uninstall(bool removeData)
    {
        Console.WriteLine("Stopping ReminderX...");
        StopRunningApp();

        Console.WriteLine("Removing logon task...");
        RunProcess("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");

        Console.WriteLine("Removing shortcuts...");
        TryDelete(DesktopShortcut);
        TryDelete(StartMenuShortcut);

        ClaudeIntegration.Unregister();
        TryDelete(ClaudeRegisteredMarker);

        Console.WriteLine($"Removing program files ({InstallDir})...");
        if (Directory.Exists(InstallDir))
            Directory.Delete(InstallDir, recursive: true);

        if (removeData)
        {
            Console.WriteLine($"Removing data ({DataDir})...");
            if (Directory.Exists(DataDir))
                Directory.Delete(DataDir, recursive: true);
        }
        else
        {
            Console.WriteLine($"Keeping your data at {DataDir} (pass --remove-data to delete it too).");
        }

        Console.WriteLine();
        Console.WriteLine("ReminderX uninstalled.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    // ---------------- Helpers ----------------

    private static bool AskYesNo(string message)
    {
        // Test hook: answer without a dialog.
        if (Environment.GetEnvironmentVariable("REMINDERX_TEST_ANSWER") is { Length: > 0 } answer)
            return answer.Equals("yes", StringComparison.OrdinalIgnoreCase);

        const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
        const int IDYES = 6;
        return MessageBoxW(IntPtr.Zero, message, "ReminderX Setup",
            MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND) == IDYES;
    }

    private static void ShowWarning(string message)
    {
        Console.WriteLine($"  {message}");
        if (Environment.GetEnvironmentVariable("REMINDERX_TEST_ANSWER") is { Length: > 0 }) return;

        const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
        MessageBoxW(IntPtr.Zero, message, "ReminderX Setup", MB_OK | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        return (process.ExitCode, stdout + stderr);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellLink = (IShellLinkW)new CShellLink();
        shellLink.SetPath(targetPath);
        shellLink.SetArguments("");
        shellLink.SetWorkingDirectory(workingDirectory);

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(StringBuilder pszName, int cchMaxName);
        void SetDescription(string pszName);
        void GetWorkingDirectory(StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory(string pszDir);
        void GetArguments(StringBuilder pszArgs, int cchMaxPath);
        void SetArguments(string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath(string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
