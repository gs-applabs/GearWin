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
}
