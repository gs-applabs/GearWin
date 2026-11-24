using GearWin.Migration;

namespace GearWin.Migration.Tests;

public sealed class PackageMigrationTests
{
    [Theory]
    [InlineData("linuxmint", "apt-get", PackageManager.Apt)]
    [InlineData("fedora", "dnf5", PackageManager.Dnf)]
    [InlineData("arch", "pacman", PackageManager.Pacman)]
    [InlineData("opensuse-tumbleweed", "zypper", PackageManager.Zypper)]
    [InlineData("alpine", "apk", PackageManager.Apk)]
    [InlineData("void", "xbps-install", PackageManager.Xbps)]
    [InlineData("gentoo", "emerge", PackageManager.Emerge)]
    [InlineData("solus", "eopkg", PackageManager.Eopkg)]
    [InlineData("mageia", "urpmi", PackageManager.Urpmi)]
    [InlineData("slackware", "slackpkg", PackageManager.Slackpkg)]
    [InlineData("chimera", "apk", PackageManager.Apk)]
    public void RoutesNativePackageManagers(string distro, string executable, PackageManager manager)
    {
        PackageProfile target = Inspect([$"ID={distro}", "VERSION_ID=\"1.0\""], [executable]);
        Assert.Equal("1.0", target.DistributionVersion);
        Assert.Equal(new PackageBackend(manager, executable), Assert.Single(target.Backends));
    }

    [Fact]
    public void UsesDistroFamilyWhenSeveralCommandsExist()
    {
        var target = Inspect(["ID=derivative", "ID_LIKE=\"unknown debian\""], ["apt-get", "pacman", "flatpak"]);
        Assert.Equal(new[] { PackageManager.Apt, PackageManager.Flatpak }, target.Backends.Select(x => x.Manager));
    }

    [Fact]
    public void UnknownDistributionWithAmbiguousNativeToolsCanStillUseFlatpak()
    {
        var target = Inspect(["ID=unknown"], ["apt-get", "pacman", "flatpak"]);
        Assert.Equal(PackageManager.Flatpak, Assert.Single(target.Backends).Manager);
        Assert.NotNull(target.NativeBackendNote);
    }

    [Fact]
    public void UsesOstreeInsteadOfDnfOnAnAtomicDeployment()
    {
        var target = Inspect(["ID=fedora", "VARIANT_ID=silverblue"], ["dnf", "rpm-ostree"], isOstree: true);
        var plan = PlanOne(target, new(PackageManager.Dnf, "vlc"), new(PackageManager.RpmOstree, "vlc"));
        Assert.Equal("silverblue", target.VariantId);
        Assert.Equal("rpm-ostree", plan.Command?.Executable);
        Assert.Contains("reboot", plan.Note);
    }

    [Fact]
    public void TransactionalOpenSuseDoesNotUseOrdinaryZypperPreview()
    {
        var target = Inspect(["ID=opensuse-aeon", "ID_LIKE=opensuse"], ["zypper", "flatpak"]);
        Assert.Equal(PackageManager.Flatpak, Assert.Single(target.Backends).Manager);
        Assert.NotNull(target.NativeBackendNote);
    }

    [Fact]
    public void TransactionalOpenSuseCanPreviewViaItsOwnBackend()
    {
        var target = Inspect(["ID=opensuse-aeon"], ["zypper", "transactional-update"]);
        var plan = PlanOne(target, new(PackageManager.Zypper, "vlc"), new(PackageManager.TransactionalUpdate, "vlc"));
        Assert.Equal("transactional-update", plan.Command?.Executable);
        Assert.Equal(new[] { "pkg", "install", "vlc" }, plan.Command?.Arguments);
    }

    [Fact]
    public void UnknownDistributionCanPreviewPackageKitAndCrossDistroBackends()
    {
        var target = Inspect(["ID=unknown"], ["apt-get", "dnf", "pkcon", "flatpak", "snap"]);
        Assert.Equal(new[] { PackageManager.PackageKit, PackageManager.Flatpak, PackageManager.Snap },
            target.Backends.Select(backend => backend.Manager));
        Assert.Equal("pkcon", PlanOne(target, new PackageCandidate(PackageManager.PackageKit, "vlc")).Command?.Executable);
        Assert.Equal("snap", PlanOne(target, new PackageCandidate(PackageManager.Snap, "vlc")).Command?.Executable);
    }

