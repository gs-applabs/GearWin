using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class MaintenanceSafetyTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Vendor\\app.exe\" /silent", @"C:\Program Files\Vendor\app.exe")]
    [InlineData(@"C:\Tools\app.exe /silent", @"C:\Tools\app.exe")]
    [InlineData(@"C:\Program Files\Vendor\app.exe /silent", null)]
    [InlineData(@"msiexec.exe /x {GUID}", null)]
    [InlineData(@"rundll32.exe example.dll,Run", null)]
    [InlineData("\"C:\\Tools\\app.exe", null)]
    public void RegistryCommandParsingDoesNotGuess(string command, string? expected)
        => Assert.Equal(expected, HealthCleanupService.GetExePath(command));

    [Theory]
    [InlineData(@"\\server\share\missing.exe")]
    [InlineData(@"relative.exe")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\%GEARWIN_UNDEFINED_TEST_VAR%\missing.exe")]
    public void UncertainPathsAreNeverClassifiedAsMissing(string path)
        => Assert.False(HealthCleanupService.IsConfirmedMissingLocalPath(path));

    [Theory]
    [InlineData(true, 1, 1, true)]
    [InlineData(true, 0, 1, false)]
    [InlineData(false, 0, 1, false)]
    public void ProfileRestoreDoesNotOverwriteIndependentChanges(bool exists, int value, int applied, bool owned)
        => Assert.Equal(owned, PowerModeService.StillOwned(exists, value, applied));

    [Fact]
    public void GameSaveBackupVerifiesArchiveBeforeReplacing()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temp);
        try
        {
            var saves = System.IO.Path.Combine(temp, "Saves");
            System.IO.Directory.CreateDirectory(saves);
            var save = System.IO.Path.Combine(saves, "game.sav");
            var archive = System.IO.Path.Combine(temp, "backup.zip");
            System.IO.File.WriteAllText(save, "new save");
            System.IO.File.WriteAllText(archive, "last good backup");
            using (var locked = System.IO.File.Open(save, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                Assert.False(GameSaveBackupService.BackupFolders(new[] { saves }, archive));
                Assert.Equal("last good backup", System.IO.File.ReadAllText(archive));
            }
            Assert.True(GameSaveBackupService.BackupFolders(new[] { saves }, archive));
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            using var reader = new System.IO.StreamReader(zip.GetEntry("Saves/game.sav")!.Open());
            Assert.Equal("new save", reader.ReadToEnd());
        }
        finally { System.IO.Directory.Delete(temp, true); }
    }

    [Fact]
    public void GameSaveBackupRefusesDestinationWithinSource()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(temp);
        try
        {
            var archive = System.IO.Path.Combine(temp, "backup.zip");
            System.IO.File.WriteAllText(archive, "old");
            Assert.False(GameSaveBackupService.BackupFolders(new[] { temp }, archive));
            Assert.Equal("old", System.IO.File.ReadAllText(archive));
        }
        finally { System.IO.Directory.Delete(temp, true); }
    }
}
