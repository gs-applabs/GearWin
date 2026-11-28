using GearWin.Migration;

namespace GearWin.Migration.Tests;

public sealed class ApplicationInventoryTests
{
    [Fact]
    public void ReadsDesktopIdsInPriorityOrderAndHonorsHiddenOverrides()
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory();
        try
        {
            string user = Path.Combine(fixture.FullName, "user");
            string system = Path.Combine(fixture.FullName, "system");
            WriteEntry(user, "hidden.desktop", "[Desktop Entry]\nHidden=true\n");
            WriteEntry(user, "vlc.desktop", "[Desktop Entry]\nType=Application\nName=My VLC\n");
            WriteEntry(system, "hidden.desktop", Entry("System helper"));
            WriteEntry(system, "vlc.desktop", Entry("System VLC"));
            WriteEntry(system, "nested/editor.desktop", Entry("Editor") + "\n[Desktop Action Edit]\nName=Ignored action\n");
            WriteEntry(system, "links.desktop", "[Desktop Entry]\nType=Link\nName=Link\n");
            WriteEntry(system, "helper.desktop", Entry("Hidden helper") + "NoDisplay=true\n");

            var inventory = ApplicationDiscovery.InspectLinux([user, system]);
            Assert.Empty(inventory.Warnings);
            Assert.Collection(inventory.Applications,
                editor => { Assert.Equal("nested-editor.desktop", editor.Id); Assert.Equal("Editor", editor.Name); },
                vlc => { Assert.Equal("vlc.desktop", vlc.Id); Assert.Equal("My VLC", vlc.Name); });
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void InventoryPlansInBothDirectionsAndKeepsUnmatchedAppsVisible()
    {
        var manifest = new PackageManifest(1,
        [
            new("VLC", [new(PackageManager.Winget, "VideoLAN.VLC"), new(PackageManager.Apt, "vlc")],
            [new(InventorySource.LinuxDesktop, Id: "vlc.desktop"),
             new(InventorySource.WindowsRegistry, Name: "VLC media player", Publisher: "VideoLAN")])
        ]);

        var linuxInventory = new ApplicationInventory(1, "linux",
            [new(InventorySource.LinuxDesktop, "vlc.desktop", "Localized name"),
             new(InventorySource.LinuxDesktop, "other.desktop", "Other")], []);
        var windows = new PackageProfile("windows", "x64", null, "10.0", null, [new(PackageManager.Winget, "winget.exe")], null);
        Assert.Collection(PackagePlanner.CreatePlan(windows, manifest, linuxInventory),
            vlc => { Assert.Equal("VideoLAN.VLC", vlc.Candidate?.PackageId); Assert.Equal("vlc.desktop", vlc.Source?.Id); },
            other => { Assert.Equal("Other", other.Name); Assert.Equal(PlanStatus.Unresolved, other.Status); });

        var windowsInventory = new ApplicationInventory(1, "windows",
            [new(InventorySource.WindowsRegistry, "VLC", "VLC media player", Publisher: "VideoLAN")], []);
        var linux = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        Assert.Equal("vlc", Assert.Single(PackagePlanner.CreatePlan(linux, manifest, windowsInventory)).Candidate?.PackageId);
    }

    [Fact]
    public void AmbiguousIdentitiesAndMismatchedPublishersAreNotGuessed()
    {
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var identity = new ApplicationIdentity(InventorySource.WindowsRegistry, Name: "VLC media player", Publisher: "VideoLAN");
        var request = new ApplicationRequest("VLC", [new(PackageManager.Apt, "vlc")], [identity]);
        var manifest = new PackageManifest(1, [request, request with { Name = "Duplicate" }]);
        var source = new ApplicationInventory(1, "windows",
            [new(InventorySource.WindowsRegistry, "vlc", "VLC media player", Publisher: "VideoLAN"),
             new(InventorySource.WindowsRegistry, "other", "VLC media player", Publisher: "Different")], []);

        var plans = PackagePlanner.CreatePlan(target, manifest, source);
        Assert.All(plans, plan => Assert.Equal(PlanStatus.Unresolved, plan.Status));
        Assert.Contains("More than one", plans[0].Note);
        Assert.Contains("No explicit", plans[1].Note);
    }

    [Fact]
    public void OversizedDesktopEntryDoesNotBlockTheRestOfTheInventory()
    {
        DirectoryInfo fixture = Directory.CreateTempSubdirectory();
        try
        {
            WriteEntry(fixture.FullName, "large.desktop", Entry("Oversized") + new string('X', 64 * 1024));
            WriteEntry(fixture.FullName, "small.desktop", Entry("Small"));
            var inventory = ApplicationDiscovery.InspectLinux([fixture.FullName]);
            Assert.Equal("small.desktop", Assert.Single(inventory.Applications).Id);
            Assert.Single(inventory.Warnings);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void MultipleAliasesForOneCatalogApplicationResolveOnce()
    {
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1,
            [new("VLC", [new(PackageManager.Apt, "vlc")],
                [new(InventorySource.WindowsRegistry, Id: "vlc"),
                 new(InventorySource.WindowsRegistry, Name: "vlc media player", Publisher: "videolan")])]);
        var source = new ApplicationInventory(1, "windows",
            [new(InventorySource.WindowsRegistry, "VLC", "VLC media player", Publisher: "VideoLAN")], []);
        Assert.Equal("vlc", Assert.Single(PackagePlanner.CreatePlan(target, manifest, source)).Candidate?.PackageId);
    }

    [Fact]
    public void StoreFamilyIdentityIsStableAcrossArchitectureAndSupportsExplicitMapping()
    {
        var inventory = new ApplicationInventory(1, "windows", [], []);
        inventory = ApplicationDiscovery.AddStorePackages(inventory, [
            ("Vendor.App", "Vendor.App_1.2.3.4_x64__abcdefghijklm"),
            ("Vendor.App", "Vendor.App_1.2.3.4_arm64__abcdefghijklm"),
            ("Wrong", "Vendor.App_1.2.3.4_x64__abcdefghijklm")
        ]);
        var app = Assert.Single(inventory.Applications);
        Assert.Equal(InventorySource.WindowsStorePackage, app.Kind);
        Assert.Equal("Vendor.App_abcdefghijklm", app.Id);
        Assert.Equal("1.2.3.4", app.Version);
        Assert.Equal(2, inventory.Warnings.Count);
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1, [
            new("Example", [new(PackageManager.Apt, "example")],
                [new(InventorySource.WindowsStorePackage, Id: "VENDOR.APP_ABCDEFGHIJKLM")])
        ]);
        Assert.Equal("example", Assert.Single(PackagePlanner.CreatePlan(target, manifest, inventory)).Candidate?.PackageId);
        Assert.Equal(PlanStatus.Unresolved, Assert.Single(PackagePlanner.CreatePlan(target, new PackageManifest(1, []), inventory)).Status);
    }

    [Fact]
    public void StoreSupplementCannotBeAddedToLinuxInventory()
    {
        Assert.Throws<ArgumentException>(() => ApplicationDiscovery.AddStorePackages(new ApplicationInventory(1, "linux", [], []), []));
    }

    [Fact]
    public void SelectedExecutablesStayManualEvenWithMatchingCatalog()
    {
        var source = ApplicationDiscovery.AddSelectedExecutables(new ApplicationInventory(1, "windows", [], []),
            ["portable.exe", "PORTABLE.EXE", @"C:\\private\\other.exe", "not-an-app.txt"]);
        var entry = Assert.Single(source.Applications);
        Assert.Equal("portable.exe", entry.Id);
        Assert.Equal(2, source.Warnings.Count);
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1, [new("Portable", [new(PackageManager.Apt, "example")],
            [new(InventorySource.WindowsSelectedExecutable, Id: "portable.exe")])]);
        var plan = Assert.Single(PackagePlanner.CreatePlan(target, manifest, source));
        Assert.Equal(PlanStatus.Manual, plan.Status);
        Assert.Null(plan.Command);
    }

