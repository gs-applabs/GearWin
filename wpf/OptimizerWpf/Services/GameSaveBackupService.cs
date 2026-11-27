using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    // Πρόταση χρήστη: "αντίγραφο ασφαλείας αποθηκεύσεων παιχνιδιών". ΔΕΝ υπάρχει ενιαίος, αξιόπιστος
    // τρόπος να μαντέψουμε τη θέση αποθήκευσης ΚΑΘΕ παιχνιδιού (ποικίλει ανά εκδότη/μηχανή, πολλά
    // είναι αποκλειστικά cloud-save) - το να ΠΡΟΣΠΟΙΗΘΟΥΜΕ πληρότητα θα ήταν ψευδής διαβεβαίωση.
    // Αντί γι' αυτό, καλύπτει μόνο τις ΔΥΟ πραγματικά τεκμηριωμένες, γενικές συμβάσεις των Windows
    // που πολλά (όχι όλα) παιχνίδια χρησιμοποιούν: τον Known Folder "Saved Games" και
    // Documents\My Games. Το UI λέει ρητά ποια καλύπτει, όχι "όλα τα παιχνίδια".
    public static class GameSaveBackupService
    {
        private static readonly Guid SavedGamesFolderId = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        private static string? SavedGamesFolder()
        {
            try
            {
                if (SHGetKnownFolderPath(SavedGamesFolderId, 0, IntPtr.Zero, out var ptr) != 0) return null;
                var path = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
                return path;
            }
            catch { return null; }
        }

        public static IEnumerable<string> KnownSaveFolders()
        {
            if (SavedGamesFolder() is { } saved) yield return saved;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games");
        }

        public static long EstimateSize() => KnownSaveFolders().Where(Directory.Exists).Sum(DirSize);

        private static long DirSize(string dir)
        {
            long total = 0;
            foreach (var f in EnumerateFilesNoReparse(dir)) { try { total += new FileInfo(f).Length; } catch { } }
            return total;
        }

        // Ίδια λογική με την αναδρομική απαρίθμηση, αλλά ΔΕΝ διασχίζει φακέλους-junction/symlink (κύκλοι → ατέρμονο
        // zip / διαρροή αρχείων εκτός των δύο φακέλων αποθήκευσης). Αρχεία-placeholder (OneDrive) περιλαμβάνονται.
        private static IEnumerable<string> EnumerateFilesNoReparse(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] files = Array.Empty<string>(), dirs = Array.Empty<string>();
                try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch { }
                foreach (var f in files) yield return f;
                foreach (var d in dirs)
                {
                    try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(d);
                }
            }
        }

        public static Task<bool> BackupAsync(string destinationZipPath) =>
            GearWinCore.RunAsync(CoreWork.Backup, () => Task.Run(() => BackupFolders(KnownSaveFolders(), destinationZipPath)));

        // Commit a new archive only after complete enumeration, stable reads and read-back verification.
        // On any failure the old archive remains untouched.
        internal static bool BackupFolders(IEnumerable<string> folders, string destinationZipPath)
        {
            string? staged = null;
            try
            {
                var destination = Path.GetFullPath(destinationZipPath);
                var roots = new List<string>();
                foreach (var folder in folders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if ((File.GetAttributes(folder) & FileAttributes.Directory) == 0) return false;
                        if (!MaintenancePaths.IsOrdinaryLocalPath(folder)) return false;
                        roots.Add(folder);
                    }
                    catch (DirectoryNotFoundException) { }
                    catch (FileNotFoundException) { }
                }
                var parent = Path.GetDirectoryName(destination);
                if (parent == null || !MaintenancePaths.IsOrdinaryLocalPath(parent) ||
                    (File.Exists(destination) && !MaintenancePaths.IsOrdinaryLocalPath(destination))) return false;
                if (roots.Count == 0) return false;
                foreach (var root in roots)
                {
                    if (destination.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                staged = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var buffer = new byte[128 * 1024];
                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        foreach (var folder in roots)
                        {
                            var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
                            foreach (var file in EnumerateStrict(folder))
                            {
                                var name = prefix + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');
                                // FileShare.Read denies concurrent writers/deletion while the copy is made.
                                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                                using var output = entry.Open();
                                using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                                int read;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    output.Write(buffer, 0, read);
                                    hash.AppendData(buffer, 0, read);
                                }
                                expected.Add(name, Convert.ToHexString(hash.GetHashAndReset()));
                            }
                        }
                    }
                    stream.Flush(flushToDisk: true);
                }
                using (var verify = ZipFile.OpenRead(staged))
                {
                    if (verify.Entries.Count != expected.Count) return false;
                    foreach (var entry in verify.Entries)
                    {
                        using var data = entry.Open();
                        if (!expected.TryGetValue(entry.FullName, out var hash) ||
                            hash != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))) return false;
                    }
                }
                // Same-directory staging; do not remove the last good copy before commit.
                File.Move(staged, destination, overwrite: true);
                staged = null;
                return true;
            }
            catch { return false; }
            finally { if (staged != null) try { File.Delete(staged); } catch { } }
        }

        private static IEnumerable<string> EnumerateStrict(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var directory = stack.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Backup refuses redirected directories.");
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Backup refuses redirected files.");
                    yield return file;
                }
                foreach (var child in Directory.EnumerateDirectories(directory)) stack.Push(child);
            }
        }
    }
}
