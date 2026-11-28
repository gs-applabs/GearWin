using GearWin.Migration;

namespace GearWin.Migration.Tests;

public sealed class PackageVerificationTests
{
    [Theory]
    [InlineData("winget", "Vendor.App", VerificationStatus.InstalledOnDestination)]
    [InlineData("msstore", "Vendor.App", VerificationStatus.NotConfirmed)]
    [InlineData("winget", "Vendor.App.Other", VerificationStatus.NotConfirmed)]
    [InlineData("", "Vendor.App", VerificationStatus.NotConfirmed)]
    public void WindowsInstalledEvidenceRequiresExactIdAndExplicitSource(string source, string id, VerificationStatus expected)
    {
        var plan = new[] { new ApplicationPlan("Example", PlanStatus.CommandPreview,
            new(PackageManager.Winget, "vendor.app"), null, "") };
        var verified = PackageVerifier.VerifyWindowsInstalled(plan, [new(id, source, "2.0")]);
        Assert.Equal(expected, Assert.Single(verified).Verification?.Status);
        Assert.Equal(PlanStatus.CommandPreview, Assert.Single(verified).Status);
    }

    [Fact]
    public void UnavailableOrAmbiguousWindowsInventoryNeverConfirmsInstallation()
    {
        var plan = new[] { new ApplicationPlan("Example", PlanStatus.CommandPreview,
            new(PackageManager.Winget, "Vendor.App", "msstore"), null, "") };
        Assert.Equal(VerificationStatus.NotConfirmed, Assert.Single(PackageVerifier.VerifyWindowsInstalled(plan, null)).Verification?.Status);
        Assert.Equal(VerificationStatus.NotConfirmed, Assert.Single(PackageVerifier.VerifyWindowsInstalled(plan,
            [new("Vendor.App", "msstore", "1.0"), new("Vendor.App", "msstore", "2.0")])).Verification?.Status);
    }

    [Fact]
    public void AptVerificationRequiresAnExactNameAndRequestedArchitecture()
    {
        const string records = "Package: app-assets\nVersion: 1\nArchitecture: all\n\n" +
            "Package: app\nVersion: 2\nArchitecture: i386\n\n" +
            "Package: app\nVersion: 3\nArchitecture: amd64\nDescription: example\n Package: unrelated\n";
        var result = PackageVerifier.ParseLocalMetadata(new(PackageManager.Apt, "app:amd64"), records);
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("3", result.Version);
        Assert.Equal("amd64", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(new(PackageManager.Apt, "app:arm64"), records).Status);
    }

    [Fact]
    public void PacmanVerificationDistinguishesARecordFromAPrefixMatch()
    {
        const string output = "Repository      : extra\nName            : vlc\nVersion         : 3.0.22-2\nArchitecture    : x86_64\n";
        var result = PackageVerifier.ParseLocalMetadata(new(PackageManager.Pacman, "vlc"), output);
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("3.0.22-2", result.Version);
        Assert.Equal("x86_64", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(new(PackageManager.Pacman, "vl"), output).Status);
    }

    [Fact]
    public void DnfRequiresAnExactPackageNameAndNativeArchitecture()
    {
        const string output = "vlc-addon\t5.1-1\tx86_64\nvlc\t5.2-1\ti686\nvlc\t5.2-2\tx86_64\n";
        var candidate = new PackageCandidate(PackageManager.Dnf, "vlc");
        var result = PackageVerifier.ParseLocalMetadata(candidate, output, "x86_64");
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("5.2-2", result.Version);
        Assert.Equal("x86_64", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed, PackageVerifier.ParseLocalMetadata(candidate, output, "aarch64").Status);
    }

