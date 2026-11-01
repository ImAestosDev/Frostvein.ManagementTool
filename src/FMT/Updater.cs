using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace Fmt.App;

// Downloads new releases in the background and swaps the exe while FMT is closed to the badge.
public sealed class Updater : IDisposable
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AppState _state;
    private readonly Func<bool> _canRestart;
    private readonly Action _quit;
    private readonly string _exe = Environment.ProcessPath!;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _checkTimer = new() { Interval = StartDelay };
    private readonly DispatcherTimer _installTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _busy;
    private bool _ready;
    private bool _restartWanted;
    private bool _leaving;

    public string Status { get; private set; } = "Noch nicht geprüft";
    public event Action? StatusChanged;

    public static Version CurrentVersion => Assembly.GetEntryAssembly()!.GetName().Version!;
    public static string CurrentVersionText => CurrentVersion.ToString(3);

    private string NewFile => _exe + ".new";
    private string OldFile => _exe + ".old";

    public Updater(AppState state, Func<bool> canRestart, Action quit)
    {
        _state = state;
        _canRestart = canRestart;
        _quit = quit;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"FMT/{CurrentVersionText}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Interval = CheckInterval;
            if (_state.S.AutoUpdate) _ = CheckAsync();
        };
        _installTimer.Tick += (_, _) => TryInstall();
    }

    public void Start()
    {
        RemoveLeftovers();
        _checkTimer.Start();
    }

    public async Task CheckAsync()
    {
        if (_busy || _ready) return;
        _busy = true;
        try
        {
            SetStatus("Prüfe auf Updates …");
            var json = await _http.GetStringAsync(UpdateFeed.LatestUrl);
            var update = UpdateFeed.Parse(json, CurrentVersion);
            if (update == null)
            {
                SetStatus($"Aktuell, zuletzt geprüft um {DateTime.Now:HH:mm} Uhr");
                return;
            }
            SetStatus($"Lade Version {update.Version.ToString(3)} …");
            await DownloadAsync(update);
            if (!UpdateFeed.MatchesHash(NewFile, update.Sha256))
            {
                File.Delete(NewFile);
                SetStatus("Update verworfen: Prüfsumme stimmt nicht");
                return;
            }
            _ready = true;
            SetStatus($"Version {update.Version.ToString(3)} wird installiert, sobald das Panel geschlossen ist");
            _installTimer.Start();
            TryInstall();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException)
        {
            // No internet or no release yet must never bother the player.
            SetStatus($"Update-Prüfung fehlgeschlagen: {e.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DownloadAsync(UpdateInfo update)
    {
        using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(NewFile);
        await source.CopyToAsync(target);
    }

    // Button in the panel: installs a waiting or new version, otherwise just restarts.
    public async Task RestartAsync()
    {
        if (_restartWanted) return;
        _restartWanted = true;
        try
        {
            while (_busy) await Task.Delay(200);
            await CheckAsync();
            if (_leaving) return;
            if (_ready) Install();
            else Restart();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"Neustart fehlgeschlagen: {e.Message}");
        }
        finally
        {
            _restartWanted = false;
        }
    }

    private void TryInstall()
    {
        if (_ready && _canRestart()) Install();
    }

    private void Install()
    {
        if (_leaving) return;
        _installTimer.Stop();
        try
        {
            // A running exe can be renamed, so its path is free for the new one.
            File.Move(_exe, OldFile, true);
            try
            {
                File.Move(NewFile, _exe);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                File.Move(OldFile, _exe);
                throw;
            }
            Restart();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _ready = false;
            SetStatus($"Update konnte nicht installiert werden: {e.Message}");
        }
    }

    private void Restart()
    {
        var start = new ProcessStartInfo(_exe) { UseShellExecute = false };
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        var wait = args.IndexOf(App.WaitForArgument);
        if (wait >= 0) args.RemoveRange(wait, Math.Min(2, args.Count - wait));
        args.Remove(App.OpenArgument);
        args.ForEach(start.ArgumentList.Add);
        start.ArgumentList.Add(App.WaitForArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (_restartWanted) start.ArgumentList.Add(App.OpenArgument);
        Process.Start(start);
        _leaving = true;
        _quit();
    }

    private void RemoveLeftovers()
    {
        foreach (var file in new[] { OldFile, NewFile })
        {
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _checkTimer.Stop();
        _installTimer.Stop();
        _http.Dispose();
    }
}
