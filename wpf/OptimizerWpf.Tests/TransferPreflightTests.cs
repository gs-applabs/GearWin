using System;
using System.IO;
using OptimizerWpf.Services;
namespace OptimizerWpf.Tests
{
    public sealed class TransferPreflightTests
    {
        [Fact]
        public void SeparateFoldersPassWithoutAnyDestinationWrites()
        {
            var root = Path.Combine(Path.GetTempPath(), "gearwin-test-" + Guid.NewGuid().ToString("N"));
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            try
            {
                Directory.CreateDirectory(src);
                Directory.CreateDirectory(dst);
                File.WriteAllText(Path.Combine(src, "sample.txt"), "gear");
                var report = TransferPreflight.Inspect(src, dst);
                Assert.True(report.Ready, report.Message);
                Assert.Equal(1, report.FileCount);
                Assert.Equal(4, report.ByteCount);
                Assert.Empty(Directory.GetFileSystemEntries(dst));
                Assert.False(TransferPreflight.Inspect(src, src).Ready);
                Assert.False(TransferPreflight.Inspect(root, dst).Ready);
                Assert.False(TransferPreflight.Inspect(src, dst, 0).Ready);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
