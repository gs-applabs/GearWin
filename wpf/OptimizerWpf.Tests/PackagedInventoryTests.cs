using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public sealed class PackagedInventoryTests
{
    private const string Sample = """
        [{"Name":"Vendor.App","PackageFullName":"Vendor.App_1.2.3.4_x64__abcdefghijklm","Signature":"Store"},
         {"Name":"Vendor.Other","PackageFullName":"Vendor.Other_1.2.3.4_x64__abcdefghijklm","Signature":"Developer"},
         {"Name":"Wrong","PackageFullName":"Vendor.Other_1.2.3.4_x64__abcdefghijklm","Signature":"Store"}]
        """;

    [Fact]
    public void BroaderInventoryDoesNotBroadenUninstallScan()
    {
        var broad = BloatwareService.ParsePackagedInventory(Sample, false);
        var narrow = BloatwareService.ParsePackagedInventory(Sample, true);
        Assert.Equal(2, broad.Apps.Count);
        Assert.True(broad.Incomplete);
        Assert.True(Assert.Single(narrow.Apps).IsStore);
        Assert.True(narrow.Incomplete);
        Assert.Contains("-not $_.NonRemovable", BloatwareService.PackagedInventoryScript(true));
        Assert.Contains("SignatureKind -eq 'Store'", BloatwareService.PackagedInventoryScript(true));
        Assert.DoesNotContain("-not $_.NonRemovable", BloatwareService.PackagedInventoryScript(false));
        Assert.DoesNotContain("Remove-AppxPackage", BloatwareService.PackagedInventoryScript(false));
    }

    [Fact]
    public async Task OutputLimitDrainsWithoutRetainingExcess()
    {
        using var reader = new StringReader(new string('x', 10000));
        var result = await BloatwareService.ReadInventoryOutputAsync(reader, 100, CancellationToken.None);
        Assert.True(result.Exceeded);
        Assert.True(result.Text.Length <= 100);
        Assert.Equal(-1, reader.Peek());
    }

    [Fact]
    public async Task OutputAtExactLimitIsRetained()
    {
        using var reader = new StringReader("123456");
        var result = await BloatwareService.ReadInventoryOutputAsync(reader, 6, CancellationToken.None);
        Assert.False(result.Exceeded);
        Assert.Equal("123456", result.Text);
    }
}
