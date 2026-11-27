using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace OptimizerWpf.Services
{
    // OWVAULT1 authenticated encrypted archive; preserve originals until full recovery is proven.
    public static class FolderLockService
    {
        private static readonly byte[] Magic = System.Text.Encoding.ASCII.GetBytes("OWVAULT1");
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int Pbkdf2Iterations = 210_000;

        public const string VaultExtension = ".vault";

        // OWVAULT1 uses whole-buffer AES-GCM. Bound memory until a compatible streaming
        // format and verified restore flow are implemented.
        internal const long MaxPlainBytes = 64L * 1024 * 1024;
        private const int MaxEntries = 10000;
        private const int HeaderBytes = 8 + SaltSize + NonceSize + TagSize;
        private const long MaxVaultBytes = MaxPlainBytes + 8L * 1024 * 1024 + HeaderBytes;

        public static Task LockFolderAsync(string folderPath, string password, IProgress<string>? progress = null) =>
            Task.Run(() => CreateVaultAsync(folderPath, password, progress));

        private static async Task CreateVaultAsync(string folderPath, string password, IProgress<string>? progress)
        {
            folderPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(folderPath) || IsProtectedFolder(folderPath) || !MaintenancePaths.IsOrdinaryLocalPath(folderPath))
                throw new IOException("Folder is unavailable, protected or redirected.");
            var vaultPath = Path.TrimEndingDirectorySeparator(folderPath) + VaultExtension;
            if (File.Exists(vaultPath)) throw new IOException("Vault file already exists.");
            var staged = vaultPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            byte[]? plain = null, key = null;
            try
            {
                progress?.Report(LanguageService.T("FolderLock_Compressing"));
                using (var ms = new MemoryStream())
                {
                    long total = 0;
                    int count = 0;
                    var pending = new System.Collections.Generic.Stack<string>();
                    pending.Push(folderPath);
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        while (pending.Count > 0)
                        {
                            var dir = pending.Pop();
                            if (!MaintenancePaths.IsOrdinaryLocalPath(dir)) throw new IOException("Redirected directory refused.");
                            if (dir != folderPath)
                                zip.CreateEntry(Path.GetRelativePath(folderPath, dir).Replace('\\', '/') + "/");
                            foreach (var child in Directory.GetDirectories(dir))
                            {
                                if (++count > MaxEntries) throw new IOException("Vault entry limit exceeded.");
                                pending.Push(child);
                            }
                            foreach (var file in Directory.GetFiles(dir))
                            {
                                if (++count > MaxEntries || !MaintenancePaths.IsOrdinaryLocalPath(file))
                                    throw new IOException("Vault entry limit exceeded or redirected file.");
                                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                                if (input.Length > MaxPlainBytes - total) throw new IOException("Vault input exceeds the 64 MiB safety limit.");
                                var entry = zip.CreateEntry(Path.GetRelativePath(folderPath, file).Replace('\\', '/'), CompressionLevel.Optimal);
                                using var output = entry.Open();
                                var buffer = new byte[81920];
                                int read;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    total += read;
                                    if (total > MaxPlainBytes) throw new IOException("Vault input exceeds the 64 MiB safety limit.");
                                    output.Write(buffer, 0, read);
                                }
                            }
                        }
                    }
                    if (ms.Length > MaxVaultBytes - HeaderBytes) throw new IOException("Vault exceeds the safety limit.");
                    plain = ms.ToArray();
                }

                progress?.Report(LanguageService.T("FolderLock_Encrypting"));
                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
                var ciphertext = new byte[plain.Length];
                var tag = new byte[TagSize];
                using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, ciphertext, tag);
                using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await file.WriteAsync(Magic);
                    await file.WriteAsync(salt);
                    await file.WriteAsync(nonce);
                    await file.WriteAsync(tag);
                    await file.WriteAsync(ciphertext);
                    file.Flush(flushToDisk: true);
                }
                var verified = await ReadVaultAsync(staged, password);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(plain, verified))
                        throw new IOException("Vault verification failed.");
                }
                finally { CryptographicOperations.ZeroMemory(verified); }
                File.Move(staged, vaultPath, overwrite: false);
                // Safety containment: do not automatically destroy original data. OWVAULT1
                // does not preserve all NTFS metadata and cannot prove a stable tree snapshot.
            }
            finally
            {
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (plain != null) CryptographicOperations.ZeroMemory(plain);
                if (File.Exists(staged)) try { File.Delete(staged); } catch { }
            }
        }

        private static async Task<byte[]> ReadVaultAsync(string path, string password)
        {
            if (!MaintenancePaths.IsOrdinaryLocalPath(path)) throw new IOException("Unsafe vault path.");
            byte[] all;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < HeaderBytes || input.Length > MaxVaultBytes)
                    throw new InvalidDataException("Invalid vault size or 64 MiB safety limit exceeded.");
                all = new byte[checked((int)input.Length)];
                await input.ReadExactlyAsync(all);
            }
            if (!all.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Not a valid vault.");
            var key = Rfc2898DeriveBytes.Pbkdf2(password, all.AsSpan(Magic.Length, SaltSize), Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
            var plain = new byte[all.Length - HeaderBytes];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(all.AsSpan(Magic.Length + SaltSize, NonceSize), all.AsSpan(HeaderBytes),
                    all.AsSpan(Magic.Length + SaltSize + NonceSize, TagSize), plain);
                return plain;
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(plain);
                throw new UnauthorizedAccessException(LanguageService.T("FolderLock_WrongPassword"));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        public static Task UnlockFolderAsync(string vaultPath, string password, string destinationFolder, IProgress<string>? progress = null) =>
            Task.Run(async () =>
            {
                destinationFolder = Path.GetFullPath(destinationFolder);
                var parent = Path.GetDirectoryName(destinationFolder);
                if (parent == null || !MaintenancePaths.IsOrdinaryLocalPath(parent) || IsProtectedFolder(destinationFolder) ||
                    Directory.Exists(destinationFolder) || File.Exists(destinationFolder))
                    throw new IOException("Restore requires a new folder in a safe local location.");
                progress?.Report(LanguageService.T("FolderLock_Decrypting"));
                var plain = await ReadVaultAsync(vaultPath, password);
                var staged = destinationFolder + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    progress?.Report(LanguageService.T("FolderLock_Extracting"));
                    using var ms = new MemoryStream(plain);
                    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                    if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("Vault entry limit exceeded.");
                    long expanded = 0;
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Length > MaxPlainBytes - expanded) throw new InvalidDataException("Expanded vault exceeds the safety limit.");
                        expanded += entry.Length;
                    }
                    Directory.CreateDirectory(staged);
                    zip.ExtractToDirectory(staged, overwriteFiles: false);
                    Directory.Move(staged, destinationFolder);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                    // Never recursively delete an unverified directory on failure.
                    // A partial staging directory may remain; original vault is never removed.
                }
            });

        private static bool IsProtectedFolder(string folderPath)
        {
            var full = Path.GetFullPath(folderPath).TrimEnd('\\', '/');
            if (Path.GetPathRoot(full)?.TrimEnd('\\', '/').Equals(full, StringComparison.OrdinalIgnoreCase) == true) return true; // ρίζα δίσκου
            foreach (var special in new[]
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            })
            {
                var sp = Environment.GetFolderPath(special).TrimEnd('\\', '/');
                if (sp.Length == 0) continue;
                // ίδιος φάκελος Ή γονικός του (θα περιείχε τον προστατευμένο)
                if (full.Equals(sp, StringComparison.OrdinalIgnoreCase)) return true;
                if (sp.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

    }
}
