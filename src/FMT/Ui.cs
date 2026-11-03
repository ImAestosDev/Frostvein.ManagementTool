using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace Fmt.App;

// Small UI kit; all colors stay resource references so themes switch live.
public static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static class G
    {
        public const string Restart = "", Power = "", Damage = "", Packets = "", Shrink = "", Grow = "";
        public const string Exp = "", Drops = "", Monsters = "", Items ="", Session ="";
        public const string Home ="", Check = "", Settings = "", Add = "", Edit = "", Delete = "",
            Copy = "", Up = "", Down = "", Close = "", Export = "", Sliders = "", Back = "", Forward = "",
            Clock = "", Flame = "", Folder = "", Undo = "";
    }

    public static readonly string[] IconChoices =
    [
        "", "", "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "", "", "",
    ];

    public static readonly string[] ColorChoices =
    [
        "#E8B65A", "#F0D264", "#5CC28F", "#54C7A0", "#6FB7E9", "#6F8FE9", "#B79CED", "#E58AC6", "#E5787C", "#EE9A5B", "#8EA3B5", "#E6EEF5",
    ];

    public static Color ColorOf(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Colors.Gray; }
    }

    public static bool IsColor(string text)
    {
        try { return text.StartsWith('#') && ColorConverter.ConvertFromString(text) is Color; }
        catch (FormatException) { return false; }
    }

    public static Brush Hex(string hex) => new SolidColorBrush(ColorOf(hex));

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static TextBlock T(string text, double size = 13, string brush = "Ink", bool bold = false, bool wrap = false, bool trim = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Glyph(string glyph, double size = 14, string brush = "Muted")
    {
        var t = T(glyph, size, brush);
        t.FontFamily = IconFont;
        t.TextAlignment = TextAlignment.Center;
        return t;
    }

    public static TextBlock GlyphColored(string glyph, string hex, double size = 14)
    {
        var t = Glyph(glyph, size);
        t.Foreground = Hex(hex);
        return t;
    }

    public static Border Card(UIElement child, double pad = 12)
    {
        var b = new Border { Child = child, Padding = new Thickness(pad), BorderThickness = new Thickness(1) };
        b.SetResourceReference(Border.BackgroundProperty, "Surface");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.SetResourceReference(Border.CornerRadiusProperty, "Corner");
        return b;
    }

    public static Button Btn(string text, Action click, string? style = null, string? glyph = null, string? tip = null)
    {
        var b = new Button { ToolTip = tip };
        if (style != null) b.Style = (Style)Application.Current.Resources[style];
        var fg = style == "Primary" ? "AccentInk" : style == "Ghost" ? "Muted" : "Ink";
        b.Content = glyph == null
            ? T(text, 13, fg)
            : Row(6, Glyph(glyph, 12, fg), T(text, 13, fg));
        b.Click += (_, _) => click();
        return b;
    }

    public static Button IconBtn(string glyph, string tip, Action click, double size = 13)
    {
        var b = new Button
        {
            Style = (Style)Application.Current.Resources["Ghost"],
            Content = Glyph(glyph, size),
            ToolTip = tip,
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static StackPanel Col(double gap, params UIElement?[] children) => Stack(Orientation.Vertical, gap, children);

    public static StackPanel Row(double gap, params UIElement?[] children) => Stack(Orientation.Horizontal, gap, children);

    private static StackPanel Stack(Orientation o, double gap, UIElement?[] children)
    {
        var s = new StackPanel { Orientation = o };
        foreach (var c in children) AddTo(s, c, gap);
        return s;
    }

    public static void AddTo(StackPanel s, UIElement? child, double gap)
    {
        if (child == null) return;
        if (s.Children.Count > 0 && child is FrameworkElement f)
        {
            var m = f.Margin;
            f.Margin = s.Orientation == Orientation.Vertical
                ? new Thickness(m.Left, m.Top + gap, m.Right, m.Bottom)
                : new Thickness(m.Left + gap, m.Top, m.Right, m.Bottom);
        }
        s.Children.Add(child);
    }

    // Columns spec such as "*,Auto,120"; children fill the columns left to right.
    public static Grid Cols(string spec, params UIElement?[] children)
    {
        var g = new Grid();
        foreach (var part in spec.Split(','))
        {
            var length = part.Trim() switch
            {
                "Auto" => GridLength.Auto,
                "*" => new GridLength(1, GridUnitType.Star),
                var s when s.EndsWith('*') => new GridLength(double.Parse(s[..^1], CultureInfo.InvariantCulture), GridUnitType.Star),
                var s => new GridLength(double.Parse(s, CultureInfo.InvariantCulture)),
            };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        }
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is not { } child) continue;
            Grid.SetColumn(child, i);
            g.Children.Add(child);
        }
        return g;
    }

    public static FrameworkElement Bar(double fraction, string brush = "Accent", double height = 5)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Surface2");
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        var host = new Grid { Height = height };
        host.Children.Add(track);
        host.Children.Add(fill);
        host.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(fraction, 0, 1);
        return host;
    }

    public static TextBox Input(string text, Action<string>? changed = null, string hint = "")
    {
        var t = new TextBox { Text = text, Tag = hint };
        if (changed != null) t.TextChanged += (_, _) => changed(t.Text);
        return t;
    }

    public static ComboBox Combo<T>(IEnumerable<(string Label, T Value)> items, T selected, Action<T> changed)
    {
        var c = new ComboBox();
        foreach (var (label, value) in items)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            c.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(value, selected)) c.SelectedItem = item;
        }
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is ComboBoxItem { Tag: T v }) changed(v);
        };
        return c;
    }

    public static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var c = new CheckBox { IsChecked = value, Content = T(label) };
        c.Click += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    public static FrameworkElement Labeled(string label, UIElement control) => Col(4, T(label, 12, "Muted"), control);

    public static TextBlock GoldText(long value, double size = 13, bool bold = true)
    {
        var brush = value > 0 ? "Positive" : value < 0 ? "Negative" : "Muted";
        var t = T(Gold.FormatSigned(value), size, brush, bold);
        t.ToolTip = Gold.FormatFull(value) + " Gold";
        return t;
    }

    public static ImageSource Logo => _logo ??= BitmapFrame.Create(new Uri("pack://application:,,,/Assets/logo.png"));
    private static ImageSource? _logo;

    public static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T found) return found;
            if (FindChild<T>(c) is { } deeper) return deeper;
        }
        return null;
    }

    public static string DateLabel(DateOnly d, DateOnly today)
    {
        var diff = today.DayNumber - d.DayNumber;
        var text = d.ToString("ddd dd.MM.yyyy", new CultureInfo("de-DE"));
        return diff switch { 0 => "Heute · " + text, 1 => "Gestern · " + text, _ => text };
    }

    public static DateOnly? ParseDate(string text, DateOnly today)
    {
        var s = text.Trim().ToLowerInvariant();
        switch (s)
        {
            case "heute": return today;
            case "gestern": return today.AddDays(-1);
            case "vorgestern": return today.AddDays(-2);
        }
        var de = new CultureInfo("de-DE");
        if (DateOnly.TryParseExact(s, ["d.M.yyyy", "d.M.yy"], de, DateTimeStyles.None, out var full)) return full;
        return DateOnly.TryParseExact(s.TrimEnd('.'), "d.M", de, DateTimeStyles.None, out var partial)
            ? new DateOnly(today.Year, partial.Month, partial.Day)
            : null;
    }
}

// Base for everything shown in the panel's content area.
public abstract class PageView : ContentControl
{
    public abstract void Refresh();

    // True when the page consumed Esc, e.g. to close its editor.
    public virtual bool HandleEscape() => false;
}

public sealed class UniformRow : UniformGrid
{
    public UniformRow(double gap, params FrameworkElement[] items)
    {
        Rows = 1;
        Columns = items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            items[i].Margin = new Thickness(i == 0 ? 0 : gap / 2, 0, i == items.Length - 1 ? 0 : gap / 2, 0);
            Children.Add(items[i]);
        }
    }
}
