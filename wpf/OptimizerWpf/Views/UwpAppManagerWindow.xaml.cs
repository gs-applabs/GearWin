using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    public partial class UwpAppManagerWindow : Window
    {
        private void NestedScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => NestedScrollHelper.Forward(sender, e);

        private readonly ObservableCollection<UwpAppInfo> _apps = new();

        public UwpAppManagerWindow()
        {
            InitializeComponent();
            Closed += (_, _) => _closed = true;
            ThemeManager.AttachWindow(this);
            _view = System.Windows.Data.CollectionViewSource.GetDefaultView(_apps);
            _view.Filter = o => o is UwpAppInfo a && (TxtSearch.Text.Trim().Length == 0 || a.Name.Contains(TxtSearch.Text.Trim(), System.StringComparison.OrdinalIgnoreCase)
                                                    || a.PackageFullName.Contains(TxtSearch.Text.Trim(), System.StringComparison.OrdinalIgnoreCase));
            ListApps.ItemsSource = _view;
            Loaded += (_, _) => BtnScan_Click(this, new RoutedEventArgs());   // 6.9.1: η λίστα φαίνεται αμέσως (αυτόματη σάρωση στο άνοιγμα)
        }

        private readonly System.ComponentModel.ICollectionView _view;
        private bool _scanning;

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

        // ΔΙΟΡΘΩΣΗ (γενικός έλεγχος γραμμής κατάστασης, ρητό αίτημα χρήστη) - και οι δύο χειριστές
        // εδώ έκαναν πραγματική, όχι-στιγμιαία δουλειά (σάρωση Appx, απεγκατάσταση) χωρίς να αγγίζουν
        // καθόλου το StatusService - το τοπικό TxtStatus δεν αρκεί, ασυνεπές με όλες τις άλλες
        // σαρώσεις/ενέργειες της εφαρμογής που ήδη δείχνουν το κυκλάκι στην κύρια γραμμή κατάστασης.
        private bool _closed;

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_scanning || _closed) return;
            _scanning = true;
            TxtStatus.Text = LanguageService.T("Adv_Scanning");
            using var busy = StatusService.BeginScope(LanguageService.T("Adv_Scanning"));
            try
            {
                var results = await BloatwareService.ScanUwpAppsAsync();
                if (_closed) return;
                _apps.Clear();
                foreach (var a in results) _apps.Add(a);
                TxtStatus.Text = $"{LanguageService.T("Uwp_FoundPrefix")}{results.Count}{LanguageService.T("Uwp_FoundSuffix")}";
            }
            catch (System.Exception ex)
            {
                if (!_closed) TxtStatus.Text = ex.Message;
                busy.End(ex.Message);
            }
            finally { _scanning = false; }
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _scanning || sender is not Button { Tag: UwpAppInfo app } button) return;
            if (ThemedMessageBox.Show($"{LanguageService.T("Uwp_UninstallConfirmPrefix")}{app.PackageFullName};", LanguageService.T("Adv_ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _scanning = true;
            button.IsEnabled = false;
            using var busy = StatusService.BeginScope($"{LanguageService.T("Uwp_UninstallConfirmPrefix")}{app.PackageFullName}...");
            try
            {
                if (await RestoreGuardService.EnsureRecentAsync() == RestoreGuardResult.Failed)
                {
                    if (!_closed) TxtStatus.Text = LanguageService.Current == "el"
                        ? "Αποτυχία σημείου επαναφοράς. Δεν έγινε απεγκατάσταση."
                        : "Restore point failed. Uninstall not started.";
                    return;
                }
                var ok = await BloatwareService.RemoveUwpAppAsync(app.PackageFullName);
                if (_closed) return;
                if (ok) { _apps.Remove(app); RecommendedInventoryService.Invalidate(); }
                else ThemedMessageBox.Show(LanguageService.T("Uwp_UninstallFailed"), LanguageService.T("Adv_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.Exception ex)
            {
                if (!_closed) TxtStatus.Text = ex.Message;
                busy.End(ex.Message);
            }
            finally { button.IsEnabled = true; _scanning = false; }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
