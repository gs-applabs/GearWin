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
}
