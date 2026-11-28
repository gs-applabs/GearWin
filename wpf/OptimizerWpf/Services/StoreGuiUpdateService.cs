using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace OptimizerWpf.Services
{
    public enum StoreUpdateOutcome { Updated, AlreadyCurrent, Failed, TimedOut, Unconfirmed }

    // Interactive fallback only. Never invoke an ambiguous row, a generic
    // enabled button, or an update-all action. Missing rows are not proof.
    public static class StoreGuiUpdateService
    {
        private const string StoreUri = "ms-windows-store://downloadsandupdates";
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly Regex UpdateAvailable = new(@",\s*(Update available|Διαθέσιμη ενημέρωση)(?:\s*[,.;]|\s*$)", Opt);
        private static readonly Regex Active = new(@",\s*(Downloading|Installing|Pending|Queued|Starting|Acquiring|Processing|Updating|Downloaded|Λήψη|Γίνεται λήψη|Εγκατάσταση|Γίνεται εγκατάσταση|Σε αναμονή|Σε ουρά|Εκκίνηση|Επεξεργασία|Ενημέρωση σε εξέλιξη|Γίνεται ενημέρωση)(?:\b|\s|$)", Opt);
        private static readonly Regex Finished = new(@",\s*(Modified moments ago|Updated|Installed|Ενημερώθηκε|Εγκαταστάθηκε|Τροποποιήθηκε)(?:\b|\s|$)", Opt);

        internal enum RowState { Unknown, Available, Active, Finished }
        internal static RowState ClassifyRow(string name)
        {
            if (UpdateAvailable.IsMatch(name)) return RowState.Available;
            if (Active.IsMatch(name)) return RowState.Active;
            if (Finished.IsMatch(name)) return RowState.Finished;
            return RowState.Unknown;
        }

        internal static int SelectUniqueTitleIndex(IReadOnlyList<string> titles, string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return -1;
            int match = -1;
            for (int i = 0; i < titles.Count; i++)
            {
                if (!string.Equals(titles[i].Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (match >= 0) return -1;
                match = i;
            }
            return match;
        }

        internal static bool ConfirmedTransition(RowState before, RowState after, string previous, string current) =>
            before is RowState.Available or RowState.Active && after == RowState.Finished &&
            !string.Equals(previous, current, StringComparison.Ordinal);

        public static Task<StoreUpdateOutcome> UpdateAsync(string packageName, Action<string>? log, CancellationToken ct, int timeoutSeconds = 300) =>
            Task.Run(() => UpdateBlocking(packageName, log, ct, Math.Clamp(timeoutSeconds, 30, 600)), ct);

        private static AutomationElement? FindStoreWindow() =>
            AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
                .Cast<AutomationElement>().FirstOrDefault(w => w.Current.Name == "Microsoft Store");

        private static AutomationElement? FindRow(AutomationElement storeWindow, string wanted)
        {
            var titleCond = new PropertyCondition(AutomationElement.AutomationIdProperty, "ProductTitle");
            var all = storeWindow.FindAll(TreeScope.Descendants, titleCond).Cast<AutomationElement>().ToList();
            int index = SelectUniqueTitleIndex(all.Select(t => t.Current.Name).ToArray(), wanted);
            if (index < 0) return null;
            // Accept the nearest custom ancestor only if it represents one
            // product and one action, not the entire list or a toolbar.
            var parent = all[index];
            for (int depth = 0; depth < 4; depth++)
            {
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
                if (parent == null || parent.Equals(storeWindow)) return null;
                if (parent.Current.ControlType != ControlType.Custom) continue;
                var titles = parent.FindAll(TreeScope.Descendants, titleCond);
                if (titles.Count != 1) return null;
                var actions = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ActionButton"));
                if (actions.Count == 1) return parent;
                if (actions.Count > 1) return null;
            }
            return null;
        }

        private static void Wait(CancellationToken ct, int milliseconds)
        {
            if (ct.WaitHandle.WaitOne(milliseconds)) ct.ThrowIfCancellationRequested();
        }

        private static StoreUpdateOutcome UpdateBlocking(string wanted, Action<string>? log, CancellationToken ct, int timeoutSeconds)
        {
            wanted = (wanted ?? "").Trim();
            if (wanted.Length == 0) return StoreUpdateOutcome.Failed;
            ct.ThrowIfCancellationRequested();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool navigated = false, started = false, observedActive = false;
            TimeSpan invokedAt = TimeSpan.Zero;
            string initialName = "", lastStatus = "";
            RowState initialState = RowState.Unknown;
            while (clock.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var win = FindStoreWindow();
                    var row = win == null ? null : FindRow(win, wanted);
                    if (!started && row == null && !navigated)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(StoreUri) { UseShellExecute = true })?.Dispose();
                        navigated = true;
                        log?.Invoke("Store: locating selected update");
                    }
                    if (row != null)
                    {
                        string rowName = row.Current.Name;
                        var state = ClassifyRow(rowName);
                        if (!started && state == RowState.Active)
                        {
                            started = observedActive = true;
                            invokedAt = clock.Elapsed;
                            initialName = rowName;
                            initialState = state;
                        }
                        else if (!started && state == RowState.Available)
                        {
                            var action = row.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ActionButton"));
                            if (action != null && action.Current.IsEnabled &&
                                action.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                            {
                                ct.ThrowIfCancellationRequested();
                                initialName = rowName;
                                initialState = state;
                                ((InvokePattern)pattern).Invoke();
                                started = true;
                                invokedAt = clock.Elapsed;
                                log?.Invoke("Store: selected update requested");
                            }
                        }
                        else if (started)
                        {
                            if (state == RowState.Active) observedActive = true;
                            if (ConfirmedTransition(initialState, state, initialName, rowName))
                                return StoreUpdateOutcome.Updated;
                            if (!observedActive && state == RowState.Available && rowName == initialName &&
                                clock.Elapsed - invokedAt >= TimeSpan.FromSeconds(30))
                                return StoreUpdateOutcome.Unconfirmed;
                        }
                        if (started && rowName != lastStatus)
                        {
                            lastStatus = rowName;
                            log?.Invoke(rowName);
                        }
                    }
                    // Store may be loading, virtualizing rows, or displaying a
                    // different page. Absence, disappearance and old "Installed"
                    // labels do not establish an update.
                    if (!started && clock.Elapsed >= TimeSpan.FromSeconds(40))
                        return StoreUpdateOutcome.Unconfirmed;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (lastStatus != ex.Message) { lastStatus = ex.Message; log?.Invoke("Store UI: " + ex.Message); }
                    if (!started && clock.Elapsed >= TimeSpan.FromSeconds(40))
                        return StoreUpdateOutcome.Unconfirmed;
                }
                Wait(ct, 750);
            }
            return StoreUpdateOutcome.TimedOut;
        }

        // Retained for compatibility; selected update flow no longer closes a
        // window that may already belong to the user.
        public static void CloseStoreWindow()
        {
            try
            {
                var win = FindStoreWindow();
                var close = win?.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Close"));
                if (close != null && close.Current.IsEnabled && close.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
                    ((InvokePattern)pattern).Invoke();
            }
            catch { }
        }
    }
}
