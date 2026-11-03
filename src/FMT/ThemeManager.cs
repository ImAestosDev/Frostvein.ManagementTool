using System.Windows.Media.Imaging;

namespace Fmt.App;

public static class ThemeManager
{
    public static readonly Dictionary<string, ThemeSettings> Presets = new()
    {
        ["Frostvein"] = new ThemeSettings(),
        ["Glut"] = new ThemeSettings
        {
            Background = "#17110E", Surface = "#241A15", Surface2 = "#33251D", Border = "#4A3528",
            Text = "#F4EAE3", Muted = "#B39E90", Accent = "#EE9A5B", Positive = "#8CC97A", Negative = "#E8706A",
        },
        ["Smaragd"] = new ThemeSettings
        {
            Background = "#0D1613", Surface = "#15231E", Surface2 = "#1F322B", Border = "#2D4840",
            Text = "#E4F2EC", Muted = "#8EAA9E", Accent = "#54C7A0", Positive = "#6FD28F", Negative = "#E5787C",
        },
        ["Violett"] = new ThemeSettings
        {
            Background = "#12101C", Surface = "#1C1929", Surface2 = "#2A2539", Border = "#3D3652",
            Text = "#ECE8F6", Muted = "#A198B8", Accent = "#AE8FEE", Positive = "#6FCB9F", Negative = "#EB7F92",
        },
        ["Graphit"] = new ThemeSettings
        {
            Background = "#121212", Surface = "#1C1C1C", Surface2 = "#2A2A2A", Border = "#3B3B3B",
            Text = "#EDEDED", Muted = "#A0A0A0", Accent = "#D0D0D0", Positive = "#6FC58F", Negative = "#E5787C",
        },
        ["Dunkel"] = new ThemeSettings
        {
            Background = "#000000", Surface = "#0B0B0B", Surface2 = "#171717", Border = "#2A2A2A",
            Text = "#F2F2F2", Muted = "#8F8F8F", Accent = "#6FB7E9", Positive = "#5CC28F", Negative = "#E5787C",
        },
        ["Hell"] = new ThemeSettings
        {
            Background = "#F2F5F8", Surface = "#FFFFFF", Surface2 = "#E6ECF2", Border = "#CFD9E2",
            Text = "#17222F", Muted = "#5D6F80", Accent = "#1F7FBF", Positive = "#1E8A5A", Negative = "#C23F47",
        },
        [NosTaleSkin] = new ThemeSettings
        {
            Background = "#111517", Surface = "#1C2229", Surface2 = "#454F5E", Border = "#606467",
            Text = "#F2F2F2", Muted = "#B4BAC2", Accent = "#2E6FD0", Positive = "#7FD27F", Negative = "#E5787C",
        },
    };

    private const string NosTaleSkin = "NosTale";
    private const string DefaultFont = "Segoe UI Variable Text, Segoe UI";
    // The font of the game's own interface, part of every Windows.
    private const string NosTaleFont = "Tahoma";

    public static void ApplyPreset(ThemeSettings t, string name)
    {
        var p = Presets[name];
        // Leaving the skin gives back what it changed.
        if (t.Preset == NosTaleSkin && name != NosTaleSkin) (t.Radius, t.Opacity) = (p.Radius, p.Opacity);
        t.Preset = name;
        (t.Background, t.Surface, t.Surface2, t.Border, t.Text, t.Muted, t.Accent, t.Positive, t.Negative) =
            (p.Background, p.Surface, p.Surface2, p.Border, p.Text, p.Muted, p.Accent, p.Positive, p.Negative);
        if (name is "Dunkel" or NosTaleSkin) t.Opacity = 1;
        if (name == NosTaleSkin) t.Radius = 0;
    }

