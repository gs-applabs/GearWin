using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class AppUsageReviewTests
{
    [Theory]
    [InlineData(@"C:\Apps\Tool.exe,0", @"C:\Apps\Tool.exe")]
    [InlineData("\"C:\\Apps\\My Tool.exe\",-1", @"C:\Apps\My Tool.exe")]
    [InlineData(@"C:\Apps\Tool.exe /arg", null)]
    [InlineData(@"C:\Apps\..\Tool.exe", null)]
    [InlineData(@"\\server\Tool.exe", null)]
    [InlineData(@"C:\Apps\GearWin\Tool.exe", null)]
    public void IconMustBeExactExecutable(string icon, string? expected) =>
        Assert.Equal(expected, AppUsageReview.ParseDisplayIcon(icon));

    [Fact]
    public void DecodeRot13PreservesPathSeparators()
        => Assert.Equal(@"C:\Apps\Tool.exe", AppUsageReview.Decode(@"P:\Nccf\Gbby.rkr"));

    [Fact]
    public void TimestampRequiresKnownLayoutAndPositiveRunCount()
    {
        var now = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc);
        var bytes = new byte[72];
        var date = now.AddDays(-100);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(60), date.ToFileTimeUtc());
        Assert.Null(AppUsageReview.ReadTimestamp(bytes, now));
        bytes[4] = 1;
        Assert.Equal(date, AppUsageReview.ReadTimestamp(bytes, now));
        Assert.Null(AppUsageReview.ReadTimestamp(bytes[..71], now));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(60), now.AddDays(1).ToFileTimeUtc());
        Assert.Null(AppUsageReview.ReadTimestamp(bytes, now));
    }

    [Fact]
    public void OnlyPositiveOlderUnambiguousEvidenceIsSuggested()
    {
        var now = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc);
        InstalledApp App(string name, string exe) => new(name, null, "") { UsageExecutable = exe };
        var apps = new[] { App("Old", @"C:\Old.exe"), App("Unknown", @"C:\Missing.exe"),
            App("Recent", @"C:\Recent.exe"), App("Shared 1", @"C:\Shared.exe"), App("Shared 2", @"C:\Shared.exe") };
        var observed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase) {
            [@"C:\Old.exe"] = now.AddDays(-100), [@"C:\Recent.exe"] = now.AddDays(-2),
            [@"C:\Shared.exe"] = now.AddDays(-120) };
        Assert.Equal("Old", Assert.Single(AppUsageReview.SelectOlder(apps, observed, now)).Name);
    }
}