    [Fact]
    public void SelectedExecutableCountIsBounded()
    {
        var source = ApplicationDiscovery.AddSelectedExecutables(new ApplicationInventory(1, "windows", [], []),
            Enumerable.Range(0, 101).Select(i => $"app{i}.exe"));
        Assert.Equal(100, source.Applications.Count);
        Assert.Equal(2, source.Warnings.Count);
    }

    [Theory]
    [InlineData(0, DestinationMatchStatus.NotConfirmed)]
    [InlineData(1, DestinationMatchStatus.ExplicitIdentityFound)]
    [InlineData(2, DestinationMatchStatus.MultipleMatches)]
    public void DestinationComparisonUsesExplicitCatalogIdentityWithoutClaimingPackageVerification(int count, DestinationMatchStatus expected)
    {
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1, [new("VLC", [new(PackageManager.Apt, "vlc")],
            [new(InventorySource.WindowsRegistry, Id: "vlc"), new(InventorySource.LinuxDesktop, Id: "vlc.desktop")])]);
        var source = new ApplicationInventory(1, "windows", [new(InventorySource.WindowsRegistry, "vlc", "VLC")], []);
        var destination = new ApplicationInventory(1, "linux",
            Enumerable.Range(0, count).Select(i => new InstalledApplication(InventorySource.LinuxDesktop, "vlc.desktop", "VLC", i.ToString())).ToArray(),
            ["incomplete"]);
        var report = MigrationDocuments.CreatePlan(target, manifest, source, destination);
        var entry = Assert.Single(report.Applications);
        Assert.Equal(expected, entry.DestinationMatch?.Status);
        Assert.Null(entry.Verification);
        Assert.Equal("incomplete", Assert.Single(report.DestinationInventoryWarnings!));
    }

    [Fact]
    public void DestinationInventoryOfWrongOsIsRejected()
    {
        var target = new PackageProfile("linux", "x64", "debian", null, null, [], null);
        Assert.Throws<ArgumentException>(() => PackagePlanner.CreatePlan(target, new PackageManifest(1, []),
            destination: new ApplicationInventory(1, "windows", [], [])));
    }

    [Fact]
    public void PackagedAppsKeepStoreAndNonStoreIdentitiesSeparate()
    {
        var source = ApplicationDiscovery.AddPackagedApps(new ApplicationInventory(1, "windows", [], []), [
            ("Vendor.App", "Vendor.App_1.2.3.4_x64__abcdefghijklm", true),
            ("Vendor.App", "Vendor.App_1.2.3.4_arm64__abcdefghijklm", false)
        ], incomplete: true);
        Assert.Equal(2, source.Applications.Count);
        Assert.Contains(source.Applications, a => a.Kind == InventorySource.WindowsStorePackage);
        Assert.Contains(source.Applications, a => a.Kind == InventorySource.WindowsPackagedApp);
        Assert.Equal(2, source.Warnings.Count);
        var target = new PackageProfile("linux", "x64", "debian", "13", null, [new(PackageManager.Apt, "apt-get")], null);
        var manifest = new PackageManifest(1, [new("Example", [new(PackageManager.Apt, "example")],
            [new(InventorySource.WindowsPackagedApp, Id: "VENDOR.APP_ABCDEFGHIJKLM")])]);
        var plan = PackagePlanner.CreatePlan(target, manifest, source);
        Assert.Equal(PlanStatus.Unresolved, plan.Single(a => a.Source!.Kind == InventorySource.WindowsStorePackage).Status);
        Assert.Equal(PlanStatus.CommandPreview, plan.Single(a => a.Source!.Kind == InventorySource.WindowsPackagedApp).Status);
    }

    private static string Entry(string name) => $"[Desktop Entry]\nType=Application\nName={name}\n";

    private static void WriteEntry(string dataDirectory, string relativePath, string content)
    {
        string path = Path.Combine(dataDirectory, "applications", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
