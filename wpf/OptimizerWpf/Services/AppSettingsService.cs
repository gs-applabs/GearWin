using System;
using System.IO;
using System.Text.Json;

namespace OptimizerWpf.Services
{
    public class AppSettings
    {
        public bool SidebarEnabled { get; set; }
        public string SidebarPosition { get; set; } = "Right";
        public string ThemeName { get; set; } = "Windows 11 Fluent";
        public bool IsDarkMode { get; set; } = true;
        public bool AutoInstallDrivers { get; set; }
        // "Classic" | "Sidebar" | "HorizontalModern" - port του $global:appSettings.MenuMode
        // (Optimizer.ps1 ~792, προεπιλογή "HorizontalModern" εκεί επίσης).
        public string MenuMode { get; set; } = "HorizontalModern";
        public bool AnimatedBackgrounds { get; set; } = true;
        // "Theme" (κάθε θέμα το δικό του στυλ, βλ. ThemeBackgroundStyles) | "Gears" | "Orbit" |
        // "Particles" | "Waves" | "GridPulse" - καθολική αντικατάσταση του φόντου ΟΛΩΝ των θεμάτων.
        public string AnimationStyleOverride { get; set; } = "Theme";
        // REQ-580-05: αντικατέστησε το προηγούμενο 3-επιλογών (None/Light/Medium) WindowOpacityMode
        // με ένα συνεχές ποσοστό, port του μηχανισμού του MotionDeskStudio (ξεχωριστό project - βλ.
        // src/UI/MainWindow.cs's BuildOpacityRow: TrackBar 60-100%, ζωντανό preview καθώς σέρνεις).
        // 60% κάτω όριο διατηρήθηκε ρητά ίδιο με το MotionDeskStudio - κάτω από αυτό το κείμενο
        // γίνεται δύσκολα αναγνώσιμο.
        public int WindowOpacityPercent { get; set; } = 100;
        // "el" | "en" | "de" | "fr" - βλ. Services/LanguageService.cs.
        public string Language { get; set; } = "el";
        // Θυμάται ποιο "πραγματικό" θέμα ίσχυε πριν επιλεγεί το skin "Microsoft PC Manager", ώστε η
        // επιστροφή σε "Classic" στο Skins tab να επαναφέρει ΤΟ ΙΔΙΟ θέμα αντί για μια σταθερή προεπιλογή.
        public string LastNonPcManagerTheme { get; set; } = "Windows 11 Fluent";
        // ΝΕΟ - ρητό αίτημα χρήστη: "κουρτίνα με tutorial στο πρώτο άνοιγμα της εφαρμογής". False στην
        // πρώτη εκκίνηση (δεν υπάρχει ακόμα αρχείο ρυθμίσεων) - το App.xaml.cs δείχνει το OnboardingWindow
        // μόνο όσο αυτό παραμένει false, γίνεται true μόλις ο χρήστης πατήσει "Ξεκινήστε"/Παράλειψη/κλείσιμο.
        public bool HasSeenOnboarding { get; set; }

        // ΝΕΟ - roadmap "Προγραμματισμένη συντήρηση" - βλ. AdvancedToolsService.SetScheduledMaintenanceAsync.
        public bool ScheduledMaintenanceEnabled { get; set; }

        // ΝΕΟ (6.1.0) - σημείο επαναφοράς πριν από ριψοκίνδυνες ενέργειες (βλ. RestoreGuardService).
        public bool AutoRestorePointBeforeRiskyActions { get; set; } = true;

        // ΝΕΟ (6.1.0) - ειδοποιήσεις θερμοκρασίας / S.M.A.R.T. (βλ. AlertService).
        public bool TempAlertsEnabled { get; set; } = true;
        public int TempAlertThresholdC { get; set; } = 85;
        public bool SmartAlertsEnabled { get; set; } = true;

        // ΝΕΟ (6.1.0) - κοινή εμφάνιση με το MotionDesk Studio (βλ. SharedAppearanceService).
        public bool ShareAppearanceWithMotionDesk { get; set; }

