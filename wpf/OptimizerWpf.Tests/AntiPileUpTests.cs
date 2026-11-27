using System;
using System.Threading;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class AntiPileUpTests
{
    [Fact]
    public void ProcessHistoryRemainsBoundedUnderHighIdentityChurn()
    {
        string prefix = Guid.NewGuid().ToString("N");
        string oldest = prefix + "-oldest";
        ProcessHistoryService.RecordSample(oldest, 1);
        ProcessHistoryService.RecordSample(oldest, 2);
        Assert.NotEmpty(ProcessHistoryService.GetSparkline(oldest));
        for (int i = 0; i < ProcessHistoryService.MaxProcesses * 4; i++)
            ProcessHistoryService.RecordSample(prefix + i, 2);
        Assert.Equal(ProcessHistoryService.MaxProcesses, ProcessHistoryService.TrackedCount);
        Assert.Empty(ProcessHistoryService.GetSparkline(oldest));
        string recent = prefix + "-recent";
        for (int i = 0; i < 100; i++) ProcessHistoryService.RecordSample(recent, i);
        Assert.Equal(20, ProcessHistoryService.GetSparkline(recent).Length);
    }

    [Fact]
    public async Task UndoCannotAccumulateDuplicateWorkAndReleasesCompletedClosure()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var entry = ChangeJournalService.Record("test", (Func<Task>)(async () =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
        }));
        try
        {
            var first = ChangeJournalService.UndoAsync(entry);
            for (int i = 0; i < 100; i++) Assert.False(await ChangeJournalService.UndoAsync(entry));
            Assert.Equal(1, calls);
            gate.TrySetResult(true);
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Null(entry.UndoAction);
            Assert.False(entry.CanUndo);
        }
        finally { gate.TrySetResult(true); }
    }

    [Fact]
    public async Task OptionalBackgroundTicksDoNotAccumulateDuringHeavyWork()
    {
        var core = new OperationCoordinator(() => 1);
        using var active = await core.AcquireAsync(CoreWork.Backup, CorePriority.User);
        for (int i = 0; i < 10000; i++)
            Assert.Null(core.TryAcquireBackground(CoreWork.ProfileChange));
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(1, core.ActiveCount);
    }
}
