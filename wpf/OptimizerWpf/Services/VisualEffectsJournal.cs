using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

internal sealed record VisualPresetPlan(int Mode, int Enabled)
{
    internal bool IsValid => (Mode == 0 && Enabled == 1) || (Mode == 1 && Enabled == 1) || (Mode == 2 && Enabled == 0);
}

internal static class VisualEffectsJournal
{
    internal static RegTweak[] Values(VisualPresetPlan plan)
    {
        if (!plan.IsValid) throw new ArgumentException("Unsupported visual preset.");
        const string desktop = @"Control Panel\Desktop";
        const string explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
        RegTweak Value(string path, string name, object value, RegistryValueKind kind) =>
            new("", "", RegistryHive.CurrentUser, path, name, value, kind, "VisualEffects." + name);
        string text = plan.Enabled == 0 ? "0" : "1";
        return new[]
        {
            Value(desktop, "DragFullWindows", text, RegistryValueKind.String),
            Value(desktop + @"\WindowMetrics", "MinAnimate", text, RegistryValueKind.String),
            Value(explorer + @"\Advanced", "ListviewAlphaSelect", plan.Enabled, RegistryValueKind.DWord),
            Value(explorer + @"\Advanced", "TaskbarAnimations", plan.Enabled, RegistryValueKind.DWord),
            Value(explorer + @"\Advanced", "ListviewShadow", plan.Enabled, RegistryValueKind.DWord),
            Value(explorer + @"\Advanced", "EnableTransparency", plan.Enabled, RegistryValueKind.DWord),
            Value(explorer + @"\VisualEffects", "VisualFXSetting", plan.Mode, RegistryValueKind.DWord),
        };
    }

    private static string UserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User?.Value ??
            throw new InvalidOperationException("Unknown current user."))));
    }

    private static string PlanPath => ProtectedOperationJournal.PathFor("visual-" + UserId());
    private static VisualPresetPlan? Read() =>
        ProtectedOperationJournal.Read<VisualPresetPlan>(PlanPath, p => p.IsValid);
    private static IDisposable Lease() =>
        CoreProcessLease.TryAcquire(@"Global\GearWin.VisualEffects.v1." + UserId())
        ?? throw new IOException("Visual preset is busy.");

    private static void RestoreOwned(VisualPresetPlan plan)
    {
        var owned = GroupedRegistryTweaks.SelectOwned(Values(plan), t => TweakRegistryJournal.Preflight(t, true));
        foreach (var t in owned) TweakRegistryJournal.Restore(t);
        File.Delete(PlanPath);
    }

    internal static void Apply(int mode, int enabled)
    {
        var desired = new VisualPresetPlan(mode, enabled);
        var values = Values(desired); // validate arguments before touching any journal
        using var lease = Lease();
        var previous = Read();
        if (previous != null && previous != desired)
        {
            // Complete the prior restoration first. Any failed restore keeps
            // the prior plan, and a new preset is not started.
            RestoreOwned(previous);
            previous = null;
        }
        _ = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, false));
        if (previous == null)
        {
            if (values.All(TweakRegistryJournal.IsApplied)) return;
            ProtectedOperationJournal.Save(PlanPath, desired, p => p.IsValid);
        }
        GroupedRegistryTweaks.Change(values, "VisualEffects." + UserId(), false);
        if (!values.All(TweakRegistryJournal.IsApplied))
            throw new IOException("Visual registry values could not be verified; journal retained.");
        // Windows may cache these settings until sign-out. No Explorer kill.
    }

    internal static bool CanRestore()
    {
        try { var plan = Read(); return plan != null && GroupedRegistryTweaks.CanRestore(Values(plan)); }
        catch { return false; }
    }

    internal static void Restore()
    {
        using var lease = Lease();
        var plan = Read() ?? throw new InvalidOperationException("No verified visual preset journal.");
        RestoreOwned(plan);
    }
}
