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
            if (ClaudeIntegration.Register(Path.Combine(InstallDir, "ReminderX.exe"), ClaudeIntegration.ReadPort(DataDir)) &&
                ClaudeIntegration.IsDesktopRunning() &&
                AskYesNo("ReminderX was added to Claude Desktop. Restart Claude Desktop now so it loads it?"))
            {
                ClaudeIntegration.RestartDesktop();
            }
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

        // Interactive installs always (re)register; silent updates only once, so an update brings
        // the registration to people who installed before it existed but never re-adds one the user
        // removed on purpose.
        var desktopChanged = false;
        if (!Silent || !File.Exists(ClaudeRegisteredMarker))
        {
            desktopChanged = ClaudeIntegration.Register(exePath, ClaudeIntegration.ReadPort(DataDir));
            try { File.WriteAllText(ClaudeRegisteredMarker, DateTime.Now.ToString("O")); }
            catch { /* the marker only keeps silent updates from registering again */ }
        }

        // Claude Desktop reads its MCP config only at startup.
        if (!Silent && desktopChanged && ClaudeIntegration.IsDesktopRunning() &&
            AskYesNo("ReminderX was added to Claude Desktop. Restart Claude Desktop now so it loads it?"))
        {
            ClaudeIntegration.RestartDesktop();
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
        Console.WriteLine($"  Installed to: {InstallDir}");
        Console.WriteLine($"  Data stored at: {DataDir}");
        Console.WriteLine();
        Console.WriteLine("Launching ReminderX...");
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
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(500); // file may still be releasing after StopRunningApp
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
