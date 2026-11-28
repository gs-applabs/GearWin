using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;

namespace OptimizerWpf.Services
{
    // Metadata-only inspection. Never writes to a disk, copies a file or follows junctions.
    public sealed record TransferCheck(bool Ready, long FileCount, long ByteCount, int SkippedLinks, string Message);

    public static class TransferPreflight
    {
        internal static System.Threading.Tasks.Task<TransferCheck> InspectAsync(string sourcePath, string targetPath, CancellationToken ct = default) =>
            GearWinCore.RunAsync(CoreWork.TransferScan, () => System.Threading.Tasks.Task.Run(() => Inspect(sourcePath, targetPath, ct: ct), ct), ct: ct);

        private static bool HasUnredirectedAncestors(string path)
        {
            for (DirectoryInfo? directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
                if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }

        public static TransferCheck Inspect(string sourcePath, string targetPath, int maxEntries = 100000, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            static TransferCheck Fail(string reason) => new(false, 0, 0, 0, reason);
            if (maxEntries < 1 || string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
                return Fail("Select source and destination folders.");
            try
            {
                if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(targetPath))
                    return Fail("Select absolute folder paths.");
                string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
                string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
                bool Under(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                    MaintenancePaths.IsWithin(a, b);
                if (!Directory.Exists(source) || !Directory.Exists(target))
                    return Fail("The selected folders must exist.");
                if (Under(source, target) || Under(target, source))
                    return Fail("Source and destination must not overlap.");
                if (!HasUnredirectedAncestors(source) || !HasUnredirectedAncestors(target))
                    return Fail("Linked folder roots or ancestors are not permitted.");

                var stack = new Stack<string>();
                stack.Push(source);
                long files = 0, bytes = 0;
                int entries = 0, skipped = 0;
                while (stack.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    string directory = stack.Pop();
                    if (!HasUnredirectedAncestors(directory)) return new(false, files, bytes, skipped, "Folder changed or redirected during scan.");
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++entries > maxEntries)
                            return new(false, files, bytes, skipped, "Scan limit reached. Inspect the folder manually.");
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) stack.Push(path);
                        else
                        {
                            files = checked(files + 1);
                            bytes = checked(bytes + new FileInfo(path).Length);
                        }
                    }
                }
                ct.ThrowIfCancellationRequested();
                // Recheck target ancestors after the potentially long source enumeration.
                if (!Directory.Exists(target) || !HasUnredirectedAncestors(target))
                    return new(false, files, bytes, skipped, "Destination folder changed or redirected during scan.");
                string? root = Path.GetPathRoot(target);
                if (root is null) return Fail("Unknown destination volume.");
                var free = new DriveInfo(root).AvailableFreeSpace;
                ct.ThrowIfCancellationRequested();
                if (bytes > free)
                    return new(false, files, bytes, skipped, "Insufficient available destination space.");
                if (skipped != 0)
                    return new(false, files, bytes, skipped, "Linked files/directories were skipped. Review before copying.");
                return new(true, files, bytes, skipped,
                    "Metadata and free space checked only. No files were copied or written. Destination write permission, source file readability and post-copy integrity have not been verified.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or OverflowException)
            {
                return Fail("Check was incomplete: " + e.Message);
            }
        }
    }
}
