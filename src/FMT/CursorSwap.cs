using System.IO;

namespace Fmt.App;

// Swaps NosTale's mouse cursors for the Moga Dark pack (or any Windows cursor pack with the usual file names).
// FMT's d3d9.dll (native/cursor) loads fmt-cursors\<hash>.cur per game cursor and default.cur for the rest,
// but only while fmt-cursors\on exists. The pack is copyrighted, so FMT never ships it: each user brings their own.
public static class CursorSwap
{
    public const string Folder = "fmt-cursors";
    public const string PackName = "Moga Dark";

    // Game cursor (hash of its pixels, as the d3d9.dll names it under fmt-cursors\game) -> Windows cursor role.
    private static readonly Dictionary<uint, string> GameCursors = new()
    {
        [0x166CC46E] = "pointer",   // orange arrow
        [0xAEDA6858] = "link",      // pointing hand
        [0x1BA60FB3] = "link",      // open hand over an item: Point (no hold)
        [0x47EA0281] = "move",      // holding or dragging an item: Point (hold)
        [0x5C8550B1] = "unavailable", // red circle, not allowed
        [0x2566E736] = "cross",     // sword over a monster: crosshair, as Adrian wanted
        [0xD1D5BF74] = "help",      // speech bubble over an NPC: Dialogue
    };

    // Windows cursor roles and the file names Moga uses for them.
    private static readonly (string Role, string File)[] Files =
    [
        ("pointer", "Normal Select"), ("help", "Help Select"), ("text", "Text Select"), ("unavailable", "Unavailable"),
        ("vert", "Vertical Resize"), ("horz", "Horizontal Resize"), ("dgn1", "Diagonal Resize 1"), ("dgn2", "Diagonal Resize 2"),
        ("move", "Move"), ("link", "Link Select"), ("cross", "Precision Select"), ("hand", "HandWriting"),
        ("alternate", "Alternate Select"), ("location", "Location Select"), ("person", "Person Select"),
    ];

    public static Dictionary<string, string> ReadPack(string folder)
    {
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, name) in Files)
        {
            var file = Path.Combine(folder, name + ".cur");
            if (File.Exists(file)) roles[role] = file;
        }
        return roles;
    }

    public static bool IsPack(string? folder) => !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && ReadPack(folder).ContainsKey("pointer");

    // Looks for an unpacked Moga Dark in the usual places, e.g. Desktop\Moga-Windows-Free\Moga-Dark.
    public static string? FindPack(string? remembered)
    {
        if (IsPack(remembered)) return remembered;
        foreach (var root in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments }.Select(Environment.GetFolderPath)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")))
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "Moga*").SelectMany(d => Directory.EnumerateDirectories(d, "Moga-Dark*").Prepend(d)))
                    if (dir.Contains("Dark", StringComparison.OrdinalIgnoreCase) && IsPack(dir)) return dir;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public static bool IsOn(string? gameFolder) => gameFolder != null && File.Exists(Path.Combine(gameFolder, Folder, "on"));

    // Copies the pack's cursors next to the game; null switches back to NosTale's own.
    public static void Apply(string gameFolder, string? pack)
    {
        var dir = Path.Combine(gameFolder, Folder);
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir, "*.cur")) File.Delete(old);
        var on = Path.Combine(dir, "on");
        if (!IsPack(pack))
        {
            if (File.Exists(on)) File.Delete(on);
            return;
        }
        var roles = ReadPack(pack!);
        File.Copy(roles["pointer"], Path.Combine(dir, "default.cur"));
        foreach (var (hash, role) in GameCursors)
        {
            if (!roles.TryGetValue(role, out var file)) continue;
            var bytes = File.ReadAllBytes(file);
            // Holding an item swaps the hover hand for the grab hand; with the hover hand's hotspot it stays in place instead of jumping.
            if (role == "move" && roles.TryGetValue("link", out var link)) Array.Copy(File.ReadAllBytes(link), 10, bytes, 10, 4);
            File.WriteAllBytes(Path.Combine(dir, $"{hash:X8}.cur"), bytes);
        }
        File.WriteAllText(on, "");
    }
}
