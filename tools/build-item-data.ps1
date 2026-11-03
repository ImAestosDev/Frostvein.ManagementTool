# Builds the local item table and icon archive for the drop tracker.
# Output stays out of git: names and icons are game data.
param(
    [string]$Dev = (Resolve-Path "$PSScriptRoot\..\..").Path
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.IO.Compression, System.IO.Compression.FileSystem

$itemDat = "$Dev\Emulator\Frostvein\Frostvein.Resources\Dat\Item.dat"
$lang = "$Dev\Client\NostaleData\NSlangData_DE.NOS"
$iconDir = "$Dev\htdocs\assets\img\Icons"
$assets = "$PSScriptRoot\..\src\FMT\Assets"

Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.IO; using System.Text;
public static class NosLang {
    const string Digits = "\0 -.0123456789";

    // Lines are chunks closed by 0xFF: marker bit 0x80 = packed digits, else text xor 0x33.
    public static Dictionary<string,string> Read(string path, string file) {
        var b = File.ReadAllBytes(path);
        var enc = Encoding.GetEncoding(1252);
        var result = new Dictionary<string,string>();
        int pos = 0, count = BitConverter.ToInt32(b, pos); pos += 4;
        for (int i = 0; i < count; i++) {
            pos += 4;
            int len = BitConverter.ToInt32(b, pos); pos += 4;
            string name = Encoding.ASCII.GetString(b, pos, len); pos += len + 4;
            int size = BitConverter.ToInt32(b, pos); pos += 4;
            if (name == file) {
                var line = new List<byte>();
                for (int p = pos, end = pos + size; p < end; ) {
                    byte marker = b[p++];
                    if (marker == 0xFF) {
                        var text = enc.GetString(line.ToArray());
                        int tab = text.IndexOf('\t');
                        if (tab > 0) result[text.Substring(0, tab)] = text.Substring(tab + 1);
                        line.Clear();
                    } else if ((marker & 0x80) != 0) {
                        int chars = marker & 0x7F;
                        for (int c = 0; c < chars; c++) {
                            int nibble = (c % 2 == 0) ? b[p + c / 2] >> 4 : b[p + c / 2] & 0xF;
                            if (nibble > 0 && nibble < Digits.Length) line.Add((byte)Digits[nibble]);
                        }
                        p += (chars + 1) / 2;
                    } else {
                        for (int c = 0; c < marker; c++) line.Add((byte)(b[p + c] ^ 0x33));
                        p += marker;
                    }
                }
            }
            pos += size;
        }
        return result;
    }
}
'@

$names = [NosLang]::Read($lang, '_code_de_Item.txt')
"Namen: $($names.Count)"

$rows = New-Object System.Collections.Generic.List[string]
$icons = New-Object 'System.Collections.Generic.HashSet[string]'
$vnum = $null; $key = $null
foreach ($line in [IO.File]::ReadLines($itemDat)) {
    $t = $line.Trim() -split '\s+'
    if ($t[0] -eq 'VNUM') { $vnum = $t[1] }
    elseif ($t[0] -eq 'NAME') { $key = $t[1] }
    elseif ($t[0] -eq 'INDEX' -and $t.Length -gt 5) {
        $name = $names[$key]
        if (-not $name) { $name = '' }
        $rows.Add("$vnum`t$($t[5])`t$name")
        [void]$icons.Add($t[5])
    }
}
[IO.File]::WriteAllLines("$assets\items.tsv", $rows, (New-Object Text.UTF8Encoding($false)))
"Items: $($rows.Count), davon ohne Namen: $(($rows | Where-Object { $_.EndsWith("`t") }).Count)"


$cardNames = [NosLang]::Read($lang, '_code_de_Card.txt')
$cardRows = New-Object System.Collections.Generic.List[string]
$card = $null
foreach ($line in [IO.File]::ReadLines("$Dev\Emulator\Frostvein\Frostvein.Resources\Dat\Card.dat")) {
    $t = $line.Trim() -split '\s+'
    if ($t[0] -eq 'VNUM') { $card = $t[1] }
    elseif ($t[0] -eq 'NAME' -and $card -ne $null) {
        $name = $cardNames[$t[1]]
        if ($name) { $cardRows.Add("$card`t$name") }
        $card = $null
    }
}
[IO.File]::WriteAllLines("$assets\cards.tsv", $cardRows, (New-Object Text.UTF8Encoding($false)))
"Buffs: $($cardRows.Count)"
$zipPath = "$assets\icons.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
$written = 0
foreach ($icon in $icons) {
    $file = "$iconDir\$icon.png"
    if (-not (Test-Path $file)) { continue }
    # Re-encoding shrinks the raw 32 bit PNGs a lot.
    $image = [Drawing.Image]::FromFile($file)
    $entry = $zip.CreateEntry("$icon.png", 'NoCompression')
    $stream = $entry.Open()
    $image.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $stream.Dispose()
    $image.Dispose()
    $written++
}
$zip.Dispose()
"Icons: $written, Archiv: $([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB"