        // 6.1.0 - παράθυρο χωρίς πλαίσιο (custom γραμμή τίτλου + κουμπιά, Window.Opacity). Προεπιλογή: κανονικό
        // πλαίσιο Windows. Αλλαγή ισχύει μετά από επανεκκίνηση (το στυλ παραθύρου δεν αλλάζει μετά το Show).
        public bool BorderlessWindow { get; set; } // παλιά ρύθμιση (6.1.x) - δεν χρησιμοποιείται πια
        // 6.2.5 - true = κανονικό πλαίσιο Windows (η διαφάνεια ΔΕΝ είναι εγγυημένη). Default false = δικό μας πλαίσιο με
        // γραμμή τίτλου/κουμπιά + Window.Opacity, που δουλεύει πάντα ζωντανά.
        public bool NativeWindowFrame { get; set; }

        // 6.2.5 - κατάσταση αναδίπλωσης της κάθετης μπάρας των skins (ίδιος μηχανισμός με το MotionDesk Studio):
        // true = συμπτυγμένη (μόνο εικονίδια), false = ανοιχτή, null = προεπιλογή του skin (Gaming Hub: συμπτυγμένη).
        public bool? RailCollapsed { get; set; }

        // ΝΕΟ (6.1.0) - καθυστερημένη εκκίνηση προγραμμάτων: όνομα καταχώρησης -> δευτερόλεπτα.
        public System.Collections.Generic.Dictionary<string, int> DelayedStartupSeconds { get; set; } = new();

        // ΝΕΟ - roadmap "Αυτόματο Gaming Mode" - βλ. AutoGamingModeService.
        public bool AutoGamingModeEnabled { get; set; }
        // Τελευταία γνωστή κατάσταση των χειροκίνητων Office/Gaming Mode (το view ξαναχτίζεται σε κάθε εναλλαγή καρτέλας).
        public bool GamingModeActive { get; set; }
        // Ελευθέρωση RAM (κλάδεμα διεργασιών παρασκηνίου + cache μνήμης, όπως το RAMMap) κατά την ενεργοποίηση του Gaming Mode.
        public bool GamingModeFreeRam { get; set; } = true;
        public bool NightlyUpdatesEnabled { get; set; }
        // Έλεγχος για νέα έκδοση του ίδιου του GearWin στο GitHub (βλ. SelfUpdateService) και η τελευταία έκδοση για την οποία ειδοποιήθηκε ο χρήστης.
        public bool SelfUpdateCheckEnabled { get; set; } = true;
        // 6.9.0: φάκελοι με git repos που ελέγχονται για ενημερώσεις στον ενημερωτή εφαρμογών (βλ. GitRepoUpdateService).
        public System.Collections.Generic.List<string> GitRepoRoots { get; set; } = new();
        // 6.9.1: ενημερώσεις που ο χρήστης επέλεξε να αγνοεί στον ενημερωτή ("πηγή|id")
        public System.Collections.Generic.List<string> IgnoredUpdates { get; set; } = new();
        // 6.9.1: η νυχτερινή εργασία εγκαθιστά και τις σημαντικές ενημερώσεις Windows (ποτέ επανεκκίνηση)
        public bool NightlyWindowsUpdates { get; set; }
        // 6.9.0: κλείδωμα διαχειριστή κωδικών browser (salted PBKDF2 hash, βλ. PasswordVaultLock)
        // 6.9.4: "auto" | "low" | "balanced" | "high"
        public string PerformanceMode { get; set; } = "auto";
        public string? PwdVaultSalt { get; set; }
        public string? PwdVaultHash { get; set; }
        public string? LastNotifiedSelfUpdate { get; set; }
        public bool OfficeModeActive { get; set; }

