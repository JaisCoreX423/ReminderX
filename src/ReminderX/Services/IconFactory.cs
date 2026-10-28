using System.Drawing;
using Application = System.Windows.Application;

namespace ReminderX.Services;

/// <summary>Loads the ReminderX app icon (Assets/reminderx.ico, embedded as a WPF resource) for the tray.</summary>
public static class IconFactory
{
    public static Icon CreateTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("Assets/reminderx.ico", UriKind.Relative));
            if (info != null)
            {
                using var stream = info.Stream;
                return new Icon(stream, 32, 32);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load tray icon from resources: {ex.Message}");
        }

        return SystemIcons.Application;
    }
}
