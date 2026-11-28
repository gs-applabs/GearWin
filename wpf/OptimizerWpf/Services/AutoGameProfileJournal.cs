using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

// Only selected live user settings. HAGS and launch-time GPU/FSO preferences
// are manual. The automatic profile does not change the active power plan.
internal sealed record AutoGamePlan(string Executable, TweakValue? ModeBefore, TweakValue? ModeAfter,
    TweakValue? DxBefore, TweakValue? DxAfter, bool? Windowed, bool? Vrr)
{
    internal bool IsValid
    {
        get
        {
            try
            {
                if (GameProfileService.CanonicalExe(Executable) != Executable) return false;
                bool mode = ModeBefore != null || ModeAfter != null;
                bool dx = DxBefore != null || DxAfter != null;
                return (mode || dx) &&
                    (!mode || ModeBefore is { IsValid: true, Kind: "missing" or "dword" } &&
                        ModeAfter is { IsValid: true, Kind: "dword", Data: "0" or "1" } && ModeBefore != ModeAfter) &&
                    (!dx ? Windowed == null && Vrr == null :
                        DxBefore is { IsValid: true, Kind: "missing" or "string" } &&
                        DxAfter is { IsValid: true, Kind: "string" } &&
                        (Windowed.HasValue || Vrr.HasValue) && DxBefore != DxAfter &&
                        DxAfter.Data == AutoGameProfileJournal.BuildDx(DxBefore.Data, Windowed, Vrr));
            }
            catch { return false; }
        }
    }
}

internal static class AutoGameProfileJournal
{
    private const string ModeKey = @"Software\Microsoft\GameBar";
    private const string ModeName = "AutoGameModeEnabled";
    private const string DxKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DxName = "DirectXUserGlobalSettings";

