using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

// An explicit on/off command, NOT a restoration of prior hibernation contents
// or a promise to reconstruct the previous file type/size or Fast Startup.
internal static class HibernateSettings
{
    internal static bool? Decode(object? value, RegistryValueKind kind) =>
        kind == RegistryValueKind.DWord && value is int n && n is 0 or 1 ? n == 1 : null;

    internal static bool? IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            if (key == null) return null;
            var value = key.GetValue("HibernateEnabled");
            return value == null ? null : Decode(value, key.GetValueKind("HibernateEnabled"));
        }
        catch { return null; }
    }

    internal static ProcessStartInfo Command(bool enabled)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("/hibernate");
        info.ArgumentList.Add(enabled ? "on" : "off");
        return info;
    }

    internal static void Change(bool enabled)
    {
        using var lease = UltimatePlanJournal.PowerLease();
        bool before = IsEnabled() ?? throw new InvalidOperationException("Hibernation state is unknown.");
        if (before == enabled) return;
        using var child = Process.Start(Command(enabled)) ?? throw new IOException("Cannot start powercfg.");
        var stdout = HealthCleanupService.ReadBoundedOutputAsync(child.StandardOutput, 4096);
        var stderr = HealthCleanupService.ReadBoundedOutputAsync(child.StandardError, 4096);
        // Wait for the actual mutation; do not kill a live power transaction.
        child.WaitForExit();
        Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        if (child.ExitCode != 0 || IsEnabled() != enabled)
            throw new IOException("Hibernation change not verified: " + stderr.Result);
    }
}
