using System.Windows;

namespace ReminderX;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavReminders.IsChecked = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var maximized = WindowState == WindowState.Maximized;

        // A chromeless window maximizes past the screen edges by its frame; pull the content back in.
        RootGrid.Margin = maximized ? MaximizedOverhang() : new Thickness(0);
        MaximizeGlyph.Text = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    /// <summary>How far the maximized window sticks out of its monitor's work area, in DIPs.</summary>
    private Thickness MaximizedOverhang()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window) || !GetMonitorInfo(monitor, ref info))
            return SystemParameters.WindowResizeBorderThickness;

        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        var work = info.rcWork;
        return new Thickness(
            Math.Max(0, work.Left - window.Left) / scale,
            Math.Max(0, work.Top - window.Top) / scale,
            Math.Max(0, window.Right - work.Right) / scale,
            Math.Max(0, window.Bottom - work.Bottom) / scale);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)
            ?.AddHook(WndProc);
    }

    /// <summary>
    /// WindowStyle=None windows maximize over the whole monitor, taskbar included. Limit the
    /// maximized size to the work area of the monitor the window is on.
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
        return IntPtr.Zero; // not handled: WPF still applies MinWidth/MinHeight
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window never really "closes" from the user's perspective — the app keeps
        // running in the tray until Exit is chosen from the tray menu.
        e.Cancel = true;
        Hide();
    }

    private void HideAllPanels()
    {
        if (RemindersPanel == null) return; // guards against events firing mid-InitializeComponent
        RemindersPanel.Visibility = Visibility.Collapsed;
        TodoPanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void NavReminders_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        RemindersPanel.Visibility = Visibility.Visible;
        RemindersPanel.Refresh();
    }

    private void NavTodo_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        TodoPanel.Visibility = Visibility.Visible;
        TodoPanel.Refresh();
    }

    private void NavHistory_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryPanel.Refresh();
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        SettingsPanel.Visibility = Visibility.Visible;
        SettingsPanel.Refresh();
    }

    public void ShowSettings() => NavSettings.IsChecked = true;

    public void RequestNewReminder()
    {
        NavReminders.IsChecked = true;
        RemindersPanel.OpenNewReminderDialog();
    }

    public void RequestNewTodo()
    {
        NavTodo.IsChecked = true;
        TodoPanel.OpenNewTodoDialog();
    }

    public void FocusReminder(string reminderId)
    {
        NavReminders.IsChecked = true;
        RemindersPanel.Refresh();
        RemindersPanel.OpenEditDialogFor(reminderId);
    }
}
