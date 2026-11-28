using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

// Explicit, one-shot local review. Windows UserAssist is incomplete evidence;
// never infer that an app is unused from a missing record. No writes or timers.
internal static class AppUsageReview
{
    internal sealed record Observation(string Name, DateTime LastObservedUtc);
    private const int MaxValues = 16384;
    private const string CountPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

    internal static string? ParseDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024) return null;
        value = value.Trim();
        if (value.StartsWith('"'))
        {
            int end = value.IndexOf('"', 1);
            if (end < 0 || !ValidIconSuffix(value[(end + 1)..])) return null;
            value = value[1..end];
        }
        else
        {
            int comma = value.LastIndexOf(',');
            if (comma >= 0)
            {
                if (!ValidIconSuffix(value[comma..])) return null;
                value = value[..comma];
            }
        }
        return Canonical(value);
    }

    private static bool ValidIconSuffix(string suffix) =>
        suffix.Length == 0 || suffix[0] == ',' && int.TryParse(suffix[1..], out _);

    internal static string? Canonical(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
                !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\") ||
                path.Contains('%') || path.Contains('"') || path.Contains('*') || path.Contains('?') ||
                path.Skip(2).Contains(':') || path.Any(char.IsControl) ||
                path.Split('\\').Any(part => part is "." or "..")) return null;
            var full = Path.GetFullPath(path);
            if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                MaintenancePaths.IsProtectedMaintenancePath(full)) return null;
            return full;
        }
        catch { return null; }
    }

    internal static string Decode(string value) => new(value.Select(c =>
        c is >= 'a' and <= 'z' ? (char)('a' + (c - 'a' + 13) % 26) :
        c is >= 'A' and <= 'Z' ? (char)('A' + (c - 'A' + 13) % 26) : c).ToArray());

    internal static DateTime? ReadTimestamp(byte[]? value, DateTime nowUtc)
    {
        // Only the known 72-byte Windows layout; other versions are unknown.
        if (value == null || value.Length != 72 || BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(4)) == 0) return null;
        try
        {
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(value.AsSpan(60));
            if (ticks <= 0) return null;
            var date = DateTime.FromFileTimeUtc(ticks);
            return date.Year >= 2000 && date <= nowUtc ? date : null;
        }
        catch { return null; }
    }

    internal static IReadOnlyList<Observation> SelectOlder(IReadOnlyList<InstalledApp> apps,
        IReadOnlyDictionary<string, DateTime> observations, DateTime nowUtc)
    {
        // Ambiguous shared executables are excluded. Results are advisory only.
        return apps.Where(app => app.UsageExecutable != null)
            .GroupBy(app => app.UsageExecutable!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(app => observations.TryGetValue(app.UsageExecutable!, out var date) &&
                date <= nowUtc.AddDays(-90) && date.Year >= 2000 &&
                !string.IsNullOrWhiteSpace(app.DisplayName) && app.DisplayName.Length <= 200)
            .Select(app => new Observation(app.DisplayName, observations[app.UsageExecutable!]))
            .OrderBy(row => row.LastObservedUtc).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(30).ToArray();
    }

    internal static IReadOnlyList<Observation>? Read(IReadOnlyList<InstalledApp> apps)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CountPath, writable: false);
            if (key == null || key.ValueCount > MaxValues) return null;
            var values = key.GetValueNames();
            if (values.Length > MaxValues) return null;
            var now = DateTime.UtcNow;
            var observations = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in values)
            {
                if (name.Length > 1024) continue;
                var path = Canonical(Decode(name));
                if (path == null) continue;
                var date = ReadTimestamp(key.GetValue(name) as byte[], now);
                if (date.HasValue && (!observations.TryGetValue(path, out var old) || date.Value > old))
                    observations[path] = date.Value;
            }
            if (observations.Count == 0) return null;
            return SelectOlder(apps, observations, now);
        }
        catch { return null; }
    }
}
