using System;
using System.IO;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class BackupTargetSafetyTests
{
    [Theory]
    [InlineData("c:", "C:")]
    [InlineData("Z:", "Z:")]
    public void NormalizesDriveLetter(string input, string expected) =>
        Assert.Equal(expected, BackupService.NormalizeDrive(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\")]
    [InlineData("C:foo")]
    [InlineData("1:")]
    [InlineData("\\\\server\\share")]
    public void RejectsNonDriveIdentity(string? input) =>
        Assert.Throws<ArgumentException>(() => BackupService.NormalizeDrive(input));

    [Fact]
    public void VolumeArgumentUsesCanonicalGuidWithoutTrailingSlash() =>
        Assert.Equal(@"\\?\Volume{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}",
            BackupService.VolumeArgument(@"\\?\Volume{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\"));

    [Theory]
    [InlineData("C:\\")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\")]
    [InlineData(@"\\?\Volume{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}\extra")]
    [InlineData(@"\\?\Volume{bad}\")]
    public void RejectsMalformedVolumeIdentity(string input) =>
        Assert.Throws<IOException>(() => BackupService.VolumeArgument(input));

    [Fact]
    public void ConfirmationCannotBeReusedForReassignedVolumeOrDisk()
    {
        var original = new BackupService.BackupTarget("D:", "guid-a", 2, "Backup");
        Assert.True(BackupService.SameTarget(original, original with { }));
        Assert.False(BackupService.SameTarget(original, original with { VolumeGuid = "guid-b" }));
        Assert.False(BackupService.SameTarget(original, original with { DiskNumber = 3 }));
        Assert.False(BackupService.SameTarget(original, original with { Label = "Other" }));
        Assert.False(BackupService.SameTarget(original, original with { Drive = "E:" }));
    }
}
