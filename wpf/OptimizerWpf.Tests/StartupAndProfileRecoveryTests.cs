using System;
using System.Linq;
using System.Xml.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class StartupAndProfileRecoveryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupMoveNeverRenamesWithinRunKey(bool enabled)
    {
        var (source, destination) = SystemService.StartupMovePaths(enabled);
        Assert.NotEqual(source, destination);
        Assert.Equal(SystemService.DisabledStartupKeyPath, enabled ? source : destination);
        Assert.EndsWith(@"\Run", enabled ? destination : source);
    }

    [Fact]
    public void DelayTaskUsesLeastPrivilegeWithoutChangingGearWinAutostart()
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var xml = TaskSchedulerService.BuildLogonTaskXml(@"C:\Apps\example.exe", "--test", 15, "user", "test", elevated: false);
        Assert.Equal("LeastPrivilege", XDocument.Parse(xml).Descendants(ns + "RunLevel").Single().Value);
        var gearwin = TaskSchedulerService.BuildLogonTaskXml(@"C:\Apps\GearWin.exe", "--tray", 15, "user", "test");
        Assert.Equal("HighestAvailable", XDocument.Parse(gearwin).Descendants(ns + "RunLevel").Single().Value);
    }

    [Fact]
    public void ScopedTaskIdentityDoesNotCollideAcrossUsersOrSanitizedNames()
    {
        var a = StartupDelayService.BuildScopedTaskName("Example/A", "S-1-5-21-1");
        Assert.NotEqual(a, StartupDelayService.BuildScopedTaskName("Example_A", "S-1-5-21-1"));
        Assert.NotEqual(a, StartupDelayService.BuildScopedTaskName("Example/A", "S-1-5-21-2"));
        Assert.Equal(a, StartupDelayService.BuildScopedTaskName("EXAMPLE/A", "S-1-5-21-1"));
        Assert.StartsWith(@"GearWinDelayed\User-", a);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public void PendingOrExternallyChangedProfileIsNeverReportedAsActive(bool? committed, bool matches, bool expected)
        => Assert.Equal(expected, PowerModeService.IsConfirmedActive(committed, matches));
}
