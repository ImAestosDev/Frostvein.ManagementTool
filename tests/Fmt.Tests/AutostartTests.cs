using System.Xml.Linq;
using Fmt.Core;
using Xunit;

namespace Fmt.Tests;

public class AutostartTests
{
    [Fact]
    public void TaskRunsElevatedAtLogonWithoutLimits()
    {
        var xml = XDocument.Parse(AutostartTask.Xml(@"C:\Spiele & Tools\FMT.exe", "--autostart", @"PC\Adrian"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"C:\Spiele & Tools\FMT.exe", xml.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--autostart", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("HighestAvailable", xml.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal(@"PC\Adrian", xml.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("PT0S", xml.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", xml.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
    }
}
