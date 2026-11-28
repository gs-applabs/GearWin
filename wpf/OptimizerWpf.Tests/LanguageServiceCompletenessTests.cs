using System.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

// ΝΕΟ - roadmap "Περισσότερα unit tests" - κάθε νέο UI string πρέπει να προστίθεται και στις 14
// γλώσσες ταυτόχρονα (βλ. CONTRIBUTING.md). Αυτό το test πιάνει αυτόματα την περίπτωση που ένα κλειδί
// ξεχάστηκε σε 1+ από τις μη-ελληνικές γλώσσες, αντί να το ανακαλύψει κάποιος χρήστης αργότερα ζωντανά.
// Missing "el" ΔΕΝ ελέγχεται εδώ ξεχωριστά - το T() κάνει fallback σε "el" όταν λείπει η τρέχουσα
// γλώσσα (βλ. T() στο τέλος του LanguageService.cs), οπότε ένα κλειδί που υπάρχει ΜΟΝΟ στο "el"
// (π.χ. License_MitText - αγγλικό νομικό κείμενο, σκόπιμα κοινό/αμετάφραστο σε όλες τις γλώσσες, ίδιο
// πνεύμα με τον τίτλο της εφαρμογής) είναι έγκυρο σχέδιο, όχι σφάλμα.
// Όλες οι 14 γλώσσες είναι πλέον πλήρεις (el/en/de/fr/es/it/ru/zh/ja/pt/ko/tr/ar/hi).
public class LanguageServiceCompletenessTests
{
    [Fact]
    public void UsageReviewAndUninstallSafetyMessagesExistInEveryLanguage()
    {
        string[] keys = { "Bloatware_UsageReview", "Bloatware_UsageTitle", "Bloatware_UsageConsent", "Bloatware_UsageUnavailable", "Bloatware_UsageEmpty", "Bloatware_UsageResults", "Bloatware_UninstallSettings", "Bloatware_UninstallRestoreFailed", "Bloatware_UninstallSettingsOpened", "Bloatware_UninstallRestart", "Bloatware_UninstallUnverified", "Bloatware_UninstallFailed", "Bloatware_ResidualAdvisory", "Bloatware_UninstallVerified", "Bloatware_DeepUninstallBtn", "Help_Bloat_DeepUninstall", "Bloatware_UninstallConfirmPrefix", "Bloatware_UninstallConfirmSuffix" };
        foreach (var (language, dictionary) in LanguageService.AllTranslations)
            foreach (var key in keys)
                Assert.True(dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), $"{language}: {key}");
    }

    [Fact]
    public void TransferStoreAndClipboardKeysExistInAllFourteenLanguages()
    {
        string[] keys = { "Transfer_Title", "Transfer_Help", "Transfer_SourceFolder", "Transfer_DestinationFolder", "Transfer_Check", "Transfer_ExportApps", "Transfer_ExportSelected", "Migration_PlanHelp", "Migration_ExportProfile", "Migration_CreatePlan", "Migration_CompareDestination", "Store_UpdateFailed", "Store_UpdateUnconfirmed", "Clip_CaptureBackground", "Clip_CaptureFailed", "Recovery_ExportingKit" };
        Assert.Equal(14, LanguageService.AllTranslations.Count);
        foreach (var (language, dictionary) in LanguageService.AllTranslations)
            foreach (var key in keys)
                Assert.True(dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), $"{language}: {key}");
    }

    [Fact]
    public void EveryKey_IsEitherInAllLanguages_OrOnlyInGreek()
    {
        var all = LanguageService.AllTranslations;
        Assert.Equal(14, all.Count);

        var nonGreek = new[] { "en", "de", "fr", "es", "it", "ru", "zh", "ja", "pt", "ko", "tr", "ar", "hi" };
        foreach (var lang in nonGreek)
            Assert.True(all.ContainsKey(lang), $"Missing language block: {lang}");

        var allKeys = new HashSet<string>();
        foreach (var lang in all.Values)
            foreach (var key in lang.Keys)
                allKeys.Add(key);

        // "Μερική" μετάφραση = υπάρχει σε ΤΟΥΛΑΧΙΣΤΟΝ μία από τις μη-ελληνικές γλώσσες αλλά ΟΧΙ σε
        // όλες - αυτό είναι πάντα λάθος (ξεχασμένη γλώσσα), σε αντίθεση με "καμία από αυτές" (σκόπιμα
        // el-only).
        var partial = new List<string>();
        foreach (var key in allKeys)
        {
            var presentIn = nonGreek.Where(lang => all[lang].ContainsKey(key)).ToList();
            if (presentIn.Count > 0 && presentIn.Count < nonGreek.Length)
            {
                var missingFrom = nonGreek.Except(presentIn);
                partial.Add($"{key} missing in: {string.Join(", ", missingFrom)}");
            }
        }

        Assert.True(partial.Count == 0, $"{partial.Count} partially-translated key(s):\n{string.Join("\n", partial.Take(50))}");
    }
}
