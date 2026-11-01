using System.Security.Cryptography;
using System.Text.Json;

namespace Fmt.Core;

public sealed record UpdateInfo(Version Version, string DownloadUrl, string Sha256);

// Reads the latest GitHub release; anything incomplete or unexpected means no update.
public static class UpdateFeed
{
    public const string Repository = "ImAestosDev/Frostvein.ManagementTool";
    public const string AssetName = "FMT.exe";
    public const string LatestUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private const string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";

    public static UpdateInfo? Parse(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;
            if (!root.TryGetProperty("tag_name", out var tag)) return null;
            if (!Version.TryParse(tag.GetString()?.TrimStart('v', 'V'), out var version)) return null;
            if (Normalize(version) <= Normalize(current)) return null;
            if (!root.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                var hash = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                if (url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)) return null;
                if (hash == null || !hash.StartsWith("sha256:", StringComparison.Ordinal)) return null;
                return new UpdateInfo(version, url, hash["sha256:".Length..]);
            }
            return null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static bool MatchesHash(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    // 1.0.0 and 1.0.0.0 are the same version here.
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
