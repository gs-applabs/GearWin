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

    [Theory]
    [InlineData("driver.inf", true)]
    [InlineData(@"sub\driver.inf", true)]
    [InlineData(@"..\driver.inf", false)]
    [InlineData(@"C:\driver.inf", false)]
    [InlineData(@"\\server\driver.inf", false)]
    [InlineData("NUL.inf", false)]
    [InlineData("driver.inf:stream", false)]
    [InlineData("folder./driver.inf", false)]
    [InlineData("driver.inf ", false)]
    public void CabinetNamesAreConservative(string name, bool expected) =>
        Assert.Equal(expected, CatalogPackage.SafeCabinetName(name));

    private static byte[] CabinetFixture(string name, int declared, int unpacked, int flags = 0)
    {
        using var s = new MemoryStream();
        using var w = new BinaryWriter(s);
        var n = Encoding.ASCII.GetBytes(name + "\0");
        int dataOffset = 44 + 16 + n.Length;
        w.Write(0x4643534du); w.Write(0u); w.Write((uint)(dataOffset + 8 + 4));
        w.Write(0u); w.Write(44u); w.Write(0u);
        w.Write((byte)3); w.Write((byte)1); w.Write((ushort)1); w.Write((ushort)1);
        w.Write((ushort)flags); w.Write((ushort)0); w.Write((ushort)0);
        w.Write((uint)dataOffset); w.Write((ushort)1); w.Write((ushort)1);
        w.Write((uint)declared); w.Write(0u); w.Write((ushort)0); w.Write(new byte[6]); w.Write(n);
        w.Write(0u); w.Write((ushort)4); w.Write((ushort)unpacked); w.Write(new byte[4]);
        return s.ToArray();
    }

    [Theory]
    [InlineData("driver.inf", 4, 4, 0, 4, true)]
    [InlineData("driver.inf", 5, 4, 0, 10, false)]
    [InlineData("driver.inf", 4, 8, 0, 4, false)]
    [InlineData("../driver.inf", 4, 4, 0, 4, false)]
    [InlineData("driver.inf", 4, 4, 1, 4, false)]
    [InlineData("driver.inf", 4, 0, 0, 4, false)]
    public void CabinetPreflightEnforcesStructureAndQuota(string name, int declared, int unpacked, int flags, int limit, bool accepted)
    {
        using var stream = new MemoryStream(CabinetFixture(name, declared, unpacked, flags));
        if (accepted) CatalogPackage.ValidateCabinet(stream, limit);
        else Assert.Throws<InvalidDataException>(() => CatalogPackage.ValidateCabinet(stream, limit));
    }

    [Fact]
    public void TruncatedCabinetIsRefused()
    {
        var bytes = CabinetFixture("driver.inf", 4, 4);
        using var stream = new MemoryStream(bytes[..^1]);
        Assert.Throws<InvalidDataException>(() => CatalogPackage.ValidateCabinet(stream));
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
