using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OptimizerWpf.Services;

internal sealed record ServiceStartSnapshot(string Service, uint Original, uint Applied)
{
    internal bool IsValid(string name) =>
        string.Equals(Service, name, StringComparison.OrdinalIgnoreCase) && Original == 2 && Applied == 3;

    internal bool IsValidDisable() =>
        string.Equals(Service, "SysMain", StringComparison.OrdinalIgnoreCase) && Original is 2 or 3 && Applied == 4;
}

internal static class ServiceStartJournal
{
    internal const uint Automatic = 2, Manual = 3, Disabled = 4;
    internal static bool CanDisable(uint mode, bool delayed) => !delayed && mode is Automatic or Manual;
    internal static bool CanChange(uint mode, bool delayed) => mode == Automatic && !delayed;
    internal static bool CanRestore(uint mode, bool delayed) => !delayed && (mode == Automatic || mode == Manual);

    private sealed class ScHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ScHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ScHandle OpenService(ScHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(ScHandle service, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2(ScHandle service, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(ScHandle service, uint type, uint start, uint error,
        string? binary, string? group, IntPtr tag, string? dependencies, string? account, string? password, string? display);

    private static (uint Mode, bool Delayed) Read(ScHandle service)
    {
        if (QueryServiceConfig(service, IntPtr.Zero, 0, out uint needed) ||
            Marshal.GetLastWin32Error() != 122 || needed < 12 || needed > 65536)
            throw new IOException("Cannot read service startup configuration.");
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            uint mode = unchecked((uint)Marshal.ReadInt32(buffer, 4));
            if (!QueryServiceConfig2(service, 3, buffer, needed, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return (mode, Marshal.ReadInt32(buffer) != 0);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void Set(ScHandle service, uint mode)
    {
        const uint NoChange = 0xffffffff;
        if (!ChangeServiceConfig(service, NoChange, mode, NoChange, null, null, IntPtr.Zero, null, null, null, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (Read(service) != (mode, false)) throw new IOException("Service change not verified; journal retained.");
    }

    // Only the existing SystemService allowlist is accepted by the public entry points.
    internal static bool Change(string name, bool restore)
    {
        var key = name.ToUpperInvariant();
        using var lease = CoreProcessLease.TryAcquire(@"Global\GearWin.ServiceStartup.v1." + key)
            ?? throw new IOException("Service configuration is busy.");
        string path = ProtectedOperationJournal.PathFor("service-" + key);
        bool Validate(ServiceStartSnapshot s) => s.IsValid(name);
        var snapshot = ProtectedOperationJournal.Read<ServiceStartSnapshot>(path, Validate);
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenService(manager, name, 1 | 2);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var current = Read(service);

        if (restore)
        {
            if (snapshot == null || !CanRestore(current.Mode, current.Delayed)) return false;
            if (Read(service) != current) return false;
            if (current.Mode != Automatic) Set(service, Automatic);
            if (Read(service) != (Automatic, false)) return false;
            File.Delete(path);
            return true;
        }

        if (snapshot == null)
        {
            // Legacy string data is not ownership evidence; do not overwrite it.
            if (TweakBackupService.GetBackup("SVC_" + name) != null || !CanChange(current.Mode, current.Delayed))
                return false;
            snapshot = new ServiceStartSnapshot(name, Automatic, Manual);
            ProtectedOperationJournal.Save(path, snapshot, Validate);
        }
        if (!CanRestore(current.Mode, current.Delayed) || Read(service) != current) return false;
        if (current.Mode != Manual) Set(service, Manual);
        return Read(service) == (Manual, false);
    }

    // Same service lock as the existing manual-start optimizer. A journal
    // created by either tool cannot be silently reinterpreted by the other.
    internal static bool ChangeSysMain(bool restore)
    {
        const string name = "SysMain";
        using var lease = CoreProcessLease.TryAcquire(@"Global\GearWin.ServiceStartup.v1.SYSMAIN")
            ?? throw new IOException("SysMain configuration is busy.");
        string path = ProtectedOperationJournal.PathFor("service-SYSMAIN");
        var snapshot = ProtectedOperationJournal.Read<ServiceStartSnapshot>(path, s => s.IsValidDisable());
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenService(manager, name, 1 | 2);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var current = Read(service);

        if (snapshot == null)
        {
            if (restore || TweakBackupService.GetBackup("SVC_" + name) != null || !CanDisable(current.Mode, current.Delayed))
                return false;
            snapshot = new ServiceStartSnapshot(name, current.Mode, Disabled);
            ProtectedOperationJournal.Save(path, snapshot, s => s.IsValidDisable());
        }

        if (current.Delayed || (current.Mode != snapshot.Original && current.Mode != Disabled) || Read(service) != current)
            return false;
        uint target = restore ? snapshot.Original : Disabled;
        if (current.Mode != target) Set(service, target);
        if (Read(service) != (target, false)) return false;
        if (restore) File.Delete(path);
        // Never stop or start a running service. Startup change takes effect
        // normally on next boot; no claim that RAM is immediately released.
        return true;
    }

    internal static bool CanRestoreSysMain()
    {
        try
        {
            var snapshot = ProtectedOperationJournal.Read<ServiceStartSnapshot>(
                ProtectedOperationJournal.PathFor("service-SYSMAIN"), s => s.IsValidDisable());
            if (snapshot == null) return false;
            using var manager = OpenSCManager(null, null, 1);
            if (manager.IsInvalid) return false;
            using var service = OpenService(manager, "SysMain", 1);
            if (service.IsInvalid) return false;
            var current = Read(service);
            return !current.Delayed && (current.Mode == snapshot.Original || current.Mode == Disabled);
        }
        catch { return false; }
    }

}
