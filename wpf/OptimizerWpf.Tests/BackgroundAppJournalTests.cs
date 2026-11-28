using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class BackgroundAppJournalTests
{
    private const string A = "Vendor.App_123456789abcd";
    private const string B = "Vendor.Other_abcdefghijklm";

    [Fact]
    public void PlanIsCanonicalAndDeduplicated()
    {
        var names = BackgroundAppJournal.ParsePackages(B + "\r\n" + A + "\r\n" + A);
        Assert.Equal(new[] { A, B }, names);
        Assert.True(new BackgroundPackagePlan(string.Join("\n", names)).IsValid);
        Assert.False(new BackgroundPackagePlan(B + "\n" + A).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Vendor.App_123")]
    [InlineData("Vendor.App_123456789abcd\\Other")]
    [InlineData("..\\Vendor.App_123456789abcd")]
    [InlineData("Vendor.App_123456789abcd\n[Output truncated]")]
    public void InventoryRejectsInvalidOrTruncatedPaths(string output) =>
        Assert.Throws<InvalidDataException>(() => BackgroundAppJournal.ParsePackages(output));

    [Fact]
    public void PackageLimitIsCheckedBeforeDedup()
    {
        Assert.Throws<InvalidDataException>(() => BackgroundAppJournal.ParsePackages(
            string.Join("\n", Enumerable.Repeat(A, BackgroundAppJournal.MaxPackages + 1))));
    }

    [Fact]
    public void ValuesOnlyTouchTwoKnownUserPermissionsPerPackage()
    {
        var rows = BackgroundAppJournal.Values(A + "\n" + B);
        Assert.Equal(4, rows.Length);
        Assert.All(rows, t =>
        {
            Assert.Equal(RegistryHive.CurrentUser, t.Hive);
            Assert.Equal(RegistryValueKind.DWord, t.Kind);
            Assert.Equal(1, t.OnValue);
            Assert.Contains(t.Name, new[] { "Disabled", "DisabledByUser" });
        });
        Assert.Equal(4, rows.Select(x => x.BackupKey).Distinct().Count());
    }

    [Theory]
    [InlineData(2u, false, true)]
    [InlineData(3u, false, true)]
    [InlineData(4u, false, false)]
    [InlineData(2u, true, false)]
    [InlineData(0u, false, false)]
    public void SysMainDisableOnlyFromSupportedOriginal(uint mode, bool delayed, bool expected) =>
        Assert.Equal(expected, ServiceStartJournal.CanDisable(mode, delayed));

    [Theory]
    [InlineData("SysMain", 2u, 4u, true)]
    [InlineData("SysMain", 3u, 4u, true)]
    [InlineData("SysMain", 2u, 3u, false)]
    [InlineData("Other", 2u, 4u, false)]
    [InlineData("SysMain", 4u, 4u, false)]
    public void DisabledServiceJournalCannotBeConfusedWithManualMode(string name, uint original, uint applied, bool expected) =>
        Assert.Equal(expected, new ServiceStartSnapshot(name, original, applied).IsValidDisable());
}
