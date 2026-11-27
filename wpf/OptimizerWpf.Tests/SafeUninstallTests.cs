using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class SafeUninstallTests
{
    private const string Product = "{12345678-1234-1234-1234-123456789ABC}";

    [Theory]
    [InlineData("MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC}")]
    [InlineData("msiexec /x {12345678-1234-1234-1234-123456789abc}")]
    [InlineData("  MSIEXEC.EXE /X {12345678-1234-1234-1234-123456789ABC}  ")]
    public void OnlySimpleMatchingMsiIdentityIsAccepted(string command)
        => Assert.Equal(Product, SafeUninstallService.MatchMachineMsi(Product, command, true, true));

    [Theory]
    [InlineData("cmd.exe /c anything")]
    [InlineData("msiexec /x {12345678-1234-1234-1234-123456789ABD}")]
    [InlineData("msiexec /x {12345678-1234-1234-1234-123456789ABC} & other.exe")]
    [InlineData("msiexec /x {12345678-1234-1234-1234-123456789ABC} /forcerestart")]
    [InlineData("msiexec /x C:\\Users\\Public\\package.msi")]
    [InlineData("C:\\Users\\Public\\msiexec.exe /x {12345678-1234-1234-1234-123456789ABC}")]
    [InlineData("powershell.exe -Command test")]
    [InlineData("")]
    public void ArbitraryRegistryCommandNeverBecomesElevatedProcess(string command)
        => Assert.Null(SafeUninstallService.MatchMachineMsi(Product, command, true, true));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void UserOrNonMsiRegistrationCannotUseDirectMsiPath(bool machine, bool msi)
        => Assert.Null(SafeUninstallService.MatchMachineMsi(Product, "msiexec /x " + Product, machine, msi));

    [Theory]
    [InlineData("Example App", "example app", true)]
    [InlineData("Example App", "Example App Documents", false)]
    [InlineData("Example App", "Old Example App", false)]
    [InlineData("App", "App", false)]
    [InlineData("", "Microsoft", false)]
    public void ResidualReviewRejectsPartialNames(string app, string folder, bool expected)
        => Assert.Equal(expected, BloatwareService.IsResidualNameMatch(app, folder));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_11.2405.1.0_x64__8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.WindowsCalculator_11.2405.1.0_neutral__8wekyb3d8bbwe", true)]
    [InlineData("Example_1.2.3.4_arm64__abcdefghijklm", true)]
    [InlineData("Microsoft.*", false)]
    [InlineData("Example_1.2.3.4_x64__abcdefghijklm' ; exit", false)]
    [InlineData("Example_1.2.3_x64__abcdefghijklm", false)]
    [InlineData("Example_1.2.3.4_x64__short", false)]
    [InlineData("Example_1.2.3.4_x64__abcdefghijklm\n", false)]
    public void StorePackageRemovalRequiresAnExactIdentity(string package, bool expected)
        => Assert.Equal(expected, BloatwareService.IsValidUwpPackageIdentity(package));
}
