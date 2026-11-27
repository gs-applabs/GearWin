using System.Diagnostics;
using System.Linq;
using System.Management;

namespace OptimizerWpf.Services
{
    // GPU (ρητό αίτημα χρήστη: 4ο πλακίδιο απόδοσης με θερμοκρασία + ποσοστό χρήσης). Το όνομα
    // έρχεται από WMI (Win32_VideoController) εδώ. Η ΘΕΡΜΟΚΡΑΣΙΑ δεν διεκπεραιώνεται από αυτή την
    // κλάση πλέον - καμία WMI κλάση δεν την εκθέτει χωρίς vendor SDK (NVIDIA NVML/AMD ADL),
    // επιβεβαιωμένο με έρευνα (βλ. HANDOFF.md) - βλ. αντ' αυτού Services/SensorService.cs
    // (LibreHardwareMonitorLib). Το ΠΟΣΟΣΤΟ ΧΡΗΣΗΣ όμως ΕΙΝΑΙ αξιόπιστα διαθέσιμο μέσω της
    // ενσωματωμένης κατηγορίας μετρητών "GPU Engine" (Windows 10+, καμία vendor εξάρτηση) - η ΙΔΙΑ
    // πηγή δεδομένων που δείχνει το tab Απόδοση > GPU του Task Manager.
    public static class GpuInfoService
    {
        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη: "να δείχνει και τον ενσωματωμένο σε CPU αν υπάρχει") - πολλά
        // συστήματα έχουν ΚΑΙ ενσωματωμένη (iGPU, π.χ. AMD Radeon Graphics μέσα σε APU/Intel UHD)
        // ΚΑΙ διακριτή (dGPU) κάρτα γραφικών - `Win32_VideoController` τις επιστρέφει ΟΛΕΣ, οπότε
        // εδώ ενώνονται όλα τα ονόματα αντί να κρατηθεί μόνο το πρώτο (που ΔΕΝ είναι εγγυημένα η
        // "κύρια" κάρτα - η σειρά επιστροφής της WMI δεν είναι αξιόπιστη ένδειξη προτεραιότητας).
        private static readonly object NameGate = new();
        private static System.Threading.Tasks.Task<string>? _nameTask;
        private static long _nameAt;

        internal static System.Threading.Tasks.Task<string> GetNameAsync()
        {
            lock (NameGate)
            {
                if (_nameTask is { IsCompleted: false }) return _nameTask;
                var now = System.Environment.TickCount64;
                if (_nameTask is { IsCompletedSuccessfully: true } && now - _nameAt < 120000) return _nameTask;
                _nameAt = now;
                return _nameTask = System.Threading.Tasks.Task.Run(GetName);
            }
        }

        public static string GetName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                using var results = searcher.Get();
                var names = new System.Collections.Generic.List<string>();
                foreach (ManagementBaseObject item in results)
                {
                    using (item)
                        if (item["Name"]?.ToString() is string name && !string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                return names.Count == 0 ? "GPU" : string.Join(" + ", names);
            }
            catch
            {
                return "GPU";
            }
        }

        // Ένας μετρητής ανά "engtype_3D" instance (ίδιο instance filtering με το Task Manager) - οι
        // μετρητές πρέπει να παραμείνουν ζωντανοί ανάμεσα σε κλήσεις (το NextValue() της πρώτης
        // κλήσης πάντα επιστρέφει 0 - ίδιος περιορισμός με τον υπάρχοντα μετρητή CPU). Ο καλών
        // (HomeView) κρατάει το αποτέλεσμα και καλεί Sample() σε κάθε tick του υπάρχοντος 1s timer.
        public static PerformanceCounter[]? CreateUsageCounters()
        {
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine")) return null;
                var category = new PerformanceCounterCategory("GPU Engine");
                var instances = category.GetInstanceNames().Where(n => n.Contains("engtype_3D")).ToArray();
                if (instances.Length == 0) return null;

                var counters = instances
                    .Select(n =>
                    {
                        PerformanceCounter? c = null;
                        try
                        {
                            c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                            c.NextValue(); // πρώτη κλήση πάντα 0 - προθέρμανση
                            return c;
                        }
                        catch { c?.Dispose(); return null; }
                    })
                    .Where(c => c != null)
                    .Cast<PerformanceCounter>()
                    .ToArray();
                return counters.Length > 0 ? counters : null;
            }
            catch
            {
                return null;
            }
        }

        public static int SampleUsagePercent(PerformanceCounter[] counters)
        {
            double sum = 0;
            foreach (var c in counters)
            {
                try { sum += c.NextValue(); } catch { /* instance can disappear mid-run (process exited) */ }
            }
            return (int)System.Math.Round(System.Math.Min(100, sum));
        }
    }
}
