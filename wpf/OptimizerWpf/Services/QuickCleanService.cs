using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    public record QuickCleanItem(string Key, string Label, long SizeBytes, bool Recommended);

    // ΝΕΟ - ρητό αίτημα χρήστη: "θέλω αυτόματο καθαρισμό συστήματος στο Home όπως στο PC Manager,
    // έλεγξε ποιες λειτουργίες ενσωματώνει αυτό το εργαλείο γρήγορου καθαρισμού εκτός από temp αρχεία".
    // Έρευνα (WebSearch, βλ. roadmap #research) έδειξε ότι το Microsoft PC Manager's Quick/Deep Clean
    // καλύπτει: temp αρχεία, Κάδο Ανακύκλωσης, cache μικρογραφιών, cache Windows Update, αρχεία
    // Delivery Optimization, cache shader κάρτας γραφικών, και cache περιηγητών - όλα τα παρακάτω.
    public static class QuickCleanService
    {
        private static string SystemRoot => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static string WindowsTempDir => Path.Combine(SystemRoot, "Temp");
        private static string ExplorerCacheDir => Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
        private static string WindowsUpdateCacheDir => Path.Combine(SystemRoot, "SoftwareDistribution", "Download");
        private static string DeliveryOptimizationDir => Path.Combine(SystemRoot, "SoftwareDistribution", "DeliveryOptimization");

        private static IEnumerable<string> ShaderCacheDirs() => new[]
        {
            Path.Combine(LocalAppData, "D3DSCache"),
            Path.Combine(LocalAppData, "NVIDIA", "DXCache"),
            Path.Combine(LocalAppData, "NVIDIA", "GLCache"),
            Path.Combine(LocalAppData, "AMD", "DxCache"),
            Path.Combine(LocalAppData, "AMD", "GLCache"),
        };

        // Πρόταση χρήστη: "καθαρισμός cache launcher παιχνιδιών" - ΜΟΝΟ τεκμηριωμένα, καθαρά
        // HTTP/web-cache φάκελοι (σαν cache browser - αναδημιουργούνται μόνοι τους, καμία απώλεια
        // δεδομένων/προόδου) - ΟΧΙ "depotcache"/"downloading" του Steam, που κρατούν delta-patch
        // δεδομένα ή ενεργές λήψεις.
        private static IEnumerable<string> LauncherCacheDirs()
        {
            var dirs = new List<string>();
            if (GameLibraryService.SteamRoot() is { } steam) dirs.Add(Path.Combine(steam, "appcache", "httpcache"));
            dirs.Add(Path.Combine(LocalAppData, "EpicGamesLauncher", "Saved", "webcache"));
            dirs.Add(Path.Combine(LocalAppData, "EpicGamesLauncher", "Saved", "webcache_4147"));
            return dirs;
        }

        internal sealed record CleanupRule(string Category, string Root, TimeSpan MinAge, bool Recursive = true, string Pattern = "*");

        private static readonly string[] Categories = {
            "UserTemp", "ThumbnailCache", "WindowsTemp", "RecycleBin", "WindowsUpdateCache",
            "DeliveryOptimization", "ShaderCache", "LauncherCache", "BrowserCache"
        };

        private static bool Recommended(string category) => category is "UserTemp" or "ThumbnailCache";

        // One rule catalog for both estimation and execution. Earlier categories own
        // overlap; selecting a later category cannot silently expand its scan estimate.
        private static IEnumerable<CleanupRule> RulesFor(string category)
        {
            var age = TimeSpan.FromDays(2);
            switch (category)
            {
                case "UserTemp":
                    if (MaintenancePaths.VerifiedUserTemp() is string temp) yield return new(category, temp, age);
                    break;
                case "ThumbnailCache":
                    yield return new(category, ExplorerCacheDir, age, false, "thumbcache_*.db");
                    yield return new(category, ExplorerCacheDir, age, false, "iconcache_*.db");
                    break;
                case "WindowsTemp": yield return new(category, WindowsTempDir, TimeSpan.FromDays(7)); break;
                case "WindowsUpdateCache": yield return new(category, WindowsUpdateCacheDir, TimeSpan.FromDays(7)); break;
                case "DeliveryOptimization": yield return new(category, DeliveryOptimizationDir, TimeSpan.FromDays(7)); break;
                case "ShaderCache":
                    foreach (var root in ShaderCacheDirs()) yield return new(category, root, age);
                    break;
                case "LauncherCache":
                    foreach (var root in LauncherCacheDirs()) yield return new(category, root, age);
                    break;
                case "BrowserCache":
                    foreach (var root in HealthCleanupService.AllBrowserCacheDirectories(HealthCleanupService.DetectBrowsers()))
                        yield return new(category, root, age);
                    break;
            }
        }

        internal static bool OwnsFile(CleanupRule rule, string file, DateTime modifiedUtc, DateTime nowUtc)
        {
            if (modifiedUtc > nowUtc - rule.MinAge || !MaintenancePaths.IsWithin(rule.Root, file)) return false;
            if (!rule.Recursive && !string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Root)), Path.GetDirectoryName(Path.GetFullPath(file)),
                StringComparison.OrdinalIgnoreCase)) return false;
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(rule.Pattern, Path.GetFileName(file), ignoreCase: true);
        }

        private static bool Overlaps(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || MaintenancePaths.IsWithin(a, b) || MaintenancePaths.IsWithin(b, a);

        internal static bool CoversRule(CleanupRule earlier, CleanupRule later) =>
            earlier.MinAge <= later.MinAge && (earlier.Pattern == "*" ||
                string.Equals(earlier.Pattern, later.Pattern, StringComparison.OrdinalIgnoreCase)) &&
            (earlier.Recursive
                ? string.Equals(earlier.Root, later.Root, StringComparison.OrdinalIgnoreCase) || MaintenancePaths.IsWithin(earlier.Root, later.Root)
                : !later.Recursive && string.Equals(earlier.Root, later.Root, StringComparison.OrdinalIgnoreCase));

        private static Dictionary<string, long> RunPlan(HashSet<string> selected, bool clean)
        {
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            var prior = new List<CleanupRule>();
            var now = DateTime.UtcNow;
            int last = Array.FindLastIndex(Categories, selected.Contains);
            for (int i = 0; i <= last; i++)
            {
                string category = Categories[i];
                bool run = selected.Contains(category);
                long bytes = 0;
                if (category == "RecycleBin" && run)
                {
                    var before = RecycleBinSize();
                    if (clean) { EmptyRecycleBin(); bytes = Math.Max(0, before - RecycleBinSize()); }
                    else bytes = before;
                }
                foreach (var raw in RulesFor(category))
                {
                    if (!Path.IsPathFullyQualified(raw.Root) || !MaintenancePaths.IsOrdinaryLocalPath(raw.Root) ||
                        MaintenancePaths.IsProtectedMaintenancePath(raw.Root) || !Directory.Exists(raw.Root)) continue;
                    var rule = raw with { Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw.Root)) };
                    if (prior.Any(earlier => CoversRule(earlier, rule))) continue;
                    if (run)
                    {
                        var overlapping = prior.Where(earlier => Overlaps(earlier.Root, rule.Root)).ToArray();
                        Func<string, bool>? exclude = overlapping.Length == 0 ? null : file =>
                        {
                            var modified = File.GetLastWriteTimeUtc(file);
                            return overlapping.Any(earlier => OwnsFile(earlier, file, modified, now));
                        };
                        bytes += clean
                            ? MaintenancePaths.CleanFiles(rule.Root, rule.MinAge, rule.Recursive, rule.Pattern, exclude, now)
                            : MaintenancePaths.MeasureFiles(rule.Root, rule.MinAge, rule.Recursive, rule.Pattern, exclude, now);
                    }
                    prior.Add(rule);
                }
                if (run) totals[category] = bytes;
            }
            return totals;
        }

        public static Task<IReadOnlyList<QuickCleanItem>> ScanAsync() => ScanCoreAsync(false);
        internal static Task<IReadOnlyList<QuickCleanItem>> ScanRecommendedAsync() => ScanCoreAsync(true);
        private static Task<IReadOnlyList<QuickCleanItem>> ScanCoreAsync(bool recommendedOnly) => GearWinCore.RunAsync(CoreWork.CleanupScan, () => Task.Run(() =>
        {
            var selected = Categories.Where(category => !recommendedOnly || Recommended(category)).ToHashSet(StringComparer.Ordinal);
            var totals = RunPlan(selected, clean: false);
            return (IReadOnlyList<QuickCleanItem>)Categories.Where(selected.Contains)
                .Select(category => new QuickCleanItem(category, LanguageService.T("QuickClean_" + category), totals[category], Recommended(category))).ToArray();
        }));

        // The weekly task bypasses the GUI's single-instance mutex. Use a user-specific
        // mutex spanning processes in the interactive session. Acquire/release on the same thread.
        internal static Task<long> RunExclusiveAsync(Func<long> action, CorePriority priority = CorePriority.User) => GearWinCore.RunAsync(CoreWork.Cleanup, () => Task.Run(() =>
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new IOException("Cannot identify maintenance owner.");
            using var mutex = new System.Threading.Mutex(false, @"Local\GearWin.Cleanup." + sid);
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (System.Threading.AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new TimeoutException("Another maintenance operation is active.");
                return action();
            }
            finally
            {
                if (acquired) { HealthScoreService.Invalidate(); mutex.ReleaseMutex(); }
            }
        }), priority);

        public static Task<long> CleanAsync(IReadOnlyList<string> keys)
        {
            var selected = keys.Distinct(StringComparer.Ordinal).ToArray();
            return RunExclusiveAsync(() => CleanCore(selected));
        }

        private static long CleanCore(IReadOnlyList<string> keys) =>
            RunPlan(keys.ToHashSet(StringComparer.Ordinal), clean: true).Values.Sum();

        // ===== Κάδος Ανακύκλωσης μέσω shell32.dll - καμία διαθέσιμη διαχειριζόμενη API για
        // μέγεθος/άδειασμα, ίδιο μοτίβο P/Invoke με τα ήδη υπάρχοντα Win32 calls της εφαρμογής. =====
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        private static long RecycleBinSize()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }

        private static void EmptyRecycleBin() =>
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return $"{size:0.#} {units[unit]}";
        }
    }
}
