using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;

namespace ReminderX.Services;

/// <summary>
/// Test-only: renders UI elements to PNG when REMINDERX_SNAPSHOT_DIR is set, so UI changes can be
/// checked visually without a person in front of the screen. No-op in normal use.
/// </summary>
public static class DiagSnapshot
{
    public static string? Dir => Environment.GetEnvironmentVariable("REMINDERX_SNAPSHOT_DIR");

    public static void Save(FrameworkElement element, string name, Brush? background = null)
    {
        var dir = Dir;
        if (string.IsNullOrEmpty(dir)) return;

        try
        {
            element.UpdateLayout();
            var w = (int)Math.Ceiling(element.ActualWidth);
            var h = (int)Math.Ceiling(element.ActualHeight);
            if (w <= 0 || h <= 0)
            {
                Logger.Log($"DiagSnapshot {name}: element has zero size");
                return;
            }

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, w, h);
                if (background != null) ctx.DrawRectangle(background, null, rect);
                // Absolute viewbox: otherwise the brush stretches the content bounds, not the element's box.
                ctx.DrawRectangle(new VisualBrush(element)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = rect,
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                }, null, rect);
            }

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(dir);
            using var file = File.Create(Path.Combine(dir, name + ".png"));
            encoder.Save(file);
            Logger.Log($"DiagSnapshot saved {name}");
        }
        catch (Exception ex)
        {
            Logger.Log($"DiagSnapshot {name} failed: {ex.Message}");
        }
    }
}
