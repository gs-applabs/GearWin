using System;
using System.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public sealed class StoreUpdateRoutingTests
{
    [Theory]
    [InlineData("9NBLGGH4NNS1", "Example", "msstore", true)]
    [InlineData("XP9KHM4BK9FZ7Q", "Example", "msstore", true)]
    [InlineData("9NBLGGH4NNS1", "9NBLGGH4NNS1", "msstore", false)]
    [InlineData("Microsoft Photos", "Microsoft Photos", "msstore", false)]
    [InlineData("9NBLGGH4NNS1", "Example", "winget", false)]
    [InlineData("9NBLGGH4NN\"1", "Example", "msstore", false)]
    public void OnlyExactStoreProductIdsEnterDirectRoute(string id, string name, string source, bool expected)
    {
        var row = new WingetUpdate(name, id, "1.0", "2.0", source);
        Assert.Equal(expected, WingetService.HasStoreProductId(row));
        if (expected)
        {
            var command = WingetService.StoreUpgradeArguments(row);
            Assert.Contains("--exact --source msstore", command);
            Assert.DoesNotContain("start ms-windows-store", command);
        }
        else Assert.Throws<ArgumentException>(() => WingetService.StoreUpgradeArguments(row));
    }

    [Fact]
    public void StoreScanDoesNotDuplicateExactTitleAlreadyReportedByWinget()
    {
        var existing = new[] { new WingetUpdate("Example", "9NBLGGH4NNS1", "1.0", "2.0", "msstore") };
        var discovered = new[] { new WingetUpdate("example", "example", "?", "?", "msstore"),
            new WingetUpdate("Other", "Other", "?", "?", "msstore"),
            new WingetUpdate("OTHER", "OTHER", "?", "?", "msstore") };
        Assert.Equal("Other", Assert.Single(WingetService.AdditionalStoreRows(existing, discovered)).Name);
    }

    [Theory]
    [InlineData("2.0", "2.0", true)]
    [InlineData("2.1", "2.0", true)]
    [InlineData("1.9", "2.0", false)]
    [InlineData("Unknown", "2.0", false)]
    [InlineData("2.0", "Update available", false)]
    public void StoreVersionVerificationRequiresComparableVersions(string installed, string expected, bool result) =>
        Assert.Equal(result, WingetService.StoreVersionAtLeast(installed, expected));

    [Fact]
    public void StoreVersionReadbackRequiresOneExactRow()
    {
        string Row(string name, string id, string version) => name.PadRight(25) + id.PadRight(25) + version;
        var header = Row("Name", "Id", "Version");
        var sample = header + "\n" + new string('-', header.Length) + "\n" + Row("Example", "9NBLGGH4NNS1", "2.0");
        Assert.Equal("2.0", WingetService.ReadExactStoreVersion(sample, "9NBLGGH4NNS1"));
        Assert.Null(WingetService.ReadExactStoreVersion(sample, "9NBLGGH4NNS2"));
        Assert.Null(WingetService.ReadExactStoreVersion(sample + "\n" + Row("Example", "9NBLGGH4NNS1", "3.0"), "9NBLGGH4NNS1"));
    }

    [Fact]
    public void StoreUiRequiresUniqueExactTitleAndExplicitTransition()
    {
        Assert.Equal(0, StoreGuiUpdateService.SelectUniqueTitleIndex(new[] { "Photos", "Photos Legacy" }, "Photos"));
        Assert.Equal(-1, StoreGuiUpdateService.SelectUniqueTitleIndex(new[] { "Photos Legacy" }, "Photos"));
        Assert.Equal(-1, StoreGuiUpdateService.SelectUniqueTitleIndex(new[] { "Photos", "PHOTOS" }, "Photos"));
        var available = StoreGuiUpdateService.ClassifyRow("Photos, Update available");
        var active = StoreGuiUpdateService.ClassifyRow("Photos, Installing");
        var finished = StoreGuiUpdateService.ClassifyRow("Photos, Updated");
        Assert.Equal(StoreGuiUpdateService.RowState.Available, available);
        Assert.Equal(StoreGuiUpdateService.RowState.Active, active);
        Assert.Equal(StoreGuiUpdateService.RowState.Finished, finished);
        Assert.True(StoreGuiUpdateService.ConfirmedTransition(available, finished, "before", "after"));
        Assert.False(StoreGuiUpdateService.ConfirmedTransition(available, StoreGuiUpdateService.RowState.Unknown, "before", ""));
        Assert.False(StoreGuiUpdateService.ConfirmedTransition(finished, finished, "before", "after"));
        Assert.False(StoreGuiUpdateService.ConfirmedTransition(active, finished, "same", "same"));
    }

    [Fact]
    public void NightlyUpdatesExcludeStore() => Assert.Contains("--source winget", WingetService.NightlyWingetArguments);
}
