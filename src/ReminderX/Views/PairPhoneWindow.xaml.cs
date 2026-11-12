using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using ReminderX.Services;

namespace ReminderX.Views;

/// <summary>
/// Shows the one-time pairing QR (server, vault, code and encryption key) and waits for a phone to
/// redeem it: the device list is polled every 3 s and the window says so when a new phone appears.
/// </summary>
public partial class PairPhoneWindow : Window
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private HashSet<string> _knownDevices = new();
    private DateTime _expiresAt;
    private int _secondsToPoll;
    private bool _linked;

    public PairPhoneWindow()
    {
        InitializeComponent();
        _tick.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => await CreateCodeAsync();
        Closed += (_, _) => _tick.Stop();
    }

    private async Task CreateCodeAsync()
    {
        NewCodeButton.Visibility = Visibility.Collapsed;
        QrImage.Source = null;
        QrPlaceholder.Text = "Creating a code…";
        QrPlaceholder.Visibility = Visibility.Visible;
        StatusText.Text = "";

        try
        {
            var pairing = await AppHost.Sync.StartPairingAsync();
            _knownDevices = (await AppHost.Sync.ListDevicesAsync()).Select(d => d.Id).ToHashSet();
            _expiresAt = pairing.ExpiresAt;

            QrImage.Source = RenderQr(pairing.QrPayload);
            QrPlaceholder.Visibility = Visibility.Collapsed;
            Logger.Log("Sync: pairing code shown");
            if (DiagSnapshot.Dir != null)
            {
                Directory.CreateDirectory(DiagSnapshot.Dir);
                File.WriteAllText(Path.Combine(DiagSnapshot.Dir, "pairing.txt"), pairing.QrPayload);
            }

            _secondsToPoll = 3;
            _tick.Start();
            UpdateCountdown();
            SaveDiagSnapshot();
        }
        catch (Exception ex)
        {
            Logger.Log($"Sync: couldn't start pairing: {ex.Message}");
            QrPlaceholder.Text = "Couldn't create a code";
            StatusText.Text = $"{ex.Message}\nCheck the internet connection and try again.";
            NewCodeButton.Content = "Try again";
            NewCodeButton.Visibility = Visibility.Visible;
        }
    }

    private static BitmapImage RenderQr(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10, [0x1D, 0x1D, 0x1F], [0xFF, 0xFF, 0xFF], drawQuietZones: false);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task TickAsync()
    {
        if (_linked) return;
        if (UpdateCountdown()) return;

        if (--_secondsToPoll > 0) return;
        _secondsToPoll = 3;
        try
        {
            var linked = (await AppHost.Sync.ListDevicesAsync()).FirstOrDefault(d => !_knownDevices.Contains(d.Id));
            if (linked == null || !IsLoaded) return;

            _linked = true;
            _tick.Stop();
            Logger.Log($"Sync: phone linked ({linked.Name})");
            StatusText.Text = $"Linked to {linked.Name}. Your reminders are syncing.";
            QrImage.Opacity = 0.15;
            DoneButton.Content = "Done";
            DoneButton.Style = (Style)FindResource("Button.Primary");
            _ = AppHost.Sync.SyncNowAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Sync: device poll failed: {ex.Message}");
        }
    }

    /// <summary>Returns true once the code has expired.</summary>
    private bool UpdateCountdown()
    {
        var left = _expiresAt - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            _tick.Stop();
            QrImage.Opacity = 0.15;
            StatusText.Text = "This code expired.";
            NewCodeButton.Content = "New code";
            NewCodeButton.Visibility = Visibility.Visible;
            return true;
        }
        StatusText.Text = $"Waiting for your phone… code expires in {(int)left.TotalMinutes}:{left.Seconds:00}";
        return false;
    }

    private async void NewCode_Click(object sender, RoutedEventArgs e)
    {
        QrImage.Opacity = 1;
        await CreateCodeAsync();
    }

    private void ApkLink_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppInfo.RepoUrl + "/releases/latest") { UseShellExecute = true });

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveDiagSnapshot()
    {
        if (DiagSnapshot.Dir == null) return;
        Dispatcher.BeginInvoke(() => DiagSnapshot.Save((FrameworkElement)Content, "pair-phone", Background),
            DispatcherPriority.ApplicationIdle);
    }
}
