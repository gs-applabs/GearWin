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
            if (ThemedMessageBox.Show(
                    $"{LanguageService.T("Backup_StartButton")} → {targetDrive}",
                    LanguageService.T("Backup_StartButton"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            BackupControls.IsEnabled = false;
            ListBackupVersions.ItemsSource = null;
            TxtBackupStatus.Text = LanguageService.T("Backup_Running");
            StatusService.SetBusy(LanguageService.T("Backup_Running"));
            try
            {
                var (success, output) = await BackupService.StartSystemImageBackupAsync(targetDrive);
                TxtBackupStatus.Text = LanguageService.T(success ? "Backup_Success" : "Backup_Failed");
                if (!success && !string.IsNullOrWhiteSpace(output))
                    ListBackupVersions.ItemsSource = new[] { output };
            }
            finally
            {
                BackupControls.IsEnabled = true;
                StatusService.SetIdle(LanguageService.T("Ready"));
            }
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

        public RecoveryCards()
        {
            InitializeComponent();
            ListRestorePoints.ItemsSource = _restorePoints;
            _ = LoadRestorePointsAsync();
            _ = LoadBackupTargetDrivesAsync();
        }
    }
}
