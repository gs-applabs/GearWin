using System;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class StatusLifecycleTests
{
    [Fact]
    public void UnmatchedLegacyIdleCannotEndTrackedOperation()
    {
        var before = StatusService.ActiveCount;
        using var scope = StatusService.BeginScope("tracked backup");
        StatusService.SetIdle("unmatched notification");
        Assert.Equal(before + 1, StatusService.ActiveCount);
        Assert.Contains("tracked backup", StatusService.Snapshot().Message);
        Assert.True(StatusService.Snapshot().Busy);
    }

    [Fact]
    public async Task OutOfOrderLegacyCompletionRetainsOtherAsyncFlow()
    {
        var before = StatusService.ActiveCount;
        var startedA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = Task.Run(async () =>
        {
            StatusService.SetBusy("legacy-A");
            startedA.TrySetResult(true);
            try { await finishA.Task; }
            finally { StatusService.SetIdle("A ended"); }
        });
        var b = Task.Run(async () =>
        {
            await startedA.Task;
            StatusService.SetBusy("legacy-B");
            startedB.TrySetResult(true);
            try { await finishB.Task; }
            finally { StatusService.SetIdle("B ended"); }
        });
        try
        {
            await startedB.Task.WaitAsync(TimeSpan.FromSeconds(5));
            finishB.TrySetResult(true);
            await b.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before + 1, StatusService.ActiveCount);
            Assert.Contains("legacy-A", StatusService.Snapshot().Message);
            Assert.DoesNotContain("legacy-B", StatusService.Snapshot().Message);
        }
        finally
        {
            finishA.TrySetResult(true);
            finishB.TrySetResult(true);
            await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(before, StatusService.ActiveCount);
    }

    [Fact]
    public void DisposingScopesOutOfOrderPreservesCorrectIdentity()
    {
        var before = StatusService.ActiveCount;
        using var first = StatusService.BeginScope("scope-A");
        using var second = StatusService.BeginScope("scope-B");
        second.End("done");
        second.Dispose();
        Assert.Equal(before + 1, StatusService.ActiveCount);
        Assert.Contains("scope-A", StatusService.Snapshot().Message);
        Assert.DoesNotContain("scope-B", StatusService.Snapshot().Message);
    }

    [Fact]
    public void FaultySubscriberCannotLeakStatusEntryOrBlockOtherSubscriber()
    {
        var before = StatusService.ActiveCount;
        bool received = false;
        void Fault(string _, bool __) => throw new InvalidOperationException("test");
        void Observe(string _, bool __) => received = true;
        StatusService.Changed += Fault;
        StatusService.Changed += Observe;
        try
        {
            using (StatusService.BeginScope("operation")) Assert.True(received);
            Assert.Equal(before, StatusService.ActiveCount);
        }
        finally { StatusService.Changed -= Fault; StatusService.Changed -= Observe; }
    }

    [Fact]
    public async Task GracefulExitRefusesHeavyWorkThenClosesAdmission()
    {
        var core = new OperationCoordinator(() => 2);
        using var active = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        var pending = core.AcquireAsync(CoreWork.Cleanup, CorePriority.User);
        Assert.False(core.TryStopWhenSafe());
        active.Dispose();
        // Admission is synchronous under the coordinator gate; do not time the thread pool.
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(1, core.ActiveCount);
        Assert.Equal(0, core.PendingCount);
        using var cleanup = await pending;
        Assert.False(core.TryStopWhenSafe());
        cleanup.Dispose();
        Assert.True(core.TryStopWhenSafe());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.AcquireAsync(CoreWork.Repair, CorePriority.User));
    }

    [Fact]
    public async Task RepeatedNoncancellableHandoffsDoNotAccumulatePendingWork()
    {
        var core = new OperationCoordinator(() => 2);
        for (int i = 0; i < 250; i++)
        {
            using var first = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
            var pending = core.AcquireAsync(CoreWork.Cleanup, CorePriority.User);
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, core.PendingCount);
            first.Dispose();
            Assert.True(pending.IsCompletedSuccessfully);
            using var next = await pending;
            Assert.Equal(1, core.ActiveCount);
            Assert.Equal(0, core.PendingCount);
        }
        Assert.Equal(0, core.ActiveCount);
        Assert.True(core.TryStopWhenSafe());
    }

    [Fact]
    public async Task CompatibleUserReadsStillRunTogether()
    {
        var core = new OperationCoordinator(() => 1);
        using var first = await core.AcquireAsync(CoreWork.CleanupScan, CorePriority.User);
        var secondTask = core.AcquireAsync(CoreWork.UpdateScan, CorePriority.User);
        Assert.True(secondTask.IsCompletedSuccessfully);
        using var second = await secondTask;
        Assert.Equal(2, core.ActiveCount);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public async Task CancelledQueuedRequestReleasesItsQueueSlot()
    {
        var core = new OperationCoordinator(() => 2);
        using var first = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        using var cancel = new System.Threading.CancellationTokenSource();
        var pending = core.AcquireAsync(CoreWork.Cleanup, CorePriority.User, cancel.Token);
        Assert.Equal(1, core.PendingCount);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, core.PendingCount);
        first.Dispose();
        Assert.Equal(0, core.ActiveCount);
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.D3, "Health#1")]
    [InlineData(System.Windows.Input.Key.NumPad3, "Health#1")]
    [InlineData(System.Windows.Input.Key.D8, "System#1")]
    [InlineData(System.Windows.Input.Key.NumPad8, "System#1")]
    [InlineData(System.Windows.Input.Key.D1, "Home")]
    [InlineData(System.Windows.Input.Key.D9, "System")]
    [InlineData(System.Windows.Input.Key.F12, null)]
    public void NumericShortcutResolvesExistingDestination(System.Windows.Input.Key key, string? expected)
        => Assert.Equal(expected, OptimizerWpf.MainWindow.ShortcutDestination(key));
}
