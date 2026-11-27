using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class ServiceStartJournalTests
{
    [Theory]
    [InlineData(2u, false, true)]
    [InlineData(2u, true, false)]
    [InlineData(3u, false, false)]
    [InlineData(4u, false, false)]
    [InlineData(0u, false, false)]
    public void OnlyNonDelayedAutomaticServiceCanBeNewlyChanged(uint mode, bool delayed, bool expected)
        => Assert.Equal(expected, ServiceStartJournal.CanChange(mode, delayed));

    [Theory]
    [InlineData(2u, false, true)]
    [InlineData(3u, false, true)]
    [InlineData(4u, false, false)]
    [InlineData(2u, true, false)]
    [InlineData(3u, true, false)]
    public void RestoreRequiresExpectedState(uint mode, bool delayed, bool expected)
        => Assert.Equal(expected, ServiceStartJournal.CanRestore(mode, delayed));

    [Theory]
    [InlineData("SysMain", 2u, 3u, true)]
    [InlineData("sysmain", 2u, 3u, true)]
    [InlineData("Other", 2u, 3u, false)]
    [InlineData("SysMain", 4u, 3u, false)]
    [InlineData("SysMain", 2u, 4u, false)]
    public void SnapshotCannotSupplyAnotherServiceOrStartupMode(string service, uint original, uint applied, bool expected)
        => Assert.Equal(expected, new ServiceStartSnapshot(service, original, applied).IsValid("SysMain"));
}
