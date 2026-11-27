using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OptimizerWpf.Services
{
    // ΑΣΦΑΛΕΙΑ: η εφαρμογή τρέχει ως Administrator. Κάθε αρχείο που γράφεται και ΜΕΤΑ εκτελείται/διαβάζεται από
    // elevated διεργασία (XML scheduled task, πακέτο οδηγού, εξαγόμενα εργαλεία, associations XML) δεν πρέπει να
    // περνά από φάκελο εγγράψιμο από μη-elevated κώδικα του ίδιου χρήστη (%TEMP%, %LocalAppData%) - αλλιώς το
    // αρχείο μπορεί να αντικατασταθεί ανάμεσα στη δημιουργία και στη χρήση (privilege escalation χωρίς UAC).
    // Εδώ: %ProgramData%\GearWin με ACL μόνο-Administrators/SYSTEM εγγραφής, Users μόνο ανάγνωση/εκτέλεση. Οι
    // Users δεν μπορούν ούτε να μετονομάσουν/διαγράψουν τον φάκελο (δεν έχουν δικαίωμα στον γονέα να σβήσουν παιδιά).
    public static class SecureStorage
    {
        public static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GearWin");

        private static readonly SecurityIdentifier Admin = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        private const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData |
            FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes;

        internal static bool IsTrustedWriter(SecurityIdentifier sid) =>
            sid.Equals(Admin) || sid.Equals(SystemSid) || sid.Equals(TrustedInstaller);

        private static bool IsProtected(FileSystemSecurity security)
        {
            // A null DACL grants unrestricted access; an empty rule enumeration is not proof of protection.
            if (new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl == null) return false;
            if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrustedWriter(owner)) return false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow ||
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 ||
                    (rule.FileSystemRights & WriteRights) == 0) continue;
                if (rule.IdentityReference is not SecurityIdentifier sid || !IsTrustedWriter(sid)) return false;
            }
            return true;
        }

        public static bool IsDirectoryAdminOnlyWritable(string? dir)
        {
            try
            {
                return !string.IsNullOrEmpty(dir) && MaintenancePaths.IsOrdinaryLocalPath(dir) &&
                    Directory.Exists(dir) && IsProtected(new DirectoryInfo(dir).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
            }
            catch { return false; }
        }

        internal static bool IsProtectedExecutable(string? path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) &&
                    string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
                    MaintenancePaths.IsOrdinaryLocalPath(path) && File.Exists(path) &&
                    IsDirectoryAdminOnlyWritable(Path.GetDirectoryName(path)) &&
                    IsProtected(new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner));
            }
            catch { return false; }
        }

        private static DirectorySecurity NewSecurity()
        {
            var security = new DirectorySecurity();
            security.SetOwner(Admin);
            security.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Admin, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        public static void SecureDirectory(string dir)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDir));
            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) && !MaintenancePaths.IsWithin(root, full))
                throw new IOException("Secure storage must be under the GearWin root.");
            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                SecureDirectory(Path.GetDirectoryName(full)!);
            else if (!MaintenancePaths.IsOrdinaryLocalPath(Path.GetDirectoryName(root)!))
                throw new IOException("Unsafe secure storage parent.");

            // Existing storage is never blindly re-ACL'd: it could contain untrusted files
            // or be a link. Fail closed, preserve its contents for explicit migration.
            if (!Directory.Exists(full))
                new DirectoryInfo(full).Create(NewSecurity());
            if (!IsDirectoryAdminOnlyWritable(full))
                throw new IOException("Secure storage is not protected. Repair or migrate its permissions before proceeding.");
        }

        public static void EnsureRoot() => SecureDirectory(RootDir);

        public static string NewPrivateTempDir(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || prefix.Length > 40 ||
                !System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\A[A-Za-z0-9_-]+\z"))
                throw new ArgumentException("Invalid temporary directory prefix.", nameof(prefix));
            var dir = Path.Combine(RootDir, "tmp", $"{prefix}_{Guid.NewGuid():N}");
            SecureDirectory(dir);
            // Do not purge old directories by age alone: another process may still own
            // a long-running job. Orphan cleanup requires explicit ownership evidence.
            return dir;
        }

        public static string NewPrivateTempFile(string prefix, string extension)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(extension ?? "", @"\A\.[A-Za-z0-9]{1,12}\z"))
                throw new ArgumentException("Invalid extension.", nameof(extension));
            return Path.Combine(NewPrivateTempDir(prefix), "file" + extension);
        }
    }
}