        // ΝΕΟ - roadmap "Καρφιτσωμένες συντομεύσεις" - κλειδιά μορφής "<λίστα>:<Label>" (βλ.
        // TweakRowVm.PinKey στο TweaksView.xaml.cs) για tweaks καρφιτσωμένα στην Αρχική. ΣΗΜΕΙΩΣΗ
        // ΕΙΛΙΚΡΙΝΕΙΑΣ: το Label είναι μεταφρασμένο κείμενο (LanguageService.T) - μια καρφίτσα δεν θα
        // ταιριάξει αν ο χρήστης αλλάξει γλώσσα εφαρμογής μετά το καρφίτσωμα (καμία σταθερή, γλωσσο-
        // ανεξάρτητη ταυτότητα υπάρχει ακόμα ανά tweak). Αποδεκτός συμβιβασμός για ένα "Μεσαίο" χαρακτηριστικό.
        public System.Collections.Generic.List<string> PinnedTweakKeys { get; set; } = new();

        // ΝΕΟ - roadmap "Προγραμματισμένος (background) Πλήρης Έλεγχος Υγείας" - απλούστερη, ασφαλέστερη
        // εκδοχή από πλήρες Windows Task Scheduler background scan: η Αρχική δείχνει μια ήπια υπενθύμιση
        // αν δεν έχει τρέξει Πλήρης Έλεγχος Υγείας εδώ και 7+ μέρες (ή ποτέ) - βλ. HomeView.xaml.cs's
        // CheckHealthCheckReminder. Καθαρά ενημερωτικό banner, ΚΑΜΙΑ αυτόματη σάρωση/ενέργεια στο
        // παρασκήνιο χωρίς να το ζητήσει ρητά ο χρήστης πατώντας το ίδιο το κουμπί.
        public DateTime? LastHealthCheckRunAt { get; set; }

        // ΝΕΟ - roadmap "Widget επιφάνειας εργασίας" - σε αντίθεση με το ήδη υπάρχον tray popup
        // (ανοίγει μόνο σε κλικ, κλείνει αυτόματα στο Deactivated), αυτό παραμένει ΠΑΝΤΑ ορατό στην
        // επιφάνεια εργασίας όσο είναι ενεργοποιημένο - βλ. Services/DesktopWidgetService.cs,
        // Views/DesktopWidgetWindow.xaml(.cs). Θέση null σημαίνει "δεν έχει μετακινηθεί ακόμα από τον
        // χρήστη" (προεπιλογή κάτω-δεξιά, ίδιο μοτίβο με το tray popup).
        public bool DesktopWidgetEnabled { get; set; }
        // Explicit opt-in; previously this listener was always on.
        public bool ClipboardHistoryEnabled { get; set; }
        public double? DesktopWidgetX { get; set; }
        public double? DesktopWidgetY { get; set; }

        // ΝΕΟ - ρητό αίτημα χρήστη: ειδοποίηση τύπου Windows πάνω από τη γραμμή εργασιών όταν
        // υπάρχουν διαθέσιμες ενημερώσεις εφαρμογών/οδηγών, ακόμα κι όταν η εφαρμογή είναι
        // ελαχιστοποιημένη στο tray - βλ. Services/UpdateNotificationService.cs. Τα LastNotified*
        // αποτρέπουν την επανάληψη της ΙΔΙΑΣ ειδοποίησης σε κάθε περιοδικό έλεγχο - ειδοποιεί ξανά
        // ΜΟΝΟ όταν ο αριθμός αλλάξει από την τελευταία φορά που ειδοποιήθηκε ο χρήστης.
        public bool UpdateNotificationsEnabled { get; set; } = true;
        public int UpdateCheckIntervalHours { get; set; } = 6;
        public DateTime? LastUpdateCheckAt { get; set; }
        public int? LastNotifiedAppUpdateCount { get; set; }
        public int? LastNotifiedDriverUpdateCount { get; set; }
        // ΝΕΟ - roadmap ιδέα #3 (ρητό αίτημα χρήστη: "κάνε το 3 από τις ιδέες") - βλ.
        // Services/WindowsUpdateService.cs.
        public int? LastNotifiedWindowsUpdateCount { get; set; }

