using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

// Πρόταση βελτίωσης που εγκρίθηκε ("κάνε τα όλα") - βασικά unit tests για την καθαρή λογική
// (χωρίς I/O/UI) του driver-engine, η οποία μέχρι τώρα δεν είχε καμία αυτοματοποιημένη επαλήθευση.
public class DriverServiceTests
{
    [Theory]
    [InlineData("2026-01-01", "2025-01-01", true)]   // νεότερο - πράγματι νεότερο
    [InlineData("2025-01-01", "2026-01-01", false)]  // "νεότερο" στην πραγματικότητα παλιότερο
    [InlineData("2025-01-01", "2025-01-01", false)]  // ίδια ημερομηνία - ΟΧΙ "νεότερο"
    [InlineData(null, "2025-01-01", false)]           // καμία διαθέσιμη ημερομηνία - δεν μπορεί να ισχυριστεί τίποτα
    [InlineData("2026-01-01", null, true)]            // άγνωστη εγκατεστημένη ημερομηνία, αλλά υπάρχει επίσημη - συντηρητικά "πιθανή ενημέρωση"
    [InlineData("not-a-date", "2025-01-01", false)]   // μη-έγκυρη ημερομηνία - ΠΟΤΕ crash, απλά false
    public void IsDateNewer_MatchesExpected(string? latest, string? installed, bool expected)
    {
        Assert.Equal(expected, DriverService.IsDateNewer(latest, installed));
    }

    [Theory]
    [InlineData(@"PCI\VEN_1234&DEV_5678", true)]
    [InlineData("", false)]
    [InlineData("bad\"id", false)]
    [InlineData("bad;id", false)]
    [InlineData("bad\nid", false)]
    public void DeviceIdentityRejectsAmbiguousValues(string id, bool expected) =>
        Assert.Equal(expected, DriverService.ValidDeviceIdentity(id));

    private static PnpDeviceInfo Device(string id, string version) =>
        new("Device", @"PCI\VEN_1234", Version.Parse(version), DateTime.MinValue) { DeviceInstanceId = id };

    private static CatalogCandidate Candidate(string id, string version) =>
        new("Device", @"PCI\VEN_1234", "00000000-0000-0000-0000-000000000001",
            "Driver", Version.Parse(version), DateTime.MinValue, false) { DeviceInstanceId = id };

    [Theory]
    [InlineData("1.0", "2.0", true)]
    [InlineData("0.0.0.0", "2.0", false)]
    [InlineData("2.0", "2.0", false)]
    [InlineData("3.0", "2.0", false)]
    public void InstallRequiresKnownOlderVersion(string installed, string requested, bool accepted) =>
        Assert.Equal(accepted, DriverService.CatalogTargetStillEligible(Candidate("a", requested), new[] { Device("a", installed) }));

    [Fact]
    public void RecheckRefusesMissingOrAmbiguousInstanceAndOtherNewerDevice()
    {
        var c = Candidate("a", "2.0");
        Assert.False(DriverService.CatalogTargetStillEligible(c, new[] { Device("b", "1.0") }));
        Assert.False(DriverService.CatalogTargetStillEligible(c, new[] { Device("a", "1.0"), Device("A", "1.0") }));
        Assert.False(DriverService.CatalogTargetStillEligible(c, new[] { Device("a", "1.0"), Device("b", "3.0") }));
        Assert.True(DriverService.CatalogTargetStillEligible(c, new[] { Device("a", "1.0"), Device("b", "1.0") }));
    }

    [Fact]
    public void BestCandidateUsesVersionTieBreakAndExactHardwareId()
    {
        var device = Device("a", "1.0");
        var wrong = Candidate("a", "9.0") with { HardwareId = "other" };
        Assert.Equal(new Version(3, 0), DriverService.PickBest(device,
            new() { Candidate("a", "2.0"), wrong, Candidate("a", "3.0") })!.Version);
    }

    [Theory]
    [InlineData(true, 10, 4, true)]
    [InlineData(false, 0, 0, true)]
    [InlineData(false, 5, 0, true)]
    [InlineData(false, 5, 2, false)]
    [InlineData(false, 1, 0, false)]
    public void IncompleteDriverScanIsNotReportedAsComplete(bool failed, int scanned, int found, bool expected) =>
        Assert.Equal(expected, DriverService.CatalogScanIncomplete(failed, scanned, found));

    [Fact]
    public void HasSourceOverlap_MatchesSubstringEitherDirection()
    {
        var confirmed = new[] { "NVIDIA GeForce RTX 4070" };
        Assert.True(DriverService.HasSourceOverlap("NVIDIA GeForce RTX 4070 Laptop GPU", confirmed));
        Assert.True(DriverService.HasSourceOverlap("GeForce RTX 4070", confirmed));
        Assert.False(DriverService.HasSourceOverlap("AMD Radeon RX 7900 GRE", confirmed));
    }

    [Fact]
    public void HasSourceOverlap_EmptyInputsNeverMatch()
    {
        Assert.False(DriverService.HasSourceOverlap(null, new[] { "Anything" }));
        Assert.False(DriverService.HasSourceOverlap("Some GPU", Array.Empty<string>()));
        Assert.False(DriverService.HasSourceOverlap("Some GPU", new[] { "", null! }));
    }
}