    [Fact]
    public void ZypperIgnoresPatternsAndAcceptsArchitectureIndependentPackages()
    {
        const string output = """
            <stream><search-result version="0.0"><solvable-list>
            <solvable name="vlc" kind="pattern" edition="9.0" arch="noarch"/>
            <solvable name="vlc" kind="package" edition="3.0.22-2" arch="noarch"/>
            </solvable-list></search-result></stream>
            """;
        var candidate = new PackageCandidate(PackageManager.Zypper, "vlc");
        var result = PackageVerifier.ParseLocalMetadata(candidate, output, "x86_64");
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("3.0.22-2", result.Version);
        Assert.Equal("noarch", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(candidate, "<!DOCTYPE stream>" + output).Status);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(candidate, output + "<trailing>").Status);
    }

    [Fact]
    public void ApkConfirmsThePackageNameAndNativeArchitectureEvenWhenTheOriginDiffers()
    {
        const string output = "libstdc++-12-3.0-r1 x86_64 {gcc12} (GPL)\n" +
            "libstdc++-15.2.0-r5 aarch64 {gcc} (GPL)\nlibstdc++-15.2.0-r5 x86_64 {gcc} (GPL) [installed]\n";
        var candidate = new PackageCandidate(PackageManager.Apk, "libstdc++");
        var result = PackageVerifier.ParseLocalMetadata(candidate, output, "x86_64");
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("15.2.0-r5", result.Version);
        Assert.Equal("x86_64", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed, PackageVerifier.ParseLocalMetadata(candidate, output, "s390x").Status);
    }

    [Fact]
    public void XbpsChecksTheResolvedPackageInsteadOfConfirmingVirtualProviders()
    {
        const string output = "pipewire-pulse-1.4.9_1\nx86_64-musl\n";
        var candidate = new PackageCandidate(PackageManager.Xbps, "pipewire-pulse");
        var result = PackageVerifier.ParseLocalMetadata(candidate, output, "x86_64");
        Assert.Equal(VerificationStatus.FoundInLocalMetadata, result.Status);
        Assert.Equal("1.4.9_1", result.Version);
        Assert.Equal("x86_64-musl", result.Architecture);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(candidate with { PackageId = "pulseaudio-daemon" }, output, "x86_64").Status);
        Assert.Equal(VerificationStatus.NotConfirmed, PackageVerifier.ParseLocalMetadata(candidate, output, "aarch64").Status);
    }

    [Fact]
    public void FlatpakOnlyReportsInstalledAppsFromTheSelectedOrigin()
    {
        const string output = "VLC addon\tx86_64\t3.0\tflathub\norg.videolan.VLC\tx86_64\t3.0.22\tcustom\n" +
            "org.videolan.VLC\taarch64\t3.0\tflathub\norg.videolan.VLC\tx86_64\t\tflathub\n";
        var app = new PackageCandidate(PackageManager.Flatpak, "org.videolan.VLC");
        var result = PackageVerifier.ParseLocalMetadata(app, output, "x86_64");
        Assert.Equal(VerificationStatus.InstalledOnDestination, result.Status);
        Assert.Equal("x86_64", result.Architecture);
        Assert.Null(result.Version);
        var custom = PackageVerifier.ParseLocalMetadata(app with { Source = "custom" }, output, "x86_64");
        Assert.Equal("3.0.22", custom.Version);
        Assert.Equal(VerificationStatus.NotConfirmed,
            PackageVerifier.ParseLocalMetadata(app with { Source = "unlisted" }, output).Status);
    }

    [Theory]
    [InlineData("example-")]
    [InlineData("g++")]
    [InlineData("g++:amd64")]
    public void AptNamesWithActionSuffixesRequireManualReview(string packageId)
    {
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1, [new("Example", [new(PackageManager.Apt, packageId)])]);
        var plan = Assert.Single(PackagePlanner.CreatePlan(target, manifest));
        Assert.Equal(PlanStatus.Manual, plan.Status);
        Assert.Null(plan.Command);
    }

    [Fact]
    public async Task UnsupportedVerifierLeavesCommandAsAPreview()
    {
        var candidate = new PackageCandidate(PackageManager.Winget, "Example.App");
        var command = new CommandPreview("winget.exe", ["install", "--id", "Example.App", "--exact"]);
        var plan = new ApplicationPlan("Example", PlanStatus.CommandPreview, candidate, command, "Preview");
        var verified = Assert.Single(await PackageVerifier.VerifyAsync([plan]));
        Assert.Equal(VerificationStatus.Unsupported, verified.Verification?.Status);
        Assert.Equal(command, verified.Command);
    }

    [Fact]
    public void BoundedInputsStillAcceptWindowsPowerShellEncoding()
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory();
        try
        {
            string file = Path.Combine(fixture.FullName, "profile.json");
            File.WriteAllText(file, "{\"value\":\"δοκιμή\"}", System.Text.Encoding.Unicode);
            Assert.Contains("δοκιμή", BoundedText.ReadFile(file, 1024));
            Assert.Throws<IOException>(() => BoundedText.ReadFile(file, 4));
        }
        finally { fixture.Delete(recursive: true); }
    }
}