        // ΝΕΟ - roadmap ιδέα #4 (ρητό αίτημα χρήστη: "κάνε τα 4-7") - "Ειδοποίηση χαμηλού χώρου
        // δίσκου" - ίδιο background μηχανισμό με τις ενημερώσεις (UpdateNotificationService), αλλά
        // χρονικό (ΟΧΙ αριθμητικό) dedup: το πόσο χαμηλός είναι ο χώρος δεν είναι διακριτός μετρήσιμος
        // αριθμός σαν το "3 ενημερώσεις" - αρκεί μία ειδοποίηση ανά 24ωρο όσο παραμένει κάτω από το
        // όριο, βλ. UpdateNotificationService.CheckLowDiskSpace.
        public bool LowDiskNotificationsEnabled { get; set; } = true;
        public int LowDiskThresholdPercent { get; set; } = 10;
        public DateTime? LastLowDiskNotifyAt { get; set; }

        // ΝΕΟ - roadmap ιδέα #5 (ρητό αίτημα χρήστη) - "Σύγκριση πριν/μετά σε όλες τις σαρώσεις" -
        // αθροιστικός μετρητής χώρου που ελευθερώθηκε ΣΥΝΟΛΙΚΑ από τον Γρήγορο Καθαρισμό/Πλήρη Έλεγχο
        // Υγείας/καθαρισμό cache browser από τότε που εγκαταστάθηκε η εφαρμογή - βλ.
        // Services/ImpactTrackingService.cs. Persisted (ΟΧΙ session-only) ακριβώς επειδή ο σκοπός είναι
        // να δείξει το ΣΥΝΟΛΙΚΟ όφελος στον χρόνο, όχι μόνο τη μία τρέχουσα συνεδρία.
        public long TotalBytesFreedAllTime { get; set; }

        // ΝΕΟ - ROADMAP.md REQ-570-02/12 (ρητό αίτημα χρήστη) - βλ.
        // Services/SystemService.SetLaunchWithWindowsToTray + App.xaml.cs's "--tray" χειρισμό.
        public bool LaunchWithWindowsToTray { get; set; }
    }

    // Port του $global:appSettings / Save-AppSettings του Optimizer.ps1 - JSON persisted ρυθμίσεις
    // που έπρεπε να επιβιώνουν επανεκκινήσεις (θέμα, πλευρικό μενού) αλλά δεν είχαν ακόμα δικό τους
    // αποθηκευτικό μηχανισμό στο WPF port - το θέμα π.χ. επανερχόταν πάντα σε Windows 11 Fluent/Dark
    // σε κάθε εκκίνηση.
    public static class AppSettingsService
    {
        // ΑΣΦΑΛΕΙΑ ΔΕΔΟΜΕΝΩΝ ΧΡΗΣΤΗ: το GEARWIN_DATA_DIR (ορίζεται ΜΟΝΟ από το test assembly, βλ. TestIsolation.cs) στέλνει τις
        // ρυθμίσεις σε προσωρινό φάκελο. Πριν, το live UI-Automation click-through πατούσε κάθε διακόπτη και ΑΠΟΘΗΚΕΥΕ στο
        // πραγματικό AppSettings.json του χρήστη - έκλεισε π.χ. τις ειδοποιήσεις ενημερώσεων και ο αυτόματος έλεγχος
        // σταμάτησε (UpdateNotificationsEnabled=false).
        private static readonly string StorePath = Path.Combine(
            Environment.GetEnvironmentVariable("GEARWIN_DATA_DIR") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf"),
            "AppSettings.json");

        private static AppSettings? _cache;

