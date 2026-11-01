# FMT – Frostvein Management Tool

In-game overlay for NosTale: dailies, trading, farming, raids, crafting, events and gold per day.
Everything is data driven, so categories, fields, formulas, layout and colors can be changed inside the tool.

The overlay works from outside the game: no DLL, no hook into the game process, no packets.

## Run

`publish/FMT.exe` (needs the .NET 10 Desktop Runtime). Default hotkeys: `Ctrl+Alt+F` toggles, `Ctrl+Alt+N` quick entry.
A tray icon offers open and quit. Starting the exe a second time toggles the running instance.

## Build

```
dotnet test tests/Fmt.Tests/Fmt.Tests.csproj
dotnet publish src/FMT/FMT.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Debug builds can render all pages to PNG with sample data: `FMT.exe --data <temp.json> --shots <dir>`.

## Data

`%APPDATA%\Frostvein\FMT\data.json`, written atomically, with `backups\` (one per day, 30 kept).
`--data <path>` uses another file.

## Attaching to NosTale

- The game window has class `TNosTaleMainF`. `Process.MainWindowHandle` is only the hidden 0x0 `TApplication` shell.
- The client runs elevated, so Windows refuses owning its window. FMT is topmost instead and hides while another program is in front.
- With several clients FMT follows the active one.
