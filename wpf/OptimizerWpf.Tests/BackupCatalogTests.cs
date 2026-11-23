using OptimizerWpf.Services;

namespace OptimizerWpf.Tests
{
    public class BackupCatalogTests
    {
        [Fact]
        public void CatalogMustChangeToAssertNewBackup()
        {
            Assert.False(BackupService.HasNewCatalogVersion("Version identifier: 01/01/2026-00:00", " Version identifier: 01/01/2026-00:00\n"));
            Assert.True(BackupService.HasNewCatalogVersion("Version identifier: 01/01/2026-00:00", "Version identifier: 01/01/2026-00:00\nVersion identifier: 02/01/2026-00:00"));
            Assert.True(BackupService.HasNewCatalogVersion(null, "Version identifier: 02/01/2026-00:00"));
            Assert.False(BackupService.HasNewCatalogVersion(null, "  "));
        }
    }
}
