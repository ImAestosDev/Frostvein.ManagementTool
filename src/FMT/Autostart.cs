using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Fmt.App;

// Elevated: scheduled task, so reading the network starts without a UAC prompt. Otherwise the user's Run key.
public static class Autostart
{
    private const string TaskName = "FMT Autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FMT";

    private static bool? _enabled;

    public static bool IsEnabled => _enabled ??= HasRunValue() || Schtasks("/Query", "/TN", TaskName).Code == 0;

    // Returns an error text or null.
    public static string? Set(bool enabled)
    {
        var error = enabled ? Enable() : Disable();
        _enabled = null;
        return error;
    }

    private static string? Enable()
    {
        var exe = Environment.ProcessPath!;
        if (!PacketCapture.IsAdmin)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(RunValue, $"\"{exe}\" {App.AutostartArgument}");
            return null;
        }

        var file = Path.Combine(Path.GetTempPath(), "fmt-autostart.xml");
        try
        {
            // Task Scheduler expects the UTF-16 the XML header announces.
            File.WriteAllText(file, AutostartTask.Xml(exe, App.AutostartArgument, $"{Environment.UserDomainName}\\{Environment.UserName}"), Encoding.Unicode);
            var (code, output) = Schtasks("/Create", "/TN", TaskName, "/XML", file, "/F");
            if (code != 0) return output;
        }
        finally
        {
            File.Delete(file);
        }
        // The task replaces an older Run value.
        RemoveRunValue();
        return null;
    }

    private static string? Disable()
    {
        RemoveRunValue();
        var (code, output) = Schtasks("/Query", "/TN", TaskName);
        if (code != 0) return null;
        (code, output) = Schtasks("/Delete", "/TN", TaskName, "/F");
        return code == 0 ? null : output;
    }

    private static bool HasRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) != null;
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(RunValue, false);
    }

    private static (int Code, string Output) Schtasks(params string[] args)
    {
        var start = new ProcessStartInfo("schtasks") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, (output + error.Result).Trim());
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, e.Message);
        }
    }
}
