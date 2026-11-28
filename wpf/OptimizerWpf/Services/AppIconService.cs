using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OptimizerWpf.Services
{
    // Ρητό αίτημα χρήστη: "στα κουμπιά βάλε τα logos των εφαρμογών" (καθαρισμός cache περιηγητών +
    // προτεινόμενες εφαρμογές winget). Εξάγει το ΠΡΑΓΜΑΤΙΚΟ εικονίδιο από το ίδιο το εγκατεστημένο exe
    // (SHGetFileInfo - ήδη διαθέσιμο μέσω P/Invoke, καμία ανάγκη για νέο πακέτο System.Drawing.Common)
    // αντί να φτιάχνει/κατεβάζει δικά μας αντίγραφα εμπορικών λογότυπων. ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ: μόνο
    // ήδη εγκατεστημένες εφαρμογές έχουν πραγματικό, τοπικό εικονίδιο διαθέσιμο - για τις προτεινόμενες
    // εφαρμογές winget που ΔΕΝ είναι ακόμα εγκατεστημένες δεν υπάρχει τοπική πηγή λογότυπου χωρίς λήψη
    // από το διαδίκτυο (εκτός εμβέλειας εδώ) - εμφανίζεται γενικό εικονίδιο πακέτου σε αυτή την περίπτωση.
    public static class AppIconService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;

        public static ImageSource? GetIconForFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var info = new SHFILEINFO();
                var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
        }

        // Exact product identity first; only known version/architecture suffixes are accepted.
        // In particular Chrome Remote Desktop, Opera GX and Firefox ESR are different products.
        internal static bool MatchesInstalledTitle(string displayName, string title)
        {
            var names = title switch
            {
                "VLC" => new[] { "VLC", "VLC media player" },
                "PowerToys" => new[] { "PowerToys", "Microsoft PowerToys" },
                "Zoom" => new[] { "Zoom", "Zoom Workplace" },
                "LibreOffice" => new[] { "LibreOffice" },
                _ => new[] { title }
            };
            foreach (var name in names)
            {
                if (displayName.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
                if (!displayName.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = displayName[(name.Length + 1)..];
                if (System.Text.RegularExpressions.Regex.IsMatch(suffix,
                    @"^(?:v?\d+(?:\.\d+)*(?:\s*\((?:x64|x86|64-bit|32-bit)[^)]*\))?|\((?:x64|x86|64-bit|32-bit)[^)]*\))$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        // Shared read-only display-icon evidence. Unlike AppUsageReview this
        // accepts non-executable icon resources and expanded environment variables.
        internal static string? ParseInstalledIconPath(string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon) || icon.Length > 4096) return null;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(
                    System.Text.RegularExpressions.Regex.Replace(icon.Trim(), @",\s*-?\d+$", "").Trim('"'));
                // Never probe a network share or device path from registry metadata.
                if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:\\") ||
                    !Path.IsPathFullyQualified(path) || path.Skip(2).Contains(':') ||
                    path.Any(char.IsControl) || path.Contains('"') || path.Contains('*') || path.Contains('?')) return null;
                return path;
            }
            catch { return null; }
        }

        private sealed record IconEvidence(string Title, string Path);

        internal static IReadOnlyList<(string Title, string Path)> ReadInstalledIconEntries(string? appTitle = null)
        {
            var entries = SystemInstallProbe.ReadRegistrations<IconEvidence>((hive, name, key) =>
            {
                var title = key.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(title) ||
                    appTitle != null && !MatchesInstalledTitle(title, appTitle)) return null;
                var path = ParseInstalledIconPath(key.GetValue("DisplayIcon") as string);
                if (path == null || !MaintenancePaths.IsOrdinaryLocalPath(path, allowRemovable: true) ||
                    !File.Exists(path)) return null;
                return new IconEvidence(title, path);
            });
            if (appTitle != null) entries = entries.Take(1);
            return entries.Select(row => (row.Title, row.Path)).ToArray();
        }

        public static string? FindInstalledIconPath(string appTitle) =>
            ReadInstalledIconEntries(appTitle).FirstOrDefault().Path;
    }
}
