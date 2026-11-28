using System.Collections.ObjectModel;
using System.IO;
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
        private int _viewGeneration;
        private System.Threading.CancellationTokenSource? _viewLifetime;
        private bool _driveRead, _driveReadPending, _restoreRead, _restoreReadPending, _restoreMutation;


        private async System.Threading.Tasks.Task LoadBackupTargetDrivesAsync()
        {
            if (!IsLoaded || _viewLifetime == null) return;
            if (_driveRead) { _driveReadPending = true; return; }
            _driveRead = true;
            int generation = _viewGeneration;
            var ct = _viewLifetime.Token;
            var previous = ListBackupTargetDrives.SelectedItem as string;
            ListBackupTargetDrives.IsEnabled = false;
            try
            {
                var systemDrive = Path.GetPathRoot(System.Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
                var names = await GearWinCore.RunAsync(CoreWork.HardwareScan, () => System.Threading.Tasks.Task.Run(() =>
                {
                    var found = new System.Collections.Generic.List<string>();
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var name = drive.Name.TrimEnd('\\');
                            if (drive.IsReady && (drive.DriveType is DriveType.Fixed or DriveType.Removable) &&
                                !string.Equals(name, systemDrive, System.StringComparison.OrdinalIgnoreCase))
                                found.Add(name);
                        }
                        catch (IOException) { }
                        catch (System.UnauthorizedAccessException) { }
                    }
                    return found;
                }, ct), ct: ct);
                if (ct.IsCancellationRequested || !IsLoaded || generation != _viewGeneration) return;
                ListBackupTargetDrives.Items.Clear();
                foreach (var name in names) ListBackupTargetDrives.Items.Add(name);
                // Never silently switch the user's target after a refresh.
                if (previous != null && names.Contains(previous)) ListBackupTargetDrives.SelectedItem = previous;
            }
            catch (System.Exception ex)
            {
                if (IsLoaded && generation == _viewGeneration)
                {
                    ListBackupTargetDrives.Items.Clear();
                    TxtBackupStatus.Text = LanguageService.T("Backup_Failed") + " " + ex.Message;
                }
            }
            finally
            {
                _driveRead = false;
                ListBackupTargetDrives.IsEnabled = true;
                if (_driveReadPending)
                {
                    _driveReadPending = false;
                    if (IsLoaded) _ = LoadBackupTargetDrivesAsync();
                }
            }
        }

        private async void BtnRefreshBackupTargets_Click(object sender, RoutedEventArgs e)
        {
            if (!BackupControls.IsEnabled) return;
            await LoadBackupTargetDrivesAsync();
        }

        private async void BtnStartBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_driveRead || ListBackupTargetDrives.SelectedItem is not string targetDrive) return;
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
            if (_driveRead || ListBackupTargetDrives.SelectedItem is not string targetDrive) return;
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
        private async void BtnExportOfflineClone_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || !button.IsEnabled) return;
            string warning = LanguageService.Current == "el"
                ? "Απαιτεί OFFLINE Linux Live. Η κλωνοποίηση καταστρέφει ΟΛΑ τα δεδομένα του δίσκου προορισμού και γράφει ΟΛΟΥΣ τους τομείς, ακόμη και τον κενό χώρο. Θέλετε να εξαγάγετε το πακέτο;"
                : "EXPERT: Requires offline Linux Live. Cloning permanently ERASES the entire target disk and writes ALL sectors including unused space. Export recovery kit?";
            if (ThemedMessageBox.Show(warning, "GearWin — Offline clone",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var folder = Path.Combine(System.AppContext.BaseDirectory, "Recovery");
            var picker = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "GearWin-Offline-Clone-Kit.zip",
                Filter = "ZIP (*.zip)|*.zip",
                DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true
            };
            if (picker.ShowDialog() != true) return;
            try
            {
                button.IsEnabled = false;
                using var busy = StatusService.BeginScope(Translation("Εξαγωγή πακέτου ανάκτησης…", "Exporting recovery kit…"));
                string destination = picker.FileName;
                await GearWinCore.RunAsync(CoreWork.Backup, () => System.Threading.Tasks.Task.Run(() =>
                    RecoveryKitExporter.Export(folder, destination)));
                TxtBackupStatus.Text = "Offline recovery kit exported: " + picker.FileName +
                    ". Boot Linux Live separately before attempting the clone.";
            }
            catch (System.Exception ex)
            {
                ThemedMessageBox.Show(ex.Message, "GearWin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { button.IsEnabled = true; }
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
            if (!IsLoaded || _viewLifetime == null) return;
            if (_restoreMutation) { _restoreReadPending = true; return; }
            if (_restoreRead) { _restoreReadPending = true; return; }
            _restoreRead = true;
            int generation = _viewGeneration;
            var ct = _viewLifetime.Token;
            TxtRestorePointsStatus.Text = LanguageService.T("Sys_LoadingEllipsis");
            _restorePoints.Clear();
            try
            {
                var points = await GearWinCore.RunAsync(CoreWork.Diagnostics, () => SystemService.ListRestorePointsAsync(ct), ct: ct);
                if (ct.IsCancellationRequested || !IsLoaded || generation != _viewGeneration) return;
                _restorePoints.Clear();
                foreach (var point in points) _restorePoints.Add(point);
                TxtRestorePointsStatus.Text = $"{_restorePoints.Count}{LanguageService.T("Sys_RestorePointsSuffix")}";
            }
            catch (System.Exception ex)
            {
                if (IsLoaded && generation == _viewGeneration)
                    TxtRestorePointsStatus.Text = LanguageService.T("Opt_ScanFailed") + ex.Message;
            }
            finally
            {
                _restoreRead = false;
                if (_restoreReadPending)
                {
                    _restoreReadPending = false;
                    if (IsLoaded) _ = LoadRestorePointsAsync();
                }
            }
        }

        private async void BtnRefreshRestorePoints_Click(object sender, RoutedEventArgs e) => await LoadRestorePointsAsync();

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            if (_restoreMutation || _restoreRead) return;
            _restoreMutation = true;
            bool ok = false;
            using var busy = StatusService.BeginScope(LanguageService.T("Sys_CreatingRestorePoint"));
            try
            {
                ok = await GearWinCore.RunAsync(CoreWork.Repair, RestoreGuardService.CreateVerifiedPointAsync);
            }
            catch (System.Exception ex) { System.Diagnostics.Trace.TraceWarning("Restore point creation: " + ex.Message); }
            finally
            {
                _restoreMutation = false;
                busy.End(LanguageService.T(ok ? "Sys_RestorePointCreated" : "Sys_RestorePointFailed"));
                TxtRestorePointsStatus.Text = LanguageService.T(ok ? "Sys_RestorePointCreated" : "Sys_RestorePointFailed");
            }
            if (ok || _restoreReadPending)
            {
                _restoreReadPending = false;
                await LoadRestorePointsAsync();
            }
        }

        private void BtnOpenProtectionSettings_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true });

        // Ρητά ΔΕΝ υλοποιείται delete-by-sequence - βλ. σχόλιο στο SystemService.RestoreToPointAsync.
        private async void BtnRestoreToPoint_Click(object sender, RoutedEventArgs e)
        {
            if (_restoreMutation || _restoreRead || sender is not Button { Tag: RestorePointInfo point }) return;
            if (ThemedMessageBox.Show($"{LanguageService.T("Sys_RestoreConfirmPrefix")}{point.Description}{LanguageService.T("Sys_RestoreConfirmSuffix")}",
                    LanguageService.T("Sys_RestoreWarningTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _restoreMutation = true;
            bool ok = false;
            using var busy = StatusService.BeginScope(LanguageService.T("Sys_RestoringSystem"));
            try
            {
                ok = await GearWinCore.RunAsync(CoreWork.Repair, async () =>
                {
                    var current = await SystemService.ListRestorePointsAsync();
                    if (!current.Contains(point)) return false;
                    return await SystemService.RestoreToPointAsync(point.SequenceNumber);
                });
            }
            catch (System.Exception ex) { System.Diagnostics.Trace.TraceWarning("Restore point operation: " + ex.Message); }
            finally
            {
                _restoreMutation = false;
                busy.End(LanguageService.T(ok ? "Ready" : "Sys_RestoreFailed"));
            }
            if (!ok)
            {
                TxtRestorePointsStatus.Text = LanguageService.T("Sys_RestoreFailed");
                if (IsLoaded)
                    ThemedMessageBox.Show(LanguageService.T("Sys_RestoreFailed"), LanguageService.T("Sys_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            if (_restoreReadPending)
            {
                _restoreReadPending = false;
                await LoadRestorePointsAsync();
            }
        }

        private System.Threading.CancellationTokenSource? _transferCheck;

        private void OnTransferLanguageChanged()
        {
            if (Dispatcher.CheckAccess()) TranslateTransferUi();
            else if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.BeginInvoke(new System.Action(() => { if (IsLoaded) TranslateTransferUi(); }));
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
            BtnTransferCheck.Content = _transferCheck != null ? LanguageService.T("Cancel") :
                greek ? "Έλεγχος πριν από τη μεταφορά" : "Check before transfer";
            BtnAppsExport.Content = greek ? "Εξαγωγή εγκατεστημένων εφαρμογών (JSON)" : "Export installed apps (JSON)";
            BtnAppsExportPortable.Content = Translation("Εξαγωγή με επιλεγμένα EXE", "Export with selected EXEs");
            TranslateMigrationPlanUi();
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
            if (ChooseFolder() is string path) { TxtTransferFrom.Text = path; TxtTransferStatus.Text = ""; }
        }

        private void ChooseTransferTo_Click(object sender, RoutedEventArgs e)
        {
            if (ChooseFolder() is string path) { TxtTransferTo.Text = path; TxtTransferStatus.Text = ""; }
        }

        private async void CheckTransfer_Click(object sender, RoutedEventArgs e)
        {
            if (_transferCheck != null) { _transferCheck.Cancel(); return; }
            using var cancel = new System.Threading.CancellationTokenSource();
            _transferCheck = cancel;
            BtnChooseFrom.IsEnabled = BtnChooseTo.IsEnabled = false;
            TranslateTransferUi();
            try
            {
                var from = TxtTransferFrom.Text;
                var to = TxtTransferTo.Text;
                TxtTransferStatus.Text = Translation("Έλεγχος φακέλων…", "Checking folders…");
                var result = await TransferPreflight.InspectAsync(from, to, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                TxtTransferStatus.Text = (result.Ready
                    ? Translation("Έλεγχος ολοκληρώθηκε: ", "Preflight passed: ")
                    : Translation("Ο έλεγχος δεν ολοκληρώθηκε: ", "Preflight blocked: ")) +
                    Translation($"{result.FileCount:N0} αρχεία, {result.ByteCount:N0} bytes, {result.SkippedLinks:N0} παραλειφθέντες σύνδεσμοι. ",
                        $"{result.FileCount:N0} files, {result.ByteCount:N0} bytes, {result.SkippedLinks:N0} skipped links. ") + result.Message;
            }
            catch (System.OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                TxtTransferStatus.Text = Translation("Ο έλεγχος ακυρώθηκε.", "Preflight canceled.");
            }
            catch (System.Exception ex) { TxtTransferStatus.Text = ex.Message; }
            finally
            {
                _transferCheck = null;
                BtnChooseFrom.IsEnabled = BtnChooseTo.IsEnabled = true;
                TranslateTransferUi();
            }
        }

        private async void ExportApps_Click(object sender, RoutedEventArgs e) => await ExportAppsAsync(false);

        private async void ExportAppsPortable_Click(object sender, RoutedEventArgs e) => await ExportAppsAsync(true);

        private async System.Threading.Tasks.Task ExportAppsAsync(bool includeSelectedExecutables)
        {
            if (!BtnAppsExport.IsEnabled) return;
            if (ThemedMessageBox.Show(
                    Translation("Η λίστα περιέχει ονόματα και εκδόσεις εφαρμογών. Συνέχεια;",
                                "The JSON contains app names and versions. Continue?"),
                    Translation("Εξαγωγή εφαρμογών", "Export applications"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            string[] selectedNames = [];
            if (includeSelectedExecutables)
            {
                var files = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Translation("Επιλέξτε έως 100 EXE (μόνο ονόματα)", "Select up to 100 EXEs (names only)"),
                    Filter = "Executable (*.exe)|*.exe", CheckFileExists = true, Multiselect = true
                };
                if (files.ShowDialog() != true) return;
                if (files.FileNames.Length > 100)
                {
                    TxtAppsExportStatus.Text = Translation("Μέγιστο 100 αρχεία.", "Select no more than 100 files.");
                    return;
                }
                selectedNames = files.FileNames.Select(p => Path.GetFileName(p) ?? "").ToArray();
            }

            var picker = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = "gearwin-installed-apps.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (picker.ShowDialog() != true) return;

            BtnAppsExport.IsEnabled = BtnAppsExportPortable.IsEnabled = false;
            try
            {
                TxtAppsExportStatus.Text = Translation("Απογραφή εφαρμογών…", "Reading installed applications…");
                var inventory = await GearWinCore.RunAsync(CoreWork.TransferScan, () => System.Threading.Tasks.Task.Run(GearWin.Migration.ApplicationDiscovery.InspectHost));
                // The shared read-only packaged-app scanner acquires its own core lease.
                // Its broader migration result is never used for uninstall eligibility.
                try
                {
                    var packaged = await BloatwareService.ScanPackagedAppsForInventoryAsync();
                    inventory = GearWin.Migration.ApplicationDiscovery.AddPackagedApps(inventory,
                        packaged.Apps.Select(a => (a.Name, a.PackageFullName, a.IsStore)), packaged.Incomplete);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("Packaged app inventory: " + ex.Message);
                    inventory = inventory with { Warnings = inventory.Warnings.Concat(new[] { "Packaged app inventory failed or was unavailable." }).ToArray() };
                }
                if (includeSelectedExecutables)
                    inventory = GearWin.Migration.ApplicationDiscovery.AddSelectedExecutables(inventory, selectedNames);
                string json = GearWin.Migration.MigrationDocuments.SerializeFile(inventory);
                await GearWinCore.RunAsync(CoreWork.Backup, () => System.Threading.Tasks.Task.Run(() =>
                    AppSettingsService.WriteIfChanged(Path.GetFullPath(picker.FileName), json, null)));
                TxtAppsExportStatus.Text = Translation(
                    $"Εξήχθησαν {inventory.Applications.Count:N0} εγγραφές, προειδοποιήσεις: {inventory.Warnings.Count:N0}. {picker.FileName}",
                    $"Exported {inventory.Applications.Count:N0} entries, warnings: {inventory.Warnings.Count:N0}. {picker.FileName}");
            }
            catch (System.Exception ex)
            {
                TxtAppsExportStatus.Text = ex.Message;
            }
            finally { BtnAppsExport.IsEnabled = BtnAppsExportPortable.IsEnabled = true; }
        }

        public RecoveryCards()
        {
            InitializeComponent();
            ListRestorePoints.ItemsSource = _restorePoints;
            TranslateTransferUi();
            Loaded += (_, _) =>
            {
                LanguageService.Changed -= OnTransferLanguageChanged;
                LanguageService.Changed += OnTransferLanguageChanged;
                TranslateTransferUi();
                ++_viewGeneration;
                _viewLifetime?.Cancel();
                _viewLifetime?.Dispose();
                _viewLifetime = new System.Threading.CancellationTokenSource();
                _ = LoadRestorePointsAsync();
                _ = LoadBackupTargetDrivesAsync();
            };
            Unloaded += (_, _) =>
            {
                LanguageService.Changed -= OnTransferLanguageChanged;
                ++_viewGeneration;
                _viewLifetime?.Cancel();
                _viewLifetime?.Dispose();
                _viewLifetime = null;
                _driveReadPending = _restoreReadPending = false;
                _transferCheck?.Cancel();
            };
        }
    }
}