    [Fact]
    public void GuixRemainsAnExplicitManualPlan()
    {
        var target = Inspect(["ID=guix"], ["guix", "pkcon"]);
        var plan = PlanOne(target, new PackageCandidate(PackageManager.Guix, "vlc"));
        Assert.Equal(PlanStatus.Manual, plan.Status);
        Assert.Null(plan.Command);
    }

    [Fact]
    public void SameManifestPlansTheExactPackageForEachDestination()
    {
        var windows = new PackageProfile("windows", "x64", null, "10.0", null,
            [new(PackageManager.Winget, "winget.exe")], null);
        var linux = Inspect(["ID=debian"], ["apt-get", "flatpak"]);
        PackageCandidate[] candidates =
        [
            new(PackageManager.Winget, "VideoLAN.VLC"),
            new(PackageManager.Flatpak, "org.videolan.VLC"),
            new(PackageManager.Apt, "vlc")
        ];

        var windowsCommand = Assert.IsType<CommandPreview>(PlanOne(windows, candidates).Command);
        Assert.Equal("winget.exe", windowsCommand.Executable);
        Assert.Equal(new[] { "install", "--id", "VideoLAN.VLC", "--exact", "--source", "winget" }, windowsCommand.Arguments);
        var linuxCommand = Assert.IsType<CommandPreview>(PlanOne(linux, candidates).Command);
        Assert.Equal("flatpak", linuxCommand.Executable);
        Assert.Equal(new[] { "install", "flathub", "org.videolan.VLC" }, linuxCommand.Arguments);
    }

    [Fact]
    public void NixOsPlansDeclarativeReviewAndUnknownPackageStaysUnresolved()
    {
        var target = Inspect(["ID=nixos"], ["apt-get"]);
        var manual = PlanOne(target, new PackageCandidate(PackageManager.NixOs, "vlc"));
        Assert.Equal(PlanStatus.Manual, manual.Status);
        Assert.Null(manual.Command);
        var unresolved = PlanOne(target, new PackageCandidate(PackageManager.Apt, "vlc"));
        Assert.Equal(PlanStatus.Unresolved, unresolved.Status);
        Assert.Null(unresolved.Candidate);
    }

    [Fact]
    public void PrefersAnExactDistroAndArchitectureMatchOverAnEarlierIncompatibleCandidate()
    {
        var target = Inspect(["ID=debian", "VERSION_ID=13"], ["apt-get"]);
        var plan = PlanOne(target,
            new(PackageManager.Apt, "arm64-only", Target: new(Architecture: "arm64")),
            new(PackageManager.Apt, "other-distro", Target: new(DistributionId: "ubuntu")),
            new(PackageManager.Apt, "vlc", Target: new(DistributionId: "debian", Version: "13", Architecture: "x64")));
        Assert.Equal("vlc", plan.Candidate?.PackageId);
    }

    [Theory]
    [InlineData("--allow-unauthenticated")]
    [InlineData("vlc;echo")]
    [InlineData("../vlc")]
    public void RejectsPackageIdsThatAreNotIdentifiers(string id)
    {
        var target = Inspect(["ID=debian"], ["apt-get"]);
        Assert.Throws<ArgumentException>(() => PlanOne(target, new PackageCandidate(PackageManager.Apt, id)));
    }

    [Fact]
    public void ImportedProfileCannotSupplyAnArbitraryExecutable()
    {
        var target = new PackageProfile("linux", "x64", "debian", null, null,
            [new(PackageManager.Apt, "/tmp/other-tool")], null);
        Assert.Equal("apt-get", PlanOne(target, new PackageCandidate(PackageManager.Apt, "vlc")).Command?.Executable);
    }

    private static PackageProfile Inspect(string[] osRelease, string[] commands, bool isOstree = false) =>
        PackageDiscovery.InspectLinux(osRelease, "x64", commands.Contains, isOstree);

    private static ApplicationPlan PlanOne(PackageProfile target, params PackageCandidate[] candidates) =>
        Assert.Single(PackagePlanner.CreatePlan(target, new(1, [new("VLC", candidates)])));
}
