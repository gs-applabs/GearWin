using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Shared execution policy for the existing Full Health Check, Fix All and tray Quick Clean.
// No registry, personal tweaks, startup changes, servicing cache or recycle bin in this plan.
internal static class SystemMaintenanceService
{
    internal static IReadOnlyList<string> BuildCleanupPlan(IEnumerable<QuickCleanItem> items, bool space, bool browser)
        => items.Where(i => i.SizeBytes > 0 &&
            ((space && i.Recommended && i.Key is "UserTemp" or "ThumbnailCache") ||
             (browser && i.Key == "BrowserCache")))
            .Select(i => i.Key).Distinct(StringComparer.Ordinal).ToArray();

    internal static async Task<long> CleanSelectedAsync(IReadOnlyList<QuickCleanItem> items, bool space, bool browser)
    {
        var keys = BuildCleanupPlan(items, space, browser);
        if (keys.Count == 0) return 0;
        var removed = await QuickCleanService.CleanAsync(keys);
        if (removed > 0) ImpactTrackingService.RecordBytesFreed(removed);
        return removed;
    }

    internal static async Task<long> QuickCleanAsync()
        => await CleanSelectedAsync(await QuickCleanService.ScanRecommendedAsync(), true, false);

    internal static Task<long> ScheduledCleanAsync() => QuickCleanService.RunExclusiveAsync(() =>
        MaintenancePaths.VerifiedUserTemp() is string temp ? MaintenancePaths.CleanFiles(temp, TimeSpan.FromDays(7), recursive: false) : 0, CorePriority.Background);
}
