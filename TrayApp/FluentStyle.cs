using System.Drawing;
using System.Drawing.Drawing2D;

namespace BatteryPerformanceManager.TrayApp;

/// Shared Windows 11 look of the flyout controls: fonts and rounded shapes.
/// Sizes are given in logical pixels (as at 100% scaling) and converted for the DPI
/// of the window they're drawn in, so text stays crisp at any display scaling.
internal static class FluentStyle
{
    private static readonly string[] TextFamilies = { "Segoe UI Variable Text", "Segoe UI" };
    private static readonly string[] SemiboldFamilies = { "Segoe UI Variable Text Semibold", "Segoe UI Semibold" };
    private static readonly string[] IconFamilies = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };

    public static Font TextFont(float logicalPx, int dpi, bool semibold = false) =>
        CreateFont(semibold ? SemiboldFamilies : TextFamilies, logicalPx, dpi);

    public static Font IconFont(float logicalPx, int dpi) => CreateFont(IconFamilies, logicalPx, dpi);

    public static int Scale(int logicalPx, int dpi) => (int)Math.Round(logicalPx * dpi / 96f);

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Windows 11 fonts first, Windows 10 ones as fallback. GDI+ silently substitutes a
    // missing family, so each candidate is checked by name.
    private static Font CreateFont(string[] families, float logicalPx, int dpi)
    {
        float px = logicalPx * dpi / 96f;
        foreach (string family in families)
        {
            var font = new Font(family, px, GraphicsUnit.Pixel);
            if (font.Name == family)
            {
                return font;
            }

            font.Dispose();
        }

        return new Font(FontFamily.GenericSansSerif, px, GraphicsUnit.Pixel);
    }
}

/// Icons as glyphs of the Segoe Fluent Icons font that ships with Windows 11 (Segoe
/// MDL2 Assets on Windows 10, same code points): the app carries no icon images of
/// its own besides the logo.
internal static class Glyphs
{
    public const string Leaf = "\uEC0A";
    public const string Battery8 = "\uEBA8";
    public const string Battery10 = "\uEBAA";
    public const string Bolt = "\uE945";
    public const string SpeedMedium = "\uEC49";
    public const string Thermometer = "\uE9CA";
    public const string Moon = "\uE708";
    public const string SpeedHigh = "\uEC4A";
    public const string Close = "\uE711";
}
