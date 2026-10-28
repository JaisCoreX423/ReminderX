using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ReminderX.Converters;
using ReminderX.Models;
using ReminderX.Services;
using Color = System.Windows.Media.Color;
using Screen = System.Windows.Forms.Screen;

namespace ReminderX.Views;

/// <summary>One per monitor: dims the screen. The one on the active monitor also carries the reminder card.</summary>
public partial class AlertOverlayWindow : Window
{
    private static int _snapshotCounter;

    private bool _actionTaken;
    private bool _closing;

    public event Action<AlertAction>? ActionChosen;

    public bool HasCard { get; }

    public AlertOverlayWindow(Screen screen, AlertItem? item, int waitingCount, ReminderService? reminders)
    {
        InitializeComponent();

        // Place it on its monitor (approximate DIPs); maximizing then snaps it to that monitor's exact bounds.
        var scale = Screen.PrimaryScreen!.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        var bounds = screen.Bounds;
        Left = bounds.Left / scale;
        Top = bounds.Top / scale;
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
        SourceInitialized += (_, _) => WindowState = WindowState.Maximized;

        HasCard = item != null;
        if (item != null)
        {
            Populate(item.Reminder, reminders);
            CardHost.Visibility = Visibility.Visible;
            SetWaitingCount(waitingCount);
        }

        Loaded += (_, _) => AnimateIn();
    }

    public void SetWaitingCount(int count)
    {
        WaitingChip.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WaitingText.Text = count == 1 ? "1 more waiting" : $"{count} more waiting";
    }

    private void Populate(Reminder reminder, ReminderService? reminders)
    {
        TitleText.Text = reminder.Title;

        var when = (string)new FriendlyDateConverter().Convert(reminder.DueAt, typeof(string), null!, CultureInfo.CurrentCulture);
        WhenText.Text = reminder.Recurrence != null ? $"{when}  ·  {reminder.Recurrence.Describe()}" : when;

        if (!string.IsNullOrWhiteSpace(reminder.Description))
        {
            DescriptionText.Text = reminder.Description;
            DescriptionHost.Visibility = Visibility.Visible;
        }

        var image = reminder.Attachments.FirstOrDefault(a => a.IsImage);
        if (image != null && reminders != null)
        {
            var path = reminders.GetAttachmentFullPath(image);
            if (File.Exists(path))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 900;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    AttachmentBrush.ImageSource = bitmap;
                    ImageHost.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Alert could not load attachment image: {ex.Message}");
                }
            }
        }
    }

    private void AnimateIn()
    {
        if (_actionTaken) return; // already fading out; don't fade back in over it

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => OnShown();
        Root.BeginAnimation(OpacityProperty, fade);

        var grow = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void OnShown()
    {
        if (!HasCard || DiagSnapshot.Dir == null) return;

        _snapshotCounter++;
        DiagSnapshot.Save((FrameworkElement)Content, $"alert-{_snapshotCounter}",
            new LinearGradientBrush(Color.FromRgb(0x3A, 0x6E, 0xA5), Color.FromRgb(0x8E, 0x5B, 0x9E), 30));
        DiagSnapshot.Save(CardHost, $"alert-{_snapshotCounter}-card");

        if (Environment.GetEnvironmentVariable("REMINDERX_TEST_AUTOCLICK") == "1")
            Choose(AlertAction.Complete);
    }

    public void FadeOutAndClose()
    {
        _actionTaken = true;
        if (_closing) return;

        var fade = new DoubleAnimation(Root.Opacity, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            if (!_closing) Close();
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void Choose(AlertAction action)
    {
        if (_actionTaken) return;
        _actionTaken = true;
        ActionChosen?.Invoke(action);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);

        // Alt+F4 on the card counts as "Snooze" so the reminder isn't silently lost.
        if (HasCard && !_actionTaken)
        {
            _actionTaken = true;
            ActionChosen?.Invoke(AlertAction.Snooze);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => Choose(AlertAction.Open);
    private void Snooze_Click(object sender, RoutedEventArgs e) => Choose(AlertAction.Snooze);
    private void Complete_Click(object sender, RoutedEventArgs e) => Choose(AlertAction.Complete);
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Choose(AlertAction.Dismiss);
}
