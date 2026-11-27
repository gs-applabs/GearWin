using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

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

        public static string? FindInstalledIconPath(string appTitle)
        {
            foreach (var (hive, subPath) in new[]
                     {
                         (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                         (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                         (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                     })
            {
                try
                {
                    using var key = hive.OpenSubKey(subPath);
                    if (key == null) continue;
                    foreach (var name in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var sub = key.OpenSubKey(name);
                            var displayName = sub?.GetValue("DisplayName") as string;
                            if (displayName == null || !MatchesInstalledTitle(displayName, appTitle)) continue;
                            var icon = sub?.GetValue("DisplayIcon") as string;
                            if (string.IsNullOrWhiteSpace(icon)) continue;
                            var path = System.Text.RegularExpressions.Regex.Replace(icon.Trim(), @",\s*-?\d+$", "").Trim('"');
                            path = Environment.ExpandEnvironmentVariables(path);
                            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path;
                        }
                        catch { /* inaccessible entry is not evidence of an installed app */ }
                    }
                }
                catch { /* inaccessible hive: continue with the other views */ }
            }
            return null;
        }
    }
}
