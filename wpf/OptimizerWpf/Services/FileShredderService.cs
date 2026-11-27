using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    // ΝΕΟ - roadmap "Ασφαλής καταστροφέας αρχείων (file shredder)" - μόνιμη διαγραφή με επικάλυψη
    // αντί για απλό File.Delete, στάνταρ χαρακτηριστικό στο Glary Utilities/CCleaner Pro.
    // ΣΗΜΕΙΩΣΗ ΕΙΛΙΚΡΙΝΕΙΑΣ: σε δίσκους SSD/NVMe η επικάλυψη ΔΕΝ είναι εγγυημένη λόγω wear-leveling
    // (ο controller μπορεί να γράψει τα νέα δεδομένα σε διαφορετικό φυσικό block, αφήνοντας τα παλιά
    // δεδομένα ανέπαφα μέχρι το TRIM/garbage collection) - γνωστός, τεκμηριωμένος περιορισμός κάθε
    // λογισμικού shredder σε SSD, ΟΧΙ κάτι που μπορούμε να αποφύγουμε. Το UI το αναφέρει ρητά.
    public static class FileShredderService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity info);

        internal static bool IsAllowedTarget(string path)
        {
            try
            {
                if (!MaintenancePaths.IsOrdinaryLocalPath(path) || !File.Exists(path)) return false;
                var full = Path.GetFullPath(path);
                foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System,
                    Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                    Environment.SpecialFolder.CommonApplicationData })
                {
                    var root = Environment.GetFolderPath(special);
                    if (!string.IsNullOrEmpty(root) && MaintenancePaths.IsWithin(root, full)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // A single overwrite limits avoidable writes. This is not a guarantee of erasure on SSDs.
        public static Task<bool> ShredFileAsync(string path, int passes = 1) => Task.Run(() =>
        {
            try
            {
                if (passes < 1 || passes > 3 || !IsAllowedTarget(path)) return false;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    // Overwriting one hard link would modify all names of the same file.
                    if (!GetFileInformationByHandle(stream.SafeFileHandle, out var identity) || identity.Links != 1)
                        return false;
                    var length = stream.Length;
                    var buffer = new byte[81920];
                    for (var pass = 0; pass < passes; pass++)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        long remaining = length;
                        while (remaining > 0)
                        {
                            var chunk = (int)Math.Min(buffer.Length, remaining);
                            RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                            stream.Write(buffer, 0, chunk);
                            remaining -= chunk;
                        }
                        stream.Flush(flushToDisk: true);
                    }
                }

                if (!IsAllowedTarget(path)) return false;
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}
