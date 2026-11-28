using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

// Fixed, code-defined groups only. No user-supplied registry paths. Each value
// uses the existing typed journal; no default resets or subtree deletion.
// A partial failure retains all surviving journals for explicit recovery.
internal static class GroupedRegistryTweaks
{
    internal static IReadOnlyList<RegTweak> ClassicMenu => new[]
    {
        Value(RegistryHive.CurrentUser, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
            "", "", RegistryValueKind.String, "ClassicMenu.Default")
    };

    internal static IReadOnlyList<RegTweak> Mouse => new[]
    {
        Value(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String, "MouseAccel.Speed"),
        Value(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String, "MouseAccel.Threshold1"),
        Value(RegistryHive.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String, "MouseAccel.Threshold2")
    };

    internal static IReadOnlyList<RegTweak> GameBar => new[]
    {
        Value(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord, "GameBar.User"),
        Value(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord, "GameBar.Policy")
    };

    private static RegTweak Value(RegistryHive hive, string path, string name, object value,
        RegistryValueKind kind, string id) => new("", "", hive, path, name, value, kind, id);

    internal static bool? IsOn(IReadOnlyList<RegTweak> group)
    {
        try { return group.All(TweakRegistryJournal.IsApplied); }
        catch { return null; }
    }

    internal static IReadOnlyList<RegTweak> SelectOwned(IReadOnlyList<RegTweak> group, Func<RegTweak, bool> preflight)
    {
        // Materialize the complete preflight before any write.
        return group.Where(preflight).ToArray();
    }

    internal static bool CanRestore(IReadOnlyList<RegTweak> group)
    {
        try { return SelectOwned(group, t => TweakRegistryJournal.Preflight(t, true)).Count != 0; }
        catch { return false; }
    }

    internal static void Change(IReadOnlyList<RegTweak> group, string id, bool restore)
    {
        using var lease = CoreProcessLease.TryAcquire(@"Global\GearWin.RegistryGroup.v1." + id)
            ?? throw new IOException("Registry group is busy.");
        var owned = SelectOwned(group, t => TweakRegistryJournal.Preflight(t, restore));
        if (restore && owned.Count == 0)
            throw new InvalidOperationException("No verified original group values; nothing changed.");
        foreach (var tweak in restore ? owned : group)
        {
            if (restore) TweakRegistryJournal.Restore(tweak);
            else TweakRegistryJournal.Apply(tweak);
        }
    }

    internal static IEnumerable<(string Id, IReadOnlyList<RegTweak> Values)> Groups()
    {
        yield return ("ClassicMenu", ClassicMenu);
        yield return ("MouseAccel", Mouse);
        yield return ("GameBar", GameBar);
    }
}
