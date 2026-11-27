using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    // ΝΕΟ - ρητό αίτημα χρήστη: "θέλω αντίστοιχο γραφικό με το Health Check του PC Manager" +
    // "το 'PC status can be optimized' σε δευτερεύον παράθυρο". Δύο φάσεις: σάρωση (πραγματικά scans
    // από ήδη υπάρχοντα services, ΕΝΑ pill φωτίζεται τη φορά) -> αποτελέσματα (κάρτες σύνοψης +
    // Βελτιστοποίηση Τώρα). Καλείται από το Home tab.
    public partial class HealthCheckWindow : Window
    {
        private readonly ObservableCollection<ScanChecklistRow> _checklist = new();
        private readonly ObservableCollection<ResultCardVm> _results = new();
        private bool _cancelled;
        private bool _scanning;
        private bool _applying;
        private MemoryCleanerService.MemoryStats _memory;

        // Ευρήματα από τη σάρωση - διατηρούνται για να τα εφαρμόσει το BtnOptimizeNow_Click.
        private IReadOnlyList<QuickCleanItem> _spaceItems = Array.Empty<QuickCleanItem>();
        private IReadOnlyList<RegistryFinding> _traceFindings = Array.Empty<RegistryFinding>();

        // ΝΕΟ - ρητό αίτημα χρήστη: το γράφημα/παράθυρο αποτελεσμάτων να προσθέσει κάτι πέρα από την
        // απλή αντιγραφή του PC Manager's "PC status can be optimized" pill layout. Το ήδη υπάρχον,
        // ήδη δοκιμασμένο HealthScoreService (χρησιμοποιείται αλλού στην Αρχική) υπολογίζει ΜΙΑ
        // πραγματική 0-100 βαθμολογία υγείας από 5 ανεξάρτητους ελέγχους (χώρος δίσκου, εκκρεμής
        // επανεκκίνηση, Defender, πλήθος εκκίνησης, σημεία επαναφοράς) - επαναχρησιμοποιείται εδώ ΩΣ
        // ΕΧΕΙ (καμία διπλή υλοποίηση) για να δώσει στο αποτέλεσμα μια συγκεκριμένη αριθμητική
        // βαθμολογία αντί του γενικού στατικού "!" εικονιδίου, ΚΑΙ μια επιπλέον κάρτα για τυχόν
        // ευρήματα που δεν καλύπτονται ήδη από τα 5 στάδια σάρωσης (π.χ. Defender off, reboot pending).
        private HealthResult? _scoreResult;

        public HealthCheckWindow()
        {
            InitializeComponent();
            ThemeManager.AttachWindow(this);
            ListScanChecklist.ItemsSource = _checklist;
            ListResultCards.ItemsSource = _results;
            Loaded += async (_, _) => await RunScanAsync();
            // Κλείσιμο με το X (όχι μόνο το Ακύρωση) πρέπει επίσης να σταματά τη σάρωση/ενημέρωση UI.
            Closed += (_, _) => _cancelled = true;
            Closing += (_, e) => { if (_applying) e.Cancel = true; };
        }

        private void SetPillState(FrameworkElement pill, bool active)
        {
            var border = (System.Windows.Controls.Border)pill;
            border.BorderBrush = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("CardBorderBrush");
            border.BorderThickness = new Thickness(active ? 2.5 : 1.5);
        }

        private async Task RunScanAsync()
        {
            if (_scanning || _cancelled) return;
            _scanning = true;
            using var busy = StatusService.BeginScope(LanguageService.T("HealthCheck_Scanning"));
            try { await RunScanCoreAsync(); }
            finally { _scanning = false; }
        }

        private async Task RunScanCoreAsync()
        {
            var stages = new (System.Windows.Controls.Border Pill, string TitleKey, string DescKey, Func<Task> Run)[]
            {
                (PillNetwork, "HealthCheck_StageNetwork", "HealthCheck_DescNetwork", ScanNetworkAsync),
                (PillSpace, "HealthCheck_StageSpace", "HealthCheck_DescSpace", ScanSpaceAsync),
                (PillTraces, "HealthCheck_StageTraces", "HealthCheck_DescTraces", ScanTracesAsync),
                (PillRestore, "HealthCheck_StageRestore", "HealthCheck_DescRestore", ScanRestoreAsync),
                (PillApps, "HealthCheck_StageApps", "HealthCheck_DescApps", ScanAppsAsync),
            };

            // ΔΙΟΡΘΩΣΗ - roadmap "Καμία επαναφορά progress bar σε exception": χωρίς αυτό, ένα Retry
            // μετά από αποτυχία θα διπλασίαζε τις γραμμές του checklist (το foreach παρακάτω τις
            // προσθέτει ξανά χωρίς Clear).
            _checklist.Clear();
            BtnRetryScan.Visibility = Visibility.Collapsed;
            foreach (var stage in stages)
                _checklist.Add(new ScanChecklistRow(LanguageService.T(stage.TitleKey), LanguageService.T(stage.DescKey)));

            BarScanProgress.Maximum = stages.Length;
            BarScanProgress.Value = 0;
            for (var i = 0; i < stages.Length; i++)
            {
                if (_cancelled) return;
                var stage = stages[i];
                TxtCurrentStage.Text = $"{LanguageService.T("HealthCheck_Scanning")}{LanguageService.T(stage.TitleKey)}...";
                SetPillState(stage.Pill, true);


                // ΔΙΟΡΘΩΣΗ - roadmap "Καμία επαναφορά progress bar σε exception": πριν, μια εξαίρεση
                // σε ΟΠΟΙΟΔΗΠΟΤΕ στάδιο (π.χ. QuickCleanService.ScanAsync μέσα στο ScanSpaceAsync)
                // άφηνε το BarScanProgress παγωμένο, το pill μόνιμα φωτισμένο, και το παράθυρο ΠΟΤΕ
                // δεν έφτανε στη φάση αποτελεσμάτων - μόνο το καθολικό DispatcherUnhandledException
                // έδειχνε ένα message box, χωρίς να αγγίζει καθόλου την τοπική κατάσταση UI εδώ.
                try
                {
                    await stage.Run();
                }
                catch (Exception ex)
                {
                    SetPillState(stage.Pill, false);
                    TxtCurrentStage.Text = $"{LanguageService.T("HealthCheck_StageFailedPrefix")}{LanguageService.T(stage.TitleKey)}{LanguageService.T("HealthCheck_StageFailedSuffix")}{ex.Message}";
                    BtnRetryScan.Visibility = Visibility.Visible;
                    return;
                }

                if (_cancelled) return;
                SetPillState(stage.Pill, false);
                _checklist[i].MarkDone();
                BarScanProgress.Value = i + 1;
            }

            if (_cancelled) return;
            try { _scoreResult = await Task.Run(HealthScoreService.ComputeFresh); }
            catch { _scoreResult = null; }
            if (_cancelled) return;
            // ΝΕΟ - roadmap ιδέα #7 (ρητό αίτημα χρήστη: "κάνε τα 4-7") - βλ.
            // Services/HealthScoreDailyHistoryService.cs (ΞΕΧΩΡΙΣΤΟ από το ήδη υπάρχον, in-memory
            // HealthScoreHistoryService που τροφοδοτεί το sparkline της Αρχικής), ίδιο μοτίβο με το
            // Disk Trend (Health tab).
            if (_scoreResult?.IsComplete == true) HealthScoreDailyHistoryService.RecordIfNeeded(_scoreResult.Score, _scoreResult.Issues?.Count ?? 0);
            _memory = await Task.Run(MemoryCleanerService.Snapshot);
            if (_cancelled) return;
            _results.Clear();
            BuildResults();
            ScanPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;

            // ΝΕΟ - roadmap "Προγραμματισμένος Πλήρης Έλεγχος Υγείας" - καταγράφει ΠΟΤΕ ολοκληρώθηκε
            // μια σάρωση, ώστε η Αρχική να μπορεί να υπενθυμίσει αν έχουν περάσει 7+ μέρες.
            AppSettingsService.Current.LastHealthCheckRunAt = DateTime.Now;
            AppSettingsService.Save();
        }

        private async void BtnRetryScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

        private (bool Connected, int? LatencyMs) _networkStatus;
        private long _spaceFreeableBytes;
        private long _browserCacheBytes;
        private int _tracesCount;
        private int _restorableCount;
        private int _appsCount;

        private async Task ScanNetworkAsync() => _networkStatus = await NetworkService.CheckInternetAsync();

        private async Task ScanSpaceAsync()
        {
            _spaceItems = await QuickCleanService.ScanAsync();
            _browserCacheBytes = _spaceItems.FirstOrDefault(i => i.Key == "BrowserCache")?.SizeBytes ?? 0;
            _spaceFreeableBytes = _spaceItems.Where(i => SystemMaintenanceService.BuildCleanupPlan(new[] { i }, true, false).Count > 0).Sum(i => i.SizeBytes);
        }

        private async Task ScanTracesAsync()
        {
            _traceFindings = await HealthCleanupService.ScanRegistryAsync();
            _tracesCount = _traceFindings.Count;
        }

        private async Task ScanRestoreAsync() => _restorableCount = await Task.Run(TweakService.CountRestorableTweaks);

        private async Task ScanAppsAsync()
        {
            var apps = await BloatwareService.ScanUwpAppsAsync();
            _appsCount = apps.Count;
        }

        private void BuildResults()
        {
            bool memoryPressure = MemoryCleanerService.NeedsMaintenance(_memory);
            var anyIssue = _spaceFreeableBytes > 1024 * 1024 || _tracesCount > 0 || memoryPressure || (_scoreResult?.Issues.Count ?? 0) > 0;
            bool greek = LanguageService.Current == "el";
            TxtResultHeadline.Text = LanguageService.T(anyIssue ? "HealthCheck_HeadlineIssues" : "HealthCheck_HeadlineGood");

            var score = _scoreResult?.Score ?? 0;
            TxtScoreBadge.Text = _scoreResult?.DisplayScore ?? "—";
            ScoreBadge.Background = new SolidColorBrush(score >= 80 ? Color.FromRgb(0x4C, 0xAF, 0x50) : score >= 50 ? Color.FromRgb(0xFF, 0x98, 0x00) : Color.FromRgb(0xE5, 0x39, 0x35));
            TxtScoreLine.Text = _scoreResult?.IsComplete != true ? (greek ? "Βαθμολογία μη διαθέσιμη" : "Score unavailable") : $"{LanguageService.T("HealthCheck_ScorePrefix")}{score}/100";
            if (_scoreResult?.IsComplete != true) TxtResultHeadline.Text = greek ? "Ο έλεγχος δεν ολοκληρώθηκε πλήρως" : "Check incomplete";

            _results.Add(new ResultCardVm("Space", LanguageService.T("HealthCheck_CardSpaceTitlePrefix") + QuickCleanService.FormatSize(_spaceFreeableBytes),
                LanguageService.T("HealthCheck_CardSpaceDesc"), _spaceFreeableBytes > 1024 * 1024, canSelect: _spaceFreeableBytes > 0, isSelected: _spaceFreeableBytes > 1024 * 1024,
                actionLabel: null));

            _results.Add(new ResultCardVm("Browser", (greek ? "Cache περιηγητών: " : "Browser cache: ") + QuickCleanService.FormatSize(_browserCacheBytes),
                greek ? "Προαιρετικός καθαρισμός μόνο επιτρεπόμενων φακέλων cache." : "Optional cleanup of allowlisted cache folders only.",
                false, canSelect: _browserCacheBytes > 0, isSelected: false, actionLabel: null));
            _results.Add(new ResultCardVm("Memory", (greek ? "Διαθέσιμη RAM: " : "Available RAM: ") + $"{_memory.AvailableGb:0.0} GB",
                greek ? "Προαιρετική συντήρηση μόνο όταν υπάρχει πίεση μνήμης." : "Optional maintenance only under memory pressure.",
                memoryPressure, canSelect: memoryPressure, isSelected: false, actionLabel: null));

            _results.Add(new ResultCardVm("Traces", $"{_tracesCount} {LanguageService.T("HealthCheck_CardTracesTitleSuffix")}",
                LanguageService.T("HealthCheck_CardTracesDesc"), _tracesCount > 0, canSelect: false, isSelected: false,
                actionLabel: LanguageService.T("HealthCheck_CardScoreAction")));

            _results.Add(new ResultCardVm("Restore", $"{_restorableCount} {LanguageService.T("HealthCheck_CardRestoreTitleSuffix")}",
                LanguageService.T("HealthCheck_CardRestoreDesc"), false, canSelect: false, isSelected: false,
                actionLabel: LanguageService.T("HealthCheck_CardScoreAction")));

            _results.Add(new ResultCardVm("Apps", $"{_appsCount} {LanguageService.T("HealthCheck_CardAppsTitleSuffix")}",
                LanguageService.T("HealthCheck_CardAppsDesc"), false, canSelect: false, isSelected: false,
                actionLabel: LanguageService.T("HealthCheck_CardAppsAction")));

            var netLabel = _networkStatus.Connected
                ? $"{LanguageService.T("HealthCheck_CardNetworkOk")}{(_networkStatus.LatencyMs.HasValue ? $" ({_networkStatus.LatencyMs} ms)" : "")}"
                : LanguageService.T("HealthCheck_CardNetworkFail");
            _results.Add(new ResultCardVm("Network", netLabel, LanguageService.T("HealthCheck_CardNetworkDesc"), false, canSelect: false, isSelected: false,
                actionLabel: null));

            // ΝΕΟ - βλ. σχόλιο στο _scoreResult field: ευρήματα του HealthScoreService (Defender off,
            // εκκρεμής επανεκκίνηση, πολλά προγράμματα εκκίνησης, χωρίς πρόσφατο σημείο επαναφοράς)
            // που ΔΕΝ καλύπτονται ήδη από τα 5 στάδια σάρωσης παραπάνω - μόνο ενημερωτική κάρτα (χωρίς
            // CheckBox/επιλογή), ίδιο πνεύμα ασφάλειας με την κάρτα "Apps": καμία αυτόματη ενέργεια σε
            // ρυθμίσεις ασφαλείας/συστήματος χωρίς ρητή επιβεβαίωση του χρήστη στην αντίστοιχη καρτέλα.
            var extraIssues = _scoreResult?.Issues ?? Array.Empty<HealthIssue>();
            var scoreDesc = extraIssues.Count > 0
                ? $"{LanguageService.T("HealthCheck_CardScoreDesc")} {string.Join("  •  ", extraIssues.Select(i => i.Title))}"
                : _scoreResult == null ? (greek ? "Δεν υπάρχουν πλήρη δεδομένα." : "Complete data unavailable.") : LanguageService.T("HealthCheck_CardScoreAllGood");
            // ΝΕΟ - roadmap "HealthScoreService ευρήματα - ενέργεια διόρθωσης": ίδιο πνεύμα με την
            // κάρτα "Apps" - κουμπί που πηγαίνει κατευθείαν στην καρτέλα Σύστημα (καλύπτει Startup Apps
            // + Σημεία Επαναφοράς, 2 από τα 4 πιθανά είδη ευρήματος) αντί για καμία ενέργεια. Το Defender
            // ΔΕΝ ελέγχεται από καμία καρτέλα της εφαρμογής (μόνο εμφανίζεται, ΠΟΤΕ αλλάζεται αυτόματα -
            // βλ. Help_Home_HealthScore) - η καρτέλα Σύστημα παραμένει ο πιο λογικός γενικός προορισμός.
            _results.Add(new ResultCardVm("Score", $"{extraIssues.Count} {LanguageService.T("HealthCheck_CardScoreTitleSuffix")}",
                scoreDesc, extraIssues.Count > 0, canSelect: false, isSelected: false,
                actionLabel: extraIssues.Count > 0 ? LanguageService.T("HealthCheck_CardScoreAction") : null));
        }

        private async void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            BtnExportReport.IsEnabled = false;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{LanguageService.T("HealthCheck_ScorePrefix")}{(_scoreResult?.IsComplete != true ? "—" : _scoreResult.Score + "/100")}");
                sb.AppendLine();
                foreach (var card in _results)
                {
                    sb.AppendLine($"=== {card.Headline} ===");
                    sb.AppendLine(card.Description);
                    sb.AppendLine();
                }

                // ΝΕΟ - roadmap ιδέα #7 (ρητό αίτημα χρήστη: "κάνε τα 4-7" - "αρχείο τάσης στον
                // χρόνο") - το PDF εξαγόμενο εδώ πλέον περιλαμβάνει ΟΛΟΚΛΗΡΟ το ιστορικό βαθμολογιών
                // (όχι μόνο το τρέχον στιγμιότυπο παραπάνω), ίδιο πνεύμα με το Disk Trend section.
                var history = HealthScoreDailyHistoryService.GetHistory();
                if (history.Count > 1)
                {
                    sb.AppendLine($"=== {LanguageService.T("HealthCheck_TrendSectionTitle")} ===");
                    foreach (var snap in history)
                        sb.AppendLine($"{snap.Date}   {snap.Score}/100   ({snap.IssueCount} {LanguageService.T("HealthCheck_TrendIssuesSuffix")})");
                    sb.AppendLine();
                }

                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"HealthCheck_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                await Task.Run(() => PdfExportService.ExportTextReport(path, LanguageService.T("HealthCheck_Title"), sb.ToString()));
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                TxtOptimizeResult.Text = $"{LanguageService.T("HealthCheck_ExportReportSaved")}{path}";
            }
            catch (Exception ex)
            {
                TxtOptimizeResult.Text = $"{LanguageService.T("HealthCheck_ExportReportFailed")} {ex.Message}";
            }
            finally
            {
                BtnExportReport.IsEnabled = true;
            }
        }

        private async void BtnOptimizeNow_Click(object sender, RoutedEventArgs e)
        {
            // ΝΕΟ (6.1.0) - dry-run/προεπισκόπηση: πριν αλλάξει οτιδήποτε, ο χρήστης βλέπει ακριβώς τι θα
            // γίνει (επιλεγμένες κάρτες + μεγέθη/πλήθη από την ήδη ολοκληρωμένη σάρωση) και επιβεβαιώνει.
            if (_applying || _scanning || _cancelled) return;
            var planned = _results.Where(r => r.IsSelected && r.CanSelect && r.Key is "Space" or "Browser" or "Memory").ToList();
            if (planned.Count == 0) { TxtOptimizeResult.Text = LanguageService.T("HealthCheck_NothingSelected"); return; }
            if (planned.Count > 0)
            {
                var preview = new System.Text.StringBuilder(LanguageService.T("DryRun_Intro")).AppendLine().AppendLine();
                foreach (var card in planned) preview.AppendLine($"• {card.Headline}").AppendLine($"   {card.Description}");
                preview.AppendLine().Append(LanguageService.T("DryRun_Footer"));
                if (ThemedMessageBox.Show(preview.ToString(), LanguageService.T("DryRun_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }

            _applying = true;
            BtnOptimizeNow.IsEnabled = false;
            TxtOptimizeResult.Text = LanguageService.T("HealthCheck_Optimizing");
            using var busy = StatusService.BeginScope(LanguageService.T("HealthCheck_Optimizing"));

            try
            {
            // UMP02: this entry point must never mutate registry/tweaks. Dedicated tools retain explicit review.
            var summary = new List<string>();
            bool space = planned.Any(r => r.Key == "Space");
            bool browser = planned.Any(r => r.Key == "Browser");
            bool memory = planned.Any(r => r.Key == "Memory");
            if (space || browser)
            {
                var freed = await SystemMaintenanceService.CleanSelectedAsync(_spaceItems, space, browser);
                summary.Add(string.Format(LanguageService.T("HealthCheck_SummarySpace"), QuickCleanService.FormatSize(freed)));
                await ScanSpaceAsync();
            }
            if (memory) summary.Add(MemoryCleanerService.Describe(await MemoryCleanerService.CleanAsync()));
            _memory = await Task.Run(MemoryCleanerService.Snapshot);
            _scoreResult = await Task.Run(HealthScoreService.ComputeFresh);
            _results.Clear();
            BuildResults();

            TxtOptimizeResult.Text = summary.Count > 0 ? string.Join("  •  ", summary) : LanguageService.T("HealthCheck_NothingSelected");
            BtnOptimizeNow.Content = LanguageService.T("HealthCheck_Done");
            }
            catch (Exception ex)
            {
                TxtOptimizeResult.Text = $"{LanguageService.T("HealthCheck_ExportReportFailed")} {ex.Message}";
            }
            finally
            {
                _applying = false;
                BtnOptimizeNow.IsEnabled = true;
            }
        }

        private void ResultCardAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
            if ((Owner as MainWindow ?? Application.Current.MainWindow as MainWindow) is not MainWindow mw) return;
            if (_applying) return;
            switch (tag)
            {
                case "Traces":
                    mw.SelectTab("Health#0");
                    break;
                case "Restore":
                    mw.SelectTab("Tweaks");
                    break;
                case "Apps":
                    // "App Uninstall" ΔΕΝ απεγκαθιστά αυτόματα (μη αναστρέψιμη ενέργεια) - ανοίγει την
                    // καρτέλα Εφαρμογές & Bloat για έλεγχο/επιβεβαίωση από τον χρήστη, ίδιο πνεύμα
                    // ασφάλειας με όλες τις υπόλοιπες καταστροφικές ενέργειες της εφαρμογής.
                    mw.SelectTab("Bloatware#1");
                    break;
                case "Score":
                    // ΝΕΟ - βλ. σχόλιο στο BuildResults(): τα ευρήματα HealthScoreService (Defender/
                    // reboot/startup/restore point) ΔΕΝ διορθώνονται αυτόματα εδώ - πηγαίνει στην
                    // καρτέλα Σύστημα για χειροκίνητο έλεγχο/ενέργεια.
                    // Προορισμός ανάλογα με το πρώτο εύρημα: εκκίνηση -> Εκκίνηση, σημείο επαναφοράς -> Αντίγραφα, ασφάλεια -> Ασφάλεια.
                    var first = _scoreResult?.Issues.FirstOrDefault()?.FixType;
                    mw.SelectTab(first switch
                    {
                        "Startup" => "Optimization#2",
                        "RestorePoint" => "System#1",
                        "Defender" or "DefenderOtherAV" => "Network#0",
                        _ => "Health",
                    });
                    break;
                default:
                    return;
            }
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_applying) return;
            _cancelled = true;
            Close();
        }
    }

    public class ScanChecklistRow : INotifyPropertyChanged
    {
        public string Title { get; }
        public string Description { get; }
        private Visibility _doneVisibility = Visibility.Collapsed;
        public Visibility DoneVisibility { get => _doneVisibility; private set { _doneVisibility = value; PropertyChanged?.Invoke(this, new(nameof(DoneVisibility))); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        public ScanChecklistRow(string title, string description) { Title = title; Description = description; }
        public void MarkDone() => DoneVisibility = Visibility.Visible;
    }

    public class ResultCardVm : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Headline { get; }
        public Brush HeadlineColor { get; }
        public string Description { get; }
        public bool CanSelect { get; }
        public string? ActionLabel { get; }
        public Visibility ActionVisibility => string.IsNullOrEmpty(ActionLabel) ? Visibility.Collapsed : Visibility.Visible;

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        public ResultCardVm(string key, string headline, string description, bool hasIssue, bool canSelect, bool isSelected, string? actionLabel)
        {
            Key = key;
            Headline = headline;
            Description = description;
            CanSelect = canSelect;
            _isSelected = isSelected;
            ActionLabel = actionLabel;
            HeadlineColor = hasIssue ? new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)) : new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
    }
}
