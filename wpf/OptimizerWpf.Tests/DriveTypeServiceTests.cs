using System;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

// ΝΕΟ - roadmap "Περισσότερα unit tests" (v4.3.5). Καλύπτει το GuessFromModelName fallback (καθαρή
// string/regex λογική, μηδενικό I/O) - χρησιμοποιείται μόνο όταν το WMI αποτυγχάνει εντελώς, βλ.
// σχόλιο ειλικρίνειας στο ίδιο το service ότι πρόκειται για ΕΚΤΙΜΗΣΗ, όχι σίγουρη ανίχνευση.
public class DriveTypeServiceTests
{
    [Theory]
    [InlineData("Samsung SSD 970 EVO Plus 1TB", PhysicalDriveKind.Nvme)]
    [InlineData("WD Blue SN580 1TB", PhysicalDriveKind.Nvme)]
    [InlineData("Crucial P3 Plus 2TB", PhysicalDriveKind.Nvme)]
    [InlineData("KINGSTON NV2 500GB", PhysicalDriveKind.Nvme)]
    [InlineData("Crucial MX500 SSD 1TB", PhysicalDriveKind.Ssd)]
    [InlineData("KINGSTON KC3000 1TB", PhysicalDriveKind.Nvme)]
    [InlineData("Samsung SSD 870 EVO", PhysicalDriveKind.Ssd)]
    [InlineData("Generic Drive 980", PhysicalDriveKind.Unknown)]
    [InlineData("ST2000DM008-2FR102 (Barracuda)", PhysicalDriveKind.Hdd)]
    [InlineData("Seagate IronWolf 4TB", PhysicalDriveKind.Hdd)]
    [InlineData("WD40EZRZ", PhysicalDriveKind.Hdd)]
    [InlineData("TOSHIBA DT01ACA100", PhysicalDriveKind.Hdd)]
    [InlineData("Some Unrecognized Model X1", PhysicalDriveKind.Unknown)]
    [InlineData(null, PhysicalDriveKind.Unknown)]
    [InlineData("", PhysicalDriveKind.Unknown)]
    [InlineData("   ", PhysicalDriveKind.Unknown)]
    public void GuessFromModelName_MatchesExpectedKind(string? model, PhysicalDriveKind expected)
    {
        Assert.Equal(expected, DriveTypeService.GuessFromModelName(model));
    }

    [Theory]
    [InlineData(17, false, PhysicalDriveKind.Nvme)]
    [InlineData(17, null, PhysicalDriveKind.Nvme)]
    [InlineData(17, true, PhysicalDriveKind.Unknown)]
    [InlineData(11, true, PhysicalDriveKind.Hdd)]
    [InlineData(11, false, PhysicalDriveKind.Ssd)]
    [InlineData(11, null, PhysicalDriveKind.Unknown)]
    [InlineData(7, true, PhysicalDriveKind.Usb)]
    [InlineData(12, false, PhysicalDriveKind.Usb)]
    [InlineData(13, null, PhysicalDriveKind.Usb)]
    [InlineData(8, false, PhysicalDriveKind.Unknown)]
    [InlineData(15, false, PhysicalDriveKind.Unknown)]
    [InlineData(16, false, PhysicalDriveKind.Unknown)]
    public void ClassifyUsesDeviceBusAndSeekPenalty(int bus, bool? seek, PhysicalDriveKind expected) =>
        Assert.Equal(expected, DriveTypeService.Classify(bus, seek));

    [Theory]
    [InlineData(@"SCSI\DISK\1", @"SCSI\DISK\1_0", true)]
    [InlineData(@"SCSI\DISK\1", @"scsi\disk\1", true)]
    [InlineData(@"SCSI\DISK\1", @"SCSI\DISK\10_0", false)]
    [InlineData(@"SCSI\DISK\1", @"SCSI\DISK\1_fake", false)]
    [InlineData(null, @"SCSI\DISK\1", false)]
    public void SmartInstanceMustMatchExactIdentity(string? pnp, string? instance, bool expected) =>
        Assert.Equal(expected, DriveTypeService.MatchesSmartInstance(pnp, instance));

    [Fact]
    public void NativeDescriptorsRejectTruncationAndInvalidValues()
    {
        var device = new byte[36];
        device[0] = 36; device[4] = 36; device[28] = 17;
        Assert.Equal(17, StorageTemperatureService.ParseBusType(device));
        Assert.Null(StorageTemperatureService.ParseBusType(device.AsSpan(0, 35)));
        device[4] = 37;
        Assert.Null(StorageTemperatureService.ParseBusType(device));
        var seek = new byte[12]; seek[0] = 12; seek[4] = 12;
        Assert.False(StorageTemperatureService.ParseSeekPenalty(seek));
        seek[8] = 1;
        Assert.True(StorageTemperatureService.ParseSeekPenalty(seek));
        seek[8] = 2;
        Assert.Null(StorageTemperatureService.ParseSeekPenalty(seek));
    }

    [Fact]
    public void GuessFromModelName_NvmePatternWinsOverGenericSsd()
    {
        // Το "EVO" (NVMe pattern) πρέπει να ελεγχθεί ΠΡΙΝ το γενικό "SSD" branch - ένα μοντέλο που
        // περιέχει και τα δύο λέξη-κλειδιά πρέπει να ταξινομείται ως Nvme, όχι απλό Ssd.
        Assert.Equal(PhysicalDriveKind.Nvme, DriveTypeService.GuessFromModelName("Samsung SSD 990 EVO"));
    }

    [Fact]
    public void GuessFromModelName_IsCaseInsensitive()
    {
        Assert.Equal(PhysicalDriveKind.Nvme, DriveTypeService.GuessFromModelName("samsung nvme 970 evo"));
    }
}
