using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

internal sealed record BackgroundPackagePlan(string Packages)
{
    internal bool IsValid => BackgroundAppJournal.ValidPlan(Packages);
}

// An opt-in change to removable, non-system packages for the current user.
// The durable plan is saved before the first value mutation, so even removed
// packages or interrupted batches can be restored without re-enumerating apps.
internal static class BackgroundAppJournal
{
    internal const int MaxPackages = 256;
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    internal static bool ValidName(string? name) => name != null &&
        Regex.IsMatch(name, @"\A[A-Za-z0-9][A-Za-z0-9.-]{0,199}_[A-Za-z0-9]{13}\z", RegexOptions.CultureInvariant);

    internal static string[] ParsePackages(string output)
    {
        if (output.Length > 60000 || output.Contains("[Output truncated]", StringComparison.Ordinal))
            throw new InvalidDataException("Package inventory exceeded its limit.");
        var names = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0 || names.Length > MaxPackages || names.Any(n => !ValidName(n)))
            throw new InvalidDataException("Package inventory is empty, invalid or exceeds its limit.");
        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool ValidPlan(string? packages)
    {
        if (packages == null) return false;
        try { return packages == string.Join("\n", ParsePackages(packages)); }
        catch (InvalidDataException) { return false; }
    }

    internal static RegTweak[] Values(string packages)
    {
        if (!ValidPlan(packages)) throw new InvalidDataException("Invalid package plan.");
        return ParsePackages(packages).SelectMany(p => new[] { "Disabled", "DisabledByUser" }.Select(n =>
            new RegTweak("", "", RegistryHive.CurrentUser, Root + "\\" + p, n, 1, RegistryValueKind.DWord,
                "BackgroundApps." + p + "." + n))).ToArray();
    }

    private static string UserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User?.Value ??
            throw new InvalidOperationException("Unknown current user."))));
    }

    private static string PlanPath => ProtectedOperationJournal.PathFor("bgapps-" + UserId());
    private static BackgroundPackagePlan? Read() =>
        ProtectedOperationJournal.Read<BackgroundPackagePlan>(PlanPath, p => p.IsValid);

    private static IDisposable Lease() =>
        CoreProcessLease.TryAcquire(@"Global\GearWin.BackgroundApps.v1." + UserId())
        ?? throw new IOException("Background app settings are busy.");

    private static async Task<string[]> InventoryAsync()
    {
        const string script = "$ErrorActionPreference='Stop'; Get-AppxPackage | Where-Object { " +
            "-not $_.IsFramework -and -not $_.IsResourcePackage -and $_.NonRemovable -eq $false " +
            "-and $_.SignatureKind -ne 'System' } | Select-Object -ExpandProperty PackageFamilyName";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            PowerShellArgs.Command(script))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var child = Process.Start(start) ?? throw new IOException("Package inventory did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stdout = HealthCleanupService.ReadBoundedOutputAsync(child.StandardOutput, 60000);
        var stderr = HealthCleanupService.ReadBoundedOutputAsync(child.StandardError, 4096);
        var streams = Task.WhenAll(stdout, stderr);
        try
        {
            await child.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var results = await streams.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (child.ExitCode != 0 || !string.IsNullOrWhiteSpace(results[1]))
                throw new IOException("Package inventory failed; no settings changed.");
            return ParsePackages(results[0]);
        }
        catch (OperationCanceledException)
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Package inventory timed out; no settings changed.");
        }
        finally
        {
            _ = streams.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal static bool? IsOn()
    {
        try
        {
            var plan = Read();
            return plan != null && Values(plan.Packages).All(TweakRegistryJournal.IsApplied);
        }
        catch { return null; }
    }

    internal static bool CanRestore()
    {
        try { var plan = Read(); return plan != null && GroupedRegistryTweaks.CanRestore(Values(plan.Packages)); }
        catch { return false; }
    }

    internal static void Change(bool restore)
    {
        using var lease = Lease();
        var path = PlanPath;
        var plan = Read();
        if (restore)
        {
            if (plan == null) throw new InvalidOperationException("No verified original background app settings; use Windows Settings for manual review.");
            var values = Values(plan.Packages);
            var owned = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, true));
            foreach (var t in owned) TweakRegistryJournal.Restore(t);
            File.Delete(path);
            return;
        }
        if (plan == null)
        {
            plan = new BackgroundPackagePlan(string.Join("\n", InventoryAsync().GetAwaiter().GetResult()));
            var values = Values(plan.Packages);
            _ = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, false));
            if (values.All(TweakRegistryJournal.IsApplied))
                throw new InvalidOperationException("Selected app settings already match; GearWin did not change them.");
            ProtectedOperationJournal.Save(path, plan, p => p.IsValid);
        }
        GroupedRegistryTweaks.Change(Values(plan.Packages), "BackgroundApps." + UserId(), false);
    }
}
