using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OptimizerWpf.Services;

// Shared conservative filesystem checks for existing maintenance tools. These checks are
// defense in depth, not a handle-based guarantee against concurrent reparse-point substitution.
internal static class MaintenancePaths
{
    internal static bool IsOrdinaryLocalPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!System.Text.RegularExpressions.Regex.IsMatch(full, @"^[A-Za-z]:\\")) return false;
            var root = Path.GetPathRoot(full)!;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return false;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            var current = root;
            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Contains(':')) return false; // no alternate data streams
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool IsWithin(string root, string candidate)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // Only delete individual regular files. Never recursively delete an entire directory tree,
    // never remove its root, and skip recently modified temporary files.
    internal static long CleanFiles(string root, TimeSpan minAge, bool recursive = true, string pattern = "*")
    {
        if (!IsOrdinaryLocalPath(root) || !Directory.Exists(root)) return 0;
        var pending = new Stack<string>();
        pending.Push(root);
        long removed = 0;
        var cutoff = DateTime.UtcNow - minAge;
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (!IsOrdinaryLocalPath(dir)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(dir, pattern))
                {
                    try
                    {
                        if (!IsWithin(root, file) || !IsOrdinaryLocalPath(file)) continue;
                        var info = new FileInfo(file);
                        if (!info.Exists || info.LastWriteTimeUtc > cutoff) continue;
                        // Do not touch files currently in use, even when delete sharing is allowed.
                        long length;
                        using (var probe = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                            length = probe.Length;
                        if (!IsOrdinaryLocalPath(file) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
                        File.Delete(file);
                        if (!File.Exists(file)) removed += length; // logical bytes, not physical allocation
                    }
                    catch { /* inaccessible or active: leave intact */ }
                }
                if (recursive)
                    foreach (var child in Directory.GetDirectories(dir))
                        if (IsWithin(root, child) && IsOrdinaryLocalPath(child)) pending.Push(child);
            }
            catch { /* inaccessible directory: leave intact */ }
        }
        return removed;
    }

    internal static string? VerifiedUserTemp()
    {
        try
        {
            var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
            var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) && IsOrdinaryLocalPath(actual)
                ? actual : null;
        }
        catch { return null; }
    }
}
