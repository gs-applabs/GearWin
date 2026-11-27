using System;
using System.IO;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class BenchmarkSafetyTests
{
    [Theory]
    [InlineData("c", "C:\\")]
    [InlineData("D:", "D:\\")]
    [InlineData("z:\\", "Z:\\")]
    public void NormalizesOnlyDriveRoot(string input, string expected)
        => Assert.Equal(expected, SystemService.BenchmarkRoot(input));

    [Theory]
    [InlineData("")]
    [InlineData("1:")]
    [InlineData("C:\\Windows")]
    [InlineData("C:foo")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:/")]
    [InlineData("C::")]
    public void RejectsNonDrivePaths(string input)
        => Assert.Throws<ArgumentException>(() => SystemService.BenchmarkRoot(input));

    [Fact]
    public void ExistingFileIsNeverTruncatedOrDeleted()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(path, "preserve");
            Assert.Throws<IOException>(() => SystemService.OpenBenchmarkFile(path, 4096));
            Assert.Equal("preserve", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OwnedFileCanBeReadAndIsDeletedOnClose()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        using (var stream = SystemService.OpenBenchmarkFile(path, 4096))
        {
            stream.WriteByte(42);
            stream.Flush(true);
            stream.Position = 0;
            Assert.Equal(42, stream.ReadByte());
        }
        Assert.False(File.Exists(path));
    }
}
