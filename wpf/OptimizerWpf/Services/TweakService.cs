using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace OptimizerWpf.Services
{
    // Απλό DWORD/String registry toggle - "off" γυρνάει στην backed-up πραγματική προηγούμενη τιμή
    // (μέσω TweakBackupService), όχι hardcoded default - ίδια σημασιολογία με το ps1 original.
    public record RegTweak(string Label, string Description, RegistryHive Hive, string Path, string Name, object OnValue, RegistryValueKind Kind, string BackupKey);

    // ΔΙΟΡΘΩΣΗ (γνωστό κενό #01 του roadmap: "οι διακόπτες δεν διαβάζουν την πραγματική τρέχουσα τιμή
    // του μητρώου - ξεκινούν πάντα ανενεργοί") - το προαιρετικό DetectState διαβάζει την ΠΡΑΓΜΑΤΙΚΗ
    // τρέχουσα κατάσταση του συστήματος (μητρώο/powercfg), ώστε το TweaksView να δείχνει τον διακόπτη
    // ήδη ενεργό αν το tweak ισχύει ήδη - όχι πια πάντα false στο άνοιγμα. null σημαίνει "δεν υπάρχει
    // φθηνός/αξιόπιστος τρόπος ανίχνευσης" (π.χ. καμία τέτοια περίπτωση προς το παρόν - βλ. παρακάτω,
    // όλα τα 25 tweaks έχουν πλέον πραγματική ανίχνευση). Προαιρετική παράμετρος (=null προεπιλογή)
    // ώστε τα ήδη υπάρχοντα 4-args call sites να μη χρειάζονται αλλαγή.
    public record SimpleTweak(string Label, string Description, Action OnAction, Action OffAction, Func<bool?>? DetectState = null, string? Id = null, bool RequiresAdmin = false);

    // Port του Optimizer.ps1's Επιπλέον Ρυθμίσεις καρτέλα (~16519-17573) - η πυκνότερη καρτέλα της
    // εφαρμογής. Βλ. HANDOFF.md §0.4ιβ για πλήρη τεκμηρίωση όλων των 25 tweaks.
    public static class TweakService
    {
        // Properties (όχι readonly fields) ώστε να ξαναχτίζονται με την τρέχουσα γλώσσα κάθε φορά που
        // ανοίγει το TweaksView (ρητό αίτημα χρήστη: "μετάφρασε τα όλα").
        public static IReadOnlyList<RegTweak> MainTweaks => new[]
        {
            new RegTweak(LanguageService.T("Tweak_M0Label"), LanguageService.T("Tweak_M0Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 1, RegistryValueKind.DWord, "StorageSense"),
            new RegTweak(LanguageService.T("Tweak_M1Label"), LanguageService.T("Tweak_M1Desc"),
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, RegistryValueKind.DWord, "Hiberboot"),
            new RegTweak(LanguageService.T("Tweak_M2Label"), LanguageService.T("Tweak_M2Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, RegistryValueKind.DWord, "HideFileExt"),
            new RegTweak(LanguageService.T("Tweak_M3Label"), LanguageService.T("Tweak_M3Desc"),
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", 1, RegistryValueKind.DWord, "DODownloadMode"),
            new RegTweak(LanguageService.T("Tweak_M4Label"), LanguageService.T("Tweak_M4Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", 0, RegistryValueKind.DWord, "IrisRecommendations"),
            new RegTweak(LanguageService.T("Tweak_M5Label"), LanguageService.T("Tweak_M5Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 1, RegistryValueKind.DWord, "ClipboardHistory"),
            new RegTweak(LanguageService.T("Tweak_M6Label"), LanguageService.T("Tweak_M6Desc"),
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord, "NetThrottle"),
            new RegTweak(LanguageService.T("Tweak_M7Label"), LanguageService.T("Tweak_M7Desc"),
                RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, RegistryValueKind.DWord, "SysResponsiveness"),
            new RegTweak(LanguageService.T("Tweak_M8Label"), LanguageService.T("Tweak_M8Desc"),
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, RegistryValueKind.DWord, "Telemetry"),
        };

        public static IReadOnlyList<RegTweak> AiCopilotTweaks => new[]
        {
            new RegTweak(LanguageService.T("Tweak_A0Label"), LanguageService.T("Tweak_A0Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0, RegistryValueKind.DWord, "CopilotBtn"),
            new RegTweak(LanguageService.T("Tweak_A1Label"), LanguageService.T("Tweak_A1Desc"),
                RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, RegistryValueKind.DWord, "RecallCU"),
            new RegTweak(LanguageService.T("Tweak_A2Label"), LanguageService.T("Tweak_A2Desc"),
                RegistryHive.CurrentUser, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1, RegistryValueKind.DWord, "ClickToDo"),
            new RegTweak(LanguageService.T("Tweak_A3Label"), "",
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, RegistryValueKind.DWord, "AiServiceLM"),
            new RegTweak(LanguageService.T("Tweak_A4Label"), LanguageService.T("Tweak_A4Desc"),
                RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0, RegistryValueKind.DWord, "EdgeAI"),
            new RegTweak(LanguageService.T("Tweak_A5Label"), LanguageService.T("Tweak_A5Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Paint\App", "EnableCopilot", 0, RegistryValueKind.DWord, "PaintAI"),
            new RegTweak(LanguageService.T("Tweak_A6Label"), LanguageService.T("Tweak_A6Desc"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Notepad", "CocreatorEnabled", 0, RegistryValueKind.DWord, "NotepadAI"),
        };

        public static IReadOnlyList<RegTweak> PerfRegTweaks => new[]
        {
            new RegTweak(LanguageService.T("Tweak_HagsLabel"), LanguageService.T("Tweak_HagsDesc"),
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord, "HAGS"),
        };

        public static IReadOnlyList<SimpleTweak> PerfPowercfgTweaks => new[]
        {
            PowerTweak(PowerIndexSpec.Threshold, "Tweak_P0Label", "Tweak_P0Desc"),
            PowerTweak(PowerIndexSpec.Usb, "Tweak_P1Label", "Tweak_P1Desc"),
            PowerTweak(PowerIndexSpec.Pcie, "Tweak_P2Label", "Tweak_P2Desc"),
        };

        private static SimpleTweak PowerTweak(PowerIndexSpec spec, string label, string description) =>
            new(LanguageService.T(label), LanguageService.T(description),
                () => PowerIndexJournal.Change(spec, true), () => PowerIndexJournal.Change(spec, false),
                DetectState: () => PowerIndexJournal.IsOn(spec), Id: spec.Id, RequiresAdmin: true);

        // Ενιαία λίστα των 14 κύριων tweaks σε μια μορφή (RegTweak + ειδικές περιπτώσεις μαζί) -
        // απλοποιεί το UI σε ένα ItemsControl αντί για ανάμειξη δύο τύπων record.
        public static IReadOnlyList<SimpleTweak> AllMainTweaks()
        {
            var list = new List<SimpleTweak>();
            var m = MainTweaks;
            list.Add(ToSimple(m[0])); // Storage Sense
            list.Add(ToSimple(m[1])); // Fast Startup
            list.Add(new SimpleTweak(LanguageService.T("Tweak_ClassicMenuLabel"), LanguageService.T("Tweak_ClassicMenuDesc"), EnableClassicContextMenu, DisableClassicContextMenu,
                Id: "ClassicMenu", DetectState: () => GroupedRegistryTweaks.IsOn(GroupedRegistryTweaks.ClassicMenu)));
            list.Add(ToSimple(m[2])); // File extensions
            list.Add(ToSimple(m[3])); // Delivery Optimization
            list.Add(new SimpleTweak(LanguageService.T("Tweak_MouseAccelLabel"), LanguageService.T("Tweak_MouseAccelDesc"), MouseAccelOff, MouseAccelOn,
                Id: "MouseAccel", DetectState: () => GroupedRegistryTweaks.IsOn(GroupedRegistryTweaks.Mouse)));
            list.Add(new SimpleTweak(LanguageService.T("Tweak_HibernateLabel"), LanguageService.Current == "el" ? "Η απενεργοποίηση αφαιρεί το αρχείο αδρανοποίησης και επηρεάζει τη Γρήγορη εκκίνηση." : "Turning off removes the hibernation file and affects Fast Startup.", HibernateOn, HibernateOff,
                Id: "Hibernate", RequiresAdmin: true, DetectState: HibernateSettings.IsEnabled));
            list.Add(ToSimple(m[4])); // Remove Recommended
            list.Add(ToSimple(m[5])); // Clipboard history
            list.Add(ToSimple(m[6])); // Network throttling
            list.Add(ToSimple(m[7])); // System responsiveness
            list.Add(new SimpleTweak(LanguageService.T("Tweak_GameBarLabel"), LanguageService.T("Tweak_GameBarDesc"), DisableGameBar, EnableGameBar,
                Id: "GameBar", RequiresAdmin: true, DetectState: () => GroupedRegistryTweaks.IsOn(GroupedRegistryTweaks.GameBar)));
            list.Add(ToSimple(m[8])); // Telemetry
            return list;
        }

        private static SimpleTweak ToSimple(RegTweak t) => new(t.Label, t.Description, () => ApplyOn(t), () => ApplyOff(t), DetectState: () => IsRegTweakOn(t), Id: t.BackupKey,
            RequiresAdmin: t.Hive == RegistryHive.LocalMachine);

        // ΔΙΟΡΘΩΣΗ (γνωστό κενό #01) - διαβάζει την ΠΡΑΓΜΑΤΙΚΗ τρέχουσα τιμή του κάθε RegTweak από το
        // μητρώο και τη συγκρίνει με το OnValue, αντί το UI να υποθέτει πάντα "ανενεργό". Απούσα τιμή
        // (καμία τιμή στο μητρώο ακόμα) μετράει ως "ανενεργό" - ίδια σημασιολογία με το ApplyOff's
        // DeleteValue όταν δεν υπάρχει backup.
        private static bool IsRegTweakOn(RegTweak t)
        {
            using var key = OpenHive(t.Hive).OpenSubKey(t.Path);
            var current = key?.GetValue(t.Name);
            if (current == null) return false;
            if (t.Kind == RegistryValueKind.String) return string.Equals(current.ToString(), t.OnValue.ToString(), StringComparison.OrdinalIgnoreCase);
            return Convert.ToInt64(current) == Convert.ToInt64(t.OnValue);
        }

        public static IReadOnlyList<SimpleTweak> AiCopilotTweaksSimple() => AiCopilotTweaks.Select(ToSimple).ToList();

        // ΝΕΟ - ρητό αίτημα χρήστη ("ενσωμάτωσε τα... ελαφρύτερα Windows") - 2 tweaks που ελαφρώνουν
        // τα ίδια τα Windows στο παρασκήνιο, ίδιο μοτίβο SimpleTweak/DetectState με τα υπόλοιπα.
        public static IReadOnlyList<SimpleTweak> LighterWindowsTweaksSimple() => new[]
        {
            new SimpleTweak(LanguageService.T("Tweak_BgAppsLabel"), LanguageService.T("Tweak_BgAppsDesc"),
                DisableBackgroundApps, EnableBackgroundApps, Id: "BackgroundApps", DetectState: DetectBackgroundAppsOff, RequiresAdmin: true),
            new SimpleTweak(LanguageService.T("Tweak_SysMainLabel"), LanguageService.T("Tweak_SysMainDesc"),
                DisableSysMain, EnableSysMain, Id: "SysMain", DetectState: DetectSysMainOff, RequiresAdmin: true),
        };

        // 6.5.4+: το HAGS εμφανιζόταν ΚΑΙ εδώ ΚΑΙ στα Παιχνίδια › Ρυθμίσεις gaming (GamingTweaksService "Hags"), με διαφορετική σημασιολογία "off"
        // (εδώ επαναφορά/διαγραφή της τιμής, εκεί HwSchMode=1) - δύο διακόπτες για το ίδιο μητρώο. Ο διακόπτης ζει πλέον ΜΟΝΟ στα Παιχνίδια· το RegTweak
        // "HAGS" παραμένει στο PerfRegTweaks ώστε η επαναφορά (RestoreAllTrackedTweaks/CountRestorableTweaks) να αναιρεί ό,τι είχε αλλάξει παλιότερα από εδώ.
        public const string HagsBackupKey = "HAGS";

        public static IReadOnlyList<SimpleTweak> PerfTweaksSimple() =>
            PerfRegTweaks.Where(t => t.BackupKey != HagsBackupKey).Select(ToSimple).Concat(PerfPowercfgTweaks).ToList();

        private static void MouseAccelOff() => GroupedRegistryTweaks.Change(GroupedRegistryTweaks.Mouse, "MouseAccel", false);

        private static void MouseAccelOn() => GroupedRegistryTweaks.Change(GroupedRegistryTweaks.Mouse, "MouseAccel", true);

        public static void ApplyOn(RegTweak tweak) => TweakRegistryJournal.Apply(tweak);

        public static void ApplyOff(RegTweak tweak) => TweakRegistryJournal.Restore(tweak);

        private static RegistryKey OpenHive(RegistryHive hive) => hive == RegistryHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;

        // Classic context menu: restore only the original default value.
        // Leave registry containers intact; do not kill Explorer across sessions.
        // Sign out/in may be required for Windows to apply the change.
        public static void EnableClassicContextMenu() =>
            GroupedRegistryTweaks.Change(GroupedRegistryTweaks.ClassicMenu, "ClassicMenu", false);

        public static void DisableClassicContextMenu() =>
            GroupedRegistryTweaks.Change(GroupedRegistryTweaks.ClassicMenu, "ClassicMenu", true);

        // ===== Αρχείο Αδρανοποίησης / Ultimate Performance =====

        public static void HibernateOn() => HibernateSettings.Change(true);
        public static void HibernateOff() => HibernateSettings.Change(false);

        // ΔΙΟΡΘΩΣΗ (ROADMAP.md REQ-570-03, ρητό αίτημα χρήστη: "αυτόματο gaming mode κάτω από gaming
        // mode & ultimate performance") - το toggle αυτού του tweak μετακινήθηκε από τη γενική λίστα
        // AllMainTweaks() (καρτέλα Επιπλέον Ρυθμίσεις) στην καρτέλα Βελτιστοποίηση, δίπλα στο Gaming
        // Mode (βλ. OptimizationView) - πιο σωστή θεματική τοποθέτηση (ίδιο πνεύμα με
        // feedback_correct_tab_placement). Η ανίχνευση κατάστασης εξήχθη εδώ σε δική της μέθοδο (πριν
        // ήταν ανώνυμο lambda μέσα στο AllMainTweaks) ώστε να τη χρησιμοποιεί απευθείας το
        // OptimizationView.xaml.cs χωρίς να περνάει πια από το SimpleTweak/AllMainTweaks μονοπάτι.
        public static bool IsUltimatePerformanceActive() => UltimatePlanJournal.IsActive();

        public static void UltimatePerformanceOn() => UltimatePlanJournal.Enable();

        public static void UltimatePerformanceOff() => UltimatePlanJournal.Disable();

        // ===== Απενεργοποίηση Game Bar / Game DVR (ξεχωριστό από Gaming Mode) =====

        public static void DisableGameBar() =>
            GroupedRegistryTweaks.Change(GroupedRegistryTweaks.GameBar, "GameBar", false);

        public static void EnableGameBar() =>
            GroupedRegistryTweaks.Change(GroupedRegistryTweaks.GameBar, "GameBar", true);

        // ===== Οπτικά Εφέ - ΠΟΤΕ το undocumented UserPreferencesMask =====

        public static void SetVisualEffectsPreset(int visualFx, int onOff) =>
            VisualEffectsJournal.Apply(visualFx, onOff);

        // ===== Κρυπτογράφηση Συσκευής - ΠΟΤΕ registry toggle, μόνο άνοιγμα ρυθμίσεων =====

        public static void OpenDeviceEncryptionSettings()
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.BitLockerDriveEncryption") { UseShellExecute = true }); }
            catch { Process.Start(new ProcessStartInfo("ms-settings:deviceencryption") { UseShellExecute = true }); }
        }

        // ===== 2 hardcoded δεξί-κλικ επιλογές (Ιδιοκτησία/PowerShell εδώ) =====

        public static void InstallTakeOwnership()
        {
            foreach (var root in new[] { @"*\shell\OptimizerTakeOwnership", @"Directory\shell\OptimizerTakeOwnership", @"Drive\shell\OptimizerTakeOwnership" })
            {
                using var key = Registry.ClassesRoot.CreateSubKey(root, writable: true);
                key?.SetValue("", LanguageService.T("Tweak_TakeOwnershipMenuLabel"));
                key?.SetValue("HasLUAShield", "");
                using var cmd = Registry.ClassesRoot.CreateSubKey(root + @"\command", writable: true);
                cmd?.SetValue("", "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant *S-1-3-4:F /c /l & pause");
            }
        }

        public static void RemoveTakeOwnership()
        {
            foreach (var root in new[] { @"*\shell\OptimizerTakeOwnership", @"Directory\shell\OptimizerTakeOwnership", @"Drive\shell\OptimizerTakeOwnership" })
                try { Registry.ClassesRoot.DeleteSubKeyTree(root, throwOnMissingSubKey: false); } catch { }
        }

        // ΔΙΟΡΘΩΣΗ (γνωστό κενό #01) - τα 2 hardcoded ToggleButton (TweaksView.xaml) δεν περνούν από
        // SimpleTweak/TweakRowVm, οπότε χρειάζονται τη δική τους δημόσια έκθεση κατάστασης.
        public static bool IsTakeOwnershipInstalled() =>
            new[] { @"*\shell\OptimizerTakeOwnership", @"Directory\shell\OptimizerTakeOwnership", @"Drive\shell\OptimizerTakeOwnership" }
                .Any(root => { using var k = Registry.ClassesRoot.OpenSubKey(root); return k != null; });

        public static void InstallOpenPowerShellHere()
        {
            foreach (var root in new[] { @"Directory\shell\OptimizerOpenPS", @"Directory\Background\shell\OptimizerOpenPS" })
            {
                using var key = Registry.ClassesRoot.CreateSubKey(root, writable: true);
                key?.SetValue("", LanguageService.T("Tweak_OpenPsHereMenuLabel"));
                using var cmd = Registry.ClassesRoot.CreateSubKey(root + @"\command", writable: true);
                cmd?.SetValue("", root.Contains("Background")
                    ? "powershell.exe -NoExit -Command \"Set-Location -LiteralPath '%V'\""
                    : "powershell.exe -NoExit -Command \"Set-Location -LiteralPath '%1'\"");
            }
        }

        public static void RemoveOpenPowerShellHere()
        {
            foreach (var root in new[] { @"Directory\shell\OptimizerOpenPS", @"Directory\Background\shell\OptimizerOpenPS" })
                try { Registry.ClassesRoot.DeleteSubKeyTree(root, throwOnMissingSubKey: false); } catch { }
        }

        public static bool IsOpenPowerShellHereInstalled() =>
            new[] { @"Directory\shell\OptimizerOpenPS", @"Directory\Background\shell\OptimizerOpenPS" }
                .Any(root => { using var k = Registry.ClassesRoot.OpenSubKey(root); return k != null; });

        // ===== Πλήρης επαναφορά (uninstall) =====

        // Bulk reset/uninstall restores only verified originals. Presence of an
        // old CLSID, shell verb, hosts block or policy is NOT ownership evidence.
        // Legacy unjournaled settings remain for manual review.
        public static int RestoreAllTrackedTweaks()
        {
            int reverted = 0;
            foreach (var t in MainTweaks.Concat(AiCopilotTweaks).Concat(PerfRegTweaks))
            {
                if (!TweakRegistryJournal.CanRestore(t)) continue;
                try { ApplyOff(t); reverted++; }
                catch (Exception ex) { Trace.TraceError("Tweak restore: " + ex.Message); }
            }
            foreach (var group in GroupedRegistryTweaks.Groups())
            {
                if (!GroupedRegistryTweaks.CanRestore(group.Values)) continue;
                try { GroupedRegistryTweaks.Change(group.Values, group.Id, true); reverted++; }
                catch (Exception ex) { Trace.TraceError("Grouped tweak restore: " + ex.Message); }
            }
            if (BackgroundAppJournal.CanRestore())
            {
                try { BackgroundAppJournal.Change(true); reverted++; }
                catch (Exception ex) { Trace.TraceError("Background app restore: " + ex.Message); }
            }
            if (ServiceStartJournal.CanRestoreSysMain())
            {
                try { if (ServiceStartJournal.ChangeSysMain(true)) reverted++; }
                catch (Exception ex) { Trace.TraceError("SysMain restore: " + ex.Message); }
            }
            if (VisualEffectsJournal.CanRestore())
            {
                try { VisualEffectsJournal.Restore(); reverted++; }
                catch (Exception ex) { Trace.TraceError("Visual preset restore: " + ex.Message); }
            }
            return reverted;
        }

        public static int CountRestorableTweaks() =>
            MainTweaks.Concat(AiCopilotTweaks).Concat(PerfRegTweaks).Count(TweakRegistryJournal.CanRestore)
            + GroupedRegistryTweaks.Groups().Count(g => GroupedRegistryTweaks.CanRestore(g.Values))
            + (BackgroundAppJournal.CanRestore() ? 1 : 0) + (ServiceStartJournal.CanRestoreSysMain() ? 1 : 0)
            + (VisualEffectsJournal.CanRestore() ? 1 : 0);

        // ===== Ελαφρύτερα Windows (roadmap) =====

        // Only the explicitly selected, removable non-system package plan is
        // changed. Restore does not enumerate installed apps or delete trees.
        public static void DisableBackgroundApps() => BackgroundAppJournal.Change(false);
        public static void EnableBackgroundApps() => BackgroundAppJournal.Change(true);
        private static bool? DetectBackgroundAppsOff() => BackgroundAppJournal.IsOn();

        private const string SysMainServiceKey = @"SYSTEM\CurrentControlSet\Services\SysMain";

        public static void DisableSysMain()
        {
            if (!ServiceStartJournal.ChangeSysMain(false))
                throw new InvalidOperationException("SysMain startup cannot be safely changed; original setting retained.");
        }

        public static void EnableSysMain()
        {
            if (!ServiceStartJournal.ChangeSysMain(true))
                throw new InvalidOperationException("No verified SysMain original startup setting, or external change detected.");
        }

        private static bool? DetectSysMainOff()
        {
            using var key = Registry.LocalMachine.OpenSubKey(SysMainServiceKey);
            var start = key?.GetValue("Start");
            if (start == null) return null;
            return Convert.ToInt32(start) == 4; // 4 = Disabled
        }

        // ΝΕΟ - ΠΟΤΕ απευθείας χειρισμός του ευρετηρίου αναζήτησης (πολύπλοκο COM API, ρίσκο χαλασμένου
        // ευρετηρίου) - ίδια φιλοσοφία με το OpenDeviceEncryptionSettings, μόνο άνοιγμα του επίσημου
        // Windows dialog όπου ο χρήστης εξαιρεί μεγάλους φακέλους (πολυμέσα/ανάπτυξη) ο ίδιος.
        public static void OpenIndexingOptions()
        {
            try { Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.IndexingOptions") { UseShellExecute = true }); }
            catch { }
        }

        // ΝΕΟ - "Προφίλ Χαμηλής Χρήσης Πόρων" (ρητό αίτημα χρήστη) - συγκεντρώνει σε ΕΝΑ κουμπί 3 ήδη
        // υπάρχοντα, μεμονωμένα tweaks (Delivery Optimization, Εφαρμογές Παρασκηνίου, SysMain) αντί να
        // χρειάζεται ο χρήστης να τα βρει/ενεργοποιήσει ένα-ένα.
        public static void ApplyLowResourceProfile()
        {
            ApplyOn(MainTweaks[3]); // Delivery Optimization off (P2P sharing)
            DisableBackgroundApps();
            DisableSysMain();
        }

    }
}
