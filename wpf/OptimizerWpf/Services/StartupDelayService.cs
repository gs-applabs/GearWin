using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    // ΝΕΟ (6.1.0) - καθυστέρηση εκκίνησης προγραμμάτων: αντί να ξεκινούν όλα μαζί με την είσοδο στα
    // Windows, ένα πρόγραμμα της λίστας εκκίνησης μπορεί να ξεκινά N δευτερόλεπτα αργότερα. Τεχνική:
    // δημιουργείται προγραμματισμένη εργασία (Task Scheduler XML, LogonTrigger + Delay, χωρίς περιορισμούς μπαταρίας) που τρέχει την ίδια εντολή και
    // η αρχική καταχώρηση εκκίνησης απενεργοποιείται (SystemService.SetStartupItemEnabled - αναστρέψιμο).
    // Η "Αφαίρεση" σβήνει την εργασία και ξαναενεργοποιεί την αρχική καταχώρηση.
    public static class StartupDelayService
    {
        public const string TaskFolder = "GearWinDelayed";

        public static string FormatDelay(int seconds)
        {
            seconds = Math.Clamp(seconds, 5, 599 * 60 + 59);
            return $"{seconds / 60:0000}:{seconds % 60:00}";
        }

        public static string BuildTaskName(string startupItemName)
        {
            var safe = Regex.Replace(startupItemName, @"[^\w\-\. ]", "_").Trim();
            if (safe.Length == 0) safe = "item";
            return $@"{TaskFolder}\{safe}";
        }

        public static IReadOnlyDictionary<string, int> Delays => AppSettingsService.Current.DelayedStartupSeconds;

        public static async Task<bool> ApplyDelayAsync(StartupItem item, int seconds)
        {
            using var lease = await GearWinCore.EnterAsync(CoreWork.StartupChange);
            // HKLM Run executes for all users; a single-user delayed task is not equivalent.
            if (item.Location != "HKCU" || item.IsDisabled || Delays.ContainsKey(item.Name)) return false;
            if (seconds < 5 || seconds > 3600) return false;
            var task = BuildTaskName(item.Name);
            if (await TaskSchedulerService.ExistsAsync(task)) return false;
            var (exe, args) = TaskSchedulerService.SplitCommand(item.Command);
            if (!System.IO.Path.IsPathFullyQualified(exe) || !System.IO.File.Exists(exe) ||
                !string.Equals(System.IO.Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !MaintenancePaths.IsOrdinaryLocalPath(exe)) return false;
            var xml = TaskSchedulerService.BuildLogonTaskXml(exe, args, Math.Clamp(seconds, 5, 3600), TaskSchedulerService.CurrentUserId(),
                "GearWin - delayed startup: " + item.Name, elevated: false);
            if (!await TaskSchedulerService.CreateAsync(task, xml)) return false;
            // Απενεργοποίηση της αρχικής καταχώρησης ΜΟΝΟ αφού η εργασία δημιουργήθηκε (αλλιώς το πρόγραμμα χάνεται).
            if (!item.IsDisabled && !SystemService.SetStartupItemEnabled(item, false))
            {
                await TaskSchedulerService.DeleteAsync(task);
                return false;
            }
            AppSettingsService.Current.DelayedStartupSeconds[item.Name] = seconds;
            AppSettingsService.Save();
            return true;
        }

        public static async Task<bool> RemoveDelayAsync(StartupItem item)
        {
            using var lease = await GearWinCore.EnterAsync(CoreWork.StartupChange);
            if (item.Location != "HKCU" || !item.IsDisabled || !Delays.ContainsKey(item.Name)) return false;
            // Restore the original first, so a failed restoration cannot lose the only startup path.
            if (!SystemService.SetStartupItemEnabled(item, true)) return false;
            var ok = await TaskSchedulerService.DeleteAsync(BuildTaskName(item.Name));
            if (!ok) { SystemService.SetStartupItemEnabled(item, false); return false; }
            if (ok)
            {
                AppSettingsService.Current.DelayedStartupSeconds.Remove(item.Name);
                AppSettingsService.Save();
            }
            return ok;
        }
    }
}
