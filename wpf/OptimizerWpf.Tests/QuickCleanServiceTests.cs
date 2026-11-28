using System;
using System.IO;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

// ΝΕΟ - roadmap "Περισσότερα unit tests" (v4.3.5). Καλύπτει την καθαρή λογική του QuickCleanService
// που μέχρι τώρα δεν είχε καμία αυτοματοποιημένη επαλήθευση: το μέγεθος σε ανθρώπινα αναγνώσιμη
// μορφή (FormatSize) και τον αναδρομικό, ανά-φάκελο προστατευμένο περίπατο (MaintenancePaths.MeasureFiles) που διόρθωσε
// το πραγματικό bug με το lazy Directory.EnumerateFiles (βλ. HANDOFF.md/roadmap - "Γρήγορος
// Καθαρισμός κολλούσε σε σάρωση").
public class QuickCleanServiceTests
{
    [Theory]
    [InlineData(@"C:\Cache\Nested\a.txt", 3, true)]
    [InlineData(@"C:\Cache2\a.txt", 3, false)]
    [InlineData(@"C:\Cache\a.txt", 1, false)]
    [InlineData(@"C:\Cache\a.txt", 2, true)]
    public void EarlierRuleOwnsOnlyEligibleDescendants(string file, int days, bool expected)
    {
        var now = new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc);
        var rule = new QuickCleanService.CleanupRule("First", @"C:\Cache", TimeSpan.FromDays(2));
        Assert.Equal(expected, QuickCleanService.OwnsFile(rule, file, now.AddDays(-days), now));
    }

    [Fact]
    public void CoverageRespectsAgePatternAndRecursion()
    {
        var root = new QuickCleanService.CleanupRule("A", @"C:\Cache", TimeSpan.FromDays(2));
        var nested = new QuickCleanService.CleanupRule("B", @"C:\Cache\Nested", TimeSpan.FromDays(7));
        Assert.True(QuickCleanService.CoversRule(root, nested));
        Assert.False(QuickCleanService.CoversRule(nested, root));
        Assert.False(QuickCleanService.CoversRule(root with { Recursive = false }, nested));
        Assert.False(QuickCleanService.CoversRule(root with { Pattern = "thumbcache_*.db" }, nested));
        Assert.False(QuickCleanService.CoversRule(root with { MinAge = TimeSpan.FromDays(10) }, nested));
    }

    [Fact]
    public void SharedExclusionPreservesEarlierCategoryFilesInMeasureAndClean()
    {
        var root = Directory.CreateTempSubdirectory("gw_overlap_").FullName;
        try
        {
            var keep = Path.Combine(root, "first.txt");
            var remove = Path.Combine(root, "second.txt");
            File.WriteAllBytes(keep, new byte[11]);
            File.WriteAllBytes(remove, new byte[22]);
            File.SetLastWriteTimeUtc(keep, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(remove, DateTime.UtcNow.AddDays(-3));
            bool Exclude(string path) => string.Equals(path, keep, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(22L, MaintenancePaths.MeasureFiles(root, TimeSpan.FromDays(2), exclude: Exclude));
            Assert.Equal(22L, MaintenancePaths.CleanFiles(root, TimeSpan.FromDays(2), exclude: Exclude));
            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(remove));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void FormatSize_MatchesExpectedUnit(long bytes, string expected)
    {
        Assert.Equal(expected, QuickCleanService.FormatSize(bytes));
    }

    // Ξεχωριστό test για δεκαδικό μέγεθος - το δεκαδικό διαχωριστικό εξαρτάται από το τρέχον
    // culture (π.χ. "," σε el-GR, "." σε en-US), οπότε το αναμενόμενο κείμενο χτίζεται δυναμικά
    // αντί να είναι hardcoded, ώστε το test να περνάει ανεξάρτητα από το locale του μηχανήματος.
    [Fact]
    public void FormatSize_UsesCurrentCultureDecimalSeparator()
    {
        var sep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal($"1{sep}5 KB", QuickCleanService.FormatSize(1536L));
    }

    [Fact]
    public void MeasureFiles_SumsAllFilesRecursively()
    {
        var root = Directory.CreateTempSubdirectory("qc_test_").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.txt"), new byte[100]);
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllBytes(Path.Combine(sub.FullName, "b.txt"), new byte[250]);
            var subsub = Directory.CreateDirectory(Path.Combine(sub.FullName, "deeper"));
            File.WriteAllBytes(Path.Combine(subsub.FullName, "c.txt"), new byte[10]);

            Assert.Equal(360L, MaintenancePaths.MeasureFiles(root, TimeSpan.Zero));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MeasureFiles_ReturnsZero_WhenDirectoryDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), "qc_test_does_not_exist_" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0L, MaintenancePaths.MeasureFiles(missing, TimeSpan.Zero));
    }

    [Fact]
    public void MeasureFiles_IgnoresEmptySubfolders()
    {
        var root = Directory.CreateTempSubdirectory("qc_test_").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "empty1"));
            Directory.CreateDirectory(Path.Combine(root, "empty2"));
            Assert.Equal(0L, MaintenancePaths.MeasureFiles(root, TimeSpan.Zero));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
