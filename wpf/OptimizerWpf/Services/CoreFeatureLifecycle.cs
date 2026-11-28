using System;
using System.Windows;

namespace OptimizerWpf.Services;

// Existing persisted preferences are the authority. Merely constructing a service
// or a settings window never authorizes its optional background activity.
internal readonly record struct FeatureDemand(bool Updates, bool LowDisk, bool Release,
    bool Alerts, bool AutoGaming, bool Widget, bool Clipboard)
{
    internal static FeatureDemand From(AppSettings s) => new(s.UpdateNotificationsEnabled,
        s.LowDiskNotificationsEnabled, s.SelfUpdateCheckEnabled, s.TempAlertsEnabled || s.SmartAlertsEnabled,
        s.AutoGamingModeEnabled, s.DesktopWidgetEnabled, s.ClipboardHistoryEnabled);
    internal bool UpdateTimer => Updates || LowDisk;
}

internal static class CoreFeatureLifecycle
{
    private static Window? _main;
    private static FeatureDemand? _applied;

    // UI dispatcher only; no extra worker or timer.
    internal static void Start(Window main)
    {
        _main = main;
        Apply();
    }

    internal static void Apply()
    {
        var main = _main;
        if (main == null || main.Dispatcher.HasShutdownStarted) return;
        main.Dispatcher.VerifyAccess();
        var wanted = FeatureDemand.From(AppSettingsService.Current);
        var previous = _applied;
        _applied = wanted; // preference transitions, not evidence of running services

        if (previous?.Updates == true && !wanted.Updates) UpdateNotificationService.InvalidateAutomaticScan();
        if (previous?.LowDisk == true && !wanted.LowDisk) UpdateNotificationService.InvalidateLowDiskCheck();
        bool updateWasRunning = UpdateNotificationService.IsRunning;
        ApplyChange(updateWasRunning, wanted.UpdateTimer, UpdateNotificationService.Start, UpdateNotificationService.Stop);
        if (UpdateNotificationService.IsRunning)
        {
            if (wanted.Updates && (!updateWasRunning || previous?.Updates != true)) _ = UpdateNotificationService.StartupCheckAsync();
            if (wanted.LowDisk && (!updateWasRunning || previous?.LowDisk != true)) UpdateNotificationService.RequestLowDiskCheck();
        }
        ApplyChange(ReleaseCheckService.IsRunning, wanted.Release, ReleaseCheckService.Start, ReleaseCheckService.Stop);
        ApplyChange(AlertService.IsRunning, wanted.Alerts, AlertService.Start, AlertService.Stop);
        ApplyChange(AutoGamingModeService.IsRunning, wanted.AutoGaming, AutoGamingModeService.Start, AutoGamingModeService.Stop);
        ApplyChange(DesktopWidgetService.IsRunning, wanted.Widget, DesktopWidgetService.Start, () => DesktopWidgetService.Stop());
        ApplyChange(ClipboardManagerService.IsRunning, wanted.Clipboard, () => ClipboardManagerService.Start(main), ClipboardManagerService.Stop);
    }

    internal static void Reconcile(bool running, bool wanted, Action start, Action stop)
    {
        if (running == wanted) return;
        if (wanted) start(); else stop();
    }

    private static void ApplyChange(bool running, bool wanted, Action start, Action stop)
    {
        try { Reconcile(running, wanted, start, stop); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); StatusService.Report(ex.Message); }
    }

    internal static void Stop()
    {
        _main = null;
        _applied = null;
        // Shutdown uses actual service ownership, even if a preference change
        // or a partially failed start left the desired state out of sync.
        ApplyChange(UpdateNotificationService.IsRunning, false, UpdateNotificationService.Start, UpdateNotificationService.Stop);
        ApplyChange(ReleaseCheckService.IsRunning, false, ReleaseCheckService.Start, ReleaseCheckService.Stop);
        ApplyChange(AlertService.IsRunning, false, AlertService.Start, AlertService.Stop);
        ApplyChange(true, false, AutoGamingModeService.Start, AutoGamingModeService.Stop);
        ApplyChange(DesktopWidgetService.IsRunning, false, DesktopWidgetService.Start, () => DesktopWidgetService.Stop());
        ApplyChange(ClipboardManagerService.IsRunning, false, () => { }, ClipboardManagerService.Stop);
    }
}
