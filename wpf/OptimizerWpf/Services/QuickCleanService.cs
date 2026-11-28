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

        public static Task<IReadOnlyList<QuickCleanItem>> ScanAsync() => ScanCoreAsync(false);
        internal static Task<IReadOnlyList<QuickCleanItem>> ScanRecommendedAsync() => ScanCoreAsync(true);
        private static Task<IReadOnlyList<QuickCleanItem>> ScanCoreAsync(bool recommendedOnly) => GearWinCore.RunAsync(CoreWork.CleanupScan, () => Task.Run(() =>
        {
            var items = new List<QuickCleanItem>
            {
                new("UserTemp", LanguageService.T("QuickClean_UserTemp"), MaintenancePaths.VerifiedUserTemp() is string temp ? MaintenancePaths.MeasureFiles(temp, TimeSpan.FromDays(2)) : 0, true),
                new("ThumbnailCache", LanguageService.T("QuickClean_ThumbnailCache"), ThumbCacheSize(), true),
            };
            if (!recommendedOnly) items.AddRange(new QuickCleanItem[]
            {
                new("WindowsTemp", LanguageService.T("QuickClean_WindowsTemp"), MaintenancePaths.MeasureFiles(WindowsTempDir, TimeSpan.FromDays(7)), false),
                new("RecycleBin", LanguageService.T("QuickClean_RecycleBin"), RecycleBinSize(), false),
                new("WindowsUpdateCache", LanguageService.T("QuickClean_WindowsUpdateCache"), MaintenancePaths.MeasureFiles(WindowsUpdateCacheDir, TimeSpan.FromDays(7)), false),
                new("DeliveryOptimization", LanguageService.T("QuickClean_DeliveryOptimization"), MaintenancePaths.MeasureFiles(DeliveryOptimizationDir, TimeSpan.FromDays(7)), false),
                new("ShaderCache", LanguageService.T("QuickClean_ShaderCache"), ShaderCacheDirs().Sum(dir => MaintenancePaths.MeasureFiles(dir, TimeSpan.FromDays(2))), false),
                new("LauncherCache", LanguageService.T("QuickClean_LauncherCache"), LauncherCacheDirs().Sum(dir => MaintenancePaths.MeasureFiles(dir, TimeSpan.FromDays(2))), false),
                new("BrowserCache", LanguageService.T("QuickClean_BrowserCache"), HealthCleanupService.AllBrowserCacheDirectories(HealthCleanupService.DetectBrowsers()).Sum(path => MaintenancePaths.MeasureFiles(path, TimeSpan.FromDays(2))), false),
            });
            return (IReadOnlyList<QuickCleanItem>)items;
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

        private static long CleanCore(IReadOnlyList<string> keys)
        {
            long freed = 0;
            foreach (var key in keys)
            {
                switch (key)
                {
                    case "UserTemp":
                        if (MaintenancePaths.VerifiedUserTemp() is string temp)
                            freed += MaintenancePaths.CleanFiles(temp, TimeSpan.FromDays(2));
                        break;
                    case "WindowsTemp":
                        freed += MaintenancePaths.CleanFiles(WindowsTempDir, TimeSpan.FromDays(7));
                        break;
                    case "RecycleBin":
                        var before = RecycleBinSize();
                        EmptyRecycleBin();
                        freed += Math.Max(0, before - RecycleBinSize());
                        break;
                    case "ThumbnailCache":
                        freed += MaintenancePaths.CleanFiles(ExplorerCacheDir, TimeSpan.FromDays(2), false, "thumbcache_*.db") +
                            MaintenancePaths.CleanFiles(ExplorerCacheDir, TimeSpan.FromDays(2), false, "iconcache_*.db");
                        break;
                    case "WindowsUpdateCache":
                        freed += MaintenancePaths.CleanFiles(WindowsUpdateCacheDir, TimeSpan.FromDays(7));
                        break;
                    case "DeliveryOptimization":
                        freed += MaintenancePaths.CleanFiles(DeliveryOptimizationDir, TimeSpan.FromDays(7));
                        break;
                    case "ShaderCache":
                        foreach (var dir in ShaderCacheDirs())
                        {
                            freed += MaintenancePaths.CleanFiles(dir, TimeSpan.FromDays(2));
                        }
                        break;
                    case "LauncherCache":
                        foreach (var dir in LauncherCacheDirs())
                        {
                            freed += MaintenancePaths.CleanFiles(dir, TimeSpan.FromDays(2));
                        }
                        break;
                    case "BrowserCache":
                        foreach (var path in HealthCleanupService.AllBrowserCacheDirectories(HealthCleanupService.DetectBrowsers()))
                            freed += MaintenancePaths.CleanFiles(path, TimeSpan.FromDays(2));
                        break;
                }
            }
            return freed;
        }

        // ΔΙΟΡΘΩΣΗ (εξονυχιστικός έλεγχος εντόπισε): Directory.EnumerateFiles(dir, "*",
        // SearchOption.AllDirectories) είναι ΤΕΜΠΕΛΗΣ (lazy) - το UnauthorizedAccessException από έναν
        // απαγορευμένο υποφάκελο (π.χ. AppContainer temp folders μέσα στο C:\Windows\Temp, ακόμα και με
        // Administrator) πετάγεται ΜΕΣΑ στο foreach, ΟΧΙ στην αρχική κλήση - το try/catch γύρω από την
        // κλήση δεν το έπιανε καθόλου, με αποτέλεσμα η σάρωση να "κολλάει" σιωπηλά για πάντα (καμία
        // εξαίρεση, καμία ενημέρωση UI, βλ. LoadQuickCleanAsync). Αναδρομικός, ΑΝΑ-ΦΑΚΕΛΟ προστατευμένος
        // περίπατος αντί για αυτό - ένας απρόσβατος υποφάκελος παραλείπεται (συνεισφέρει 0), η σάρωση
        // συνεχίζει κανονικά στους υπόλοιπους.
        internal static long DirSize(string dir)
        {
            if (!MaintenancePaths.IsOrdinaryLocalPath(dir) || !Directory.Exists(dir)) return 0;
            long total = 0;

            string[] files;
            try { files = Directory.GetFiles(dir); } catch { files = Array.Empty<string>(); }
            foreach (var f in files)
            {
                try { if (MaintenancePaths.IsOrdinaryLocalPath(f)) total += new FileInfo(f).Length; } catch { }
            }

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); } catch { subdirs = Array.Empty<string>(); }
            foreach (var d in subdirs) total += DirSize(d);

            return total;
        }

        private static long ThumbCacheSize() =>
            MaintenancePaths.MeasureFiles(ExplorerCacheDir, TimeSpan.FromDays(2), false, "thumbcache_*.db") +
            MaintenancePaths.MeasureFiles(ExplorerCacheDir, TimeSpan.FromDays(2), false, "iconcache_*.db");

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
