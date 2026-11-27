using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Retain the single verified duplicate for reuse. Never delete a power plan
// automatically: users can edit/rename it after its creation.
internal sealed record UltimatePlanSnapshot(Guid Scheme, Guid? Previous, bool Created)
{
    internal bool IsValid => Scheme != Guid.Empty && Scheme != UltimatePlanJournal.Template &&
        Previous != Guid.Empty && Previous != Scheme && (Created || Previous.HasValue);

    internal bool CanRestore(Guid active) => IsValid && Previous.HasValue &&
        (active == Previous.Value || (Created && active == Scheme));
}

internal static class UltimatePlanJournal
{
    internal static readonly Guid Template = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static string PathName => ProtectedOperationJournal.PathFor("ultimate-plan");
    private static bool Valid(UltimatePlanSnapshot state) => state.IsValid;

    internal static IDisposable PowerLease() =>
        CoreProcessLease.TryAcquire(@"Global\GearWin.PowerPlan.v1")
        ?? throw new IOException("Another GearWin power-plan operation is active.");

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup,
        uint access, uint index, out Guid buffer, ref uint bufferSize);

    private static bool Exists(Guid target)
    {
        const uint AccessScheme = 16, NoMoreItems = 259;
        for (uint index = 0; index < 4096; index++)
        {
            uint size = 16;
            uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, out var guid, ref size);
            if (result == NoMoreItems) return false;
            if (result != 0 || size != 16) throw new IOException("Unable to enumerate power plans.");
            if (guid == target) return true;
        }
        throw new IOException("Power plan enumeration exceeded its safety bound.");
    }

    private static void Duplicate(Guid scheme)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("/duplicatescheme");
        psi.ArgumentList.Add(Template.ToString("D"));
        psi.ArgumentList.Add(scheme.ToString("D"));
        using var process = Process.Start(psi) ?? throw new IOException("Cannot start powercfg.");
        var output = HealthCleanupService.ReadBoundedOutputAsync(process.StandardOutput, 4096);
        var error = HealthCleanupService.ReadBoundedOutputAsync(process.StandardError, 4096);
        // Do not kill an active mutation. Observe both streams and actual exit.
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0 || !Exists(scheme))
            throw new IOException("Power plan duplication could not be verified.");
    }

    private static UltimatePlanSnapshot? Load() =>
        ProtectedOperationJournal.Read<UltimatePlanSnapshot>(PathName, Valid);

    private static void Save(UltimatePlanSnapshot state, bool replace = false) =>
        ProtectedOperationJournal.Save(PathName, state, Valid, replace);

    internal static bool IsActive()
    {
        try
        {
            var active = PowerModeService.GetPlan();
            var state = Load();
            return active == Template || (state is { Created: true } && state.Scheme == active);
        }
        catch { return false; }
    }

    internal static void Enable()
    {
        using var lease = PowerLease();
        var active = PowerModeService.GetPlan();
        var state = Load();
        if (state == null)
        {
            if (active == Template) return; // already selected outside GearWin
            if (TweakBackupService.GetBackup("UP_CREATED_SCHEME") != null ||
                TweakBackupService.GetBackup("UP_PREVIOUS_SCHEME") != null)
                throw new IOException("Legacy power plan backup requires manual review.");
            state = new UltimatePlanSnapshot(Guid.NewGuid(), active, false);
            if (Exists(state.Scheme)) throw new IOException("Generated power plan identity already exists.");
            Save(state);
        }
        else if (state.Previous == null)
        {
            if (active == state.Scheme || active == Template) return;
            if (!state.Created || !Exists(state.Scheme)) throw new IOException("Saved duplicate is missing; manual review required.");
            state = state with { Previous = active };
            Save(state, replace: true);
        }
        else if (active != state.Previous && (!state.Created || active != state.Scheme))
            throw new IOException("Power plan changed externally; restore or review the saved operation.");

        if (!state.Created)
        {
            // A crash after duplicate but before verified publication is ambiguous:
            // never adopt/delete a plan merely because its GUID exists.
            if (Exists(state.Scheme)) throw new IOException("Unconfirmed duplicate exists; manual review required.");
            if (PowerModeService.GetPlan() != state.Previous) throw new IOException("Active plan changed.");
            Duplicate(state.Scheme);
            state = state with { Created = true };
            Save(state, replace: true);
        }
        if (!Exists(state.Scheme)) throw new IOException("Saved power plan is missing.");
        active = PowerModeService.GetPlan();
        if (active == state.Scheme) return;
        if (active != state.Previous) throw new IOException("Active plan changed before activation.");
        PowerModeService.SetPlan(state.Scheme);
    }

    internal static void Disable()
    {
        using var lease = PowerLease();
        var state = Load();
        if (state == null || state.Previous == null)
            throw new IOException("No verified original power plan; nothing was changed.");
        var active = PowerModeService.GetPlan();
        if (!state.CanRestore(active)) throw new IOException("Active power plan changed externally; journal retained.");
        if (!state.Created)
        {
            if (Exists(state.Scheme)) throw new IOException("Unconfirmed duplicate requires manual review.");
            if (PowerModeService.GetPlan() != state.Previous) throw new IOException("Active plan changed.");
            File.Delete(PathName);
            return;
        }
        if (!Exists(state.Scheme)) throw new IOException("Saved duplicate missing; journal retained.");
        if (PowerModeService.GetPlan() != active) throw new IOException("Active plan changed before restoration.");
        if (active != state.Previous.Value) PowerModeService.SetPlan(state.Previous.Value);
        if (PowerModeService.GetPlan() != state.Previous.Value) throw new IOException("Original power plan not verified.");
        Save(state with { Previous = null }, replace: true);
    }
}
