using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GearWin.Migration;
using OptimizerWpf.Services;

namespace OptimizerWpf.Views
{
    public partial class RecoveryCards
    {
        private void TranslateMigrationPlanUi()
        {
            TxtMigrationPlanHelp.Text = Translation(
                "Σχέδιο εφαρμογών Windows / Linux: χρησιμοποιεί κατάλογο αντιστοιχίσεων, απογραφή πηγής και προφίλ προορισμού. Μόνο προεπισκόπηση, χωρίς εγκατάσταση ή έλεγχο διαθεσιμότητας πακέτων.",
                "Windows / Linux application plan: uses a mapping manifest, source inventory and destination profile. Preview only; no installation or package availability verification.");
            BtnExportTargetProfile.Content = Translation("Εξαγωγή προφίλ υπολογιστή (JSON)", "Export computer profile (JSON)");
            BtnCreateMigrationPlan.Content = Translation("Δημιουργία σχεδίου εφαρμογών (JSON)", "Create application plan (JSON)");
        }

        private static string? SelectMigrationInput(string title)
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = title, Filter = "JSON (*.json)|*.json", CheckFileExists = true, Multiselect = false
            };
            return picker.ShowDialog() == true ? picker.FileName : null;
        }

        private static string? SelectMigrationOutput(string name)
        {
            var picker = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json", FileName = name, DefaultExt = ".json",
                AddExtension = true, OverwritePrompt = true
            };
            return picker.ShowDialog() == true ? picker.FileName : null;
        }

        private static Task WriteMigrationDocumentAsync<T>(string path, T document) =>
            GearWinCore.RunAsync(CoreWork.Backup, () => Task.Run(() =>
                AppSettingsService.WriteIfChanged(Path.GetFullPath(path), MigrationDocuments.Serialize(document), null)));

        private async void ExportTargetProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnExportTargetProfile.IsEnabled) return;
            var path = SelectMigrationOutput("gearwin-target-profile.json");
            if (path == null) return;
            BtnExportTargetProfile.IsEnabled = BtnCreateMigrationPlan.IsEnabled = false;
            try
            {
                TxtMigrationPlanStatus.Text = Translation("Ανάγνωση προφίλ…", "Reading computer profile…");
                var profile = await GearWinCore.RunAsync(CoreWork.TransferScan, () => Task.Run(PackageDiscovery.InspectHost));
                await WriteMigrationDocumentAsync(path, new ProfileDocument(PackagePlanner.SchemaVersion, profile));
                TxtMigrationPlanStatus.Text = Translation("Εξαγωγή προφίλ: ", "Profile exported: ") + path;
            }
            catch (Exception ex) { TxtMigrationPlanStatus.Text = ex.Message; }
            finally { BtnExportTargetProfile.IsEnabled = BtnCreateMigrationPlan.IsEnabled = true; }
        }

        private async void CreateMigrationPlan_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnCreateMigrationPlan.IsEnabled) return;
            var manifest = SelectMigrationInput(Translation("Επιλέξτε κατάλογο αντιστοιχίσεων", "Select mapping manifest"));
            if (manifest == null) return;
            var inventory = SelectMigrationInput(Translation("Επιλέξτε απογραφή πηγής", "Select source inventory"));
            if (inventory == null) return;
            var target = SelectMigrationInput(Translation("Επιλέξτε προφίλ προορισμού", "Select destination profile"));
            if (target == null) return;
            var output = SelectMigrationOutput("gearwin-application-plan.json");
            if (output == null) return;

            BtnExportTargetProfile.IsEnabled = BtnCreateMigrationPlan.IsEnabled = false;
            try
            {
                var fullOutput = Path.GetFullPath(output);
                if (new[] { manifest, inventory, target }.Any(input =>
                    string.Equals(Path.GetFullPath(input), fullOutput, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException(Translation("Το σχέδιο δεν μπορεί να αντικαταστήσει αρχείο εισόδου.",
                        "The plan cannot replace an input document."));

                TxtMigrationPlanStatus.Text = Translation("Δημιουργία προεπισκόπησης…", "Creating preview…");
                var report = await GearWinCore.RunAsync(CoreWork.TransferScan, () => Task.Run(() =>
                    MigrationDocuments.CreatePlan(MigrationDocuments.ReadTarget(target),
                        MigrationDocuments.Read<PackageManifest>(manifest),
                        MigrationDocuments.Read<ApplicationInventory>(inventory))));
                await WriteMigrationDocumentAsync(fullOutput, report);
                var previews = report.Applications.Count(a => a.Status == PlanStatus.CommandPreview);
                var manual = report.Applications.Count(a => a.Status == PlanStatus.Manual);
                var unresolved = report.Applications.Count(a => a.Status == PlanStatus.Unresolved);
                var warnings = report.InventoryWarnings?.Count ?? 0;
                TxtMigrationPlanStatus.Text = Translation(
                    $"Προεπισκοπήσεις: {previews}, χειροκίνητα: {manual}, χωρίς αντιστοίχιση: {unresolved}, προειδοποιήσεις απογραφής: {warnings}. ",
                    $"Previews: {previews}, manual: {manual}, unresolved: {unresolved}, inventory warnings: {warnings}. ") + fullOutput;
            }
            catch (Exception ex) { TxtMigrationPlanStatus.Text = ex.Message; }
            finally { BtnExportTargetProfile.IsEnabled = BtnCreateMigrationPlan.IsEnabled = true; }
        }
    }
}
