using System;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class ResourceDemandTests
{
    [Fact]
    public void CompatibleConsumersKeepResourceUntilLastRelease()
    {
        int idle = 0;
        var demand = new ResourceDemand(() => idle++);
        var a = demand.Acquire()!;
        var b = demand.Acquire()!;
        Assert.False(demand.IsIdle);
        a.Dispose();
        a.Dispose();
        Assert.Equal(0, idle);
        b.Dispose();
        Assert.True(demand.IsIdle);
        Assert.Equal(1, idle);
    }

    [Fact]
    public void DemandCanReturnAfterIdle()
    {
        int idle = 0;
        var demand = new ResourceDemand(() => idle++);
        demand.Acquire()!.Dispose();
        using (demand.Acquire()) Assert.False(demand.IsIdle);
        Assert.Equal(2, idle);
    }

    [Fact]
    public void StopRejectsNewConsumersWithoutInvalidatingActiveLease()
    {
        int idle = 0;
        var demand = new ResourceDemand(() => idle++);
        var active = demand.Acquire()!;
        demand.Stop();
        Assert.Null(demand.Acquire());
        Assert.False(demand.IsIdle);
        active.Dispose();
        Assert.True(demand.IsIdle);
        Assert.Equal(1, idle);
    }

    [Fact]
    public void ConcurrentDuplicateDisposalOnlyReleasesOnce()
    {
        int idle = 0;
        var demand = new ResourceDemand(() => System.Threading.Interlocked.Increment(ref idle));
        var active = demand.Acquire()!;
        Parallel.For(0, 100, _ => active.Dispose());
        Assert.Equal(1, idle);
        Assert.True(demand.IsIdle);
    }
}
