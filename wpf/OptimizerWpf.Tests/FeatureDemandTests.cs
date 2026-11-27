using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class FeatureDemandTests
{
    private static AppSettings Disabled() => new()
    {
        UpdateNotificationsEnabled = false, LowDiskNotificationsEnabled = false,
        SelfUpdateCheckEnabled = false, TempAlertsEnabled = false, SmartAlertsEnabled = false,
        AutoGamingModeEnabled = false, DesktopWidgetEnabled = false, ClipboardHistoryEnabled = false
    };

    [Fact]
    public void DisabledSettingsRequestNoOptionalBackgroundServices()
    {
        var demand = FeatureDemand.From(Disabled());
        Assert.Equal(default(FeatureDemand), demand);
        Assert.False(demand.UpdateTimer);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void LowDiskAndSoftwareUpdatesHaveIndependentDemand(bool updates, bool disk, bool timer)
    {
        var settings = Disabled();
        settings.UpdateNotificationsEnabled = updates;
        settings.LowDiskNotificationsEnabled = disk;
        var demand = FeatureDemand.From(settings);
        Assert.Equal(updates, demand.Updates);
        Assert.Equal(disk, demand.LowDisk);
        Assert.Equal(timer, demand.UpdateTimer);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void HardwareAlertTimerNeedsAtLeastOneSelectedAlert(bool temp, bool smart, bool expected)
    {
        var settings = Disabled();
        settings.TempAlertsEnabled = temp;
        settings.SmartAlertsEnabled = smart;
        Assert.Equal(expected, FeatureDemand.From(settings).Alerts);
    }

    [Fact]
    public void ClipboardCaptureRequiresExplicitOptInEvenForLegacySettings()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"el\"}")!;
        Assert.False(FeatureDemand.From(legacy).Clipboard);
        legacy.ClipboardHistoryEnabled = true;
        Assert.True(FeatureDemand.From(legacy).Clipboard);
    }

    [Fact]
    public void EnablingWidgetDoesNotActivateGamingOrClipboard()
    {
        var settings = Disabled();
        settings.DesktopWidgetEnabled = true;
        var demand = FeatureDemand.From(settings);
        Assert.True(demand.Widget);
        Assert.False(demand.AutoGaming);
        Assert.False(demand.Clipboard);
        Assert.False(demand.UpdateTimer);
    }
}
