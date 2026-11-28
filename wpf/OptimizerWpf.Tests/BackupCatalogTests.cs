using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class BackupCatalogTests
{
    private const string Old = "Version identifier: 01/01/2026-00:00";
    private const string New = "Version identifier: 02/01/2026-00:00";

    [Fact]
    public void NewIdentifierIsRequiredRatherThanDifferentText()
    {
        Assert.True(BackupService.HasNewCatalogVersion(Old, Old + "\n" + New));
        Assert.True(BackupService.HasNewCatalogVersion(Old, New));
        Assert.False(BackupService.HasNewCatalogVersion(Old, "Changed header\n" + Old));
        Assert.False(BackupService.HasNewCatalogVersion(Old + "\n" + New, Old));
        Assert.False(BackupService.HasNewCatalogVersion(Old, Old + "\n[Output truncated]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Access denied")]
    [InlineData("Version identifier: 13/01/2026-00:00")]
    public void UnknownBaselineNeverProvesNewVersion(string? baseline) =>
        Assert.False(BackupService.HasNewCatalogVersion(baseline, New));

    [Fact]
    public void LocalizedLabelWithCanonicalIdentifierCanBeRead() =>
        Assert.True(BackupService.HasNewCatalogVersion("Αναγνωριστικό έκδοσης: 01/01/2026-00:00",
            "Αναγνωριστικό έκδοσης: 02/01/2026-00:00"));
}