        // Οι 14 υποστηριζόμενοι κωδικοί γλώσσας (βλ. LanguageService.AllTranslations) - hardcoded εδώ
        // αντί για αναφορά στο LanguageService ώστε να αποφευχθεί κυκλική εξάρτηση μεταξύ των δύο
        // static classes.
        private static readonly string[] SupportedLanguages = { "el", "en", "de", "fr", "es", "it", "ru", "zh", "ja", "pt", "ko", "tr", "ar", "hi" };

        // ΝΕΟ - βελτίωση: στην ΠΡΩΤΗ πραγματική εκκίνηση (δεν υπάρχει ακόμα αρχείο ρυθμίσεων), αντί να
        // ξεκινάει πάντα σε Ελληνικά, ανιχνεύει τη γλώσσα διεπαφής χρήστη των Windows και τη χρησιμοποιεί
        // αν υποστηρίζεται - πιο φιλικό για μη-ελληνόφωνους χρήστες που δεν θα ήξεραν αμέσως πού είναι
        // το κουμπί γλώσσας. Εφαρμόζεται ΜΟΝΟ όταν δεν υπάρχει καθόλου αρχείο (πραγματική πρώτη φορά) -
        // ένα αρχείο που υπάρχει αλλά απέτυχε να αποσειριοποιηθεί (πχ κατεστραμμένο) παραμένει στο
        // ασφαλές προεπιλεγμένο "el" αντί να μαντεύει.
        private static string DetectSystemLanguage()
        {
            var code = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return Array.IndexOf(SupportedLanguages, code) >= 0 ? code : "el";
        }

        private static readonly object PersistenceGate = new();
        private static string? _lastPersisted;
        private static bool _loadFailed;

        public static AppSettings Current
        {
            get
            {
                lock (PersistenceGate)
                {
                    if (_cache != null) return _cache;
                    var exists = File.Exists(StorePath);
                    try
                    {
                        if (exists)
                        {
                            var text = File.ReadAllText(StorePath);
                            _cache = JsonSerializer.Deserialize<AppSettings>(text);
                            if (_cache == null) throw new InvalidDataException("Null settings.");
                            _lastPersisted = JsonSerializer.Serialize(_cache);
                        }
                    }
                    catch (Exception ex)
                    {
                        _loadFailed = true;
                        System.Diagnostics.Trace.TraceError("Settings read failed: " + ex.Message);
                    }
                    if (_cache == null)
                    {
                        _cache = new AppSettings();
                        if (!exists) _cache.Language = DetectSystemLanguage();
                    }
                    return _cache;
                }
            }
        }

        internal static bool WriteIfChanged(string path, string json, string? lastPersisted)
        {
            if (string.Equals(json, lastPersisted, StringComparison.Ordinal) && File.Exists(path)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            finally { if (File.Exists(tmp)) try { File.Delete(tmp); } catch { } }
        }

        private static bool SaveCore()
        {
            try
            {
                var json = JsonSerializer.Serialize(Current);
                // Never replace an unreadable original with default settings.
                if (_loadFailed) return false;
                WriteIfChanged(StorePath, json, _lastPersisted);
                _lastPersisted = json;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Settings write failed: " + ex.Message);
                return false;
            }
        }

        public static void Save()
        {
            lock (PersistenceGate) SaveCore();
        }

        public static bool Export(string destinationPath)
        {
            lock (PersistenceGate)
            {
                try
                {
                    if (!SaveCore()) return false;
                    WriteIfChanged(Path.GetFullPath(destinationPath), JsonSerializer.Serialize(Current), null);
                    return true;
                }
                catch { return false; }
            }
        }

        public static bool Import(string sourcePath)
        {
            lock (PersistenceGate)
            {
                try
                {
                    var text = File.ReadAllText(sourcePath);
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty(nameof(AppSettings.Language), out _)) return false;
                    var settings = JsonSerializer.Deserialize<AppSettings>(text);
                    if (settings == null) return false;
                    var json = JsonSerializer.Serialize(settings);
                    WriteIfChanged(StorePath, json, null);
                    _cache = null;
                    _lastPersisted = null;
                    _loadFailed = false;
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
