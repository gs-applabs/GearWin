using System;
using System.IO;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class GameProfileIdentityTests
{
    [Fact]
    public void IdentityIgnoresWindowsCaseButNotPreviouslyCollidingPaths()
    {
        Assert.Equal(GameProfileService.Identity(@"C:\Games\A.exe"), GameProfileService.Identity(@"c:\games\a.EXE"));
        Assert.NotEqual(GameProfileService.Identity(@"C:\Games\A_B.exe"), GameProfileService.Identity(@"C:\Games\A\B.exe"));
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData(@"\\server\games\a.exe")]
    [InlineData(@"C:\Games\..\a.exe")]
    [InlineData(@"C:\Games\a.bat")]
    [InlineData(@"C:\Games\a.exe:other")]
    public void UnboundExecutableIdentityRejected(string input) =>
        Assert.Throws<ArgumentException>(() => GameProfileService.Identity(input));

    [Theory]
    [InlineData("{\"GpuPreference\":8}")]
    [InlineData("{\"GlobalTweaks\":{\"Other\":true}}")]
    [InlineData("{\"GlobalTweaks\":{\"GameMode\":true,\"GameMode\":false}}")]
    [InlineData("{\"GlobalTweaks\":null}")]
    [InlineData("{\"Unknown\":true}")]
    public void InvalidProfileRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => GameProfileService.Parse(json));

    [Fact]
    public void PartialProfileLeavesUnspecifiedSettingsUnknown()
    {
        var p = GameProfileService.Parse("{\"GlobalTweaks\":{\"VrrOptimize\":true}}");
        Assert.Null(p.GpuPreference);
        Assert.Null(p.DisableFullscreenOptimizations);
        Assert.Single(p.GlobalTweaks);
    }

    [Fact]
    public void GpuPreferenceRemovalPreservesUnknownOptions() =>
        Assert.Equal("Other=7;SwapEffectUpgradeEnable=1;", GamingTweaksService.ReplaceOption(
            "Other=7;GpuPreference=2;SwapEffectUpgradeEnable=1;", "GpuPreference", null));

    [Fact]
    public void DirectXUpdatePreservesUnknownText() =>
        Assert.Equal("Other=abc;VRROptimizeEnable=1;FreeForm;", GamingTweaksService.ReplaceOption(
            "Other=abc;VRROptimizeEnable=0;FreeForm;", "VRROptimizeEnable", "1"));

    [Fact]
    public void DuplicateTargetOptionRejected() =>
        Assert.Throws<ArgumentException>(() => GamingTweaksService.ReplaceOption("GpuPreference=1;gpupreference=2;", "GpuPreference", "0"));

    [Fact]
    public void EmptyPreferenceRemovalProducesMissingValue() =>
        Assert.Null(GamingTweaksService.ReplaceOption("GpuPreference=2;", "GpuPreference", null));
}
