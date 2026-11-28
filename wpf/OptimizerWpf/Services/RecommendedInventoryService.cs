using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Session-only shared inventory. Unknown is deliberately not "not installed".
internal static class RecommendedInventoryService
{
    private static readonly object Gate = new();
    private static Task<IReadOnlyDictionary<string, bool?>>? _active;
    private static IReadOnlyDictionary<string, bool?>? _cached;
    private static long _cachedAt;
    private static long _cachedSoftwareEpoch;
    private static int _generation;

    internal static void Invalidate()
    {
        lock (Gate) { _generation++; _cached = null; }
    }

    internal static Task<IReadOnlyDictionary<string, bool?>> GetAsync(bool refresh = false)
    {
        lock (Gate)
        {
            if (_active is { IsCompleted: false }) return _active;
            if (!refresh && _cached != null && _cachedSoftwareEpoch == GearWinCore.SoftwareEpoch && Environment.TickCount64 - _cachedAt < 120000)
                return Task.FromResult(_cached);
            return _active = ReadAsync(_generation);
        }
    }

    private static async Task<IReadOnlyDictionary<string, bool?>> ReadAsync(int generation)
    {
        using var lease = await GearWinCore.EnterAsync(CoreWork.UpdateScan).ConfigureAwait(false);
        var epoch = GearWinCore.SoftwareEpoch;
        var (output, exitCode, started) = await WingetService.ListInstalledWithinLeaseAsync().ConfigureAwait(false);
        var (ids, complete) = ParseInventory(output, started && exitCode == 0);
        // Read uninstall titles once. Registry matches are positive corroboration only.
        var titles = await Task.Run(() => AppIconService.ReadInstalledIconEntries().Select(row => row.Title).ToArray()).ConfigureAwait(false);
        var result = BloatwareService.RecommendedApps.ToDictionary(a => a.WingetId,
            a => ids.Contains(a.WingetId) || titles.Any(t => AppIconService.MatchesInstalledTitle(t, a.Title))
                ? (bool?)true : complete ? false : null, StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            if (generation == _generation) { _cached = result; _cachedAt = Environment.TickCount64; _cachedSoftwareEpoch = epoch; }
        }
        return result;
    }

    internal static (HashSet<string> Ids, bool Complete) ParseInventory(string output, bool success)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!success) return (ids, false);
        var lines = Regex.Replace(output, @"\x1B\[[0-9;]*[a-zA-Z]", "").Split('\n')
            .Select(l => l.TrimEnd('\r')).ToArray();
        // Fail closed on localized or malformed tables rather than guessing column positions.
        var header = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*Name\s+Id\s+Version(?:\s|$)"));
        if (header < 0 || header + 1 >= lines.Length ||
            !Regex.IsMatch(lines[header + 1], @"^\s*-{3,}\s*$")) return (ids, false);
        var idStart = lines[header].IndexOf("Id", StringComparison.Ordinal);
        var versionStart = lines[header].IndexOf("Version", StringComparison.Ordinal);
        if (idStart < 0 || versionStart <= idStart) return (ids, false);
        bool complete = true;
        for (int i = header + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length <= idStart) { complete = false; continue; }
            var id = line.Substring(idStart, Math.Min(versionStart - idStart, line.Length - idStart)).Trim();
            if (id.Length == 0 || id.Contains('…') || id.EndsWith("...", StringComparison.Ordinal))
            { complete = false; continue; }
            ids.Add(id);
        }
        return (ids, complete);
    }
}
