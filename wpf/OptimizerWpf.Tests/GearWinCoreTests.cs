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

    [Theory]
    [InlineData((int)CoreWork.SoftwareChange, (int)CoreWork.Backup)]
    [InlineData((int)CoreWork.SoftwareChange, (int)CoreWork.Repair)]
    [InlineData((int)CoreWork.SoftwareChange, (int)CoreWork.UpdateScan)]
    [InlineData((int)CoreWork.ProfileChange, (int)CoreWork.SoftwareChange)]
    [InlineData((int)CoreWork.ProfileChange, (int)CoreWork.Cleanup)]
    public void NewMutationDomainsConflictSymmetrically(int first, int second)
    {
        Assert.True(OperationCoordinator.Conflicts((CoreWork)first, (CoreWork)second));
        Assert.True(OperationCoordinator.Conflicts((CoreWork)second, (CoreWork)first));
    }

    [Fact]
    public async Task OptionalBackgroundWorkNeverBuildsABacklog()
    {
        var core = new OperationCoordinator(() => 2);
        using var active = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        for (int i = 0; i < 10; i++)
            Assert.Null(core.TryAcquireBackground(CoreWork.ProfileChange));
        Assert.Equal(0, core.PendingCount);
        active.Dispose();
        using var lease = core.TryAcquireBackground(CoreWork.ProfileChange);
        Assert.NotNull(lease);
        Assert.Equal(1, core.ActiveCount);
    }

    [Fact]
    public async Task OptionalBackgroundWorkYieldsToWaitingUser()
    {
        var core = new OperationCoordinator(() => 3);
        using var active = await core.AcquireAsync(CoreWork.UpdateScan, CorePriority.Background);
        var user = core.AcquireAsync(CoreWork.SoftwareChange, CorePriority.User);
        Assert.Null(core.TryAcquireBackground(CoreWork.HardwareScan));
        active.Dispose();
        using var lease = await user.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LegacyScanCancellationIsScopedToItsAsyncFlow()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var original = WingetService.ScanToken;
        try
        {
            WingetService.ScanToken = CancellationToken.None;
            await Task.Run(async () =>
            {
                WingetService.ScanToken = canceled.Token;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WingetService.ScanAsync());
            });
            Assert.False(WingetService.ScanToken.IsCancellationRequested);
        }
        finally { WingetService.ScanToken = original; }
    }

    [Fact]
    public async Task SoftwareEpochChangesOnlyWhenSoftwareLeaseEnds()
    {
        var core = new OperationCoordinator(() => 2);
        using (await core.AcquireAsync(CoreWork.Diagnostics, CorePriority.User))
            Assert.Equal(0L, core.SoftwareEpoch);
        Assert.Equal(0L, core.SoftwareEpoch);
        using (await core.AcquireAsync(CoreWork.SoftwareChange, CorePriority.User))
            Assert.Equal(0L, core.SoftwareEpoch);
        Assert.Equal(1L, core.SoftwareEpoch);
    }

    [Theory]
    [InlineData((int)CoreWork.NetworkChange, (int)CoreWork.SoftwareChange)]
    [InlineData((int)CoreWork.StartupChange, (int)CoreWork.ProfileChange)]
    [InlineData((int)CoreWork.Benchmark, (int)CoreWork.Backup)]
    [InlineData((int)CoreWork.Diagnostics, (int)CoreWork.Cleanup)]
    public void AdditionalDomainsRespectHeavyWork(int a, int b)
        => Assert.True(OperationCoordinator.Conflicts((CoreWork)a, (CoreWork)b));

    [Fact]
    public void InventoryUsesIdColumnNotProductName()
    {
        var table = $"{"Name",-30}{"Id",-38}Version\n" + new string('-', 80) + "\n" +
            $"{"Google.Chrome",-30}{"Other.Product",-38}1.0\n" +
            $"{"Firefox",-30}{"Mozilla.Firefox",-38}2.0\n";
        var (ids, complete) = RecommendedInventoryService.ParseInventory(table, true);
        Assert.True(complete);
        Assert.Contains("Other.Product", ids);
        Assert.Contains("Mozilla.Firefox", ids);
        Assert.DoesNotContain("Google.Chrome", ids);
    }

    [Fact]
    public void TruncatedInventoryNeverConfirmsAbsence()
    {
        var table = $"{"Name",-30}{"Id",-38}Version\n" + new string('-', 80) + "\n" +
            $"{"Application",-30}{"Vendor.Truncated…",-38}1.0\n";
        Assert.False(RecommendedInventoryService.ParseInventory(table, true).Complete);
        Assert.False(RecommendedInventoryService.ParseInventory(table, false).Complete);
        Assert.False(RecommendedInventoryService.ParseInventory("localized header", true).Complete);
    }

    [Theory]
    [InlineData("Chrome Google.Chrome 1.0", 0, true)]
    [InlineData("", unchecked((int)0x8a150014u), false)]
    [InlineData("", -1, null)]
    [InlineData("", 0, null)]
    public void ProbeDistinguishesAbsenceFromFailure(string output, int exitCode, bool? expected)
        => Assert.Equal(expected, BloatwareService.InterpretProbe(output, exitCode, "Google.Chrome"));
}
