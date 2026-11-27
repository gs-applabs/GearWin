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
        using var cleanup = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(core.TryStopWhenSafe());
        cleanup.Dispose();
        Assert.True(core.TryStopWhenSafe());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.AcquireAsync(CoreWork.Repair, CorePriority.User));
    }
}
