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
        ["Hell"] = new ThemeSettings
        {
            Background = "#F2F5F8", Surface = "#FFFFFF", Surface2 = "#E6ECF2", Border = "#CFD9E2",
            Text = "#17222F", Muted = "#5D6F80", Accent = "#1F7FBF", Positive = "#1E8A5A", Negative = "#C23F47",
        },
    };

    public static void ApplyPreset(ThemeSettings t, string name)
    {
        var p = Presets[name];
        t.Preset = name;
        (t.Background, t.Surface, t.Surface2, t.Border, t.Text, t.Muted, t.Accent, t.Positive, t.Negative) =
            (p.Background, p.Surface, p.Surface2, p.Border, p.Text, p.Muted, p.Accent, p.Positive, p.Negative);
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
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
