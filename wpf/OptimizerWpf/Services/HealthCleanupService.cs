using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OptimizerWpf.Services
{
    public record RegistryFinding(string Category, string Display, string RegPath, string? ValueName);
    public record BrowserCacheEntry(string Name, string Label, IReadOnlyList<string> Paths);
    public record WinREStatus(bool Success, bool Enabled);
    public record RegistryBackupEntry(string FileName, string FullPath, DateTime CreatedAt, long SizeBytes);

    // Port του Optimizer.ps1's Υγεία & Συντήρηση καρτέλα (~13300-13792): Registry Cleaner (ΣΚΟΠΙΜΑ
    // στενός/ασφαλής - μόνο 2 κατηγορίες), Καθαρισμός Cache Περιηγητών, WinRE.
    public static class HealthCleanupService
    {
        // Ίδιες προστατευμένες διαδρομές με το ps1 original - ΠΟΤΕ δεν προτείνονται προς διαγραφή.
        private static readonly string[] ProtectedFolders =
        {
            "System32", "SysWOW64", "Sysnative", "WinSxS", "servicing", "SystemApps", "Microsoft.NET", "Fonts",
        };

        public static Task<IReadOnlyList<RegistryFinding>> ScanRegistryAsync() => Task.Run(ScanRegistry);

        private static IReadOnlyList<RegistryFinding> ScanRegistry()
        {
            var findings = new List<RegistryFinding>();
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            var protectedPaths = ProtectedFolders.Select(f => Path.Combine(systemRoot, f)).ToArray();

            foreach (var (hive, subPath) in new[]
                     {
                         (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                         (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                         (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     })
            {
                using var uninstallKey = hive.OpenSubKey(subPath);
                if (uninstallKey == null) continue;
                foreach (var name in uninstallKey.GetSubKeyNames())
                {
                    using var sub = uninstallKey.OpenSubKey(name);
                    if (sub == null) continue;
                    var displayName = sub.GetValue("DisplayName") as string;
                    var uninstallString = sub.GetValue("UninstallString") as string;
                    if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(uninstallString)) continue;
                    if (sub.GetValue("SystemComponent") != null || sub.GetValue("WindowsInstaller") != null || sub.GetValue("NoRemove") != null) continue;

                    var exePath = GetExePath(uninstallString);
                    if (exePath == null || IsProtected(exePath, protectedPaths) || !IsConfirmedMissingLocalPath(exePath)) continue;

                    var fullRegPath = $"{HiveName(hive)}\\{subPath}\\{name}";
                    findings.Add(new RegistryFinding("MissingUninstaller", displayName, fullRegPath, null));
                }
            }

            const string muiKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            using var mui = Registry.CurrentUser.OpenSubKey(muiKey);
            if (mui != null)
            {
                foreach (var valueName in mui.GetValueNames())
                {
                    if (valueName.StartsWith("PS", StringComparison.Ordinal)) continue;
                    var cleanPath = System.Text.RegularExpressions.Regex.Replace(valueName, @"\.(FriendlyAppName|ApplicationCompany)$", "");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(cleanPath, @"^[a-zA-Z]:\\")) continue;
                    if (!IsConfirmedMissingLocalPath(cleanPath)) continue;
                    findings.Add(new RegistryFinding("ObsoleteMuiCache", cleanPath, $"HKCU\\{muiKey}", valueName));
                }
            }

            // ΝΕΟ - ρητό αίτημα χρήστη ("ενσωμάτωσε τον καθαρισμό registry" ως λειτουργία): 2 ακόμα
            // ΑΣΦΑΛΕΙΣ κατηγορίες (ίδιο αυστηρό κριτήριο με τα παραπάνω - flag ΜΟΝΟ όταν το exe-path
            // που δείχνει η καταχώρηση δεν υπάρχει πια στον δίσκο, ΠΟΤΕ heuristic/μαντεψιά).
            // App Paths: HKLM/HKCU ...\App Paths\<exe> - η (Default) τιμή είναι η πλήρης διαδρομή που
            // χρησιμοποιεί ο Windows Explorer/Run dialog για να εντοπίσει το exe με βάση μόνο το όνομά
            // του· μένει πίσω σαν "ορφανή" καταχώρηση μετά από απεγκατάσταση που δεν καθάρισε το κλειδί.
            foreach (var (hive, subPath) in new[]
                     {
                         (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                         (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                     })
            {
                using var appPathsKey = hive.OpenSubKey(subPath);
                if (appPathsKey == null) continue;
                foreach (var name in appPathsKey.GetSubKeyNames())
                {
                    using var sub = appPathsKey.OpenSubKey(name);
                    var exePath = sub?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(exePath) || IsProtected(exePath, protectedPaths) || !IsConfirmedMissingLocalPath(exePath)) continue;
                    findings.Add(new RegistryFinding("OrphanedAppPath", exePath, $"{HiveName(hive)}\\{subPath}\\{name}", null));
                }
            }

            // Καταχωρήσεις αυτόματης εκκίνησης (Run) που δείχνουν σε exe που πια δεν υπάρχει - το ίδιο
            // "σκουπίδι μητρώου" που αφήνει πίσω μια απεγκατάσταση/μετακίνηση προγράμματος, ΔΕΝ αγγίζει
            // καμία τρέχουσα λειτουργική καταχώρηση εκκίνησης (ίδιο κριτήριο Exists() με τα παραπάνω).
            foreach (var (hive, subPath) in new[]
                     {
                         (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                         (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                     })
            {
                using var runKey = hive.OpenSubKey(subPath);
                if (runKey == null) continue;
                foreach (var valueName in runKey.GetValueNames())
                {
                    var command = runKey.GetValue(valueName) as string;
                    if (string.IsNullOrWhiteSpace(command)) continue;
                    var exePath = GetExePath(command);
                    if (exePath == null || IsProtected(exePath, protectedPaths) || !IsConfirmedMissingLocalPath(exePath)) continue;
                    findings.Add(new RegistryFinding("OrphanedStartupEntry", valueName, $"{HiveName(hive)}\\{subPath}", valueName));
                }
            }

            // ΝΕΟ - roadmap "Registry Cleaner - περισσότερες ασφαλείς κατηγορίες": Shared DLLs
            // (HKLM ...\SharedDLLs) - κάθε τιμή είναι η πλήρης διαδρομή ενός DLL που καταμετράει
            // αναφορές (reference count) πολλαπλών εγκατεστημένων προγραμμάτων· ένα leftover μετά από
            // ατελή απεγκατάσταση δείχνει σε DLL που πια δεν υπάρχει - ίδιο αυστηρό κριτήριο Exists()
            // με τις υπόλοιπες κατηγορίες.
            using (var sharedDlls = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"))
            {
                if (sharedDlls != null)
                {
                    foreach (var valueName in sharedDlls.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(valueName) || IsProtected(valueName, protectedPaths) || !IsConfirmedMissingLocalPath(valueName)) continue;
                        findings.Add(new RegistryFinding("OrphanedSharedDll", valueName,
                            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs", valueName));
                    }
                }
            }

            return findings;
        }

        // Only low-risk display-name cache values are eligible for automatic deletion.
        // Uninstall trees, App Paths, startup and SharedDLL entries remain visible for review.
        private const string MuiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        private static readonly object RegistryGate = new();
        private sealed record MuiBackup(string Name, string Value);

        internal static bool CanDeleteFinding(RegistryFinding f) =>
            f.Category == "ObsoleteMuiCache" &&
            string.Equals(f.RegPath, "HKCU\\" + MuiPath, StringComparison.OrdinalIgnoreCase) &&
            f.ValueName is string name && name.Length > 0 && name.Length <= 16383 &&
            System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z]:\\.+\.(FriendlyAppName|ApplicationCompany)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static string RegistryBackupsDir
        {
            get
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var sid = identity.User?.Value ?? throw new IOException("Cannot identify user.");
                return Path.Combine(SecureStorage.RootDir, "RegistryBackups", sid);
            }
        }

        private static string EnsureBackupDirectory()
        {
            var root = SecureStorage.RootDir;
            if (Directory.Exists(root) && !MaintenancePaths.IsOrdinaryLocalPath(root)) throw new IOException("Unsafe backup root.");
            SecureStorage.EnsureRoot();
            var dir = RegistryBackupsDir;
            foreach (var item in new[] { Path.GetDirectoryName(dir)!, dir })
            {
                if (Directory.Exists(item) && !MaintenancePaths.IsOrdinaryLocalPath(item)) throw new IOException("Unsafe backup directory.");
                SecureStorage.SecureDirectory(item);
                if (!SecureStorage.IsDirectoryAdminOnlyWritable(item)) throw new IOException("Backup directory is not protected.");
            }
            return dir;
        }

        public static Task<IReadOnlyList<RegistryBackupEntry>> ListRegistryBackupsAsync() => Task.Run(() =>
        {
            var dir = RegistryBackupsDir;
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "RegistryBackups");
            var list = new List<RegistryBackupEntry>();
            foreach (var (folder, pattern) in new[] { (dir, "*.json"), (legacy, "*.reg") })
            {
                if (!MaintenancePaths.IsOrdinaryLocalPath(folder)) continue;
                foreach (var path in Directory.GetFiles(folder, pattern))
                {
                    if (!MaintenancePaths.IsOrdinaryLocalPath(path)) continue;
                    var f = new FileInfo(path);
                    list.Add(new RegistryBackupEntry(f.Name, f.FullName, f.LastWriteTime, f.Length));
                }
            }
            return (IReadOnlyList<RegistryBackupEntry>)list.OrderByDescending(f => f.CreatedAt).ToList();
        });

        internal static bool CanRestoreBackup(string path) =>
            string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

        public static Task<bool> RestoreRegistryBackupAsync(string filePath) => Task.Run(() =>
        {
            lock (RegistryGate)
            {
                try
                {
                    var dir = EnsureBackupDirectory();
                    var full = Path.GetFullPath(filePath);
                    if (!CanRestoreBackup(full) || !string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase) ||
                        !MaintenancePaths.IsOrdinaryLocalPath(full) || new FileInfo(full).Length > 1024 * 1024) return false;
                    var record = System.Text.Json.JsonSerializer.Deserialize<MuiBackup>(File.ReadAllText(full));
                    if (record == null || !CanDeleteFinding(new RegistryFinding("ObsoleteMuiCache", "", "HKCU\\" + MuiPath, record.Name))) return false;
                    using var key = Registry.CurrentUser.CreateSubKey(MuiPath);
                    if (key == null) return false;
                    // Never overwrite a value recreated by Windows or another application.
                    var existing = key.GetValue(record.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (existing != null) return existing is string old && old == record.Value && key.GetValueKind(record.Name) == RegistryValueKind.String;
                    key.SetValue(record.Name, record.Value, RegistryValueKind.String);
                    return key.GetValue(record.Name) is string restored && restored == record.Value;
                }
                catch { return false; }
            }
        });

        public static Task<bool> DeleteFindingsAsync(IReadOnlyList<RegistryFinding> findings, string backupPath) =>
            Task.Run(() =>
            {
                lock (RegistryGate)
                {
                    try
                    {
                        if (findings.Count == 0 || findings.Any(f => !CanDeleteFinding(f))) return false;
                        var dir = EnsureBackupDirectory();
                        var current = ScanRegistry().ToHashSet();
                        if (findings.Any(f => !current.Contains(f))) return false;
                        using var key = Registry.CurrentUser.OpenSubKey(MuiPath, writable: true);
                        if (key == null) return false;
                        var staged = new List<MuiBackup>();
                        foreach (var f in findings.Distinct())
                        {
                            if (key.GetValue(f.ValueName!, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value ||
                                key.GetValueKind(f.ValueName!) != RegistryValueKind.String) return false;
                            var record = new MuiBackup(f.ValueName!, value);
                            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(record);
                            if (json.Length > 1024 * 1024) return false;
                            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
                            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                stream.Write(json);
                                stream.Flush(flushToDisk: true);
                            }
                            if (System.Text.Json.JsonSerializer.Deserialize<MuiBackup>(File.ReadAllBytes(path)) != record) return false;
                            staged.Add(record);
                        }
                        // Recheck identity immediately before each deletion. A partial failure is
                        // reported as such, with every affected value backed up independently.
                        foreach (var record in staged)
                        {
                            var target = System.Text.RegularExpressions.Regex.Replace(record.Name, @"\.(FriendlyAppName|ApplicationCompany)$", "",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (!IsConfirmedMissingLocalPath(target) || key.GetValue(record.Name, null) is not string value ||
                                value != record.Value || key.GetValueKind(record.Name) != RegistryValueKind.String) return false;
                            key.DeleteValue(record.Name, throwOnMissingValue: true);
                        }
                        return true;
                    }
                    catch { return false; }
                }
            });

        // Μόνο browsers που πραγματικά ανιχνεύτηκαν στο σύστημα (ο φάκελος User Data/Profiles τους
        // υπάρχει) εμφανίζονται - ίδια συμπεριφορά με το ps1's δυναμικό $browserCacheCatalog.
        public static IReadOnlyList<BrowserCacheEntry> DetectBrowsers()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new (string Name, string Label, string BaseDir, string CachePattern)[]
            {
                ("Chrome", "Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"), "Cache"),
                ("Edge", "Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"), "Cache"),
                ("Brave", "Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"), "Cache"),
                ("Opera", "Opera", Path.Combine(appData, "Opera Software", "Opera Stable"), "Cache"),
                // ΝΕΟ - ROADMAP.md REQ-570-05 (ρητό αίτημα χρήστη: "υποστήριξη όλων των υπαρχόντων
                // browsers") - Opera GX είναι μια ξεχωριστή εγκατάσταση/φάκελος προφίλ από την κανονική
                // Opera (πολύ δημοφιλής παραλλαγή για gaming - ταιριάζει και θεματικά με το Gaming Mode
                // της εφαρμογής) - ίδια ακριβώς μεταχείριση με το ήδη υπάρχον Opera παραπάνω (ίδια
                // Chromium-derived δομή, απλώς άλλος φάκελος "Opera GX Stable").
                ("OperaGX", "Opera GX", Path.Combine(appData, "Opera Software", "Opera GX Stable"), "Cache"),
                // ΔΙΟΡΘΩΣΗ (εξονυχιστικός έλεγχος εντόπισε): έλειπε εντελώς το Vivaldi - το ps1 original
                // το ανιχνεύει (ίδιο μοτίβο με Chrome/Edge/Brave, ίδια Chromium-based δομή προφίλ).
                ("Vivaldi", "Vivaldi", Path.Combine(localAppData, "Vivaldi", "User Data"), "Cache"),
                ("Firefox", "Mozilla Firefox", Path.Combine(appData, "Mozilla", "Firefox", "Profiles"), "cache2"),
            };

            var result = new List<BrowserCacheEntry>();
            foreach (var c in candidates)
            {
                if (!Directory.Exists(c.BaseDir)) continue;
                // ΔΙΟΡΘΩΣΗ (εξονυχιστικός έλεγχος εντόπισε): το Firefox χωρίζει τα δεδομένα προφίλ - το
                // ΙΔΙΟ το προφίλ (bookmarks/prefs) είναι στο Roaming (%APPDATA%), αλλά το ΙΔΙΟ το cache2
                // είναι ΠΑΝΤΑ στο Local (%LOCALAPPDATA%) με το ΙΔΙΟ όνομα υποφακέλου προφίλ - πριν, ο
                // κώδικας έψαχνε cache2 ΜΕΣΑ στο Roaming path (όπου δεν υπάρχει ποτέ), άρα ο καθαρισμός
                // "πετύχαινε" σιωπηλά χωρίς να διαγράφει ποτέ τίποτα πραγματικό.
                var paths = c.Name == "Firefox"
                    ? SafeEnumerateDirectories(c.BaseDir).Select(p => Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles", Path.GetFileName(p), c.CachePattern)).ToList()
                    : new List<string> { c.BaseDir }; // Chrome/Edge/Brave/Vivaldi: κάθε προφίλ υποφάκελος μέσα εδώ, το caller σαρώνει *\Cache
                result.Add(new BrowserCacheEntry(c.Name, c.Label, paths));
            }
            return result;
        }

        // ΝΕΟ - ρητό αίτημα χρήστη: το μέγεθος cache περιηγητών να συνυπολογίζεται στον Πλήρη Έλεγχο
        // Υγείας (HealthCheckWindow's στάδιο "Καθαρισμός Χώρου"), ΧΩΡΙΣ να αγγίξει καθόλου την ήδη
        // υπάρχουσα, ξεχωριστή ανά-περιηγητή λίστα/κουμπιά στην καρτέλα Υγεία & Συντήρηση (ρητή
        // οδηγία χρήστη: "άσε τα as they are") - μόνο ανάγνωση μεγέθους εδώ, επαναχρησιμοποιεί το ίδιο
        // DetectBrowsers() ώστε να μετρά ΑΚΡΙΒΩΣ τους ίδιους φακέλους που θα καθάριζε το υπάρχον κουμπί.
        public static Task<long> ScanBrowserCacheSizeAsync() => Task.Run(() =>
            DetectBrowsers().Sum(BrowserCacheSize));

        internal static long BrowserCacheSize(BrowserCacheEntry browser) =>
            BrowserCacheDirectories(browser).Distinct(StringComparer.OrdinalIgnoreCase)
                .Sum(path => MaintenancePaths.MeasureFiles(path, TimeSpan.FromDays(2)));

        internal static IEnumerable<string> BrowserCacheDirectories(BrowserCacheEntry browser)
        {
            foreach (var basePath in browser.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!MaintenancePaths.IsOrdinaryLocalPath(basePath)) continue;
                if (browser.Name is "Chrome" or "Edge" or "Brave" or "Vivaldi")
                {
                    foreach (var profileDir in SafeEnumerateDirectories(basePath))
                        foreach (var cacheSub in new[] { "Cache", "Code Cache", "GPUCache" })
                            yield return Path.Combine(profileDir, cacheSub);
                }
                else if (browser.Name is "Opera" or "OperaGX")
                {
                    // Opera paths are profile roots, NEVER cache roots. Only named cache
                    // subdirectories can be touched; bookmarks, cookies, logins stay intact.
                    foreach (var cacheSub in new[] { "Cache", "Code Cache", "GPUCache" })
                        yield return Path.Combine(basePath, cacheSub);
                }
                else if (browser.Name == "Firefox" &&
                    string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(basePath)), "cache2", StringComparison.OrdinalIgnoreCase))
                    yield return basePath;
                // Unknown browsers: fail closed.
            }
        }

        public static Task<long> ClearBrowserCacheAsync(BrowserCacheEntry browser) =>
            QuickCleanService.RunExclusiveAsync(() => ClearBrowserCacheCore(browser));

        internal static long ClearBrowserCacheCore(BrowserCacheEntry browser) =>
            BrowserCacheDirectories(browser).Distinct(StringComparer.OrdinalIgnoreCase)
                .Sum(path => MaintenancePaths.CleanFiles(path, TimeSpan.FromDays(2)));

        public static Task<WinREStatus> GetWinREStatusAsync() => Task.Run(() =>
        {
            try
            {
                var output = RunAndCapture("reagentc.exe", "/info");
                var statusLine = output.Split('\n').FirstOrDefault(l => l.Contains("Windows RE status"));
                var enabled = statusLine != null && statusLine.Contains("Enabled") && !statusLine.Contains("Disabled");
                return new WinREStatus(true, enabled);
            }
            catch
            {
                return new WinREStatus(false, false);
            }
        });

        public static Task<bool> EnableWinREAsync() => Task.Run(() => RunAndWait("reagentc.exe", "/enable") == 0);

        // ===== Εργαλεία Υγείας & Συντήρησης Συστήματος (ρητό αίτημα χρήστη: "τα εργαλεία υγείας
        // συντήρησης συστήματος δεν φαίνονται πουθενά" - ολόκληρη αυτή η ενότητα, port του ps1's
        // Add-HealthToolButton (~13302-13400), έλειπε εντελώς από το WPF port). Σε αντίθεση με το
        // ps1 original (ανοίγει ελαχιστοποιημένο console παράθυρο PowerShell, Invoke-DiagnosticCommand
        // ~63), εδώ η έξοδος καταγράφεται και εμφανίζεται σε MessageBox μέσα στην ίδια την εφαρμογή -
        // πιο καθαρό WPF-native αποτέλεσμα, καμία λειτουργική διαφορά.

        public static Task<(bool Success, string Output)> RunSfcScanAsync(System.Threading.CancellationToken ct = default) => RunCommandCaptureAsync("sfc.exe", "/scannow", ct: ct);
        public static Task<(bool Success, string Output)> RunDismCheckHealthAsync(System.Threading.CancellationToken ct = default) => RunCommandCaptureAsync("Dism.exe", "/Online /Cleanup-Image /CheckHealth", ct: ct);
        public static Task<(bool Success, string Output)> RunDismRestoreHealthAsync(System.Threading.CancellationToken ct = default) => RunCommandCaptureAsync("Dism.exe", "/Online /Cleanup-Image /RestoreHealth", ct: ct);
        public static Task<(bool Success, string Output)> RunChkdskAsync(System.Threading.CancellationToken ct = default) => RunCommandCaptureAsync("chkdsk.exe", "C: /f", stdin: "Y\r\n", ct: ct);
        public static Task<(bool Success, string Output)> RunWinSxsCleanupAsync(System.Threading.CancellationToken ct = default) => RunCommandCaptureAsync("Dism.exe", "/online /Cleanup-Image /StartComponentCleanup", ct: ct);

        // ΝΕΟ - roadmap "Διαγνωστική αναφορά με ένα κλικ" - συμπίεση βασικών logs/πληροφοριών σε ένα
        // .zip, έτοιμο να μοιραστεί όταν κάποιος ζητά βοήθεια. Μαζεύει systeminfo (ίδια πηγή με το ήδη
        // υπάρχον AdvancedToolsService.CreateSystemReport) + οι τελευταίες 50 καταχωρήσεις Application/
        // System Event Log (Error/Warning μόνο - όχι θόρυβος) + το ίδιο το in-app Ιστορικό Ενεργειών.
        public static async Task<string?> GenerateDiagnosticsZipAsync()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"OptimizerWpf_Diag_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var sysInfoPath = Path.Combine(tempDir, "systeminfo.txt");
                using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c systeminfo > \"{sysInfoPath}\"") { UseShellExecute = false, CreateNoWindow = true }))
                    if (p != null) await p.WaitForExitAsync();

                File.WriteAllText(Path.Combine(tempDir, "action_log.txt"), string.Join("\n", ActionLogService.Entries));

                var eventLogText = await Task.Run(() => CaptureRecentEventErrors());
                File.WriteAllText(Path.Combine(tempDir, "recent_event_errors.txt"), eventLogText);

                var zipPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"OptimizerWpf_Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, zipPath);
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
                return zipPath;
            }
            catch { return null; }
            finally { try { Directory.Delete(tempDir, true); } catch { } }
        }

        private static string CaptureRecentEventErrors()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var logName in new[] { "Application", "System" })
            {
                try
                {
                    var session = new System.Diagnostics.Eventing.Reader.EventLogQuery(logName, System.Diagnostics.Eventing.Reader.PathType.LogName,
                        "*[System[(Level=1 or Level=2)]]");
                    using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(session);
                    sb.AppendLine($"=== {logName} (τελευταία 25 Error/Warning) ===");
                    var count = 0;
                    for (var e = reader.ReadEvent(); e != null && count < 25; e = reader.ReadEvent())
                    {
                        sb.AppendLine($"[{e.TimeCreated:yyyy-MM-dd HH:mm}] {e.LevelDisplayName} - {e.ProviderName}: {e.FormatDescription()?.Split('\n').FirstOrDefault()}");
                        count++;
                    }
                    sb.AppendLine();
                }
                catch (Exception ex) { sb.AppendLine($"=== {logName}: {ex.Message} ===\n"); }
            }
            return sb.ToString();
        }

        // ΝΕΟ - roadmap "Αναφορά μπαταρίας" - powercfg /batteryreport είναι το επίσημο εργαλείο των
        // Windows (υγεία μπαταρίας, χωρητικότητα σχεδίασης έναντι πραγματικής) - η εφαρμογή απλώς
        // παράγει την αναφορά και την ανοίγει στον προεπιλεγμένο browser (ίδιο πνεύμα με το ήδη
        // υπάρχον AdvancedToolsService.CreateSystemReport). Σε desktop χωρίς μπαταρία το powercfg
        // επιστρέφει μη-μηδενικό κωδικό εξόδου - honest failure, καμία ψεύτικη αναφορά.
        public static async Task<bool> GenerateBatteryReportAsync()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OptimizerWpf_BatteryReport_{Guid.NewGuid():N}.html");
            var exit = await Task.Run(() => RunAndWait("powercfg.exe", $"/batteryreport /output \"{path}\""));
            if (exit != 0 || !System.IO.File.Exists(path)) return false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }

        public static Task<bool> OptimizeSsdRetrimAsync() => Task.Run(() =>
            RunAndWait("powershell.exe", "-NoProfile -Command \"Optimize-Volume -DriveLetter C -ReTrim -ErrorAction SilentlyContinue\"") == 0);

        // Port του ps1's inline broken-shortcut scan (~13374) - COM WScript.Shell δεν έχει καθαρό .NET
        // ισοδύναμο χωρίς πρόσθετη COM αναφορά, οπότε τρέχει μέσω σύντομου PowerShell script (ίδιο
        // μοτίβο με το DriverService.cs's RunPowerShellScriptAsync).
        public static async Task<int> FixBrokenShortcutsAsync()
        {
            const string script = @"
$shellCom = New-Object -ComObject WScript.Shell
$searchPaths = @(""$env:USERPROFILE\Desktop"", ""$env:APPDATA\Microsoft\Windows\Start Menu"") | Where-Object { Test-Path $_ }
$brokenCount = 0
Get-ChildItem -Path $searchPaths -Recurse -Include *.lnk -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $shortcut = $shellCom.CreateShortcut($_.FullName)
        # Never delete a keyboard shortcut; uncertain targets are not proof of a broken link.
        if ($shortcut.Hotkey) { return }
        $target = $shortcut.TargetPath
        if ($target -notmatch '^[A-Za-z]:\\' -or ($target -match '%')) { return }
        $drive = [IO.DriveInfo]::new($target.Substring(0,3))
        if (-not $drive.IsReady -or $drive.DriveType -ne [IO.DriveType]::Fixed) { return }
        # Until a per-item preview and restore flow is available, this tool is read-only.
        if (-not (Test-Path -LiteralPath $target -ErrorAction Stop)) { $brokenCount++ }
    } catch {}
}
Write-Output $brokenCount
";
            var output = await RunPowerShellScriptAsync(script);
            return int.TryParse(output.Trim(), out var count) ? count : 0;
        }

        // Απευθείας ανάγνωση μητρώου (ΟΧΙ shell-out σε PowerShell, πιο απλό/γρήγορο για μια απλή τιμή) -
        // port του ps1's .NET Framework version check (~13398).
        public static string GetDotNetFrameworkVersionInfo()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            var release = key?.GetValue("Release") as int?;
            return release switch
            {
                >= 528040 => $"{LanguageService.T("HealthSvc_DotNetInstalledPrefix")}{release}{LanguageService.T("HealthSvc_DotNet48Plus")}",
                >= 461808 => $"{LanguageService.T("HealthSvc_DotNetInstalledPrefix")}{release}{LanguageService.T("HealthSvc_DotNet472")}",
                not null => $"{LanguageService.T("HealthSvc_DotNetInstalledPrefix")}{release}{LanguageService.T("HealthSvc_DotNetOlder")}",
                null => LanguageService.T("HealthSvc_DotNetNotFound"),
            };
        }

        // ===== helpers =====

        internal static string? GetExePath(string uninstallString)
        {
            var s = Environment.ExpandEnvironmentVariables(uninstallString.Trim());
            if (s.StartsWith('"'))
            {
                var endQuote = s.IndexOf('"', 1);
                if (endQuote <= 1) return null;
                s = s[1..endQuote];
            }
            else
            {
                // An unquoted command with spaces is ambiguous: do not infer a missing executable.
                var match = System.Text.RegularExpressions.Regex.Match(s, @"^([A-Za-z]:\\[^\s""]+\.exe)(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success) return null;
                s = match.Groups[1].Value;
            }
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : null;
        }

        private static bool IsProtected(string path, string[] protectedPaths) =>
            protectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        internal static bool IsConfirmedMissingLocalPath(string path)
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(expanded, @"^[A-Za-z]:\\") || expanded.Contains('%')) return false;
                var full = Path.GetFullPath(expanded);
                var root = Path.GetPathRoot(full);
                if (root == null || root.Equals(full, StringComparison.OrdinalIgnoreCase)) return false;
                var drive = new DriveInfo(root);
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return false;
                // Walk ancestors before checking the final path. Never classify inaccessible or redirected paths as missing.
                var relative = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var current = root;
                foreach (var part in relative)
                {
                    current = Path.Combine(current, part);
                    try
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                    }
                    catch (FileNotFoundException) { return true; }
                    catch (DirectoryNotFoundException) { return true; }
                }
                return false;
            }
            catch { return false; }
        }

        private static string HiveName(RegistryKey hive) => hive == Registry.LocalMachine ? "HKLM" : "HKCU";

        private static (RegistryKey? Hive, string SubKey) SplitRegPath(string fullPath)
        {
            if (fullPath.StartsWith("HKLM\\")) return (Registry.LocalMachine, fullPath["HKLM\\".Length..]);
            if (fullPath.StartsWith("HKCU\\")) return (Registry.CurrentUser, fullPath["HKCU\\".Length..]);
            return (null, "");
        }

        private static string SafeFileName(string name) =>
            string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            try { return MaintenancePaths.IsOrdinaryLocalPath(root) ? Directory.GetDirectories(root) : Array.Empty<string>(); } catch { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeEnumerateFilesTopDown(string dir)
        {
            try { return Directory.EnumerateFiles(dir); } catch { return Array.Empty<string>(); }
        }

        private static int RunAndWait(string fileName, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true });
            process?.WaitForExit();
            return process?.ExitCode ?? -1;
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            });
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit();
            return output;
        }

        // Χρησιμοποιείται από τα εργαλεία υγείας (SFC/DISM/CHKDSK/WinSxS) - μπορεί να πάρει αρκετά
        // λεπτά (ειδικά DISM RestoreHealth), γι' αυτό Task.Run + καμία timeout εδώ (ο καλών code-behind
        // δείχνει StatusService.SetBusy όσο περιμένει).
        private static Task<(bool Success, string Output)> RunCommandCaptureAsync(string fileName, string arguments, string? stdin = null, System.Threading.CancellationToken ct = default) =>
            Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo(fileName, arguments)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = stdin != null,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                    };
                    using var process = Process.Start(psi);
                    if (process == null) return (false, LanguageService.T("HealthSvc_ProcessStartFailed"));
                    if (stdin != null)
                    {
                        process.StandardInput.Write(stdin);
                        process.StandardInput.Close();
                    }
                    // Ακύρωση χρήστη: τερματίζει ΟΛΟ το δέντρο διεργασιών (sfc/DISM ξεκινούν παιδιά) - το
                    // ReadToEnd επιστρέφει μόλις κλείσει το pipe.
                    using var cancelReg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (ct.IsCancellationRequested) return (false, LanguageService.T("Health_ToolCancelled"));
                    return (process.ExitCode == 0, output);
                }
                catch (Exception ex)
                {
                    return (false, $"{LanguageService.T("HealthSvc_ErrorPrefix")}{ex.Message}");
                }
            });

        private static async Task<string> RunPowerShellScriptAsync(string script)
        {
            // ΑΣΦΑΛΕΙΑ: -EncodedCommand αντί για script στο %TEMP% (βλ. PowerShellArgs).
            var scriptFile = PowerShellArgs.Fits(script) ? null : SecureStorage.NewPrivateTempFile("pshealth", ".ps1");
            if (scriptFile != null) File.WriteAllText(scriptFile, script);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = scriptFile == null ? PowerShellArgs.Command(script) : $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };
                using var process = Process.Start(psi);
                if (process == null) return "";
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            finally
            {
                if (scriptFile != null) { try { File.Delete(scriptFile); } catch { } }
            }
        }
    }
}
