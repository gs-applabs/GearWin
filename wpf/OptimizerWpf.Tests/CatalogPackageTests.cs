using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class CatalogPackageTests
{
    [Theory]
    [InlineData("https://download.windowsupdate.com/a.cab", true)]
    [InlineData("https://catalog.s.download.windowsupdate.com/a.CAB", true)]
    [InlineData("http://download.windowsupdate.com/a.cab", false)]
    [InlineData("https://download.windowsupdate.com.evil.example/a.cab", false)]
    [InlineData("https://user@download.windowsupdate.com/a.cab", false)]
    [InlineData("https://download.windowsupdate.com:8443/a.cab", false)]
    [InlineData("https://download.windowsupdate.com/a.msu", false)]
    [InlineData("https://download.windowsupdate.com/a.exe", false)]
    [InlineData("https://download.windowsupdate.com/a.cab#fragment", false)]
    public void OnlyExactHttpsCabHostsAllowed(string url, bool accepted) =>
        Assert.Equal(accepted, CatalogPackage.Normalize(url) != null);

    [Fact]
    public void CatalogHttpLinkUpgradedButRedirectDowngradeRefused()
    {
        const string url = "http://download.windowsupdate.com/a.cab";
        Assert.Equal("https", CatalogPackage.Normalize(url, true)!.Scheme);
        Assert.Null(CatalogPackage.Normalize(url));
    }

    [Fact]
    public async Task StreamCopyHashesWithoutWholePackageBuffer()
    {
        var bytes = Encoding.ASCII.GetBytes("driver fixture");
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var hash = await CatalogPackage.CopyHashAsync(input, output, bytes.Length, 100, CancellationToken.None);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), hash);
        Assert.Equal(bytes, output.ToArray());
    }

    [Theory]
    [InlineData(5, 4L, 10L)]
    [InlineData(5, 6L, 10L)]
    [InlineData(5, null, 4L)]
    [InlineData(0, null, 10L)]
    public async Task StreamRejectsTruncationOrOversize(int size, long? expected, long limit)
    {
        using var input = new MemoryStream(new byte[size]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CatalogPackage.CopyHashAsync(input, output, expected, limit, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(4, 4, true)]
    [InlineData(5, 4, false)]
    [InlineData(8193, 8192, false)]
    public async Task HtmlReadEnforcesByteLimit(int size, int limit, bool accepted)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', size)));
        if (accepted)
            Assert.Equal(new string('a', size),
                await CatalogPackage.ReadBoundedHtmlAsync(input, limit, Encoding.UTF8, CancellationToken.None));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CatalogPackage.ReadBoundedHtmlAsync(input, limit, Encoding.UTF8, CancellationToken.None));
    }

    [Fact]
    public async Task HtmlReadHonorsCancellation()
    {
        using var input = new MemoryStream(new byte[8]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CatalogPackage.ReadBoundedHtmlAsync(input, 16, Encoding.UTF8, cts.Token));
    }

    [Fact]
    public async Task HtmlReadDecodesUtf8BomWithinByteLimit()
    {
        var bytes = new byte[] { 0xef, 0xbb, 0xbf, 0xce, 0xb1 };
        using var input = new MemoryStream(bytes);
        Assert.Equal("α", await CatalogPackage.ReadBoundedHtmlAsync(input, 5, Encoding.UTF8, CancellationToken.None));
    }

    private const string Inf = "[Manufacturer]\n%Vendor%=Models,NTamd64\n[Models.NTamd64]\n%Device%=Install,PCI\\VEN_1234&DEV_5678\n[Other]\n%Other%=Install,PCI\\VEN_9999&DEV_0000";
    [Fact]
    public void InfMustMatchDeclaredModelHardwareIdExactly()
    {
        Assert.True(CatalogPackage.MatchesInf(Inf, @"PCI\VEN_1234&DEV_5678"));
        Assert.False(CatalogPackage.MatchesInf(Inf, @"PCI\VEN_1234"));
        Assert.False(CatalogPackage.MatchesInf(Inf, @"PCI\VEN_9999&DEV_0000"));
    }
}
