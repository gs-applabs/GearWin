using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class SafePersistenceAndRepairTests
{
    [Theory]
    [InlineData("S-1-5-32-544", true)]
    [InlineData("S-1-5-18", true)]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", true)]
    [InlineData("S-1-5-32-545", false)]
    [InlineData("S-1-5-11", false)]
    [InlineData("S-1-1-0", false)]
    [InlineData("S-1-5-21-1-2-3-1234", false)]
    public void OnlyKnownPrivilegedWritersAreTrusted(string sid, bool expected)
        => Assert.Equal(expected, SecureStorage.IsTrustedWriter(new SecurityIdentifier(sid)));

    [Theory]
    [InlineData("../outside")]
    [InlineData(".xml\\outside")]
    [InlineData(".exe:stream")]
    [InlineData("")]
    public void TempExtensionCannotRedirectStagedFile(string extension)
        => Assert.Throws<ArgumentException>(() => SecureStorage.NewPrivateTempFile("test", extension));

    [Theory]
    [InlineData(10, 20)]
    [InlineData(20, 20)]
    [InlineData(100000, 20)]
    public async Task RepairOutputDrainsAllDataButRetainsBoundedText(int length, int limit)
    {
        using var reader = new StringReader(new string('x', length));
        var result = await HealthCleanupService.ReadBoundedOutputAsync(reader, limit);
        Assert.StartsWith(new string('x', Math.Min(length, limit)), result);
        Assert.Equal(-1, reader.Peek());
        if (length > limit) Assert.Equal(new string('x', limit) + "\n[Output truncated]", result);
        else Assert.Equal(length, result.Length);
    }

    [Fact]
    public void SettingsStageIsCompleteAndLeavesNoTemporaryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gearwin-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            Assert.True(AppSettingsService.WriteIfChanged(path, "{\"Language\":\"el\"}", null));
            Assert.True(AppSettingsService.WriteIfChanged(path, "{\"Language\":\"en\"}", null));
            Assert.Equal("{\"Language\":\"en\"}", AppSettingsService.ReadSettingsFile(path));
            Assert.False(AppSettingsService.WriteIfChanged(path, "{\"Language\":\"en\"}", "{\"Language\":\"en\"}"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void OversizedSettingsAreRejectedBeforeAllocatingEntireDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), "gearwin-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew)) stream.SetLength(16L * 1024 * 1024 + 1);
            Assert.Throws<InvalidDataException>(() => AppSettingsService.ReadSettingsFile(path));
        }
        finally { File.Delete(path); }
    }
}
