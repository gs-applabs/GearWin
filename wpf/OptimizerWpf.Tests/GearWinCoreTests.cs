using System;
using System.Threading;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class GearWinCoreTests
{
    [Fact]
    public async Task HeavyMaintenanceDoesNotOverlapBackupOrRepair()
    {
        var core = new OperationCoordinator(() => 3);
        using var cleanup = await core.AcquireAsync(CoreWork.Cleanup, CorePriority.User);
        var backup = core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        var repair = core.AcquireAsync(CoreWork.Repair, CorePriority.User);
        Assert.False(backup.IsCompleted);
        Assert.False(repair.IsCompleted);
        cleanup.Dispose();
        using var b = await backup.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(repair.IsCompleted);
        b.Dispose();
        using var r = await repair.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, core.ActiveCount);
    }

    [Fact]
    public async Task UserRequestPrecedesQueuedBackgroundWork()
    {
        var core = new OperationCoordinator(() => 1);
        using var first = await core.AcquireAsync(CoreWork.Repair, CorePriority.User);
        var background = core.AcquireAsync(CoreWork.UpdateScan, CorePriority.Background);
        var user = core.AcquireAsync(CoreWork.Memory, CorePriority.User);
        first.Dispose();
        using var lease = await user.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(background.IsCompleted);
        lease.Dispose();
        using var scan = await background.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GovernorCapacityChangeAllowsCompatibleWorkWithoutTimer()
    {
        int capacity = 1;
        var core = new OperationCoordinator(() => capacity);
        using var first = await core.AcquireAsync(CoreWork.CleanupScan, CorePriority.User);
        var queued = core.AcquireAsync(CoreWork.UpdateScan, CorePriority.User);
        Assert.False(queued.IsCompleted);
        capacity = 2;
        core.PolicyChanged();
        using var second = await queued.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, core.ActiveCount);
        capacity = 1;
        core.PolicyChanged();
        Assert.Equal(2, core.ActiveCount); // never abort in-flight user work
        var third = core.AcquireAsync(CoreWork.HardwareScan, CorePriority.User);
        first.Dispose();
        Assert.False(third.IsCompleted);
        second.Dispose();
        using var next = await third.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CanceledQueuedWorkDoesNotConsumeCapacity()
    {
        var core = new OperationCoordinator(() => 1);
        using var active = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        using var cancel = new CancellationTokenSource();
        var pending = core.AcquireAsync(CoreWork.Cleanup, CorePriority.User, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await pending; });
        Assert.Equal(0, core.PendingCount);
        active.Dispose();
        active.Dispose();
        Assert.Equal(0, core.ActiveCount);
    }

    [Fact]
    public async Task BackgroundCannotJumpConflictingUserWork()
    {
        var core = new OperationCoordinator(() => 3);
        using var scan = await core.AcquireAsync(CoreWork.CleanupScan, CorePriority.User);
        var user = core.AcquireAsync(CoreWork.Repair, CorePriority.User);
        var background = core.AcquireAsync(CoreWork.UpdateScan, CorePriority.Background);
        Assert.False(user.IsCompleted);
        Assert.False(background.IsCompleted);
        scan.Dispose();
        using var repair = await user.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(background.IsCompleted);
        repair.Dispose();
        using var update = await background.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ShutdownCancelsPendingButDoesNotRevokeRunningLease()
    {
        var core = new OperationCoordinator(() => 1);
        using var active = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        var pending = core.AcquireAsync(CoreWork.Memory, CorePriority.User);
        core.StopAccepting();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await pending; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await core.AcquireAsync(CoreWork.Repair, CorePriority.User);
        });
        Assert.Equal(1, core.ActiveCount);
        active.Dispose();
        Assert.Equal(0, core.ActiveCount);
    }

    [Fact]
    public async Task QueueIsBounded()
    {
        var core = new OperationCoordinator(() => 1);
        using var active = await core.AcquireAsync(CoreWork.Repair, CorePriority.User);
        var waiting = new Task<IDisposable>[32];
        for (int i = 0; i < waiting.Length; i++)
            waiting[i] = core.AcquireAsync(CoreWork.Cleanup, CorePriority.User);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        });
        core.StopAccepting();
        foreach (var task in waiting)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await task; });
    }
}
