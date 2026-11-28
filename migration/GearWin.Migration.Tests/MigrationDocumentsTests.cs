using System.Text.Json;
using GearWin.Migration;

namespace GearWin.Migration.Tests;

public sealed class MigrationDocumentsTests
{
    [Fact]
    public void SharedDocumentsRoundTripAndPreserveUnresolvedInventory()
    {
        var path = Path.GetTempFileName();
        try
        {
            var target = new PackageProfile("linux", "x64", "debian", "13", null,
                [new(PackageManager.Apt, "apt-get")], null);
            File.WriteAllText(path, MigrationDocuments.Serialize(new ProfileDocument(1, target)));
            var restored = MigrationDocuments.ReadTarget(path);
            Assert.Equal(target.DistributionId, restored.DistributionId);
            Assert.Equal(target.Backends, restored.Backends);
            var inventory = new ApplicationInventory(1, "windows",
                [new(InventorySource.WindowsRegistry, "unknown", "Unmapped app")], ["partial"]);
            var report = MigrationDocuments.CreatePlan(restored, new PackageManifest(1, []), inventory);
            Assert.Equal(PlanStatus.Unresolved, Assert.Single(report.Applications).Status);
            Assert.Equal("partial", Assert.Single(report.InventoryWarnings!));
            File.WriteAllText(path, MigrationDocuments.Serialize(report));
            Assert.Equal("Unmapped app", Assert.Single(MigrationDocuments.Read<PlanDocument>(path).Applications).Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DuplicatePropertiesAreRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"schemaVersion":1,"schemaVersion":2,"applications":[]}""");
            Assert.Throws<JsonException>(() => MigrationDocuments.Read<PackageManifest>(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ByteLimitCountsMultibyteCharacters()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "αβγ", new System.Text.UTF8Encoding(false));
            Assert.Equal("αβγ", BoundedText.ReadFile(path, 6));
            Assert.Throws<IOException>(() => BoundedText.ReadFile(path, 5));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OversizedExportCannotCreateUnreadableMigrationDocument()
    {
        Assert.Throws<IOException>(() => MigrationDocuments.SerializeFile(new string('x', 16 * 1024 * 1024)));
    }

    [Fact]
    public void WrongTargetSchemaIsRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            var profile = new PackageProfile("windows", "x64", null, null, null, [], null);
            File.WriteAllText(path, MigrationDocuments.Serialize(new ProfileDocument(2, profile)));
            Assert.Throws<ArgumentException>(() => MigrationDocuments.ReadTarget(path));
        }
        finally { File.Delete(path); }
    }
}
