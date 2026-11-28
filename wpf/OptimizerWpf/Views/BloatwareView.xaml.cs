using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    public partial class BloatwareView : UserControl
    {
        private void NestedScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => NestedScrollHelper.Forward(sender, e);

        private void TxtRecommendedSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = TxtRecommendedSearch.Text?.Trim();
            _recommendedView.Filter = string.IsNullOrEmpty(term)
                ? null
                : o => o is RecommendedAppRow r && r.App.Title.Contains(term, System.StringComparison.OrdinalIgnoreCase);
        }

        private void TxtInstalledAppsSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = TxtInstalledAppsSearch.Text?.Trim();
            _installedAppsView.Filter = string.IsNullOrEmpty(term)
                ? null
                : o => o is DeepUninstallRow r && r.App.DisplayName.Contains(term, System.StringComparison.OrdinalIgnoreCase);
        }

        // ΝΕΟ - ρητό αίτημα χρήστη: "να μην ξανασκαναρονται καθε φορα (προτεινομενες εφαρμογες
        // winget)" - το BloatwareView δημιουργείται εξ αρχής σε κάθε επίσκεψη της καρτέλας (βλ.
        // MainWindow.ShowTabContent), οπότε χωρίς αυτή τη στατική cache ο σειριακός έλεγχος winget ανά
        // εφαρμογή (CheckRecommendedInstalledAsync) επαναλαμβανόταν από την αρχή κάθε φορά. Η cache
        // ζει όσο τρέχει η εφαρμογή· ενημερώνεται ΚΑΙ από τα Install/Uninstall handlers παρακάτω ώστε
        // να μην ξεμείνει stale μέσα στην ίδια συνεδρία.
        private bool _recommendedBusy;

        private readonly ObservableCollection<BuiltinAppRow> _builtin = new();
        private readonly ObservableCollection<RecommendedAppRow> _recommended = new();
        // ΔΙΟΡΘΩΣΗ (bug εντοπίστηκε - χρήστης ανέφερε: "τα αποτελέσματα των σαρώσεων χάνονται όταν
        // αλλάζω καρτέλα") - static αντί για instance, ίδιο μοτίβο με OptimizationView/SystemView/
        // AdvancedView - το Deep Uninstall scan (BtnRefreshInstalledApps_Click) ΔΕΝ τρέχει αυτόματα
        // στον constructor (σε αντίθεση με το _recommended παραπάνω), οπότε χωρίς αυτό η λίστα
        // εμφανιζόταν εντελώς άδεια σε κάθε επιστροφή στην καρτέλα, ζητώντας νέο χειροκίνητο κλικ.
        private static readonly ObservableCollection<DeepUninstallRow> _installedApps = new();
        private readonly ListCollectionView _recommendedView;
        private readonly ICollectionView _installedAppsView;

        // ===== Πρόσθετες Λειτουργίες Windows (ενοποιημένη λίστα - μετακινήθηκε εδώ από την καρτέλα
        // Προηγμένα Εργαλεία, ρητό αίτημα χρήστη - ίδια ουσιαστικά λειτουργία με τα "Ενσωματωμένα
        // Στοιχεία Windows" παραπάνω) - static για τον ίδιο λόγο όπως παραπάνω, το scan δεν τρέχει
        // αυτόματα στον constructor. =====
        private static List<AllFeatureRow> _allFeatures = new();
        private readonly ObservableCollection<AllFeatureRow> _displayedFeatures = new();

        public BloatwareView()
        {
            InitializeComponent();
            ListBuiltin.ItemsSource = _builtin;
            ListAllFeatures.ItemsSource = _displayedFeatures;
            LoadPopularFeatureChips();
            // 6.8.0: η λίστα των πρόσθετων λειτουργιών των Windows (με την κατάστασή τους) φαίνεται πάντα· φορτώνεται αυτόματα την πρώτη φορά.
            UpdateAllFeaturesDisplay();
            Loaded += async (_, _) => { if (_allFeatures.Count == 0) await RefreshAllFeaturesAsync(silent: true); };
            // Ρητό αίτημα χρήστη: "στις προτεινόμενες εφαρμογές υπήρχε ομαδοποίηση... να είναι
            // διακριτά τα είδη" - ομαδοποίηση κατά App.Group (ίδιες ομάδες με το ps1 original) μέσω
            // ListCollectionView, βλ. ItemsControl.GroupStyle στο XAML για τις επικεφαλίδες.
            _recommendedView = new ListCollectionView(_recommended);
            _recommendedView.GroupDescriptions.Add(new PropertyGroupDescription("App.Group"));
            ListRecommended.ItemsSource = _recommendedView;

            // Ρητό αίτημα χρήστη (πρόταση βελτίωσης που εγκρίθηκε: "κάνε τα όλα") - πεδία αναζήτησης
            // στις δύο μεγαλύτερες, πιο δύσκολο-να-σαρωθούν λίστες της καρτέλας.
            _installedAppsView = CollectionViewSource.GetDefaultView(_installedApps);
            ListInstalledApps.ItemsSource = _installedAppsView;

            _builtin.Add(new BuiltinAppRow("Microsoft Copilot", "Copilot"));
            _builtin.Add(new BuiltinAppRow("Xbox Gaming Overlay", "XboxGamingOverlay"));
            _builtin.Add(new BuiltinAppRow(LanguageService.T("Bloatware_NewsWeather"), "BingNews"));
            _builtin.Add(new BuiltinAppRow("Windows Media Player (Legacy)", "WMP"));
            _builtin.Add(new BuiltinAppRow(LanguageService.T("Bloatware_PhotoViewerClassic"), "PhotoViewer"));
            _builtin.Add(new BuiltinAppRow("HEVC Video Extensions", "HEVCVideoExtensions"));

            foreach (var app in BloatwareService.RecommendedApps) _recommended.Add(new RecommendedAppRow(app));
            _ = CheckRecommendedInstalledAsync();
        }

        // ΔΙΟΡΘΩΣΗ (χρήστης ανέφερε: "ενώ έχω εγκατεστημένο το Chrome, στις προτεινόμενες εμφανίζεται
        // πως δεν είναι") - το "winget list --id X --exact" ΔΕΝ βρίσκει πάντα εφαρμογές που ΕΙΝΑΙ
        // πράγματι εγκατεστημένες (π.χ. αν εγκαταστάθηκαν με άλλον τρόπο εκτός winget, ή λόγω γνωστού
        // quirk όπου το winget δεν αντιστοιχίζει πάντα σωστά ID↔εγκατεστημένο πακέτο όταν η ίδια η
        // εφαρμογή τρέχει elevated). Τώρα ελέγχεται ΚΑΙ το μητρώο Uninstall (ίδιος μηχανισμός με το
        // AppIconService - ανεξάρτητος από winget/elevation) ως δεύτερο, ανεξάρτητο σήμα - "εγκατεστημένη"
        // αν ΤΟ ΕΝΑ ΑΠΟ ΤΑ ΔΥΟ το επιβεβαιώνει, πιο ανθεκτικό από το να βασίζεται μόνο στο winget.
        private async System.Threading.Tasks.Task CheckRecommendedInstalledAsync(bool refresh = false)
        {
            if (_recommendedBusy) return;
            _recommendedBusy = true;
            try
            {
                await System.Threading.Tasks.Task.WhenAll(_recommended.Select(async row => row.Icon = await LogoCacheService.GetLogoAsync(row.App.WingetId)));
                TxtRecommendedStatus.Text = LanguageService.T("Bloatware_CheckingInstalled");
                var inventory = await RecommendedInventoryService.GetAsync(refresh);
                foreach (var row in _recommended)
                    row.InstalledState = inventory.TryGetValue(row.App.WingetId, out var state) ? state : null;
                TxtRecommendedStatus.Text = _recommended.Any(row => row.InstalledState == null)
                    ? (LanguageService.Current == "el" ? "Ορισμένες εφαρμογές χρειάζονται επιβεβαίωση." : "Some apps need verification.")
                    : LanguageService.T("Bloatware_ReadyDot");
            }
            catch (System.Exception ex) { TxtRecommendedStatus.Text = ex.Message; }
            finally { _recommendedBusy = false; }
        }

        private async void BtnRefreshRecommended_Click(object sender, RoutedEventArgs e)
            => await CheckRecommendedInstalledAsync(refresh: true);

        private static string BuiltinTitle(BuiltinAppRow row) => row.Name;

        private async void BtnBuiltinInstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: BuiltinAppRow row }) return;
            TxtBuiltinStatus.Text = $"{LanguageService.T("Bloatware_InstallingPrefix")}{row.Name}...";
            StatusService.SetBusy($"{LanguageService.T("Bloatware_InstallingPrefix")}{row.Name}...");
            var ok = row.Key switch
            {
                "Copilot" => await BloatwareService.InstallAppxAsync("Copilot", "9NXTC3B7QF27"),
                "XboxGamingOverlay" => await BloatwareService.InstallAppxAsync("XboxGamingOverlay", "9NZKPSTSNW4P"),
                "BingNews" => await BloatwareService.InstallAppxAsync("BingNews", "9WZDNCRFHVFW"),
                "WMP" => await BloatwareService.InstallWindowsMediaPlayerAsync(),
                "HEVCVideoExtensions" => await BloatwareService.InstallAppxAsync("HEVCVideoExtensions", "9N4WGH0Z6VHQ"),
                "PhotoViewer" => RunSync(BloatwareService.InstallPhotoViewer),
                _ => false,
            };
            StatusService.SetIdle(LanguageService.T("Ready"));
            TxtBuiltinStatus.Text = ok ? $"{row.Name}{LanguageService.T("Bloatware_CompletedSuffix")}" : $"{row.Name}{LanguageService.T("Bloatware_FailedManualSuffix")}";
        }

        private async void BtnBuiltinRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: BuiltinAppRow row }) return;
            TxtBuiltinStatus.Text = $"{LanguageService.T("Bloatware_RemovingPrefix")}{row.Name}...";
            StatusService.SetBusy($"{LanguageService.T("Bloatware_RemovingPrefix")}{row.Name}...");
            var ok = row.Key switch
            {
                "WMP" => await BloatwareService.RemoveWindowsMediaPlayerAsync(),
                "PhotoViewer" => RunSync(BloatwareService.RemovePhotoViewer),
                _ => await BloatwareService.RemoveAppxAsync(row.Key),
            };
            StatusService.SetIdle(LanguageService.T("Ready"));
            TxtBuiltinStatus.Text = ok ? $"{row.Name}{LanguageService.T("Bloatware_RemovedSuffix")}" : $"{row.Name}{LanguageService.T("Bloatware_FailedSuffix")}";
        }

        private static bool RunSync(System.Action action) { action(); return true; }

        private async void BtnRecommendedToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: RecommendedAppRow row } button || _recommendedBusy) return;
            _recommendedBusy = true;
            button.IsEnabled = false;
            using var busy = StatusService.BeginScope(row.App.Title);
            try
            {
                if (row.InstalledState == null)
                {
                    row.InstalledState = await BloatwareService.ProbeInstalledAsync(row.App.WingetId);
                    if (row.InstalledState == null)
                        TxtRecommendedStatus.Text = LanguageService.Current == "el" ? "Αδυναμία επιβεβαίωσης εγκατάστασης." : "Installation state could not be verified.";
                    return;
                }
                var ok = row.IsInstalled
                    ? await BloatwareService.WingetUninstallAsync(row.App.WingetId)
                    : row.App.WingetId == "CodecGuide.K-LiteCodecPack.Mega"
                        ? await BloatwareService.InstallKLiteAsync()
                        : await BloatwareService.WingetInstallAsync(row.App.WingetId);
                RecommendedInventoryService.Invalidate();
                row.InstalledState = await BloatwareService.ProbeInstalledAsync(row.App.WingetId);
                if (!ok) ThemedMessageBox.Show($"{LanguageService.T("Bloatware_ActionFailedPrefix")}{row.App.Title}{LanguageService.T("Bloatware_ActionFailedSuffix")}", LanguageService.T("Bloatware_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex) { TxtRecommendedStatus.Text = ex.Message; }
            finally { button.IsEnabled = true; _recommendedBusy = false; }
        }

        private async void BtnInstallGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string groupName } button || _recommendedBusy) return;
            var toInstall = _recommended.Where(r => r.App.Group == groupName && r.InstalledState == false).ToList();
            if (toInstall.Count == 0) return;
            if (ThemedMessageBox.Show($"{LanguageService.T("Bloatware_InstallAllConfirmPrefix")}{toInstall.Count}{LanguageService.T("Bloatware_InstallAllConfirmSuffix")}",
                    LanguageService.T("Bloatware_ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            _recommendedBusy = true;
            button.IsEnabled = false;
            using var busy = StatusService.BeginScope(LanguageService.T("Bloatware_Install"));
            int failed = 0;
            try
            {
                foreach (var row in toInstall)
                {
                    var ok = row.App.WingetId == "CodecGuide.K-LiteCodecPack.Mega"
                        ? await BloatwareService.InstallKLiteAsync()
                        : await BloatwareService.WingetInstallAsync(row.App.WingetId);
                    row.InstalledState = null;
                    if (!ok) failed++;
                }
                RecommendedInventoryService.Invalidate();
                var inventory = await RecommendedInventoryService.GetAsync(refresh: true);
                foreach (var row in _recommended)
                    row.InstalledState = inventory.TryGetValue(row.App.WingetId, out var state) ? state : null;
                if (failed > 0) ThemedMessageBox.Show($"{failed}{LanguageService.T("Bloatware_InstallAllFailedSuffix")}", LanguageService.T("Bloatware_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex) { RecommendedInventoryService.Invalidate(); TxtRecommendedStatus.Text = ex.Message; }
            finally { button.IsEnabled = true; _recommendedBusy = false; }
        }

        private void BtnOpenUwpManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new UwpAppManagerWindow { Owner = Window.GetWindow(this) };
            window.ShowDialog();
        }

        // ΝΕΟ - roadmap "Διασταύρωση με εκκίνηση" - σήμανση εγκατεστημένων εφαρμογών που είναι ΕΠΙΣΗΣ
        // ρυθμισμένες να τρέχουν στην εκκίνηση, με ασαφή αντιστοίχιση ονόματος (η ίδια εφαρμογή συχνά
        // εμφανίζεται με ελαφρώς διαφορετικό όνομα στο Uninstall registry έναντι στο Run key).
        private async void BtnRefreshInstalledApps_Click(object sender, RoutedEventArgs e)
        {
            if (_deepUninstallBusy) return;
            _deepUninstallBusy = true;
            if (sender is Button button) button.IsEnabled = false;
            using var busy = StatusService.BeginScope(LanguageService.T("Bloatware_LoadingInstalled"));
            try
            {
                var startupItems = await SystemService.LoadStartupItemsAsync();
                var apps = await BloatwareService.LoadInstalledAppsAsync();
                _installedApps.Clear();
                foreach (var app in apps)
                {
                    var alsoAtStartup = app.DisplayName.Length >= 4 && startupItems.Any(s =>
                        s.Name.Length >= 4 &&
                        (app.DisplayName.Contains(s.Name, System.StringComparison.CurrentCultureIgnoreCase) ||
                         s.Name.Contains(app.DisplayName, System.StringComparison.CurrentCultureIgnoreCase)));
                    _installedApps.Add(new DeepUninstallRow(app, alsoAtStartup));
                }
            }
            catch (Exception ex) { busy.End(ex.Message); }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
                _deepUninstallBusy = false;
            }
        }

        private static bool s_usageReviewBusy;

        private async void BtnUsageReview_Click(object sender, RoutedEventArgs e)
        {
            if (s_usageReviewBusy || _deepUninstallBusy) return;
            var title = LanguageService.T("Bloatware_UsageTitle");
            var notice = LanguageService.T("Bloatware_UsageConsent");
            if (ThemedMessageBox.Show(notice, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            s_usageReviewBusy = true;
            BtnUsageReview.IsEnabled = false;
            using var busy = StatusService.BeginScope(title);
            try
            {
                var apps = await BloatwareService.LoadInstalledAppsAsync();
                var result = await System.Threading.Tasks.Task.Run(() => AppUsageReview.Read(apps));
                if (!IsLoaded) return;
                var text = result == null
                    ? LanguageService.T("Bloatware_UsageUnavailable")
                    : result.Count == 0
                        ? LanguageService.T("Bloatware_UsageEmpty")
                        : LanguageService.T("Bloatware_UsageResults") +
                          "\n\n" + string.Join("\n", result.Select(row => row.Name + " · " + row.LastObservedUtc.ToLocalTime().ToString("d")));
                ThemedMessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { if (IsLoaded) ThemedMessageBox.Show(LanguageService.T("Opt_Unknown"), title, MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { BtnUsageReview.IsEnabled = true; s_usageReviewBusy = false; }
        }

        private bool _deepUninstallBusy;

        private async void BtnDeepUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: DeepUninstallRow row } button || _deepUninstallBusy) return;
            var question = $"{LanguageService.T("Bloatware_UninstallConfirmPrefix")}{row.App.DisplayName}{LanguageService.T("Bloatware_UninstallConfirmSuffix")}";
            if (row.App.MachineProductCode == null)
                question += "\n\n" + LanguageService.T("Bloatware_UninstallSettings");
            if (ThemedMessageBox.Show(question, LanguageService.T("Bloatware_ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            _deepUninstallBusy = true;
            button.IsEnabled = false;
            using var busy = StatusService.BeginScope(LanguageService.T("Sys_CreatingRestorePoint"));
            try
            {
                if (row.App.MachineProductCode != null &&
                    await RestoreGuardService.EnsureRecentAsync() == RestoreGuardResult.Failed)
                {
                    ThemedMessageBox.Show(LanguageService.T("Bloatware_UninstallRestoreFailed"), "GearWin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                busy.Update($"{LanguageService.T("Bloatware_Uninstall")} {row.App.DisplayName}...");
                var result = await SafeUninstallService.UninstallAsync(row.App);
                RecommendedInventoryService.Invalidate();
                if (result.State != SafeUninstallService.Outcome.Removed)
                {
                    string message = result.State switch
                    {
                        SafeUninstallService.Outcome.WindowsSettingsOpened => LanguageService.T("Bloatware_UninstallSettingsOpened"),
                        SafeUninstallService.Outcome.RestartRequired => LanguageService.T("Bloatware_UninstallRestart"),
                        SafeUninstallService.Outcome.NotVerified => LanguageService.T("Bloatware_UninstallUnverified"),
                        _ => LanguageService.T("Bloatware_UninstallFailed") + (result.ExitCode is int code ? $" ({code})" : "")
                    };
                    busy.End(message);
                    ThemedMessageBox.Show(message, "GearWin", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _installedApps.Remove(row);
                var residuals = await System.Threading.Tasks.Task.Run(() => BloatwareService.FindResidualFolders(row.App.DisplayName));
                if (residuals.Count > 0)
                {
                    var note = LanguageService.T("Bloatware_ResidualAdvisory");
                    ThemedMessageBox.Show(note + "\n\n" + string.Join("\n", residuals),
                        LanguageService.T("Bloatware_ResidualFilesTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                busy.End(LanguageService.T("Bloatware_UninstallVerified"));
            }
            catch (Exception ex) { busy.End(ex.Message); }
            finally { button.IsEnabled = true; _deepUninstallBusy = false; }
        }

        // ===== Πρόσθετες Λειτουργίες Windows (ενοποιημένη λίστα - βλ. σχόλιο στο XAML) =====

        private void LoadPopularFeatureChips()
        {
            // ΔΙΟΡΘΩΣΗ (χρήστης ζήτησε: "κάνε τα κουμπιά πιο φαρδιά και πλατιά στις πρόσθετες
            // λειτουργίες των windows") - μεγαλύτερο Padding/FontSize ώστε τα chips να είναι πιο
            // άνετα/ευανάγνωστα - το WrapPanel γύρω τους ήδη τυλίγει σε νέα γραμμή όποτε χρειάζεται,
            // οπότε δεν υπάρχει κίνδυνος περικοπής, απλά πιο άνετο μέγεθος ανά chip.
            foreach (var f in AdvancedToolsService.CuratedFeatures)
            {
                var btn = new Button
                {
                    Content = f.DisplayName,
                    Style = (Style)FindResource("AccentButtonStyle"),
                    FontSize = 15,
                    Padding = new Thickness(24, 13, 24, 13),
                    Margin = new Thickness(0, 0, 10, 10),
                    Tag = f.DisplayName,
                };
                btn.Click += (_, _) => { TxtAllFeaturesSearch.Text = (string)btn.Tag; };
                PanelPopularFeatures.Children.Add(btn);
            }
        }

        private async void BtnRefreshAllFeatures_Click(object sender, RoutedEventArgs e) => await RefreshAllFeaturesAsync(silent: false);

        private async System.Threading.Tasks.Task RefreshAllFeaturesAsync(bool silent)
        {
            StatusService.SetBusy(LanguageService.T("Adv_ScanningFeatures"));
            var curatedNames = AdvancedToolsService.CuratedFeatures.ToDictionary(f => f.FeatureName, f => f.DisplayName);
            var (scanned, error) = await AdvancedToolsService.ScanAllFeaturesAsync();
            if (scanned == null)
            {
                var msg = string.IsNullOrWhiteSpace(error)
                    ? LanguageService.T("Adv_FeatureScanFailed")
                    : $"{LanguageService.T("Adv_FeatureScanFailed")}\n\n{error}";
                StatusService.SetIdle(LanguageService.T("Adv_FeatureScanFailed"));
                if (silent) return;   // αυτόματη φόρτωση: σιωπηλά, ο χρήστης μπορεί να πατήσει «Ανανέωση λίστας»
                ThemedMessageBox.Show(msg, LanguageService.T("Adv_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _allFeatures = scanned
                .Select(f => new AllFeatureRow(f.FeatureName, curatedNames.TryGetValue(f.FeatureName, out var friendly) ? friendly : f.FeatureName) { IsEnabled = f.Enabled })
                .OrderBy(f => f.DisplayName)
                .ToList();
            StatusService.SetIdle(LanguageService.T("Ready"));
            UpdateAllFeaturesDisplay();
        }

        private void TxtAllFeaturesSearch_TextChanged(object sender, TextChangedEventArgs e) => UpdateAllFeaturesDisplay();

        private void UpdateAllFeaturesDisplay()
        {
            _displayedFeatures.Clear();
            var term = TxtAllFeaturesSearch.Text?.Trim() ?? "";
            foreach (var f in _allFeatures.Where(f => term == "" || f.DisplayName.Contains(term, System.StringComparison.OrdinalIgnoreCase) || f.FeatureName.Contains(term, System.StringComparison.OrdinalIgnoreCase)).Take(100))
                _displayedFeatures.Add(f);
        }

        // ΔΙΟΡΘΩΣΗ (χρήστης ανέφερε: "όταν επιλέγω κάτι για προσθήκη δεν εμφανίζεται μπάρα προόδου" +
        // "επέλεξα εγκατάσταση του .NET Framework 3.5 και μετά το popup μήνυμα δεν βλέπω να γίνεται
        // εγκατάσταση") - 3 πραγματικά bugs εντοπίστηκαν εδώ:
        // 1) Καμία μπάρα προόδου δεν υπήρχε για αυτή τη λειτουργία (βλ. νέο ProgressOptionalFeature
        //    στο XAML) - μόνο το γενικό status-bar spinner, εύκολο να μην το προσέξει κανείς.
        // 2) Το αποτέλεσμα του SetFeatureEnabledAsync (bool επιτυχίας/αποτυχίας) ΑΓΝΟΟΥΝΤΑΝ εντελώς -
        //    αν το powershell command απέτυχε (π.χ. NetFx3 χρειάζεται πρόσβαση στο Windows Update και
        //    δεν την είχε), ο χρήστης ΔΕΝ έβλεπε ΚΑΝΕΝΑ μήνυμα σφάλματος και το toggle παρέμενε στη
        //    "επιτυχημένη" θέση παρόλο που η λειτουργία ΔΕΝ άλλαξε πραγματικά στο σύστημα.
        // 3) Το .NET Framework 3.5 ειδικά αποτυγχάνει συχνά χωρίς σύνδεση στο διαδίκτυο (χρειάζεται
        //    Windows Update ως πηγή) - τώρα το μήνυμα σφάλματος περιλαμβάνει μια στοχευμένη υπόδειξη
        //    ΜΟΝΟ για αυτή τη λειτουργία (Adv_FeatureNetFx3Hint).
        private async void ToggleAllFeature_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton { Tag: AllFeatureRow row } button) return;
            if (ThemedMessageBox.Show($"{(row.IsEnabled ? LanguageService.T("Adv_FeatureEnable") : LanguageService.T("Adv_FeatureDisable"))}{LanguageService.T("Adv_FeatureConfirmMid")}{row.DisplayName}{LanguageService.T("Adv_FeatureConfirmSuffix")}",
                    LanguageService.T("Adv_ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                row.IsEnabled = !row.IsEnabled;
                return;
            }
            var targetState = row.IsEnabled;
            button.IsEnabled = false;
            ProgressOptionalFeature.Visibility = Visibility.Visible;
            StatusService.SetBusy($"{row.DisplayName}...");
            var success = await AdvancedToolsService.SetFeatureEnabledAsync(row.FeatureName, targetState);
            StatusService.SetIdle(LanguageService.T("Ready"));
            ProgressOptionalFeature.Visibility = Visibility.Collapsed;
            button.IsEnabled = true;

            if (!success)
            {
                row.IsEnabled = !targetState;
                var msg = LanguageService.T("Adv_FeatureToggleFailed");
                if (row.FeatureName == "NetFx3") msg += LanguageService.T("Adv_FeatureNetFx3Hint");
                ThemedMessageBox.Show(msg, LanguageService.T("Adv_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public class AllFeatureRow : INotifyPropertyChanged
    {
        public string FeatureName { get; }
        // Φιλικό όνομα για τις γνωστές λειτουργίες (πρώην ξεχωριστή "curated" λίστα, τώρα ενοποιημένη
        // εδώ) - αλλιώς το ίδιο το τεχνικό FeatureName (π.χ. "TelnetClient").
        public string DisplayName { get; }
        private bool _isEnabled;
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; PropertyChanged?.Invoke(this, new(nameof(IsEnabled))); } }
        public AllFeatureRow(string featureName, string displayName) { FeatureName = featureName; DisplayName = displayName; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public record BuiltinAppRow(string Name, string Key);

    public class RecommendedAppRow : INotifyPropertyChanged
    {
        public RecommendedApp App { get; }
        private bool? _installedState;
        public bool? InstalledState
        {
            get => _installedState;
            set { _installedState = value; PropertyChanged?.Invoke(this, new(nameof(InstalledState))); PropertyChanged?.Invoke(this, new(nameof(IsInstalled))); PropertyChanged?.Invoke(this, new(nameof(ButtonLabel))); }
        }
        public bool IsInstalled { get => InstalledState == true; set => InstalledState = value; }
        public string ButtonLabel => InstalledState == null
            ? (LanguageService.Current == "el" ? "Έλεγχος" : "Verify")
            : IsInstalled ? LanguageService.T("Bloatware_Uninstall") : LanguageService.T("Bloatware_Install");
        public Visibility IconFallbackVisibility => _icon == null ? Visibility.Visible : Visibility.Collapsed;

        // Ρητό αίτημα χρήστη: "βάλε τα logos των εφαρμογών" - πραγματικό εικονίδιο ΜΟΝΟ αν η εφαρμογή
        // είναι ήδη εγκατεστημένη (βλ. AppIconService.FindInstalledIconPath) - καμία τοπική πηγή
        // λογότυπου υπάρχει για ΜΗ εγκατεστημένες εφαρμογές χωρίς λήψη από το διαδίκτυο.
        private System.Windows.Media.ImageSource? _icon;
        public System.Windows.Media.ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; PropertyChanged?.Invoke(this, new(nameof(Icon))); PropertyChanged?.Invoke(this, new(nameof(IconFallbackVisibility))); }
        }

        public RecommendedAppRow(RecommendedApp app) => App = app;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Εικονίδιο ανά κατηγορία προτεινόμενων εφαρμογών (port του ps1's ξεχωριστό IconType ανά ομάδα,
    // ~14761-14792) - χρησιμοποιείται στο GroupStyle.HeaderTemplate του ListRecommended.
    public class GroupHeaderIconConverter : IValueConverter
    {
        // Κλειδί = ουδέτερο-ως-προς-γλώσσα RecommendedApp.Group (βλ. BloatwareService.RecommendedApps).
        private static readonly System.Collections.Generic.Dictionary<string, (string Icon, string NameKey)> Groups = new()
        {
            ["Browsers"] = ("\U0001F310", "Bloatware_GroupBrowsers"),
            ["Compression"] = ("\U0001F5DC", "Bloatware_GroupCompression"),
            ["Multimedia"] = ("\U0001F3AC", "Bloatware_GroupMultimedia"),
            ["Communication"] = ("✉", "Bloatware_GroupCommunication"),
            ["Tools"] = ("\U0001F527", "Bloatware_GroupTools"),
            ["Documents"] = ("\U0001F4C4", "Bloatware_GroupDocuments"),
            ["SystemLibs"] = ("⚙", "Bloatware_GroupSystemLibs"),
        };

        public object Convert(object? value, System.Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string ?? "";
            return Groups.TryGetValue(key, out var g) ? $"{g.Icon} {LanguageService.T(g.NameKey)}" : key;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
            throw new System.NotSupportedException();
    }

    public record DeepUninstallRow(InstalledApp App, bool AlsoRunsAtStartup = false);
}
