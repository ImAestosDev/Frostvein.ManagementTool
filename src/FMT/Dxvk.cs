using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Fmt.App;

public enum DxvkState { NoFolder, Off, On, Foreign }

// Puts DXVK next to the game so NosTale draws through Vulkan.
// Pinned to 2.7.1: tested on the live client; 3.1.1 crashes it at start.
// Layout: FMT's own d3d9.dll (cursor shim, native/cursor) loads fmt_dxvk.dll, or Windows' d3d9.dll when DXVK is off.
public static class Dxvk
{
    public const string Version = "2.7.1";
    private const string Url = "https://github.com/doitsujin/dxvk/releases/download/v2.7.1/dxvk-2.7.1.tar.gz";
    private const string ArchiveSha256 = "D85CE7C79F57ECD765AAA1B9E7007CB875E6FDE9F6D331DF799BCE73D513CE87";
    private const string DllSha256 = "00ECD422B3B12E9D3B309785F3F19431405389D80EB4A37018EBC63E2D87D900";
    private const string Entry = "dxvk-2.7.1/x32/d3d9.dll";
    private const string Dll = "d3d9.dll", Chained = "fmt_dxvk.dll", Parked = "d3d9.dll.fmt-off", Backup = "d3d9.dll.fmt-backup";
    public const string ClientExe = "NostaleClientX.exe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly byte[] ShimMarker = Encoding.ASCII.GetBytes("FMT-CURSOR-SHIM");

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
        var bytes = File.ReadAllBytes(dll);
        if (Hash(bytes) == DllSha256) return DxvkState.On;
        if (!IsShim(bytes)) return DxvkState.Foreign;
        var chained = Path.Combine(folder!, Chained);
        return File.Exists(chained) && Hash(File.ReadAllBytes(chained)) == DllSha256 ? DxvkState.On : DxvkState.Off;
    }

    public static async Task Enable(string folder)
    {
        var chained = Path.Combine(folder, Chained);
        var dll = Path.Combine(folder, Dll);
        var parked = Path.Combine(folder, Parked);
        if (!(File.Exists(chained) && Hash(File.ReadAllBytes(chained)) == DllSha256))
        {
            byte[] bytes;
            if (File.Exists(parked) && Hash(File.ReadAllBytes(parked)) == DllSha256) bytes = File.ReadAllBytes(parked);
            else if (File.Exists(dll) && Hash(File.ReadAllBytes(dll)) == DllSha256) bytes = File.ReadAllBytes(dll);
            else bytes = await Download();
            File.WriteAllBytes(chained, bytes);
        }
        if (File.Exists(parked)) File.Delete(parked);
        InstallShim(folder);
    }

    // Renaming works even while a running client has the file loaded.
    public static void Disable(string folder, bool keepShim)
    {
        var chained = Path.Combine(folder, Chained);
        var dll = Path.Combine(folder, Dll);
        if (File.Exists(chained)) File.Move(chained, Path.Combine(folder, Parked), overwrite: true);
        if (File.Exists(dll) && Hash(File.ReadAllBytes(dll)) == DllSha256) File.Move(dll, Path.Combine(folder, Parked), overwrite: true);
        if (keepShim) InstallShim(folder);
        else RemoveShim(folder);
    }

    // Puts FMT's d3d9.dll in place; someone else's (ReShade, an other DXVK) is kept as a backup, not lost.
    public static void InstallShim(string folder)
    {
        var dll = Path.Combine(folder, Dll);
        var shim = Shim();
        if (File.Exists(dll))
        {
            var bytes = File.ReadAllBytes(dll);
            if (bytes.AsSpan().SequenceEqual(shim)) return;
            if (Hash(bytes) == DllSha256) File.Move(dll, Path.Combine(folder, Chained), overwrite: true);
            else if (!IsShim(bytes)) File.Move(dll, Path.Combine(folder, Backup), overwrite: true);
            else MoveAside(dll);
        }
        File.WriteAllBytes(dll, shim);
    }

    public static void RemoveShim(string folder)
    {
        var dll = Path.Combine(folder, Dll);
        if (File.Exists(dll) && IsShim(File.ReadAllBytes(dll))) MoveAside(dll);
        var backup = Path.Combine(folder, Backup);
        if (!File.Exists(dll) && File.Exists(backup)) File.Move(backup, dll);
    }

    public static bool ShimInstalled(string? folder)
    {
        var dll = folder == null ? null : Path.Combine(folder, Dll);
        return dll != null && File.Exists(dll) && IsShim(File.ReadAllBytes(dll));
    }

    // A loaded dll cannot be deleted, only renamed; the leftover is cleaned up on a later call.
    private static void MoveAside(string dll)
    {
        var old = dll + ".fmt-old";
        try { if (File.Exists(old)) File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { old += DateTime.Now.Ticks; }
        File.Move(dll, old);
        try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsShim(byte[] bytes) => bytes.AsSpan().IndexOf(ShimMarker) >= 0;

    private static byte[] Shim()
    {
        using var stream = typeof(Dxvk).Assembly.GetManifestResourceStream("fmt_cursor.dll")
            ?? throw new InvalidDataException("Die Cursor-DLL fehlt in FMT.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
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
