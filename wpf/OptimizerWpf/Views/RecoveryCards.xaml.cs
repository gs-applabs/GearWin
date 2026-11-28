using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    public partial class RecoveryCards : UserControl
    {
        private void NestedScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => NestedScrollHelper.Forward(sender, e);
        private readonly ObservableCollection<RestorePointInfo> _restorePoints = new();


        private async System.Threading.Tasks.Task LoadBackupTargetDrivesAsync()
        {
            var systemDrive = Path.GetPathRoot(System.Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            var names = await System.Threading.Tasks.Task.Run(() =>
                DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .Select(d => d.Name.TrimEnd('\\'))
                    .Where(n => !string.Equals(n, systemDrive, System.StringComparison.OrdinalIgnoreCase))
                    .ToList());
            foreach (var name in names) ListBackupTargetDrives.Items.Add(name);
            if (ListBackupTargetDrives.Items.Count > 0) ListBackupTargetDrives.SelectedIndex = 0;
        }

        private async void BtnStartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (ListBackupTargetDrives.SelectedItem is not string targetDrive) return;
            BackupControls.IsEnabled = false;
            try
            {
                var target = await BackupService.PrepareTargetAsync(targetDrive);
                var warning = LanguageService.Current == "el"
                    ? "Επιβεβαιώστε τον τόμο αντιγράφου ασφαλείας. Τα παλαιότερα αντίγραφα ενδέχεται να επηρεαστούν από τη διαχείριση χώρου των Windows."
                    : "Confirm the backup volume. Windows backup space management may affect older backups.";
                if (ThemedMessageBox.Show(
                        $"{warning}\n\n{target.Drive} — {target.Label}\nDisk {target.DiskNumber}\n{target.VolumeGuid}",
                        LanguageService.T("Backup_StartButton"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                ListBackupVersions.ItemsSource = null;
                TxtBackupStatus.Text = LanguageService.T("Backup_Running");
                using var scope = StatusService.BeginScope(LanguageService.T("Backup_Running"));
                var (success, output) = await BackupService.StartSystemImageBackupAsync(target);
                TxtBackupStatus.Text = LanguageService.T(success ? "Backup_Success" : "Backup_Failed");
                // Even success must show the catalog and its verification limitations.
                if (!string.IsNullOrWhiteSpace(output)) ListBackupVersions.ItemsSource = new[] { output };
                scope.End(LanguageService.T("Ready"));
            }
            catch (System.Exception ex)
            {
                TxtBackupStatus.Text = LanguageService.T("Backup_Failed");
                ListBackupVersions.ItemsSource = new[] { ex.Message };
            }
            finally { BackupControls.IsEnabled = true; }
        }

        private async void BtnViewBackupVersions_Click(object sender, RoutedEventArgs e)
        {
            if (ListBackupTargetDrives.SelectedItem is not string targetDrive) return;
            BackupControls.IsEnabled = false;
            TxtBackupStatus.Text = "";
            try
            {
                var (success, output) = await BackupService.GetBackupVersionsAsync(targetDrive);
                if (!success) TxtBackupStatus.Text = LanguageService.T("Backup_Failed");
                ListBackupVersions.ItemsSource = new[] { string.IsNullOrWhiteSpace(output)
                    ? LanguageService.T("Backup_NoVersionsFound") : output };
            }
            finally
            {
                BackupControls.IsEnabled = true;
            }
        }

        private void BtnWindowsRecoveryGuide_Click(object sender, RoutedEventArgs e) =>
            OpenBackupGuide("https://learn.microsoft.com/windows-server/administration/windows-commands/wbadmin-start-sysrecovery");

        private void BtnDiskCloneGuide_Click(object sender, RoutedEventArgs e) =>
            OpenBackupGuide("https://clonezilla.org/show-live-doc-content.php?topic=clonezilla-live/doc/03_Disk_to_disk_clone");


        // Export a self-contained recovery kit. The destructive action never runs inside Windows.
        private void BtnExportOfflineClone_Click(object sender, RoutedEventArgs e)
        {
            string warning = LanguageService.Current == "el"
                ? "Απαιτεί OFFLINE Linux Live. Η κλωνοποίηση καταστρέφει ΟΛΑ τα δεδομένα του δίσκου προορισμού και γράφει ΟΛΟΥΣ τους τομείς, ακόμη και τον κενό χώρο. Θέλετε να εξαγάγετε το πακέτο;"
                : "EXPERT: Requires offline Linux Live. Cloning permanently ERASES the entire target disk and writes ALL sectors including unused space. Export recovery kit?";
            if (ThemedMessageBox.Show(warning, "GearWin — Offline clone",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var folder = Path.Combine(System.AppContext.BaseDirectory, "Recovery");
            var script = Path.Combine(folder, "gearwin_clone.py");
            var adapter = Path.Combine(folder, "partclone_adapter.py");
            var guide = Path.Combine(folder, "README.md");
            if (!File.Exists(script) || !File.Exists(adapter) || !File.Exists(guide))
            {
                ThemedMessageBox.Show("Recovery assets are missing from this build.", "GearWin",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var picker = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "GearWin-Offline-Clone-Kit.zip",
                Filter = "ZIP (*.zip)|*.zip",
                DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true
            };
            if (picker.ShowDialog() != true) return;
            try
            {
                using var stream = new FileStream(picker.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(script, "gearwin_clone.py", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(adapter, "partclone_adapter.py", CompressionLevel.Optimal);
                archive.CreateEntryFromFile(guide, "README.md", CompressionLevel.Optimal);
                TxtBackupStatus.Text = "Offline recovery kit exported: " + picker.FileName +
                    ". Boot Linux Live separately before attempting the clone.";
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException ||
                                               ex is System.Security.SecurityException)
            {
                ThemedMessageBox.Show(ex.Message, "GearWin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCheckWindowsRe_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            TxtBackupStatus.Text = "Checking WinRE (read-only)...";
            try
            {
                var result = await RecoveryMetadataProbe.WinReAsync();
                string output = result.Success ? result.Output : "WinRE status unavailable: " + result.Output;
                TxtBackupStatus.Text = output;
            }
            catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException
                                                  or System.ComponentModel.Win32Exception)
            {
                TxtBackupStatus.Text = "Unable to check WinRE: " + ex.Message;
            }
            finally { button.IsEnabled = true; }
        }

        private void OpenBackupGuide(string address)
        {
            try
            {
                using var browser = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address)
                {
                    UseShellExecute = true,
                });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ThemedMessageBox.Show(ex.Message, LanguageService.T("Sys_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task LoadRestorePointsAsync()
        {
            TxtRestorePointsStatus.Text = LanguageService.T("Sys_LoadingEllipsis");
            _restorePoints.Clear();
            foreach (var p in await SystemService.ListRestorePointsAsync()) _restorePoints.Add(p);
            TxtRestorePointsStatus.Text = $"{_restorePoints.Count}{LanguageService.T("Sys_RestorePointsSuffix")}";
        }

        private async void BtnRefreshRestorePoints_Click(object sender, RoutedEventArgs e) => await LoadRestorePointsAsync();

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            StatusService.SetBusy(LanguageService.T("Sys_CreatingRestorePoint"));
            var ok = await SystemService.CreateRestorePointAsync();
            StatusService.SetIdle(LanguageService.T("Ready"));
            TxtRestorePointsStatus.Text = ok ? LanguageService.T("Sys_RestorePointCreated") : LanguageService.T("Sys_RestorePointFailed");
            if (ok) await LoadRestorePointsAsync();
        }

        private void BtnOpenProtectionSettings_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true });

        // Ρητά ΔΕΝ υλοποιείται delete-by-sequence - βλ. σχόλιο στο SystemService.RestoreToPointAsync.
        private async void BtnRestoreToPoint_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: RestorePointInfo point }) return;
            if (ThemedMessageBox.Show($"{LanguageService.T("Sys_RestoreConfirmPrefix")}{point.Description}{LanguageService.T("Sys_RestoreConfirmSuffix")}",
                    LanguageService.T("Sys_RestoreWarningTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            StatusService.SetBusy(LanguageService.T("Sys_RestoringSystem"));
            var ok = await SystemService.RestoreToPointAsync(point.SequenceNumber);
            // ΔΙΟΡΘΩΣΗ (εξονυχιστικός έλεγχος εντόπισε): το αποτέλεσμα αγνοούνταν εντελώς - σε επιτυχία ο
            // υπολογιστής επανεκκινεί άμεσα (η γραμμή κατάστασης παύει να έχει σημασία), αλλά σε αποτυχία
            // έμενε μόνιμα κολλημένη σε "Επαναφορά συστήματος..." χωρίς καμία εξήγηση.
            if (!ok)
            {
                StatusService.SetIdle(LanguageService.T("Sys_RestoreFailed"));
                ThemedMessageBox.Show(LanguageService.T("Sys_RestoreFailed"), LanguageService.T("Sys_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void OnTransferLanguageChanged()
        {
            if (Dispatcher.CheckAccess()) TranslateTransferUi();
            else Dispatcher.BeginInvoke(new System.Action(TranslateTransferUi));
        }

        private void TranslateTransferUi()
        {
            bool greek = LanguageService.Current == "el";
            TxtTransferTitle.Text = greek ? "Έλεγχος φακέλων και λίστα εφαρμογών" : "Folder check and application inventory";
            TxtTransferHelp.Text = greek
                ? "Έλεγχος πριν από μεταφορά: διαδρομές, σύνδεσμοι και ελεύθερος χώρος. Δεν γίνεται αντιγραφή ή δοκιμαστική εγγραφή στον SSD."
                : "Read-only check of paths, links and free space before a transfer. No copying or test writes to your SSD.";
            BtnChooseFrom.Content = greek ? "Φάκελος πηγής…" : "Source folder…";
            BtnChooseTo.Content = greek ? "Φάκελος προορισμού…" : "Destination folder…";
            BtnTransferCheck.Content = greek ? "Έλεγχος πριν από τη μεταφορά" : "Check before transfer";
            BtnAppsExport.Content = greek ? "Εξαγωγή εγκατεστημένων εφαρμογών (JSON)" : "Export installed apps (JSON)";
        }

        private static string Translation(string greek, string english) =>
            LanguageService.Current == "el" ? greek : english;

        private static string? ChooseFolder()
        {
            using var picker = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = false };
            return picker.ShowDialog() == System.Windows.Forms.DialogResult.OK ? picker.SelectedPath : null;
        }

        private void ChooseTransferFrom_Click(object sender, RoutedEventArgs e)
        {
            if (ChooseFolder() is string path) TxtTransferFrom.Text = path;
        }

        private void ChooseTransferTo_Click(object sender, RoutedEventArgs e)
        {
            if (ChooseFolder() is string path) TxtTransferTo.Text = path;
        }

        private async void CheckTransfer_Click(object sender, RoutedEventArgs e)
        {
            BtnTransferCheck.IsEnabled = false;
            try
            {
                var from = TxtTransferFrom.Text;
                var to = TxtTransferTo.Text;
                TxtTransferStatus.Text = Translation("Έλεγχος φακέλων…", "Checking folders…");
                var result = await TransferPreflight.InspectAsync(from, to);
                TxtTransferStatus.Text = (result.Ready
                    ? Translation("Έλεγχος ολοκληρώθηκε: ", "Preflight passed: ")
                    : Translation("Ο έλεγχος δεν ολοκληρώθηκε: ", "Preflight blocked: ")) +
                    $"{result.FileCount:N0} files, {result.ByteCount:N0} bytes. {result.Message}";
            }
            catch (System.Exception ex) { TxtTransferStatus.Text = ex.Message; }
            finally { BtnTransferCheck.IsEnabled = true; }
        }

        private async void ExportApps_Click(object sender, RoutedEventArgs e)
        {
            if (ThemedMessageBox.Show(
                    Translation("Η λίστα περιέχει ονόματα και εκδόσεις εφαρμογών. Συνέχεια;",
                                "The JSON contains app names and versions. Continue?"),
                    Translation("Εξαγωγή εφαρμογών", "Export applications"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var picker = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "gearwin-installed-apps.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (picker.ShowDialog() != true) return;

            BtnAppsExport.IsEnabled = false;
            try
            {
                TxtTransferStatus.Text = Translation("Απογραφή εφαρμογών…", "Reading installed applications…");
                var inventory = await GearWinCore.RunAsync(CoreWork.TransferScan, () => System.Threading.Tasks.Task.Run(GearWin.Migration.ApplicationDiscovery.InspectHost));
                var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
                };
                string json = System.Text.Json.JsonSerializer.Serialize(inventory, options);
                await GearWinCore.RunAsync(CoreWork.Backup, () => System.Threading.Tasks.Task.Run(() =>
                    AppSettingsService.WriteIfChanged(Path.GetFullPath(picker.FileName), json, null)));
                TxtTransferStatus.Text = Translation(
                    $"Εξήχθησαν {inventory.Applications.Count:N0} εφαρμογές: {picker.FileName}",
                    $"Exported {inventory.Applications.Count:N0} applications: {picker.FileName}");
            }
            catch (System.Exception ex)
            {
                TxtTransferStatus.Text = ex.Message;
            }
            finally { BtnAppsExport.IsEnabled = true; }
        }

        public RecoveryCards()
        {
            InitializeComponent();
            ListRestorePoints.ItemsSource = _restorePoints;
            _ = LoadRestorePointsAsync();
            _ = LoadBackupTargetDrivesAsync();
            TranslateTransferUi();
            Loaded += (_, _) => { LanguageService.Changed -= OnTransferLanguageChanged; LanguageService.Changed += OnTransferLanguageChanged; };
            Unloaded += (_, _) => LanguageService.Changed -= OnTransferLanguageChanged;
        }
    }
}
