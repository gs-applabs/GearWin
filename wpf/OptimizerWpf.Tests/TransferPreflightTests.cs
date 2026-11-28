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

        [Fact]
        public void CanceledPreflightDoesNotPublishSuccessEvenForInvalidPaths()
        {
            using var cancel = new System.Threading.CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<System.OperationCanceledException>(() =>
                TransferPreflight.Inspect("", "", ct: cancel.Token));
        }

        [Fact]
        public void EntryLimitReportsPartialScanWithoutWritingDestination()
        {
            var root = Path.Combine(Path.GetTempPath(), "gearwin-limit-" + Guid.NewGuid().ToString("N"));
            var src = Path.Combine(root, "src");
            var dst = Path.Combine(root, "dst");
            try
            {
                Directory.CreateDirectory(src);
                Directory.CreateDirectory(dst);
                File.WriteAllText(Path.Combine(src, "one"), "1");
                File.WriteAllText(Path.Combine(src, "two"), "22");
                var report = TransferPreflight.Inspect(src, dst, 1);
                Assert.False(report.Ready);
                Assert.Equal(1, report.FileCount);
                Assert.Contains("limit", report.Message);
                Assert.Empty(Directory.GetFileSystemEntries(dst));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void DriveRootSourceOverlapsAnyFolderOnThatDrive()
        {
            // Regression: "C:\\" keeps its trailing separator, so the overlap check used to look for "C:\\\\" and miss it.
            var root = Path.GetPathRoot(Path.GetTempPath())!;
            var dst = Path.Combine(Path.GetTempPath(), "gearwin-root-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dst);
                var forward = TransferPreflight.Inspect(root, dst);
                var backward = TransferPreflight.Inspect(dst, root);
                Assert.False(forward.Ready);
                Assert.False(backward.Ready);
                Assert.Contains("overlap", forward.Message);
                Assert.Contains("overlap", backward.Message);
            }
            finally { if (Directory.Exists(dst)) Directory.Delete(dst, true); }
        }
    }
}
