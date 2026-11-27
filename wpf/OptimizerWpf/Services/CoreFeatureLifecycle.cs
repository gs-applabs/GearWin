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
        _applied = wanted;

        ApplyChange(previous?.UpdateTimer, wanted.UpdateTimer, UpdateNotificationService.Start, UpdateNotificationService.Stop);
        if (wanted.Updates && previous?.Updates != true) _ = UpdateNotificationService.StartupCheckAsync();
        if (wanted.LowDisk && previous?.LowDisk != true) UpdateNotificationService.RequestLowDiskCheck();
        ApplyChange(previous?.Release, wanted.Release, ReleaseCheckService.Start, ReleaseCheckService.Stop);
        ApplyChange(previous?.Alerts, wanted.Alerts, AlertService.Start, AlertService.Stop);
        ApplyChange(previous?.AutoGaming, wanted.AutoGaming, AutoGamingModeService.Start, AutoGamingModeService.Stop);
        ApplyChange(previous?.Widget, wanted.Widget, DesktopWidgetService.Start, () => DesktopWidgetService.Stop());
        ApplyChange(previous?.Clipboard, wanted.Clipboard, () => ClipboardManagerService.Start(main), ClipboardManagerService.Stop);
    }

    private static void ApplyChange(bool? previous, bool wanted, Action start, Action stop)
    {
        if (previous == wanted) return;
        try { if (wanted) start(); else if (previous == true) stop(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); StatusService.Report(ex.Message); }
    }

    internal static void Stop()
    {
        _main = null;
        _applied = null;
        UpdateNotificationService.Stop();
        ReleaseCheckService.Stop();
        AlertService.Stop();
        AutoGamingModeService.Stop();
        DesktopWidgetService.Stop();
        ClipboardManagerService.Stop();
    }
}
