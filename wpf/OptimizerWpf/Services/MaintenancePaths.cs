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

    // Identical traversal and age policy for estimates and execution. Estimates do
    // not open every file exclusively; in-use files can reduce actual removed bytes.
    private static IEnumerable<string> Candidates(string root, TimeSpan minAge, bool recursive, string pattern)
    {
        if (!IsOrdinaryLocalPath(root) || !Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        var cutoff = DateTime.UtcNow - minAge;
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            if (!IsOrdinaryLocalPath(dir)) continue;
            string[] files, children;
            try { files = Directory.GetFiles(dir, pattern); } catch { files = Array.Empty<string>(); }
            foreach (var file in files)
            {
                bool eligible;
                try { eligible = IsWithin(root, file) && IsOrdinaryLocalPath(file) && File.GetLastWriteTimeUtc(file) <= cutoff; }
                catch { eligible = false; }
                if (eligible) yield return file;
            }
            if (!recursive) continue;
            try { children = Directory.GetDirectories(dir); } catch { children = Array.Empty<string>(); }
            foreach (var child in children)
                if (IsWithin(root, child) && IsOrdinaryLocalPath(child)) pending.Push(child);
        }
    }

    internal static long MeasureFiles(string root, TimeSpan minAge, bool recursive = true, string pattern = "*")
    {
        long size = 0;
        foreach (var file in Candidates(root, minAge, recursive, pattern))
        {
            try { size += new FileInfo(file).Length; } catch { }
        }
        return size;
    }

    internal static long CleanFiles(string root, TimeSpan minAge, bool recursive = true, string pattern = "*")
    {
        long removed = 0;
        var cutoff = DateTime.UtcNow - minAge;
        foreach (var file in Candidates(root, minAge, recursive, pattern))
        {
            try
            {
                long length;
                using (var probe = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                    length = probe.Length;
                if (!IsOrdinaryLocalPath(file) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
                File.Delete(file);
                if (!File.Exists(file)) removed += length;
            }
            catch { /* inaccessible or active: leave intact */ }
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
