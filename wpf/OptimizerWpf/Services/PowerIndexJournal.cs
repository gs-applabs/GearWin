using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OptimizerWpf.Services;

internal sealed record PowerIndexSpec(string Id, Guid Subgroup, Guid Setting, bool Both, uint Maximum)
{
    internal static PowerIndexSpec Threshold { get; } = new("PowerSaverThreshold",
        new("de830923-a562-41af-a086-e3a2c6bad2da"), new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4"), false, 100);
    internal static PowerIndexSpec Usb { get; } = new("PowerThrottling",
        new("2a737441-1930-4402-8d77-b2bebba308a3"), new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226"), true, 1);
    internal static PowerIndexSpec Pcie { get; } = new("PowerPerfBoost",
        new("501a4d13-42af-4429-9fd1-a8218c268e20"), new("ee12f906-d277-404b-b6da-e5fa1a576df5"), true, 2);

    internal bool Accepts(PowerIndices value) => value.Dc <= Maximum &&
        (Both ? value.Ac.HasValue && value.Ac.Value <= Maximum : !value.Ac.HasValue);
    internal PowerIndices Target => new(Both ? 0u : (uint?)null, 0u);
}

internal sealed record PowerIndices(uint? Ac, uint Dc);

internal sealed record PowerIndexSnapshot(string Id, Guid Scheme, uint? OriginalAc, uint OriginalDc)
{
    internal PowerIndices Original => new(OriginalAc, OriginalDc);
    internal bool IsValid(PowerIndexSpec spec, Guid scheme) => scheme != Guid.Empty &&
        Scheme == scheme && Id == spec.Id && spec.Accepts(Original) && Original != spec.Target;

    // Partial application/rollback is recoverable, but external values are not overwritten.
    internal bool CanChange(PowerIndexSpec spec, Guid scheme, PowerIndices current) =>
        IsValid(spec, scheme) && spec.Accepts(current) &&
        (current.Ac == OriginalAc || current.Ac == spec.Target.Ac) &&
        (current.Dc == OriginalDc || current.Dc == spec.Target.Dc);
}

internal static class PowerIndexJournal
{
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    private static uint ReadOne(PowerIndexSpec spec, Guid scheme, bool ac)
    {
        Guid subgroup = spec.Subgroup, setting = spec.Setting;
        uint value;
        uint rc = ac ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
                     : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        if (rc != 0 || value > spec.Maximum) throw new IOException("Unsupported or unreadable power setting: " + spec.Id);
        return value;
    }

    private static PowerIndices Read(PowerIndexSpec spec, Guid scheme) =>
        new(spec.Both ? ReadOne(spec, scheme, true) : (uint?)null, ReadOne(spec, scheme, false));

    private static void AssertActive(Guid scheme)
    {
        if (PowerModeService.GetPlan() != scheme) throw new IOException("Active power plan changed; journal retained.");
    }

    private static void WriteOne(PowerIndexSpec spec, Guid scheme, bool ac, uint expected, uint target)
    {
        AssertActive(scheme);
        if (ReadOne(spec, scheme, ac) != expected) throw new IOException("Power setting changed externally.");
        if (expected == target) return;
        Guid subgroup = spec.Subgroup, setting = spec.Setting;
        uint rc = ac ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, target)
                     : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, target);
        if (rc != 0 || ReadOne(spec, scheme, ac) != target)
            throw new IOException("Power setting write not verified; journal retained.");
    }

    private static void Write(PowerIndexSpec spec, Guid scheme, PowerIndices current, PowerIndices target)
    {
        if (spec.Both) WriteOne(spec, scheme, true, current.Ac!.Value, target.Ac!.Value);
        WriteOne(spec, scheme, false, current.Dc, target.Dc);
        AssertActive(scheme);
        // PowerModeService.SetPlan short-circuits on same plan; these changes
        // require reapplying the current plan even if its identity is unchanged.
        if (PowerSetActiveScheme(IntPtr.Zero, ref scheme) != 0)
            throw new IOException("Power settings not activated; journal retained.");
        AssertActive(scheme);
        if (Read(spec, scheme) != target) throw new IOException("Power settings readback failed.");
    }

    internal static bool? IsOn(PowerIndexSpec spec)
    {
        try
        {
            var scheme = PowerModeService.GetPlan();
            var value = Read(spec, scheme);
            AssertActive(scheme);
            return value == spec.Target;
        }
        catch { return null; }
    }

    internal static void Change(PowerIndexSpec spec, bool enable)
    {
        using var lease = UltimatePlanJournal.PowerLease();
        var scheme = PowerModeService.GetPlan();
        var path = ProtectedOperationJournal.PathFor("power-" + spec.Id + "-" + scheme.ToString("N"));
        bool Validate(PowerIndexSnapshot state) => state.IsValid(spec, scheme);
        var snapshot = ProtectedOperationJournal.Read<PowerIndexSnapshot>(path, Validate);
        var current = Read(spec, scheme);
        if (snapshot == null)
        {
            if (!enable) throw new IOException("No verified original power setting on this plan.");
            if (current == spec.Target) return;
            snapshot = new PowerIndexSnapshot(spec.Id, scheme, current.Ac, current.Dc);
            ProtectedOperationJournal.Save(path, snapshot, Validate);
        }
        if (!snapshot.CanChange(spec, scheme, current))
            throw new IOException("Power setting changed externally; journal retained.");
        Write(spec, scheme, current, enable ? spec.Target : snapshot.Original);
        if (!enable) File.Delete(path);
    }
}
