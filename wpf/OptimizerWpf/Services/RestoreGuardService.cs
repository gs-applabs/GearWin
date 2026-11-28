using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    public enum RestoreGuardResult { Disabled, AlreadyRecent, Created, Failed }

    // ΝΕΟ (6.1.0) - "σημείο επαναφοράς πριν από ριψοκίνδυνες ενέργειες": πριν από μαζικές αλλαγές
    // (εισαγωγή προφίλ tweaks, Fix All με registry/tweaks, απεγκατάσταση εφαρμογής) εξασφαλίζει ότι
    // υπάρχει πρόσφατο σημείο επαναφοράς. Τα Windows επιτρέπουν από προεπιλογή 1 αυτόματο σημείο ανά
    // 24 ώρες - γι' αυτό πρώτα ελέγχεται αν υπάρχει ήδη πρόσφατο και η αποτυχία δημιουργίας ΔΕΝ
    // μπλοκάρει τη ενέργεια (ο καλών αποφασίζει και ενημερώνει τον χρήστη).
    public static class RestoreGuardService
    {
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static bool NeedsNewPoint(IReadOnlyList<RestorePointInfo> points, DateTime now, TimeSpan maxAge) =>
            !points.Any(p => p.SequenceNumber > 0 && p.CreationTime <= now && now - p.CreationTime <= maxAge);

        internal static bool HasNewRecentPoint(IReadOnlyList<RestorePointInfo> before,
            IReadOnlyList<RestorePointInfo> after, DateTime now, TimeSpan maxAge)
        {
            var old = before.Select(p => p.SequenceNumber).ToHashSet();
            return after.Any(p => p.SequenceNumber > 0 && !old.Contains(p.SequenceNumber) &&
                p.CreationTime <= now && now - p.CreationTime <= maxAge);
        }

        private static async Task<bool> CreateAndVerifyAsync(IReadOnlyList<RestorePointInfo> before)
        {
            if (!await SystemService.CreateRestorePointAsync()) return false;
            // Do not retry creation. Observe the actual catalog, since Windows
            // may decline a checkpoint due to policy/frequency constraints.
            var after = await SystemService.ListRestorePointsAsync();
            return HasNewRecentPoint(before, after, DateTime.Now, DefaultMaxAge);
        }

        internal static async Task<bool> CreateVerifiedPointAsync()
        {
            await Gate.WaitAsync();
            try { return await CreateAndVerifyAsync(await SystemService.ListRestorePointsAsync()); }
            catch { return false; }
            finally { Gate.Release(); }
        }

        public static async Task<RestoreGuardResult> EnsureRecentAsync()
        {
            if (!AppSettingsService.Current.AutoRestorePointBeforeRiskyActions) return RestoreGuardResult.Disabled;
            await Gate.WaitAsync();
            try
            {
                if (!AppSettingsService.Current.AutoRestorePointBeforeRiskyActions) return RestoreGuardResult.Disabled;
                var points = await SystemService.ListRestorePointsAsync();
                if (!NeedsNewPoint(points, DateTime.Now, DefaultMaxAge)) return RestoreGuardResult.AlreadyRecent;
                return await CreateAndVerifyAsync(points) ? RestoreGuardResult.Created : RestoreGuardResult.Failed;
            }
            catch { return RestoreGuardResult.Failed; }
            finally { Gate.Release(); }
        }
    }
}
