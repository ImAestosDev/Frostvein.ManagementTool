using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Fmt.App;

public enum DxvkState { NoFolder, Off, On, Foreign }

// Puts DXVK's d3d9.dll next to the game so NosTale draws through Vulkan.
// Pinned to 2.7.1: tested on the live client; 3.1.1 crashes it at start.
public static class Dxvk
{
    public const string Version = "2.7.1";
    private const string Url = "https://github.com/doitsujin/dxvk/releases/download/v2.7.1/dxvk-2.7.1.tar.gz";
    private const string ArchiveSha256 = "D85CE7C79F57ECD765AAA1B9E7007CB875E6FDE9F6D331DF799BCE73D513CE87";
    private const string DllSha256 = "00ECD422B3B12E9D3B309785F3F19431405389D80EB4A37018EBC63E2D87D900";
    private const string Entry = "dxvk-2.7.1/x32/d3d9.dll";
    private const string Dll = "d3d9.dll", Parked = "d3d9.dll.fmt-off", Backup = "d3d9.dll.fmt-backup";
    public const string ClientExe = "NostaleClientX.exe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static bool IsClientFolder(string? folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, ClientExe));

    // A running client tells us its folder; otherwise the one picked before.
    public static string? FindFolder(string? remembered)
    {
        foreach (var p in Process.GetProcessesByName("NostaleClientX"))
        {
            try
            {
                var folder = Path.GetDirectoryName(p.MainModule?.FileName);
                if (IsClientFolder(folder)) return folder;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        if (IsClientFolder(remembered)) return remembered;
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nostale");
        return IsClientFolder(docs) ? docs : null;
    }

    public static DxvkState State(string? folder)
    {
        if (!IsClientFolder(folder)) return DxvkState.NoFolder;
        var dll = Path.Combine(folder!, Dll);
        if (!File.Exists(dll)) return DxvkState.Off;
        return Hash(File.ReadAllBytes(dll)) == DllSha256 ? DxvkState.On : DxvkState.Foreign;
    }

    public static async Task Enable(string folder)
    {
        var dll = Path.Combine(folder, Dll);
        var parked = Path.Combine(folder, Parked);
        byte[] bytes;
        if (File.Exists(parked) && Hash(File.ReadAllBytes(parked)) == DllSha256) bytes = File.ReadAllBytes(parked);
        else bytes = await Download();

        // Someone else's d3d9.dll (ReShade, an other DXVK) is kept, not lost.
        if (File.Exists(dll))
        {
            if (Hash(File.ReadAllBytes(dll)) == DllSha256) return;
            File.Move(dll, Path.Combine(folder, Backup), overwrite: true);
        }
        File.WriteAllBytes(dll, bytes);
        if (File.Exists(parked)) File.Delete(parked);
    }

    // Renaming works even while a running client has the file loaded.
    public static void Disable(string folder)
    {
        var dll = Path.Combine(folder, Dll);
        if (File.Exists(dll) && Hash(File.ReadAllBytes(dll)) == DllSha256) File.Move(dll, Path.Combine(folder, Parked), overwrite: true);
        var backup = Path.Combine(folder, Backup);
        if (!File.Exists(dll) && File.Exists(backup)) File.Move(backup, dll);
    }

    private static async Task<byte[]> Download()
    {
        var archive = await Http.GetByteArrayAsync(Url);
        if (Hash(archive) != ArchiveSha256) throw new InvalidDataException("Der DXVK-Download passt nicht zur offiziellen Version.");
        using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.Name != Entry || entry.DataStream == null) continue;
            using var ms = new MemoryStream();
            entry.DataStream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (Hash(bytes) != DllSha256) throw new InvalidDataException("Die DXVK-Datei passt nicht zur offiziellen Version.");
            return bytes;
        }
        throw new InvalidDataException("Im DXVK-Download fehlt die 32-Bit-d3d9.dll.");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