    public static void Apply(ThemeSettings t)
    {
        var res = Application.Current.Resources;
        var bg = Ui.ColorOf(t.Background);
        bg.A = (byte)(Math.Clamp(t.Opacity, 0.3, 1) * 255);
        res["Bg"] = Frozen(bg);
        res["Surface"] = Frozen(Ui.ColorOf(t.Surface));
        res["Surface2"] = Frozen(Ui.ColorOf(t.Surface2));
        res["Line"] = Frozen(Ui.ColorOf(t.Border));
        res["Ink"] = Frozen(Ui.ColorOf(t.Text));
        res["Muted"] = Frozen(Ui.ColorOf(t.Muted));
        var accent = Ui.ColorOf(t.Accent);
        res["Accent"] = Frozen(accent);
        res["AccentInk"] = Frozen(Luminance(accent) > 0.45 ? Color.FromRgb(0x0E, 0x16, 0x20) : Colors.White);
        res["Positive"] = Frozen(Ui.ColorOf(t.Positive));
        res["Negative"] = Frozen(Ui.ColorOf(t.Negative));
        res["Corner"] = new CornerRadius(t.Radius);
        res["CornerSmall"] = new CornerRadius(Math.Max(0, t.Radius - 3));
        res["AccentFill"] = res["Accent"];
        res["AccentLine"] = res["Accent"];
        res["ButtonFill"] = res["Surface2"];
        res["ButtonLine"] = res["Line"];
        res["GhostFill"] = Brushes.Transparent;
        res["GhostLine"] = Brushes.Transparent;
        res["NavLine"] = Brushes.Transparent;
        res["HeaderFill"] = Brushes.Transparent;
        res["HeaderLine"] = res["Line"];
        res["FrameOuter"] = Brushes.Transparent;
        res["ThumbFill"] = res["Line"];
        res["SlotFill"] = Fade("#54657A", "#3E4C5E");
        res["SlotLine"] = Frozen(Ui.ColorOf("#7E93AB"));
        res["SlotCorner"] = new CornerRadius(2);
        res["HeaderHeight"] = 40.0;

        var nosTale = t.Preset == NosTaleSkin;
        res["UiFont"] = new FontFamily(nosTale ? NosTaleFont : DefaultFont);
        if (!nosTale) return;

        // Colors measured from the game's windows: hard edges, faint scanlines, black title bars.
        res["Surface2"] = Scanlines("#454F5E", "#485261");
        res["SlotFill"] = res["Surface2"];
        res["SlotLine"] = Frozen(Ui.ColorOf("#8F9BAD"));
        res["SlotCorner"] = new CornerRadius(0);
        res["AccentFill"] = Fade("#5E9CEB", "#2559A8");
        res["AccentLine"] = Frozen(Ui.ColorOf("#A9CBF2"));
        res["ButtonFill"] = Fade("#4B4D4F", "#2B2D31");
        res["ButtonLine"] = Frozen(Ui.ColorOf("#7C7C7C"));
        res["GhostFill"] = Fade("#474847", "#2B2D31");
        res["GhostLine"] = Frozen(Ui.ColorOf("#7C7C7C"));
        res["NavLine"] = Frozen(Ui.ColorOf("#8F9BAD"));
        res["HeaderFill"] = Fade("#474847", "#0E1014");
        res["HeaderLine"] = Frozen(Colors.Black);
        res["FrameOuter"] = Frozen(Colors.Black);
        res["ThumbFill"] = Fade("#6B6B6B", "#3F4140");
        res["HeaderHeight"] = 32.0;
    }

    private static LinearGradientBrush Fade(string top, string bottom)
    {
        var b = new LinearGradientBrush(Ui.ColorOf(top), Ui.ColorOf(bottom), 90);
        b.Freeze();
        return b;
    }

    // Every third row is a touch lighter, as in the game's inventory slots.
    private static ImageBrush Scanlines(string color, string line)
    {
        var (a, b) = (Ui.ColorOf(color), Ui.ColorOf(line));
        byte[] pixels = [a.B, a.G, a.R, 255, a.B, a.G, a.R, 255, b.B, b.G, b.R, 255];
        var tile = BitmapSource.Create(1, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        tile.Freeze();
        var brush = new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 3),
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        return brush;
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
