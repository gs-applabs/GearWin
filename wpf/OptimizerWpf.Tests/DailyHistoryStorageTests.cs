using System;
using System.Globalization;
using System.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class DailyHistoryStorageTests
{
    [Fact]
    public void RetentionSortsDeduplicatesAndCapsAtSixtyDays()
    {
        var dates = Enumerable.Range(0, 100).Select(i => new DateOnly(2026, 1, 1).AddDays(i)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Reverse().ToArray();
        var rows = DailyHistoryStorage.Retain(dates.Concat(dates), x => x);
        Assert.Equal(60, rows.Count);
        Assert.Equal(dates[59], rows[0]);
        Assert.Equal(dates[0], rows[^1]);
    }

    [Theory]
    [InlineData("2026-02-28", true)]
    [InlineData("2026-02-29", false)]
    [InlineData("2026-11-28", true)]
    [InlineData("28/11/2026", false)]
    [InlineData(null, false)]
    public void DateFormatIsInvariant(string? date, bool expected) =>
        Assert.Equal(expected, DailyHistoryStorage.ValidDate(date));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(100, 4, true)]
    [InlineData(-1, 0, false)]
    [InlineData(101, 0, false)]
    [InlineData(50, -1, false)]
    public void HealthScoreRejectsInvalidRows(int score, int issues, bool expected) =>
        Assert.Equal(expected, HealthScoreDailyHistoryService.Valid(new("2026-11-28", score, issues)));

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(20, 10, false)]
    [InlineData(-1, 10, false)]
    [InlineData(double.NaN, 10, false)]
    [InlineData(0, double.PositiveInfinity, false)]
    public void DiskHistoryRejectsInvalidCapacity(double free, double total, bool expected) =>
        Assert.Equal(expected, DiskHealthHistoryService.Valid(new("2026-11-28", "C:", "OK", free, total)));
}
