using System;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class AutoGameProfileJournalTests
{
    private const string Exe = @"C:\Games\Game.exe";
    private static TweakValue Missing => TweakValue.Missing;
    [Fact]
    public void RebootAndLaunchTimeSettingsNeverEnterAutomaticPlan()
    {
        var data = new GameProfileData { GpuPreference = GpuPreference.HighPerformance, DisableFullscreenOptimizations = true };
        data.GlobalTweaks["Hags"] = true;
        Assert.Null(AutoGameProfileJournal.BuildPlan(Exe, data, Missing, Missing));
    }

    [Fact]
    public void OnlySelectedGlobalValuesAreChanged()
    {
        var data = new GameProfileData();
        data.GlobalTweaks["GameMode"] = false;
        data.GlobalTweaks["VrrOptimize"] = true;
        var plan = AutoGameProfileJournal.BuildPlan(Exe, data, Missing, new("string", "Other=abc;"))!;
        Assert.True(plan.IsValid);
        Assert.Equal(Missing, plan.ModeBefore);
        Assert.Equal(new TweakValue("dword", "0"), plan.ModeAfter);
        Assert.Equal("Other=abc;VRROptimizeEnable=1;", plan.DxAfter!.Data);
        Assert.Null(plan.Windowed);
    }

    [Fact]
    public void AlreadyMatchingValuesCreateNoJournal()
    {
        var data = new GameProfileData();
        data.GlobalTweaks["GameMode"] = true;
        data.GlobalTweaks["WindowedOpt"] = true;
        Assert.Null(AutoGameProfileJournal.BuildPlan(Exe, data, new("dword", "1"), new("string", "SwapEffectUpgradeEnable=1;")));
    }

    [Fact]
    public void JournalRejectsUnrelatedDirectXModification()
    {
        var data = new GameProfileData();
        data.GlobalTweaks["VrrOptimize"] = true;
        var plan = AutoGameProfileJournal.BuildPlan(Exe, data, Missing, new("string", "Other=abc;"))!;
        Assert.False((plan with { DxAfter = new("string", "Other=other;VRROptimizeEnable=1;") }).IsValid);
    }

    [Fact]
    public void RestoreRefusesExternalChanges()
    {
        var original = Missing;
        var applied = new TweakValue("dword", "1");
        Assert.True(AutoGameProfileJournal.CanRestoreValue(original, original, applied));
        Assert.True(AutoGameProfileJournal.CanRestoreValue(applied, original, applied));
        Assert.False(AutoGameProfileJournal.CanRestoreValue(new("dword", "0"), original, applied));
    }

    [Theory]
    [InlineData(null, "a", 0, 1)]
    [InlineData("a", "a", 1, 2)]
    [InlineData("a", "a", 2, 2)]
    [InlineData("a", "b", 2, 1)]
    [InlineData("a", null, 2, 0)]
    public void ForegroundMustBeStableForTwoTicks(string? previous, string? current, int count, int expected) =>
        Assert.Equal(expected, AutoGamingModeService.NextStableTicks(previous, current, count));
}
