using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OptimizerWpf.Services
{
    // Μετακινήθηκε εδώ από TweaksView.xaml.cs (ήταν private nested class) ώστε να μπορεί να
    // ξαναχρησιμοποιηθεί ΚΑΙ από το ήδη υπάρχον αρχείο-βασισμένο Export/Import ΚΑΙ από το νέο
    // NamedProfileService παρακάτω - ίδιο ακριβώς schema, καμία αλλαγή στη μορφή του JSON (πλήρης
    // συμβατότητα με ήδη εξαγμένα αρχεία προφίλ).
    public class FullProfileData
    {
        public Dictionary<string, bool> Tweaks { get; set; } = new();
        public string? ThemeName { get; set; }
        public bool? IsDarkMode { get; set; }
        public string? Language { get; set; }
        public bool? SidebarEnabled { get; set; }
        public string? SidebarPosition { get; set; }
        public string? MenuMode { get; set; }
        public List<string>? PinnedTweakKeys { get; set; }
    }

    // ΝΕΟ - roadmap "Ονομασμένα προφίλ πολλαπλών χρηστών/μηχανημάτων" - επεκτείνει το ήδη υπάρχον
    // αρχείο-βασισμένο Export/Import Profile (SaveFileDialog/OpenFileDialog, ένα αρχείο τη φορά) με
    // μια δεύτερη, γρηγορότερη επιλογή: ονομασμένα προφίλ αποθηκευμένα σε ΈΝΑ σταθερό φάκελο
    // (%LocalAppData%\OptimizerWpf\Profiles\), ώστε ο χρήστης να μπορεί να αποθηκεύσει π.χ. "Γραφείο"
    // vs "Gaming" και να εναλλάσσεται μεταξύ τους από μια απλή λίστα, χωρίς να χρειάζεται να θυμάται
    // πού αποθήκευσε κάθε αρχείο .json. ΙΔΙΟ FullProfileData/JSON schema με το ήδη υπάρχον
    // αρχείο-βασισμένο export - τα δύο μονοπάτια είναι πλήρως συμβατά μεταξύ τους.
    public static class NamedProfileService
    {
        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "Profiles");

        public static IReadOnlyList<string> ListProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesDir) || !MaintenancePaths.IsOrdinaryLocalPath(ProfilesDir)) return Array.Empty<string>();
                return Directory.EnumerateFiles(ProfilesDir, "*.json")
                    .Take(ProfileImportPolicy.MaxKeys)
                    .Where(MaintenancePaths.IsOrdinaryLocalPath)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(ProfileImportPolicy.ValidName)
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return Array.Empty<string>(); }
        }

        public static bool Save(string name, FullProfileData profile)
        {
            try
            {
                if (!ProfileImportPolicy.Valid(profile)) return false;
                string path = PathFor(name);
                ProfileImportPolicy.ValidateNamedPath(path);
                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                if (System.Text.Encoding.UTF8.GetByteCount(json) > ProfileImportPolicy.MaxBytes) return false;
                AppSettingsService.WriteIfChanged(path, json, null);
                return true;
            }
            catch { return false; }
        }

        public static FullProfileData? Load(string name)
        {
            try
            {
                var path = PathFor(name);
                ProfileImportPolicy.ValidateNamedPath(path);
                return File.Exists(path) ? ProfileImportPolicy.Read(path) : null;
            }
            catch { return null; }
        }

        public static bool Delete(string name)
        {
            try
            {
                var path = PathFor(name);
                ProfileImportPolicy.ValidateNamedPath(path);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch { return false; }
        }

        private static string PathFor(string name)
        {
            if (!ProfileImportPolicy.ValidName(name)) throw new ArgumentException("Invalid profile name.");
            return Path.Combine(ProfilesDir, name + ".json");
        }
    }
}
