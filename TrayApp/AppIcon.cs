using System.Drawing;

namespace BatteryChargeManager.TrayApp;

internal static class AppIcon
{
    /// Loads the app.ico frame closest to the requested size from the embedded
    /// resource, instead of letting Windows shrink a bigger frame - the small frames
    /// are simplified by hand to stay readable.
    public static Icon Load(Size size)
    {
        try
        {
            using Stream? stream = typeof(AppIcon).Assembly
                .GetManifestResourceStream("BatteryChargeManager.TrayApp.app.ico");

            return stream is not null ? new Icon(stream, size) : FallbackIcon();
        }
        catch
        {
            return FallbackIcon();
        }
    }

    // A copy, so callers can dispose what they get without breaking the shared system icon.
    private static Icon FallbackIcon() => (Icon)SystemIcons.Application.Clone();
}
