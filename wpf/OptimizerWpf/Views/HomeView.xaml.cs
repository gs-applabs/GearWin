using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OptimizerWpf.Services;
using OptimizerWpf.Views;

namespace OptimizerWpf.Views
{
    public partial class HomeView : UserControl
    {
        private void NestedScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => NestedScrollHelper.Forward(sender, e);

        // ΝΕΟ - roadmap "Ομαλή μετάβαση τιμής στις ProgressBar" - οι μπάρες CPU/RAM/GPU/Δίσκου άλλαζαν
        // τιμή ακαριαία κάθε 1s ("τίναγμα") - σύντομο DoubleAnimation αντί για απευθείας SetValue.
        private static void AnimateBar(System.Windows.Controls.ProgressBar bar, double newValue)
        {
            var property = System.Windows.Controls.Primitives.RangeBase.ValueProperty;
            var target = (double)bar.GetAnimationBaseValue(property);
            if (Math.Abs(target - newValue) < 0.01) return;
            var previous = bar.Value;
            bar.BeginAnimation(property, null);
            bar.Value = newValue;
            if (!PerformanceProfileService.Current.AnimationsEnabled || !bar.IsVisible) return;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(previous, newValue, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new System.Windows.Media.Animation.QuadraticEase(), FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
            bar.BeginAnimation(property, anim);
        }

        // Matches the WinForms app's periodic-refresh pattern (a Timer ticking on the UI thread,
        // reading already-computed values) - PerformanceCounter.NextValue() and
        // GlobalMemoryStatusEx are both fast, synchronous, safe to call directly on the Dispatcher
        // tick without a background thread.
        private readonly DispatcherTimer _refreshTimer;
        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη: "η ανανέωση των δίσκων να γίνεται όταν αλλάζει το ποσοστό,
        // δεν χρειάζεται live gauge, ελάφρυνση εφαρμογής") - ο χώρος δίσκου ΔΕΝ αλλάζει αισθητά μέσα
        // σε 1 δευτερόλεπτο σαν το CPU, οπότε δεν χρειάζεται να ξαναδιαβάζεται κάθε tick του γρήγορου
        // timer. Ξεχωριστός, πολύ πιο αραιός timer (20s) μόνο για τον δίσκο - CPU/RAM/GPU παραμένουν
        // στο γρήγορο 1s timer (αυτά ΕΙΝΑΙ πραγματικά live).
        private readonly DispatcherTimer _diskRefreshTimer;
        private IDisposable? _gpuSubscription;
        private bool _gpuReading;
        private readonly List<string> _drives = new();
        private int _driveIndex;

        // ΝΕΟ (χρήστης ζήτησε: "graph δίπλα στο δίκτυο, όπως στη Διαχείριση Εργασιών") - κυλιόμενο
        // ιστορικό throughput (bytes/sec), ίδιο μέγεθος παραθύρου με τα sparklines της Διαχείρισης
        // Εργασιών (~αρκετά δευτερόλεπτα ιστορικού, όχι ώρες).
        private readonly Queue<double> _netSamples = new();
        private const int NetSampleCap = 40;
        private long? _lastNetBytes;
        private DateTime _lastNetTime;

        public HomeView()
        {
            InitializeComponent();
            PopulateDriveList();
            // ΝΕΟ - roadmap "Ταχύτερη εκκίνηση εφαρμογής" - το HomeView κατασκευάζεται συγχρονισμένα
            // ΜΕΣΑ στον constructor του MainWindow (η Αρχική είναι το μοναδικό tab που φορτώνεται
            // eager, όχι lazy, βλ. MainWindow.xaml.cs's TabHome.IsChecked=true), δηλαδή ΠΡΙΝ καν
            // εμφανιστεί το κύριο παράθυρο. Το PerformanceCounterCategory("GPU Engine").GetInstanceNames()
            // μέσα στο TryInitGpuCounters (βλ. GpuInfoService.CreateUsageCounters) είναι γνωστά αργό -
            // απαριθμεί ΟΛΑ τα GPU-engine perf-counter instances σε όλο το σύστημα. Μετακινήθηκαν σε
            // background thread ώστε να μην καθυστερούν το πρώτο paint του κύριου παραθύρου - τα πεδία
            // _cpuCounter/_gpuCounters είναι ήδη null-checked στο RefreshCpuRamGpu (παρακάτω), οπότε
            // απλά παραλείπεται το CPU%/GPU% για τα πρώτα ticks μέχρι να ολοκληρωθεί η αρχικοποίηση.


            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PerformanceProfileService.Current.HomeRefreshSeconds) };
            _refreshTimer.Tick += (_, _) => { RefreshCpuRamGpu(); RefreshNetworkGraph(); };
            _diskRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _diskRefreshTimer.Tick += (_, _) => { RefreshDisk(); _ = RefreshNetworkStatusAsync(); _ = RefreshTemperaturesAsync(); if (_diskIconNeedsRetry) _ = UpdateDiskIconAsync(); };

