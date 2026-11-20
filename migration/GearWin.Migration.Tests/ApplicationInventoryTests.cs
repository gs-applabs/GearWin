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

    private static string Entry(string name) => $"[Desktop Entry]\nType=Application\nName={name}\n";

    private static void WriteEntry(string dataDirectory, string relativePath, string content)
    {
        string path = Path.Combine(dataDirectory, "applications", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
