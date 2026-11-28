using System;
using System.Windows;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    // Ροή οδηγού καλωσορίσματος → πρόταση «οικογένειας» (μία φορά). Κοινή για την εκκίνηση, τη Βοήθεια, το tray και το κλασικό μενού.
    public static class StartupGuideCoordinator
    {
        private static WelcomeGuideWindow? _guide;
        private static SuiteShowcaseWindow? _suite;

        private static ReleaseVersion Current => ReleaseVersion.ParseOrNull(App.DisplayVersion) ?? ReleaseVersion.FromVersion(SelfUpdateService.Current);

        // Μετά την κουρτίνα εκκίνησης: οδηγός μία φορά ανά νέα major.minor έκδοση. `onboardingShown`: στην πρώτη εγκατάσταση ο παλιός
        // αναλυτικός οδηγός μόλις εμφανίστηκε - δεν στοιβάζουμε δεύτερον, απλώς καταγράφεται η έκδοση.
        public static void RunAtStartup(MainWindow main, bool onboardingShown)
        {
            var store = new OncePerVersionStore(OncePerVersionStore.DefaultPath());
            if (onboardingShown) { store.MarkSeen(WelcomeGuideWindow.StoreKey, Current); return; }
            if (!store.ShouldShow(WelcomeGuideWindow.StoreKey, Current)) return;
            ShowWelcome(main, store, offerFamilyAfter: true);
        }

        public static void ShowWelcome(MainWindow main, OncePerVersionStore? store = null, bool offerFamilyAfter = false)
        {
            if (_guide != null || _suite != null || !CanOpen(main)) return;
            store ??= new OncePerVersionStore(OncePerVersionStore.DefaultPath());
            var g = new WelcomeGuideWindow(main, store);
            _guide = g;
            g.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_guide, g)) return;
                _guide = null;
                if (g.OpenFamilyAfterClose || (offerFamilyAfter && store.Get(SuiteShowcaseWindow.OfferedKey) == null))
                {
                    Queue(main, () =>
                    {
                        if (!g.OpenFamilyAfterClose && store.Get(SuiteShowcaseWindow.OfferedKey) != null) return;
                        if (TryShowSuite(main)) store.MarkSeen(SuiteShowcaseWindow.OfferedKey, Current);
                    });
                }
            };
            TryShowOverlay(g.Show, () => g.IsVisible, () =>
            {
                if (ReferenceEquals(_guide, g)) _guide = null;
                g.Close();
            });
        }

        public static void ShowSuite(MainWindow main) => TryShowSuite(main);

        private static bool TryShowSuite(MainWindow main)
        {
            if (_suite != null || _guide != null || !CanOpen(main)) return false;
            var s = new SuiteShowcaseWindow(main);
            _suite = s;
            s.Closed += (_, _) => { if (ReferenceEquals(_suite, s)) _suite = null; };
            return TryShowOverlay(s.Show, () => s.IsVisible, () =>
            {
                if (ReferenceEquals(_suite, s)) _suite = null;
                s.Close();
            });
        }

        private static bool CanOpen(MainWindow main) => main.IsVisible &&
            !main.Dispatcher.HasShutdownStarted && !main.Dispatcher.HasShutdownFinished;

        private static void Queue(MainWindow main, Action action)
        {
            if (!CanOpen(main)) return;
            main.Dispatcher.BeginInvoke(new Action(() => { if (CanOpen(main)) action(); }));
        }

        // Failed/aborted Show must neither occupy the singleton slot nor consume an offer.
        internal static bool TryShowOverlay(Action show, Func<bool> visible, Action cleanup)
        {
            try
            {
                show();
                if (visible()) return true;
            }
            catch (InvalidOperationException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
            try { cleanup(); }
            catch (InvalidOperationException ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
            return false;
        }

        // Από παράθυρα-διαλόγους (Βοήθεια): κλείνει πρώτα τον διάλογο και μετά ανοίγει την κουρτίνα.
        public static void ShowWelcomeFrom(Window dialog)
        {
            var main = Application.Current?.MainWindow as MainWindow;
            dialog.Close();
            if (main != null) Queue(main, () => ShowWelcome(main));
        }

        public static void ShowSuiteFrom(Window dialog)
        {
            var main = Application.Current?.MainWindow as MainWindow;
            dialog.Close();
            if (main != null) Queue(main, () => ShowSuite(main));
        }
    }
}
