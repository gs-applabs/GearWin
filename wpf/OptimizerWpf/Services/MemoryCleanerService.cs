using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    // Conservative memory maintenance: pressure-gated, no global modified/standby-list purge.
    public static class MemoryCleanerService
    {
        public readonly record struct MemoryStats(long TotalBytes, long AvailableBytes, long FreeBytes, long StandbyBytes, long ModifiedBytes)
        {
            public double FreeGb => FreeBytes / 1073741824.0;
            public double AvailableGb => AvailableBytes / 1073741824.0;
        }

        public sealed record MemoryCleanResult(MemoryStats Before, MemoryStats After, int TrimmedProcesses, bool ListsPurged)
        {
            // "Ελεύθερη" = μνήμη που δεν χρησιμοποιείται ούτε για cache (Zero+Free list) - αυτή είναι που αυξάνει πραγματικά.
            public double FreedGb => Math.Max(0, (After.AvailableBytes - Before.AvailableBytes) / 1073741824.0);
        }

        private const int SystemMemoryListInformation = 80;

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemMemoryList
        {
            public UIntPtr ZeroPageCount, FreePageCount, ModifiedPageCount, ModifiedNoWritePageCount, BadPageCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] PageCountByPriority;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] RepurposedPagesByPriority;
            public UIntPtr ModifiedPageCountPageFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx s);
        [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, ref SystemMemoryList info, int length, out int returned);

        private static int PageSize => Environment.SystemPageSize;

        public static MemoryStats Snapshot()
        {
            var ms = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref ms)) return default;
            long free = 0, standby = 0, modified = 0;
            try
            {
                var info = new SystemMemoryList { PageCountByPriority = new UIntPtr[8], RepurposedPagesByPriority = new UIntPtr[8] };
                if (NtQuerySystemInformation(SystemMemoryListInformation, ref info, Marshal.SizeOf<SystemMemoryList>(), out _) == 0)
                {
                    free = (long)((ulong)info.ZeroPageCount + (ulong)info.FreePageCount) * PageSize;
                    modified = (long)(ulong)info.ModifiedPageCount * PageSize;
                    foreach (var p in info.PageCountByPriority) standby += (long)(ulong)p * PageSize;
                }
            }
            catch { /* οι αριθμοί λιστών είναι πληροφοριακοί - δεν πρέπει να ακυρώσουν τη δράση */ }
            return new MemoryStats((long)ms.TotalPhys, (long)ms.AvailPhys, free, standby, modified);
        }

        internal static bool NeedsMaintenance(MemoryStats stats) =>
            stats.TotalBytes > 0 && stats.AvailableBytes >= 0 &&
            stats.AvailableBytes < Math.Max(512L * 1024 * 1024, stats.TotalBytes / 10);

        public static string Describe(MemoryCleanResult r)
        {
            var greek = LanguageService.Current == "el";
            var prefix = greek ? "Διαθέσιμη RAM" : "Available RAM";
            if (r.Before.TotalBytes <= 0 || r.After.TotalBytes <= 0)
                return greek ? "Η μέτρηση RAM δεν είναι διαθέσιμη." : "RAM measurement unavailable.";
            var message = $"{prefix}: {r.Before.AvailableGb:0.0} → {r.After.AvailableGb:0.0} GB";
            if (!NeedsMaintenance(r.Before))
                return message + (greek ? " · Δεν απαιτείται επέμβαση." : " · No intervention needed.");
            return message + (greek ? $" · Διεργασίες: {r.TrimmedProcesses}" : $" · Processes: {r.TrimmedProcesses}");
        }

        public static async Task<MemoryCleanResult> CleanAsync()
        {
            using var lease = await GearWinCore.EnterAsync(CoreWork.Memory).ConfigureAwait(false);
            try
            {
                return await Task.Run(async () =>
                {
                    var before = Snapshot();
                    if (!NeedsMaintenance(before)) return new MemoryCleanResult(before, before, 0, false);
                    // Do not flush modified pages or empty the standby cache: this can trigger
                    // needless disk writes and costly page faults without increasing useful memory.
                    var (trimmed, _) = await SystemService.FreeBackgroundMemoryAsync();
                    return new MemoryCleanResult(before, Snapshot(), trimmed, false);
                }).ConfigureAwait(false);
            }
            finally { HealthScoreService.Invalidate(); }
        }
    }
}
