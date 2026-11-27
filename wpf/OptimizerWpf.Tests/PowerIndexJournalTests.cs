using System;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class PowerIndexJournalTests
{
    private static readonly Guid Scheme = new("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData(0u, true)]
    [InlineData(20u, true)]
    [InlineData(100u, true)]
    [InlineData(101u, false)]
    public void BatteryThresholdAcceptsOnlyPercentRange(uint dc, bool expected)
        => Assert.Equal(expected, PowerIndexSpec.Threshold.Accepts(new PowerIndices(null, dc)));

    [Theory]
    [InlineData(1u, 2u, true)]
    [InlineData(0u, 2u, true)]
    [InlineData(1u, 0u, true)]
    [InlineData(0u, 0u, true)]
    [InlineData(2u, 2u, false)]
    [InlineData(1u, 1u, false)]
    public void PartialPcieChangeRestoresOnlyOriginalOrAppliedValues(uint ac, uint dc, bool expected)
    {
        var spec = PowerIndexSpec.Pcie;
        var state = new PowerIndexSnapshot(spec.Id, Scheme, 1u, 2u);
        Assert.Equal(expected, state.CanChange(spec, Scheme, new PowerIndices(ac, dc)));
    }

    [Fact]
    public void SnapshotCannotRestoreIntoAnotherPlanOrSetting()
    {
        var spec = PowerIndexSpec.Usb;
        var state = new PowerIndexSnapshot(spec.Id, Scheme, 1u, 1u);
        Assert.False(state.IsValid(spec, Guid.Empty));
        Assert.False(state.IsValid(spec, Guid.NewGuid()));
        Assert.False(state.IsValid(PowerIndexSpec.Pcie, Scheme));
        Assert.True(state.IsValid(spec, Scheme));
    }

    [Fact]
    public void AlreadyEnabledUnownedStateIsNotValidBackup()
    {
        var spec = PowerIndexSpec.Usb;
        Assert.False(new PowerIndexSnapshot(spec.Id, Scheme, 0u, 0u).IsValid(spec, Scheme));
        Assert.False(new PowerIndexSnapshot(spec.Id, Scheme, null, 1u).IsValid(spec, Scheme));
        Assert.False(new PowerIndexSnapshot(PowerIndexSpec.Threshold.Id, Scheme, 0u, 20u).IsValid(PowerIndexSpec.Threshold, Scheme));
    }
}
