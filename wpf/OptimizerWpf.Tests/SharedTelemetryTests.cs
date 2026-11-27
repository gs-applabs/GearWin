using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class SharedTelemetryTests
{
    [Theory]
    [InlineData(1499L, 0L, 1500L, true)]
    [InlineData(1500L, 0L, 1500L, false)]
    [InlineData(10L, 20L, 1500L, false)]
    [InlineData(9999L, 0L, 10000L, true)]
    [InlineData(10000L, 0L, 10000L, false)]
    public void SharedSnapshotFreshnessIsBounded(long now, long previous, long ttl, bool expected)
        => Assert.Equal(expected, SharedTelemetryService.IsFresh(now, previous, ttl));

    [Theory]
    [InlineData(50L, 100L, 50.0)]
    [InlineData(100L, 100L, 0.0)]
    [InlineData(0L, 100L, 100.0)]
    [InlineData(101L, 100L, -1.0)]
    [InlineData(0L, 0L, -1.0)]
    public void CpuDeltaRejectsInvalidCounters(long idle, long total, double expected)
        => Assert.Equal(expected, PerformanceProfileService.CpuPercent((ulong)idle, (ulong)total));

    [Theory]
    [InlineData(2500L, 1000L, 1.5)]
    [InlineData(3100L, 1000L, 2.1)]
    [InlineData(1000L, 0L, 0.0)]
    [InlineData(1000L, 1000L, 0.0)]
    [InlineData(1000L, 2000L, 0.0)]
    public void NetworkRatesUseActualElapsedWindow(long now, long previous, double expected)
        => Assert.Equal(expected, NetworkTrafficService.ElapsedWindowSeconds(now, previous));
}