            // Constructor-time Window.GetWindow is frequently null. Attach after Loaded.
            Window? parentWindow = null;
            void UpdatePolling()
            {
                if (_unloaded || !IsLoaded || !IsVisible || parentWindow == null ||
                    parentWindow.WindowState == WindowState.Minimized)
                {
                    _refreshTimer.Stop();
                    _diskRefreshTimer.Stop();
                    DisposeCounters();
                    return;
                }
                if (!_initializedVisible)
                {
                    _initializedVisible = true;
                    InitializeVisibleView();
                }
                _gpuSubscription ??= SharedTelemetryService.SubscribeGpu();
                _refreshTimer.Interval = TimeSpan.FromSeconds(PollSeconds(
                    PerformanceProfileService.Current.HomeRefreshSeconds, parentWindow.IsActive));
                if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
                if (!_diskRefreshTimer.IsEnabled) { _diskRefreshTimer.Start(); _ = RefreshTemperaturesAsync(); }
            }
            int pollingRefreshQueued = 0;
            Action perfChanged = () =>
            {
                if (Dispatcher.HasShutdownStarted || System.Threading.Interlocked.Exchange(ref pollingRefreshQueued, 1) != 0) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref pollingRefreshQueued, 0);
                    UpdatePolling();
                }));
            };
            EventHandler windowChanged = (_, _) => UpdatePolling();
            DependencyPropertyChangedEventHandler visibilityChanged = (_, _) => UpdatePolling();
            Loaded += (_, _) =>
            {
                if (_viewLifetime != null) return;
                _viewLifetime = new System.Threading.CancellationTokenSource();
                _unloaded = false;
                parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    parentWindow.Activated += windowChanged;
                    parentWindow.Deactivated += windowChanged;
                    parentWindow.StateChanged += windowChanged;
                    parentWindow.IsVisibleChanged += visibilityChanged;
                }
                IsVisibleChanged += visibilityChanged;
                PerformanceProfileService.Changed += perfChanged;
                UpdatePolling();
            };
            Unloaded += (_, _) =>
            {
                PerformanceProfileService.Changed -= perfChanged;
                IsVisibleChanged -= visibilityChanged;
                if (parentWindow != null)
                {
                    parentWindow.Activated -= windowChanged;
                    parentWindow.Deactivated -= windowChanged;
                    parentWindow.StateChanged -= windowChanged;
                    parentWindow.IsVisibleChanged -= visibilityChanged;
                }
                parentWindow = null;
            };

            Unloaded += (_, _) =>
            {
                _unloaded = true;
                _initializedVisible = false;
                var lifetime = _viewLifetime;
                _viewLifetime = null;
                if (lifetime != null) { lifetime.Cancel(); lifetime.Dispose(); }
                _refreshTimer.Stop();
                _diskRefreshTimer.Stop();
                DisposeCounters();
                ActionLogService.Changed -= RefreshRecentActivity;
                UpdatesHubService.Changed -= RefreshUpdatesSummary;
                ImpactTrackingService.Changed -= RenderHealthIssues;
            };
        }

        private void InitializeVisibleView()
        {
            RefreshCpuRamGpu();
            RefreshDisk();
            _ = RefreshNetworkStatusAsync();
            _ = UpdateDiskIconAsync();
            _ = RefreshHealthScoreAsync();
            CheckHealthCheckReminder();
            _ = LoadGpuNameAsync();
            RefreshRecentActivity();
            ActionLogService.Changed += RefreshRecentActivity;
            LoadPinnedTweaks();
            RefreshUpdatesSummary();
            UpdatesHubService.Changed += RefreshUpdatesSummary;
            _ = LoadSecuritySummaryAsync();
            RenderHealthIssues();
            ImpactTrackingService.Changed += RenderHealthIssues;
        }

        private void RefreshUpdatesSummary()
        {
            TxtUpdatesAppsCount.Text = UpdatesHubService.AppUpdatesAvailable?.ToString() ?? "—";
            TxtUpdatesDriversCount.Text = UpdatesHubService.DriverUpdatesAvailable?.ToString() ?? "—";
            TxtUpdatesWindowsCount.Text = UpdatesHubService.WindowsUpdatesAvailable?.ToString() ?? "—";
        }

        private void BtnOpenUpdatesTab_Click(object sender, RoutedEventArgs e) =>
            (Window.GetWindow(this) as MainWindow)?.SelectTab("Bloatware#0");

        // ΔΙΟΡΘΩΣΗ (GEARWIN.MD: "το συνολικό όφελος να μην είναι σε ξεχωριστό πλαίσιο, ενσωμάτωσέ το
        // στα μηνύματα της βαθμολογίας υγείας συστήματος") - πριν ήταν δική του κάρτα (TxtImpactFreed/
        // TxtImpactBootTrend, αφαιρέθηκαν από το XAML) - τώρα οι ίδιες ακριβώς γραμμές προστίθενται
        // στο ListHealthIssues (βλ. RenderHealthIssues/RefreshHealthScoreAsync).
        private IEnumerable<string> GetImpactSummaryLines()
        {
            var totalGb = ImpactTrackingService.TotalBytesFreedAllTime / 1024.0 / 1024 / 1024;
            yield return totalGb > 0.05
                ? string.Format(LanguageService.T("Home_ImpactFreedFormat"), totalGb.ToString("0.#"))
                : LanguageService.T("Home_ImpactFreedNone");

            // Ταξινόμηση ρητά κατά ημερομηνία (ΟΧΙ υπόθεση για τη σειρά επιστροφής του
            // GetRecentBootTimes) - παλιότερος μισός έναντι πιο πρόσφατου μισού, ίδιο σκεπτικό με τα
            // δύο μισά ενός A/B σύγκρισης.
            var boots = AdvancedToolsService.GetRecentBootTimes(10).OrderBy(b => b.When).ToList();
            if (boots.Count >= 2)
            {
                var half = Math.Max(1, boots.Count / 2);
                var older = boots.Take(half).Average(b => b.Seconds);
                var newer = boots.Skip(boots.Count - half).Average(b => b.Seconds);
                if (older > 0.1)
                {
                    var pct = (older - newer) / older * 100.0;
                    yield return string.Format(
                        LanguageService.T(pct >= 0 ? "Home_ImpactBootTrendBetter" : "Home_ImpactBootTrendWorse"),
                        Math.Abs(pct).ToString("0"));
                }
            }
        }

        // Κρατά το τελευταίο αποτέλεσμα του HealthScoreService.Compute() ώστε το RenderHealthIssues να
        // μπορεί να ξαναζωγραφίσει τη λίστα (π.χ. όταν αλλάζει το ImpactTrackingService) ΧΩΡΙΣ να
        // ξανατρέξει ολόκληρο το (ακριβό, πολλαπλά WMI queries) RefreshHealthScoreAsync.
        private IReadOnlyList<HealthIssue> _lastHealthIssues = Array.Empty<HealthIssue>();
        private Brush _lastScoreColor = Brushes.Gray;

        private void RenderHealthIssues()
        {
            var baseRows = _lastHealthIssues.Count == 0
                ? new[] { new HealthIssueRow(LanguageService.T("Home_NoIssuesFound"), _lastScoreColor) }
                : _lastHealthIssues.Select(i => new HealthIssueRow(i.Title, _lastScoreColor));
            var rows = baseRows
                .Concat(GetImpactSummaryLines().Select(line => new HealthIssueRow(line, _lastScoreColor)))
                .ToList();
            if (_healthCheckReminderText != null)
                rows.Add(new HealthIssueRow(_healthCheckReminderText, new SolidColorBrush(Color.FromRgb(230, 170, 60))));
            ListHealthIssues.ItemsSource = rows;
        }

        // ΝΕΟ - roadmap "Καρφιτσωμένες συντομεύσεις" - συγκεντρώνει τα καρφιτσωμένα tweaks από όλες
        // τις 4 λίστες (βλ. TweakRowVm.PinKey/AppSettingsService.PinnedTweakKeys) για γρήγορο toggle
        // απευθείας από την Αρχική, χωρίς μετάβαση στις Επιπλέον Ρυθμίσεις.
        private void LoadPinnedTweaks()
        {
            var pins = AppSettingsService.Current.PinnedTweakKeys;
            // ΒΕΛΤΙΩΣΗ ΑΠΟΔΟΣΗΣ: τα rows χτίζονται χωρίς ανίχνευση (φθηνό - μόνο κλειδιά), φιλτράρονται στα καρφιτσωμένα,
            // και ΜΟΝΟ γι' αυτά διαβάζεται η πραγματική κατάσταση, στο παρασκήνιο (πριν: ανίχνευση ΟΛΩΝ των tweaks
            // σύγχρονα στο UI νήμα κάθε φορά που άνοιγε η Αρχική, συμπεριλαμβανομένων powercfg processes).
            var all = TweakService.AllMainTweaks().Select(t => new TweakRowVm(t, "Main", detectNow: false))
                .Concat(TweakService.AiCopilotTweaksSimple().Select(t => new TweakRowVm(t, "Ai", detectNow: false)))
                .Concat(TweakService.PerfTweaksSimple().Select(t => new TweakRowVm(t, "Perf", detectNow: false)))
                .Concat(TweakService.LighterWindowsTweaksSimple().Select(t => new TweakRowVm(t, "Lighter", detectNow: false)))
                .Where(r => pins.Contains(r.PinKey) || pins.Contains(r.LegacyPinKey))
                .ToList();
            _ = TweakRowVm.DetectAllAsync(all, ViewToken);

            ListPinnedTweaks.ItemsSource = all;
            TxtNoPinned.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ΝΕΟ - σύνοψη ασφάλειας (βλ. σχόλιο στο XAML). Στο παρασκήνιο (WMI/registry/PowerShell), αγνοείται αν το view έχει ήδη φύγει.
        private async Task LoadSecuritySummaryAsync()
        {
            try
            {
                var token = ViewToken;
                var items = await SecurityStatusService.GetAsync().WaitAsync(token);
                if (token.IsCancellationRequested || _unloaded) return;
                var good = items.Count(i => i.Level == SecurityLevel.Good);
                var total = items.Count(i => i.Level != SecurityLevel.Info);
                TxtSecuritySummary.Text = string.Format(LanguageService.T("Home_SecuritySummary"), good, total);
                ListSecurity.ItemsSource = items.Select(i => new SecurityRowVm(i)).ToList();
            }
            catch { if (!_unloaded) TxtSecuritySummary.Text = "—"; }
        }

        private void BtnOpenSecurityCenter_Click(object sender, RoutedEventArgs e) =>
            new MaintenanceCenterWindow { Owner = Window.GetWindow(this) }.Show();

        private async void TogglePinnedTweak_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton { Tag: TweakRowVm row } button) return;
            button.IsEnabled = false;
            try { await row.ApplyRequestedAsync(row.IsOn); }
            catch
            {
                row.IsOn = !row.IsOn;
                ThemedMessageBox.Show(LanguageService.T("Net_ChangeFailed"), LanguageService.T("Net_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { button.IsEnabled = true; }
        }

        // ΝΕΟ - ρητό αίτημα χρήστη: "Γρήγορος Καθαρισμός & Έλεγχος Υγείας πρέπει να συγχωνευθούν" - η
        // ξεχωριστή κάρτα "Γρήγορος Καθαρισμός" στην Αρχική αφαιρέθηκε (μαζί με BtnScanQuickClean/
        // LoadQuickCleanAsync/QuickCleanItemVm κ.λπ. - βλ. git history) επειδή σάρωνε ΑΚΡΙΒΩΣ το ίδιο
        // QuickCleanService.ScanAsync() που ήδη τρέχει μέσα στο στάδιο "Καθαρισμός Χώρου" του Πλήρους
        // Ελέγχου Υγείας (HealthCheckWindow.ScanSpaceAsync) - δύο ξεχωριστές κάρτες για την ίδια ουσιαστικά
        // σάρωση. Το "Πλήρης Έλεγχος Υγείας" παρακάτω είναι πλέον το ΕΝΑ σημείο εισόδου για καθαρισμό.
        private void BtnRunHealthCheck_Click(object sender, RoutedEventArgs e) =>
            new HealthCheckWindow { Owner = Window.GetWindow(this) }.ShowDialog();

        // Ενίσχυση (Boost): ΜΟΝΟ άμεση ελευθέρωση RAM (κλάδεμα διεργασιών παρασκηνίου + cache μνήμης, MemoryCleanerService)
        // με πραγματική μέτρηση πριν/μετά. ΔΕΝ διαγράφει αρχεία - ο καθαρισμός αρχείων ανήκει στον Πλήρη Έλεγχο Υγείας
        // (πριν, το Boost, η Διόρθωση Όλων και ο Πλήρης Έλεγχος καθάριζαν και τα τρία τα ίδια προσωρινά).
        private async void BtnBoost_Click(object sender, RoutedEventArgs e)
        {
            BtnBoost.IsEnabled = false;
            StatusService.SetBusy(LanguageService.T("PcmHome_Boosting"));
            try
            {
                var result = await MemoryCleanerService.CleanAsync();
                (Window.GetWindow(this) as MainWindow)?.ShowToast(MemoryCleanerService.Describe(result));
            }
            catch (Exception ex) { (Window.GetWindow(this) as MainWindow)?.ShowToast(ex.Message); }
            finally
            {
                StatusService.SetIdle(LanguageService.T("Ready"));
                BtnBoost.IsEnabled = true;
            }
        }

        private void RefreshRecentActivity()
        {
            var recent = ActionLogService.Entries.Reverse().Take(3).ToList();
            ListRecentActivity.ItemsSource = recent;
            TxtNoRecentActivity.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnOpenActionLog_Click(object sender, RoutedEventArgs e) =>
            new ActionLogWindow { Owner = Window.GetWindow(this) }.ShowDialog();

        private async Task LoadGpuNameAsync()
        {
            var token = ViewToken;
            try
            {
                var name = await GpuInfoService.GetNameAsync().WaitAsync(token);
                if (!token.IsCancellationRequested && !_unloaded) TxtGpuName.Text = name;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
        }

        internal static double PollSeconds(double configuredSeconds, bool active) =>
            active ? Math.Max(2, configuredSeconds) : Math.Max(8, configuredSeconds);

        private bool _unloaded = true;
        private bool _initializedVisible;
        private System.Threading.CancellationTokenSource? _viewLifetime;
        private System.Threading.CancellationToken ViewToken => _viewLifetime?.Token ?? new System.Threading.CancellationToken(true);

        private void DisposeCounters()
        {
            _gpuSubscription?.Dispose();
            _gpuSubscription = null;
        }

        private async Task RefreshSharedGpuAsync()
        {
            if (_gpuReading || _gpuSubscription == null || _unloaded) return;
            _gpuReading = true;
            try
            {
                var value = await SharedTelemetryService.ReadGpuAsync().WaitAsync(ViewToken);
                if (_unloaded || _gpuSubscription == null) return;
                TxtGpuPercent.Text = value.HasValue ? $"{value}%" : "—";
                if (value.HasValue) AnimateBar(BarGpu, value.Value);
            }
            catch { }
            finally { _gpuReading = false; }
        }

        // ΝΕΟ v3.2.0 (χρήστης ζήτησε: "δείκτης κατάστασης δικτύου στην Αρχική") - βλ.
        // NetworkService.CheckInternetAsync για το πλήρες σκεπτικό (τοπική σύνδεση vs πραγματικό
        // Internet, ping με σύντομο timeout).
        private bool _networkRefreshInFlight;
        private async Task RefreshNetworkStatusAsync()
        {
            if (_unloaded || _networkRefreshInFlight) return;
            _networkRefreshInFlight = true;
            try
            {
            var (connected, latencyMs) = await NetworkService.CheckInternetAsync().WaitAsync(ViewToken);
            if (_unloaded) return;
            DotNetworkStatus.Fill = new SolidColorBrush(connected ? Color.FromRgb(76, 175, 80) : Color.FromRgb(220, 53, 69));
            TxtNetworkStatus.Text = connected
                ? $"{LanguageService.T("Home_NetworkConnected")}{latencyMs} ms"
                : LanguageService.T("Home_NetworkDisconnected");
            }
            catch { }
            finally { _networkRefreshInFlight = false; }
        }

        private void BtnOpenNetworkTab_Click(object sender, RoutedEventArgs e) => (System.Windows.Window.GetWindow(this) as OptimizerWpf.MainWindow)?.SelectTab("Network#1");

        // ΝΕΟ (χρήστης ζήτησε: "graph δίπλα στο δίκτυο, όπως στη Διαχείριση Εργασιών") - απλό,
        // αυτο-κλιμακούμενο sparkline: bytes/sec υπολογίζεται από τη διαφορά δύο διαδοχικών
        // στιγμιότυπων του NetworkService.GetTotalNetworkBytes (καθαρά τοπικοί μετρητές, καμία σχέση
        // με το ping του RefreshNetworkStatusAsync). Ζωγραφίζεται χειροκίνητα με Polyline/Polygon αντί
        // για βιβλιοθήκη γραφημάτων - πολύ απλό σχήμα, δεν αξίζει νέα εξάρτηση.
        private void RefreshNetworkGraph()
        {
            var now = DateTime.UtcNow;
            var bytes = NetworkService.GetTotalNetworkBytes();
            if (_lastNetBytes.HasValue)
            {
                var elapsed = (now - _lastNetTime).TotalSeconds;
                var bytesPerSec = elapsed > 0 ? Math.Max(0, (bytes - _lastNetBytes.Value) / elapsed) : 0;
                _netSamples.Enqueue(bytesPerSec);
                while (_netSamples.Count > NetSampleCap) _netSamples.Dequeue();
                DrawNetworkGraph();
            }
            _lastNetBytes = bytes;
            _lastNetTime = now;
        }

        private void DrawNetworkGraph()
        {
            var canvas = CanvasNetworkGraph;
            var w = canvas.ActualWidth;
            var h = canvas.ActualHeight;
            canvas.Children.Clear();
            if (w <= 0 || h <= 0 || _netSamples.Count < 2) return;

            var samples = _netSamples.ToArray();
            // Ελάχιστη κλίμακα 64 KB/s - αποφεύγει το graph να "τρελαίνεται" σε μικροσκοπικά
            // αναπάντεχα ενεργά κτ. bytes όταν δεν τρέχει ουσιαστική κίνηση δικτύου.
            var max = Math.Max(samples.Max(), 64.0 * 1024);
            var stepX = w / (NetSampleCap - 1);
            var startX = w - (samples.Length - 1) * stepX;

            var linePoints = new PointCollection();
            var fillPoints = new PointCollection();
            fillPoints.Add(new Point(startX, h));
            for (var i = 0; i < samples.Length; i++)
            {
                var x = startX + i * stepX;
                var y = h - (samples[i] / max) * (h - 2) - 1;
                linePoints.Add(new Point(x, y));
                fillPoints.Add(new Point(x, y));
            }
            fillPoints.Add(new Point(startX + (samples.Length - 1) * stepX, h));

            var accent = (Color)ColorConverter.ConvertFromString("#FF4CAF50")!;
            canvas.Children.Add(new Polygon { Points = fillPoints, Fill = new SolidColorBrush(Color.FromArgb(50, accent.R, accent.G, accent.B)) });
            canvas.Children.Add(new Polyline { Points = linePoints, Stroke = new SolidColorBrush(accent), StrokeThickness = 1.5 });
        }

        // Δεν αλλάζει σε δευτερόλεπτα σαν το CPU - αρκεί μία φορά στην εκκίνηση, καμία ανάγκη για
        // επαναλαμβανόμενο timer (ίδιο σκεπτικό "ελάφρυνση εφαρμογής" με τον δίσκο παραπάνω).
        private bool _temperatureReading;
        private bool _diskTemperatureReading;
        private int _diskSelectionGeneration;

        private async Task RefreshTemperaturesAsync()
        {
            if (_unloaded || !IsVisible || !IsLoaded) return;
            _ = RefreshDiskTemperatureAsync();
            if (_temperatureReading) return;
            _temperatureReading = true;
            try
            {
                var sample = await SensorService.ReadHomeTemperaturesAsync().WaitAsync(ViewToken);
                if (_unloaded || !IsVisible) return;
                SetTempText(TxtCpuTemp, sample.Cpu, "CPU", sample.Pawn);
                SetTempText(TxtGpuTemp, sample.Gpu, "GPU", sample.Pawn);
                SetTempText(TxtRamTemp, sample.Ram, "RAM", sample.Pawn);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
            finally { _temperatureReading = false; }
        }

        private async Task RefreshDiskTemperatureAsync()
        {
            var drive = CurrentDrive;
            if (_diskTemperatureReading || drive == null || _unloaded || !IsVisible || !IsLoaded) return;
            _diskTemperatureReading = true;
            var generation = _diskSelectionGeneration;
            try
            {
                var temperature = await SensorService.ReadHomeDiskAsync(drive).WaitAsync(ViewToken);
                if (!_unloaded && IsVisible && generation == _diskSelectionGeneration)
                    SetTempText(TxtDiskTemp, temperature, "Disk", null);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
            finally
            {
                _diskTemperatureReading = false;
                if (!_unloaded && IsVisible && generation != _diskSelectionGeneration)
                    _ = RefreshDiskTemperatureAsync();
            }
        }

        private static void SetTempText(System.Windows.Controls.TextBlock target, int? temp, string device, string? pawn)
        {
            bool greek = LanguageService.Current == "el";
            target.Text = temp.HasValue ? $"{temp}°C" : "—";
            if (temp.HasValue)
            {
                target.ToolTip = device == "Disk" ? "Windows Storage / SMART / WMI" : "Hardware sensor / vendor API";
                return;
            }
            target.ToolTip = device == "Disk"
                ? (greek ? "Ο επιλεγμένος δίσκος δεν επέστρεψε θερμοκρασία από τις διαθέσιμες πηγές." : "Selected disk did not report a temperature through the available sources.")
                : (greek ? "Μη διαθέσιμη πραγματική μέτρηση. " : "No available sensor reading. ") +
                    (pawn == null ? (greek ? "Ελέγξτε την επίσημη εγκατάσταση PawnIO (pawnio.eu) και επανεκκινήστε το GearWin." : "Check the official PawnIO installation (pawnio.eu), then restart GearWin.")
                    : "PawnIO " + pawn + (greek ? " εντοπίστηκε· ο αισθητήρας ή η πρόσβαση μπορεί να μην υποστηρίζεται." : " detected; sensor/access may be unsupported."));
        }

        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη): αντί για ComboBox, τα δύο βελάκια στο πλακίδιο του δίσκου
        // κυκλώνουν μέσα σε αυτή τη λίστα - βλ. BtnDrivePrev_Click/BtnDriveNext_Click. Περιλαμβάνει
        // πλέον ΚΑΙ αφαιρούμενους δίσκους (USB), όχι μόνο Fixed, ώστε η ανίχνευση τύπου δίσκου να
        // έχει νόημα να δείξει "USB" όταν υπάρχει συνδεδεμένο flash drive.
        private void PopulateDriveList()
        {
            _drives.Clear();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)))
            {
                _drives.Add(drive.Name.TrimEnd('\\'));
            }
            _driveIndex = 0;
            if (_drives.Count > 0) TxtDriveLabel.Text = _drives[_driveIndex];

            // Ο δίσκος της Ανάλυσης Χώρου επιλέγεται ΞΕΧΩΡΙΣΤΑ (δίπλα στο κουμπί "Ανάλυση"), ανεξάρτητα από το πλακίδιο Disk.
            // Προεπιλογή: ο δίσκος του συστήματος.
            CmbAnalysisDrive.ItemsSource = _drives.ToList();
            var sysRoot = (System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
            var preferred = _drives.FirstOrDefault(d => string.Equals(d, sysRoot, StringComparison.OrdinalIgnoreCase)) ?? _drives.FirstOrDefault();
            if (preferred != null) CmbAnalysisDrive.SelectedItem = preferred;
        }

        private string? AnalysisDrive => CmbAnalysisDrive.SelectedItem as string;

        // Άλλος δίσκος = το προηγούμενο αποτέλεσμα δεν ισχύει πια: καθαρίζει τη μπάρα/το legend ώστε να μη δείχνει δεδομένα άλλου δίσκου.
        private void CmbAnalysisDrive_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsLoaded && DiskBarGrid == null) return;
            DiskBarGrid.ColumnDefinitions.Clear();
            DiskBarGrid.Children.Clear();
            ListDiskLegend.ItemsSource = null;
            TxtDiskAnalysisStatus.Text = LanguageService.T("Home_DiskAnalysisPrompt");
        }

        private string? CurrentDrive => _drives.Count > 0 ? _drives[_driveIndex] : null;

        private async void BtnDrivePrev_Click(object sender, System.Windows.RoutedEventArgs e) => await SwitchDrive(-1);
        private async void BtnDriveNext_Click(object sender, System.Windows.RoutedEventArgs e) => await SwitchDrive(1);

        private async Task SwitchDrive(int delta)
        {
            if (_drives.Count == 0) return;
            _driveIndex = (_driveIndex + delta + _drives.Count) % _drives.Count;
            _diskSelectionGeneration++;
            TxtDiskTemp.Text = "—";
            TxtDiskTemp.ToolTip = null;
            TxtDriveLabel.Text = _drives[_driveIndex];
            RefreshDisk();
            _ = RefreshDiskTemperatureAsync();
            await UpdateDiskIconAsync();
        }

        // Icon/color/label per physical drive type (HDD/SSD/USB) - the WMI lookup in
        // DriveTypeService can take a moment, so this runs off the UI thread and is only called
        // when the selected drive actually changes (at startup and on prev/next), not on every
        // 1-second RefreshMetrics tick.
        private bool _diskIconNeedsRetry;
        private bool _diskIconReading;

        private async Task UpdateDiskIconAsync()
        {
            var drive = CurrentDrive;
            if (drive == null || _unloaded) return;
            if (_diskIconReading) { _diskIconNeedsRetry = true; return; }
            _diskIconReading = true;
            _diskIconNeedsRetry = false;
            var generation = _diskSelectionGeneration;
            var token = ViewToken;
            HomeDiskMetadataService.Snapshot? snapshot;
            try { snapshot = await HomeDiskMetadataService.ReadAsync(drive).WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); return; }
            finally { _diskIconReading = false; }
            if (_unloaded || token.IsCancellationRequested) return;
            if (generation != _diskSelectionGeneration || snapshot == null)
            {
                _diskIconNeedsRetry = true;
                return;
            }
            var (model, kind, isGuess) = snapshot;
            // ΔΙΟΡΘΩΣΗ (χρήστης ανέφερε: "ζήτησα διαφορετικά εικονίδια για κάθε είδος δίσκου όταν
            // γίνεται η εναλλαγή") - το "Unknown" (ανίχνευση τύπου απέτυχε) χρησιμοποιούσε ΤΟ ΙΔΙΟ
            // glyph+χρώμα με το HDD - αν ένας δίσκος ανιχνευόταν Unknown, η εναλλαγή σε/από αυτόν
            // δεν έδειχνε ΚΑΜΙΑ οπτική αλλαγή, ενισχύοντας ακριβώς την εντύπωση "δεν αλλάζει τίποτα".
            // Τώρα ξεχωριστό, ουδέτερο γκρι glyph.
            var label = kind switch
            {
                PhysicalDriveKind.Nvme => "NVMe",
                PhysicalDriveKind.Ssd => "SSD",
                PhysicalDriveKind.Hdd => "HDD",
                PhysicalDriveKind.Usb => "USB",
                _ => "?",
            };

            TxtDriveKind.Text = isGuess ? $"{label} {LanguageService.T("Home_EstimateSuffix")}" : label;

            // ΝΕΟ - ρητό αίτημα χρήστη: "όπου ο δίσκος είναι SSD ή NVMe χρησιμοποίησε το αντίστοιχο
            // εικονίδιο" - HDD/USB/Unknown συνεχίζουν με το γενικό disk-icon.png.
            ImgDiskIcon.Source = new BitmapImage(new Uri(kind switch
            {
                PhysicalDriveKind.Nvme => "/GearWin;component/Assets/nvme-icon.png",
                PhysicalDriveKind.Ssd => "/GearWin;component/Assets/ssd-icon.png",
                _ => "/GearWin;component/Assets/disk-icon.png",
            }, UriKind.Relative));

            // Μάρκα/περιγραφή δίσκου + θερμοκρασία (ρητό αίτημα χρήστη) - μαζί με το είδος δίσκου
            // εδώ, όχι στο γρήγορο 1s tick, αφού ΚΑΝΕΝΑ από τα δύο δεν αλλάζει ζωντανά.
            TxtDriveModel.Text = model ?? "";

        }

        // Δεν αλλάζει σε δευτερόλεπτα σαν το ποσοστό χρήσης - αρκεί μία φορά στην εκκίνηση.
        // Live κάθε 1s - CPU/RAM/GPU αλλάζουν πραγματικά μέσα σε δευτερόλεπτα.
        private void RefreshCpuRamGpu()
        {
            var cpu = PerformanceProfileService.SampleCpu();
            if (cpu >= 0)
            {
                var value = (int)Math.Round(cpu);
                TxtCpuPercent.Text = $"{value}%";
                AnimateBar(BarCpu, value);
            }

            if (NativeMethods.GlobalMemoryStatusEx(out var memStatus))
            {
                var totalGb = memStatus.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
                var usedGb = totalGb - (memStatus.ullAvailPhys / 1024.0 / 1024.0 / 1024.0);
                var pct = (int)memStatus.dwMemoryLoad; // already an integer 0-100 percentage
                TxtRamPercent.Text = $"{pct}%";
                AnimateBar(BarRam, pct);
                TxtRamDetail.Text = $"{usedGb:0.0} / {totalGb:0.0} GB";
            }

            _ = RefreshSharedGpuAsync();
        }

        // ΔΙΟΡΘΩΣΗ (ρητό αίτημα χρήστη: "η ανανέωση των δίσκων να γίνεται όταν αλλάζει το ποσοστό,
        // δεν χρειάζεται live gauge") - αραιό timer (20s) + κλήση στην εκκίνηση/αλλαγή δίσκου, ΟΧΙ
        // πλέον σε κάθε 1s tick του γρήγορου timer.
        private void RefreshDisk()
        {
            if (CurrentDrive is not string driveName) return;
            try
            {
                var drive = new DriveInfo(driveName);
                var totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                var freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                var usedPct = (int)Math.Round(100.0 * (1 - drive.TotalFreeSpace / (double)drive.TotalSize));
                TxtDiskPercent.Text = $"{usedPct}%";
                AnimateBar(BarDisk, usedPct);
                TxtDiskDetail.Text = $"{freeGb:0.0} GB {LanguageService.T("Sys_DrivesFreeOf")} {totalGb:0.0} GB";
            }
            catch { /* drive can become unready (removable media ejected mid-run) */ }
        }

        private bool _healthBusy;

        private async void BtnRefreshHealth_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            HealthScoreService.Invalidate();
            await RefreshHealthScoreAsync();
        }

        // Διορθώνει ΜΟΝΟ τα ευρήματα της βαθμολογίας και λέει ειλικρινά τι έγινε: χώρος δίσκου -> καθαρισμός των "προτεινόμενων"
        // προσωρινών (QuickCleanService, ίδιος μηχανισμός με τον Πλήρη Έλεγχο· όχι διαγραφή ολόκληρου του TEMP), έλλειψη
        // σημείου επαναφοράς -> δημιουργία σημείου. Ασφάλεια (Defender), εκκρεμής επανεκκίνηση και προγράμματα εκκίνησης
        // ΔΕΝ αλλάζουν αυτόματα - αναφέρονται ως "χρειάζονται δική σας ενέργεια".
        private async void BtnFixAllHealth_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_healthBusy) return;
            _healthBusy = true;
            using var busy = StatusService.BeginScope(LanguageService.T("Home_CalculatingHealthScore"));
            var done = new List<string>();
            var manual = new List<string>();
            try
            {
                var result = await HealthScoreService.ComputeAsync(fresh: true);
                foreach (var issue in result.Issues)
                {
                    switch (issue.FixType)
                    {
                        case "Storage":
                            var freed = await SystemMaintenanceService.QuickCleanAsync();
                            done.Add(string.Format(LanguageService.T("Home_FixAllSpace"), QuickCleanService.FormatSize(freed)));
                            break;
                        case "RestorePoint":
                            if (await SystemService.CreateRestorePointAsync()) done.Add(LanguageService.T("Home_FixAllRestorePoint"));
                            else manual.Add(issue.Title);
                            break;
                        default:
                            manual.Add(issue.Title);
                            break;
                    }
                }
            }
            catch (Exception ex) { manual.Add(ex.Message); }
            finally { _healthBusy = false; }

            var summary = new List<string>(done);
            if (manual.Count > 0) summary.Add(string.Format(LanguageService.T("Home_FixAllManual"), string.Join(" / ", manual)));
            if (summary.Count == 0) summary.Add(LanguageService.T("Home_FixAllNothing"));
            (Window.GetWindow(this) as MainWindow)?.ShowToast(string.Join("  •  ", summary));
            HealthScoreService.Invalidate();
            await RefreshHealthScoreAsync();
        }

        // Category -> bar color, matching a reasonable visually-distinct palette (Optimizer.ps1
        // draws its own bar+legend colors per category - not ported 1:1 here, just kept distinct).
        private static readonly Dictionary<string, Color> CategoryColors = new()
        {
            ["Games"] = Color.FromRgb(90, 140, 255),
            ["Apps"] = Color.FromRgb(170, 110, 255),
            ["Photos"] = Color.FromRgb(90, 200, 120),
            ["Videos"] = Color.FromRgb(255, 150, 60),
            ["Documents"] = Color.FromRgb(60, 190, 190),
            ["Downloads"] = Color.FromRgb(230, 200, 60),
            ["Windows"] = Color.FromRgb(220, 90, 90),
            ["Other"] = Color.FromRgb(140, 140, 140),
        };

        // Port του Get-DiskCategoryLabel (Optimizer.ps1 ~18105). Property (όχι readonly field) ώστε να
        // ξαναχτίζεται με την τρέχουσα γλώσσα σε κάθε κλήση του BtnAnalyzeDisk_Click.
        private static Dictionary<string, string> CategoryLabels => new()
        {
            ["Games"] = LanguageService.T("Home_CatGames"),
            ["Apps"] = LanguageService.T("Home_CatApps"),
            ["Photos"] = LanguageService.T("Home_CatPhotos"),
            ["Videos"] = LanguageService.T("Home_CatVideos"),
            ["Documents"] = LanguageService.T("Home_CatDocuments"),
            ["Downloads"] = LanguageService.T("Home_CatDownloads"),
            ["Windows"] = "Windows",
            ["Other"] = LanguageService.T("Home_CatOther"),
        };

        private System.Threading.CancellationTokenSource? _analysisCts;

        // Το ίδιο κουμπί λειτουργεί ως "Ακύρωση" όσο τρέχει η ανάλυση (δεν χρειάζεται δεύτερο κουμπί).
        private async void BtnAnalyzeDisk_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_analysisCts != null) { _analysisCts.Cancel(); return; }
            if (AnalysisDrive is not string driveName) return;

            TxtDiskAnalysisStatus.Text = $"{LanguageService.T("Home_DiskAnalysisRunningPrefix")}{driveName}{LanguageService.T("Home_DiskAnalysisRunningSuffix")}";
            StatusService.SetBusy($"{LanguageService.T("Home_DiskAnalysisBusyPrefix")}{driveName}...");
            DiskBarGrid.ColumnDefinitions.Clear();
            DiskBarGrid.Children.Clear();
            ListDiskLegend.ItemsSource = null;
            BtnAnalyzeDisk.Content = LanguageService.T("Cancel");
            CmbAnalysisDrive.IsEnabled = false;
            _analysisCts = new System.Threading.CancellationTokenSource();

            DiskAnalysisResult result;
            try { result = await DiskAnalysisService.AnalyzeAsync(driveName, _analysisCts.Token); }
            catch (OperationCanceledException)
            {
                TxtDiskAnalysisStatus.Text = LanguageService.T("Home_DiskAnalysisPrompt");
                return;
            }
            catch (Exception ex)
            {
                TxtDiskAnalysisStatus.Text = ex.Message;
                return;
            }
            finally
            {
                StatusService.SetIdle(LanguageService.T("Ready"));
                _analysisCts.Dispose();
                _analysisCts = null;
                BtnAnalyzeDisk.Content = LanguageService.T("Home_Analyze");
                CmbAnalysisDrive.IsEnabled = true;
            }

            TxtDiskAnalysisStatus.Text = $"{LanguageService.T("Home_DiskAnalysisTotalPrefix")}{result.TotalUsedGb:0.0} GB ({driveName})";

            var cats = result.Categories.Where(c => c.SizeGb > 0).OrderByDescending(c => c.SizeGb).ToList();
            if (cats.Count == 0) return;

            var totalGb = Math.Max(0.01, result.TotalUsedGb);
            foreach (var cat in cats)
            {
                var color = CategoryColors.GetValueOrDefault(cat.Name, Color.FromRgb(150, 150, 150));
                DiskBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.5, cat.SizeGb), GridUnitType.Star) });
                var seg = new Border { Background = new SolidColorBrush(color) };
                Grid.SetColumn(seg, DiskBarGrid.ColumnDefinitions.Count - 1);
                DiskBarGrid.Children.Add(seg);
            }

            ListDiskLegend.ItemsSource = cats.Select(c =>
            {
                var pct = Math.Round(100.0 * c.SizeGb / totalGb, 1);
                var label = CategoryLabels.GetValueOrDefault(c.Name, c.Name);
                return new DiskLegendRow(
                    label,
                    $"{label} - {c.SizeGb:0.0} GB ({pct}%)",
                    new SolidColorBrush(CategoryColors.GetValueOrDefault(c.Name, Color.FromRgb(150, 150, 150))),
                    c.Roots);
            }).ToList();
        }

        // Port του Show-DiskCategoryDrilldown click routing (Optimizer.ps1 ~18341-18348) - μόνο
        // κατηγορίες με roots (όλες εκτός "Λοιπά") ανοίγουν το δευτερεύον παράθυρο.
        private void DiskLegendItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: DiskLegendRow row } || row.Roots.Count == 0) return;
            var window = new DiskDrilldownWindow(row.CategoryLabel, row.Roots) { Owner = System.Windows.Window.GetWindow(this) };
            window.Show();
        }

        // ΝΕΟ - roadmap "Προγραμματισμένος (background) Πλήρης Έλεγχος Υγείας" - απλή, τοπική εκδοχή
        // χωρίς Windows Task Scheduler: αν δεν έχει τρέξει ΠΟΤΕ Πλήρης Έλεγχος Υγείας, ή έχουν περάσει
        // 7+ μέρες από την τελευταία φορά (βλ. AppSettings.LastHealthCheckRunAt, γράφεται από
        // HealthCheckWindow όταν ολοκληρώνεται σάρωση), δείχνει ήπιο banner με απευθείας κουμπί - καμία
        // αυτόματη σάρωση/ενέργεια στο παρασκήνιο.
        // Η υπενθύμιση "έχουν περάσει Χ μέρες από τον Πλήρη Έλεγχο" εμφανίζεται ως ΜΙΑ ΓΡΑΜΜΗ μέσα στη λίστα μηνυμάτων της Βαθμολογίας
        // Υγείας (μαζί με τα υπόλοιπα ευρήματα) - όχι ως ξεχωριστό πλαίσιο με δεύτερο κουμπί "Πλήρης Έλεγχος Υγείας".
        private string? _healthCheckReminderText;

        private void CheckHealthCheckReminder()
        {
            var lastRun = AppSettingsService.Current.LastHealthCheckRunAt;
            var daysSince = lastRun.HasValue ? (DateTime.Now - lastRun.Value).TotalDays : (double?)null;
            _healthCheckReminderText = lastRun.HasValue && daysSince < 7
                ? null
                : lastRun.HasValue
                    ? string.Format(LanguageService.T("Home_HealthCheckReminderDays"), (int)daysSince!.Value)
                    : LanguageService.T("Home_HealthCheckReminderNever");
            RenderHealthIssues();
        }

        private async Task RefreshHealthScoreAsync()
        {
            if (_healthBusy) return;
            _healthBusy = true;
            var token = ViewToken;
            try { await RefreshHealthScoreCoreAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { if (!_unloaded) StatusService.Report(ex.Message); }
            finally { _healthBusy = false; }
        }

        private async Task RefreshHealthScoreCoreAsync(System.Threading.CancellationToken token)
        {
            TxtHealthLabel.Text = LanguageService.T("Home_Calculating");
            using var busy = StatusService.BeginScope(LanguageService.T("Home_CalculatingHealthScore"));
            // HealthScoreService.Compute() does several WMI queries (Defender status, AV product,
            // restore points) which can take a noticeable moment - runs off the UI thread so the
            // window stays responsive while it's working (see the async-UI rule this project follows).
            var result = await HealthScoreService.ComputeAsync().WaitAsync(token);
            if (token.IsCancellationRequested || _unloaded) return;

            if (result.IsComplete) HealthScoreHistoryService.RecordSample(result.Score);
            var sparkline = HealthScoreHistoryService.GetSparkline();
            TxtHealthSparkline.Text = sparkline;
            TxtHealthSparkline.Visibility = sparkline.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            // ΝΕΟ - roadmap "πιο εμφανές S.M.A.R.T. στην Αρχική" - έλεγχος του δίσκου συστήματος μόνο
            // (όχι όλων των δίσκων - κρατά το badge σύντομο/ενιαίο, το πλήρες breakdown ανά δίσκο
            // παραμένει στην καρτέλα Σύστημα). GetSmartHealthy κάνει WMI ερωτήματα - εκτελείται off the
            // UI thread, ίδιο μοτίβο με το HealthScoreService.Compute() παραπάνω.
            var sysDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var smartHealthy = await HomeDiskMetadataService.ReadSystemSmartAsync(sysDrive).WaitAsync(token);
            if (token.IsCancellationRequested || _unloaded) return;
            if (smartHealthy.HasValue)
            {
                TxtDiskSmart.Text = smartHealthy.Value
                    ? $"✓ {LanguageService.T("Home_DiskSmartGood")}"
                    : $"⚠ {LanguageService.T("Home_DiskSmartWarning")}";
                TxtDiskSmart.Foreground = smartHealthy.Value
                    ? new SolidColorBrush(Color.FromRgb(90, 200, 120))
                    : new SolidColorBrush(Color.FromRgb(220, 80, 80));
                TxtDiskSmart.Visibility = Visibility.Visible;
            }
            else
            {
                TxtDiskSmart.Visibility = Visibility.Collapsed;
            }

            TxtHealthScore.Text = result.DisplayScore;
            var scoreColor = result.Score >= 80
                ? new SolidColorBrush(Color.FromRgb(90, 200, 120))
                : result.Score >= 50
                    ? new SolidColorBrush(Color.FromRgb(230, 170, 60))
                    : new SolidColorBrush(Color.FromRgb(220, 80, 80));

            TxtHealthScore.Foreground = scoreColor;
            TxtHealthLabel.Foreground = scoreColor;
            TxtHealthLabel.Text = !result.IsComplete ? (LanguageService.Current == "el" ? "Ελλιπής έλεγχος" : "Incomplete check") : result.Score >= 80 ? LanguageService.T("Home_HealthGood") : result.Score >= 50 ? LanguageService.T("Home_HealthFair") : LanguageService.T("Home_HealthNeedsAttention");

            _lastHealthIssues = result.Issues;
            _lastScoreColor = scoreColor;
            RenderHealthIssues();
            TxtHealthUpdated.Text = string.Format(LanguageService.T("Home_UpdatedAt"), DateTime.Now.ToString("HH:mm:ss"));
        }
    }

    // Bindable row for the ItemsControl in HomeView.xaml (Title + the dot color, matching the
    // score-colored dot per issue that Optimizer.ps1 draws next to each issue label).
    public record HealthIssueRow(string Title, Brush DotColor);

    // Bindable row for the Disk Analysis legend (WrapPanel below the single segmented bar).
    public record DiskLegendRow(string CategoryLabel, string LegendText, Brush BarColor, IReadOnlyList<string> Roots);

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeGlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static bool GlobalMemoryStatusEx(out MEMORYSTATUSEX status)
        {
            status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            return NativeGlobalMemoryStatusEx(ref status);
        }
    }

    public class SecurityRowVm
    {
        public SecurityRowVm(SecurityItem item)
        {
            Label = item.Label;
            Detail = item.Detail;
            Dot = new System.Windows.Media.SolidColorBrush(item.Level switch
            {
                SecurityLevel.Good => System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50),
                SecurityLevel.Warning => System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B),
                _ => System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E),
            });
        }
        public string Label { get; }
        public string Detail { get; }
        public System.Windows.Media.Brush Dot { get; }
    }
}
