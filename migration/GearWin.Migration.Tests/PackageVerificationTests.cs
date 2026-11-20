using GearWin.Migration;

namespace GearWin.Migration.Tests;

public sealed class PackageVerificationTests
{
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
