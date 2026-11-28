using System;
using System.IO;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class RecoveryMetadataProbeTests
{
    [Theory]
    [InlineData("c:", "C:")]
    [InlineData("D:", "D:")]
    public void CatalogUsesOnlyReadOnlyAbsoluteSystemCommand(string input, string expected)
    {
        var info = RecoveryMetadataProbe.CatalogStart(input);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "wbadmin.exe"), info.FileName);
        Assert.Equal(new[] { "get", "versions", "-backupTarget:" + expected }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput && info.RedirectStandardError);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C: -quiet")]
    [InlineData("start backup")]
    public void CatalogRejectsNonDriveArguments(string input) =>
        Assert.Throws<ArgumentException>(() => RecoveryMetadataProbe.CatalogStart(input));

    [Fact]
    public void WinReOnlyReadsInfo()
    {
        var info = RecoveryMetadataProbe.WinReStart();
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "reagentc.exe"), info.FileName);
        Assert.Equal(new[] { "/info" }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput && info.RedirectStandardError);
    }
}
