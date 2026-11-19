using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests
{
    // 6.9.10/6.9.11: ιστορικό Βοήθειας (φθίνουσα σειρά, χωρίς διπλές καρτέλες), μετονομασία λογαριασμού GitHub, έλεγχος GUID καταλόγου.
    public class Release6911Tests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [Fact]
        public void HelpVersionHistoryIsNewestFirstWithoutDuplicates()
        {
            var xaml = File.ReadAllText(Path.Combine(Root(), "wpf", "OptimizerWpf", "Views", "HelpWindow.xaml"));
            var tab = xaml.Substring(xaml.IndexOf("{tr:Tr VerHist_Title}", StringComparison.Ordinal));
            var keys = Regex.Matches(tab, @"\{tr:Tr VerHist_v(\d+)Title\}").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Version V(string k) => new(int.Parse(k[..1]), int.Parse(k[1..2]), int.Parse(k[2..]));
            var versions = keys.Select(V).ToList();
            Assert.Equal(versions.OrderByDescending(v => v).ToList(), versions);
            Assert.Equal("6912", keys[0]);
            // η παλιά μπλε σύνοψη "6.9.0 (6.7 - 6.9)" δεν υπάρχει πια πάνω από τις εκδόσεις
            Assert.DoesNotContain("VerHist_690Title", xaml);
        }

        [Fact]
        public void LatestHistoryEntryMatchesTheProjectVersion()
        {
            var csproj = File.ReadAllText(Path.Combine(Root(), "wpf", "OptimizerWpf", "OptimizerWpf.csproj"));
            var ver = Regex.Match(csproj, @"<Version>([\d.]+)</Version>").Groups[1].Value;
            Assert.Equal("6.9.12", ver);
            Assert.Contains("## v" + ver + " ", File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md")));
        }

        [Fact]
        public void NoCurrentFilePointsAtTheOldGitHubAccountExceptLegacyCompatibility()
        {
            const string old = "giorstergiopoulos-design";
            var allowed = new[] { "CHANGELOG.md", "SuiteCatalogService.cs", "Release6911Tests.cs", "PROJECT_STATE.md" };
            var offenders = new List<string>();
            foreach (var f in Directory.EnumerateFiles(Root(), "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(Root().Length);
                if (rel.Contains(@"\.git\") || rel.Contains(@"\bin\") || rel.Contains(@"\obj\") || rel.Contains("/.git/")) continue;
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dll" or ".exe" or ".png" or ".ico" or ".pdb" or ".zip" or ".json" && !f.EndsWith("suite-apps.json")) continue;
                if (allowed.Contains(Path.GetFileName(f))) continue;
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                if (text.Contains(old, StringComparison.OrdinalIgnoreCase)) offenders.Add(rel);
            }
            Assert.True(offenders.Count == 0, "old account name still referenced in: " + string.Join(", ", offenders));
        }

        [Fact]
        public void TrustedUrlChecksAcceptBothAccountNames()
        {
            Assert.Equal("gs-applabs", SuiteCatalogService.Owner);
            foreach (var o in new[] { "gs-applabs", "giorstergiopoulos-design" })
            {
                Assert.True(SelfUpdateService.IsTrustedDownloadUrl($"https://github.com/{o}/GearWin/releases/download/v6.9.11/GearWin-Setup-6.9.11.exe"));
                Assert.True(GitHubReleaseClient.IsRepoDownloadUrl($"https://github.com/{o}/Opsis/releases/download/v1.0.0/x.exe", "gs-applabs", "Opsis"));
                Assert.True(SuiteCatalogService.IsKnownOwner(o));
            }
            Assert.False(SelfUpdateService.IsTrustedDownloadUrl("https://github.com/evil/GearWin/releases/download/v6.9.11/GearWin-Setup-6.9.11.exe"));
            Assert.False(SuiteCatalogService.IsKnownOwner("evil"));
            Assert.False(GitHubReleaseClient.IsRepoDownloadUrl("https://github.com/evil/Opsis/releases/download/v1/x.exe", "gs-applabs", "Opsis"));
        }
    }
}
