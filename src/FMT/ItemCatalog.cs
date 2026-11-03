using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media.Imaging;

namespace Fmt.App;

// Item names and icons, built into the exe by tools/build-item-data.ps1.
public static class ItemCatalog
{
    private const string Base = "pack://application:,,,/Assets/";

    private sealed record Row(string Icon, string Name);

    private static Dictionary<int, Row>? _rows;
    private static ZipArchive? _icons;
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    public static string Name(int vnum) =>
        Rows.TryGetValue(vnum, out var row) && row.Name.Length > 0 ? row.Name : $"Item {vnum}";

    public static ImageSource? Icon(int vnum)
    {
        if (!Rows.TryGetValue(vnum, out var row)) return null;
        if (Cache.TryGetValue(row.Icon, out var known)) return known;
        return Cache[row.Icon] = LoadIcon(row.Icon);
    }

    private static Dictionary<int, Row> Rows => _rows ??= LoadRows();

    private static Stream? Open(string file)
    {
        try
        {
            return Application.GetResourceStream(new Uri(Base + file))?.Stream;
        }
        catch (IOException)
        {
            // Builds without the local item data just show empty slots.
            return null;
        }
    }

    private static Dictionary<int, Row> LoadRows()
    {
        var rows = new Dictionary<int, Row>();
        using var stream = Open("items.tsv");
        if (stream == null) return rows;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 3 && int.TryParse(parts[0], out var vnum)) rows[vnum] = new Row(parts[1], parts[2]);
        }
        return rows;
    }

    private static ImageSource? LoadIcon(string icon)
    {
        _icons ??= Open("icons.zip") is { } zip ? new ZipArchive(zip, ZipArchiveMode.Read) : null;
        if (_icons?.GetEntry(icon + ".png") is not { } entry) return null;
        using var source = entry.Open();
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        copy.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = copy;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