    private static string JournalPath
    {
        get
        {
            using var user = WindowsIdentity.GetCurrent();
            var sid = user.User?.Value ?? throw new IOException("Unknown registry user.");
            return ProtectedOperationJournal.PathFor("auto-game-" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))));
        }
    }

    private static AutoGamePlan? Read() => ProtectedOperationJournal.Read<AutoGamePlan>(JournalPath, p => p.IsValid);
    internal static bool HasPending => Read() != null;

    internal static string? BuildDx(string? before, bool? windowed, bool? vrr)
    {
        string? value = before;
        if (windowed.HasValue) value = GamingTweaksService.ReplaceOption(value, "SwapEffectUpgradeEnable", windowed.Value ? "1" : "0");
        if (vrr.HasValue) value = GamingTweaksService.ReplaceOption(value, "VRROptimizeEnable", vrr.Value ? "1" : "0");
        return value;
    }

    internal static AutoGamePlan? BuildPlan(string executable, GameProfileData profile, TweakValue mode, TweakValue dx)
    {
        if (!GameProfileService.Valid(profile) || !mode.IsValid || !dx.IsValid ||
            mode.Kind is not ("missing" or "dword") || dx.Kind is not ("missing" or "string"))
            throw new InvalidDataException("Invalid automatic profile input.");
        var global = profile.GlobalTweaks;
        TweakValue? modeAfter = global.TryGetValue("GameMode", out bool game)
            ? new TweakValue("dword", game ? "1" : "0") : null;
        bool? windowed = global.TryGetValue("WindowedOpt", out bool w) ? w : null;
        bool? vrr = global.TryGetValue("VrrOptimize", out bool v) ? v : null;
        TweakValue? dxAfter = windowed.HasValue || vrr.HasValue ? new("string", BuildDx(dx.Data, windowed, vrr)) : null;
        if (modeAfter == mode) modeAfter = null;
        if (dxAfter == dx) dxAfter = null;
        if (modeAfter == null && dxAfter == null) return null;
        var plan = new AutoGamePlan(GameProfileService.CanonicalExe(executable), modeAfter != null ? mode : null, modeAfter,
            dxAfter != null ? dx : null, dxAfter, dxAfter != null ? windowed : null, dxAfter != null ? vrr : null);
        return plan.IsValid ? plan : throw new InvalidDataException("Invalid automatic profile plan.");
    }

    private static TweakValue Current(string path, string name, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key == null) return TweakValue.Missing;
        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw == null && Array.FindIndex(key.GetValueNames(), n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) < 0)
            return TweakValue.Missing;
        if (key.GetValueKind(name) != kind) throw new IOException("Unexpected automatic gaming setting type.");
        var value = TweakValue.Capture(raw, kind);
        if (!value.IsValid) throw new IOException("Invalid automatic gaming setting.");
        return value;
    }

    private static void Write(string path, string name, RegistryValueKind kind, TweakValue expected, TweakValue desired)
    {
        if (Current(path, name, kind) != expected) throw new IOException("Gaming setting changed externally.");
        if (expected == desired) return;
        using var key = desired == TweakValue.Missing ? Registry.CurrentUser.OpenSubKey(path, true) :
            Registry.CurrentUser.CreateSubKey(path, true);
        if (desired == TweakValue.Missing) key?.DeleteValue(name, false);
        else (key ?? throw new IOException("Cannot open gaming registry key.")).SetValue(name, desired.ToRegistryValue(), kind);
        key?.Flush();
        if (Current(path, name, kind) != desired) throw new IOException("Gaming setting readback failed.");
    }

    internal static bool CanRestoreValue(TweakValue current, TweakValue before, TweakValue after) =>
        current == before || current == after;

    // All callers hold the shared power/profile lease; manual global controls
    // restore this journal before editing so that an automatic session cannot
    // silently undo a later manual choice.
    internal static void RestoreUnderPowerLease()
    {
        var plan = Read();
        if (plan == null) return;
        var mode = plan.ModeBefore == null ? null : Current(ModeKey, ModeName, RegistryValueKind.DWord);
        var dx = plan.DxBefore == null ? null : Current(DxKey, DxName, RegistryValueKind.String);
        if ((mode != null && !CanRestoreValue(mode, plan.ModeBefore!, plan.ModeAfter!)) ||
            (dx != null && !CanRestoreValue(dx, plan.DxBefore!, plan.DxAfter!)))
            throw new IOException("Automatic gaming settings changed externally. Recovery journal retained.");
        if (mode != null) Write(ModeKey, ModeName, RegistryValueKind.DWord, mode, plan.ModeBefore!);
        if (dx != null) Write(DxKey, DxName, RegistryValueKind.String, dx, plan.DxBefore!);
        File.Delete(JournalPath);
    }

    internal static void Restore()
    {
        using var lease = UltimatePlanJournal.PowerLease();
        RestoreUnderPowerLease();
    }

    internal static void Apply(string executable, GameProfileData profile)
    {
        // Do not acquire PowerModeService.Gate after the power lease;
        // the protected snapshot is read directly under the same resource lease.
        using var lease = UltimatePlanJournal.PowerLease();
        if (PowerModeService.HasSavedProfileUnderPowerLease())
            throw new IOException("A power profile already owns gaming settings.");
        if (Read() != null) throw new IOException("Recover previous automatic gaming settings first.");
        var global = profile.GlobalTweaks;
        var mode = global.ContainsKey("GameMode") ? Current(ModeKey, ModeName, RegistryValueKind.DWord) : TweakValue.Missing;
        var dx = global.ContainsKey("WindowedOpt") || global.ContainsKey("VrrOptimize") ?
            Current(DxKey, DxName, RegistryValueKind.String) : TweakValue.Missing;
        var plan = BuildPlan(executable, profile, mode, dx);
        if (plan == null) return;
        ProtectedOperationJournal.Save(JournalPath, plan, p => p.IsValid);
        try
        {
            if (plan.ModeBefore != null) Write(ModeKey, ModeName, RegistryValueKind.DWord, plan.ModeBefore, plan.ModeAfter!);
            if (plan.DxBefore != null) Write(DxKey, DxName, RegistryValueKind.String, plan.DxBefore, plan.DxAfter!);
        }
        catch
        {
            RestoreUnderPowerLease();
            throw;
        }
    }
}
