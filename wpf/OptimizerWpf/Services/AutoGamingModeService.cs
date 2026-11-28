using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OptimizerWpf.Services;

// Opt-in, existing five-second timer. No generic high-performance plan or
// reboot-required changes are made on behalf of a per-game saved profile.
public static class AutoGamingModeService
{
    private static DispatcherTimer? _timer;
    internal static bool IsRunning => _timer != null;
    private static string? _candidate, _active;
    private static int _stableTicks, _misses;
    private static bool _recoveryChecked;
    private static bool _reportedFailure;
    private static long _lastProfileProbe;
    private static long _lastFailureAt;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    internal static bool CoversMonitor(int left, int top, int right, int bottom,
        int monitorLeft, int monitorTop, int monitorRight, int monitorBottom) =>
        monitorRight > monitorLeft && monitorBottom > monitorTop &&
        left == monitorLeft && top == monitorTop && right == monitorRight && bottom == monitorBottom;

    public static void Start()
    {
        if (_timer != null || !AppSettingsService.Current.AutoGamingModeEnabled) return;
        _candidate = _active = null;
        _stableTicks = _misses = 0;
        _recoveryChecked = _reportedFailure = false;
        _lastProfileProbe = _lastFailureAt = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public static void Stop()
    {
        if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        // An interrupted session remains recoverable even after disable/exit.
        // Never start a retry timer for a disabled feature.
        if (_active != null || _recoveryChecked)
        {
            try { AutoGameProfileJournal.Restore(); _active = null; }
            catch (Exception ex) { Report(ex); }
        }
        _candidate = null;
        _stableTicks = _misses = 0;
        _recoveryChecked = false;
    }

    private static void Report(Exception ex)
    {
        if (_reportedFailure) return;
        _reportedFailure = true;
        Trace.TraceWarning("Automatic gaming: " + ex.Message);
        StatusService.Report(ex.Message);
    }

    internal static int NextStableTicks(string? previous, string? current, int ticks) =>
        current == null ? 0 : string.Equals(previous, current, StringComparison.Ordinal) ? Math.Min(2, ticks + 1) : 1;

    private static void OnTick(object? sender, EventArgs e)
    {
        if (!AppSettingsService.Current.AutoGamingModeEnabled) return;
        if (_lastFailureAt != 0 && SharedTelemetryService.IsFresh(Environment.TickCount64, _lastFailureAt, 30000)) return;
        try
        {
            // Recover an interrupted automatic session before any new one.
            if (!_recoveryChecked)
            {
                using var lease = GearWinCore.TryEnterBackground(CoreWork.ProfileChange);
                if (lease == null) return;
                AutoGameProfileJournal.Restore();
                _recoveryChecked = true;
                _reportedFailure = false;
            }
            var settings = AppSettingsService.Current;
            var current = settings.GamingModeActive || settings.OfficeModeActive ? null : ForegroundGame();
            string? identity = current?.Identity;
            if (_candidate != identity) _lastProfileProbe = 0;
            _stableTicks = NextStableTicks(_candidate, identity, _stableTicks);
            _candidate = identity;
            if (_active != null)
            {
                _misses = _active == identity ? 0 : Math.Min(2, _misses + 1);
                if (_misses < 2) return;
                using var restoreLease = GearWinCore.TryEnterBackground(CoreWork.ProfileChange);
                if (restoreLease == null) return;
                AutoGameProfileJournal.Restore();
                _active = null;
                _reportedFailure = false;
                _misses = 0;
            }
            if (current == null || _stableTicks < 2) return;
            long now = Environment.TickCount64;
            if (_lastProfileProbe != 0 && SharedTelemetryService.IsFresh(now, _lastProfileProbe, 30000)) return;
            using var applyLease = GearWinCore.TryEnterBackground(CoreWork.ProfileChange);
            if (applyLease == null || !settings.AutoGamingModeEnabled || settings.GamingModeActive || settings.OfficeModeActive) return;
            _lastProfileProbe = now;
            var profile = GameProfileService.Load(current.Value.Path);
            if (profile == null) return;
            AutoGameProfileJournal.Apply(current.Value.Path, profile);
            _active = identity; // even a no-op remains stable until a different game
            _reportedFailure = false;
        }
        catch (Exception ex) { _lastFailureAt = Environment.TickCount64; Report(ex); }
    }

    private static (string Path, string Identity)? ForegroundGame()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
            var monitor = MonitorFromWindow(hwnd, 0); // MONITOR_DEFAULTTONULL
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) ||
                !CoversMonitor(r.Left, r.Top, r.Right, r.Bottom, info.Monitor.Left,
                    info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom)) return null;
            GetWindowThreadProcessId(hwnd, out int pid);
            using var process = Process.GetProcessById(pid);
            if (pid == Environment.ProcessId || process.ProcessName is "explorer" or "dwm" or "dotnet") return null;
            string? path = process.MainModule?.FileName;
            if (path == null) return null;
            // PID + start time prevents PID reuse; exact profile path prevents
            // another executable with a coincidentally similar file name.
            return (path, pid + ":" + process.StartTime.ToUniversalTime().Ticks + ":" + GameProfileService.Identity(path));
        }
        catch { return null; }
    }
}
