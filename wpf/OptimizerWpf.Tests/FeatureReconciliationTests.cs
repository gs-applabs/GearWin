using System;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class FeatureReconciliationTests
{
    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(false, true, 1, 0)]
    [InlineData(true, false, 0, 1)]
    [InlineData(true, true, 0, 0)]
    public void ReconcileOnlyChangesActualMismatch(bool running, bool wanted, int starts, int stops)
    {
        int a = 0, b = 0;
        CoreFeatureLifecycle.Reconcile(running, wanted, () => a++, () => b++);
        Assert.Equal(starts, a);
        Assert.Equal(stops, b);
    }

    [Fact]
    public void FailedStartCanBeRetriedWithoutChangingPreference()
    {
        bool running = false;
        Assert.Throws<InvalidOperationException>(() => CoreFeatureLifecycle.Reconcile(running, true,
            () => throw new InvalidOperationException(), () => running = false));
        CoreFeatureLifecycle.Reconcile(running, true, () => running = true, () => running = false);
        Assert.True(running);
    }

    [Theory]
    [InlineData(9L, 100L, 10, true)]
    [InlineData(10L, 100L, 10, false)]
    [InlineData(0L, 0L, 10, false)]
    [InlineData(-1L, 100L, 10, false)]
    [InlineData(101L, 100L, 10, false)]
    public void LowDiskDecisionRequiresValidCapacity(long free, long total, int threshold, bool expected) =>
        Assert.Equal(expected, UpdateNotificationService.LowDiskEligible(free, total, threshold));

    [Theory]
    [InlineData(1, 1, true, false, true)]
    [InlineData(1, 2, true, false, false)]
    [InlineData(1, 1, false, false, false)]
    [InlineData(1, 1, true, true, false)]
    public void DisabledOrStaleLowDiskReadCannotPublish(int requested, int current, bool enabled, bool stopped, bool expected) =>
        Assert.Equal(expected, UpdateNotificationService.CanPublishLowDisk(requested, current, enabled, stopped));
}
