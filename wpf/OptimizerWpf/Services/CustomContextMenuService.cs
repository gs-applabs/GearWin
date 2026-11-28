using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OptimizerWpf.Services
{
    public record CustomContextMenuItem(string Id, string MenuText, string Command, string Scope);
    internal sealed record CustomMenuIndex(string Entries);

    public static class CustomContextMenuService
    {
        private const int MaxItems = 32;
        private static readonly string LegacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimizerWpf", "CustomContextMenu.json");

        private static string UserId()
        {
            using var user = WindowsIdentity.GetCurrent();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.User?.Value ??
                throw new InvalidOperationException("Unknown registry user."))));
        }

        private static string IndexPath => ProtectedOperationJournal.PathFor("custom-menu-" + UserId());
        private static IDisposable Lease() => CoreProcessLease.TryAcquire(@"Global\GearWin.CustomMenu.v1." + UserId())
            ?? throw new IOException("Custom context menu is busy.");

        internal static bool ValidId(string? id) => id != null &&
            Regex.IsMatch(id, @"\AOptimizerCustom_[a-f0-9]{32}\z", RegexOptions.CultureInvariant);

        internal static bool ValidScope(string? scope) => scope is "Directory" or "FolderBackground" or "Files";
        private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value)
            && value.Length <= max && !value.Any(char.IsControl);

        // A direct executable path only, optionally followed by the one
        // quoted selection argument. No command interpreter or implicit
        // search path. Do not feed shell-expanded filenames to scripts.
        internal static string BuildCommand(string input, string scope, bool checkFile = true)
        {
            if (!ValidScope(scope) || !ValidText(input, 512))
                throw new ArgumentException("Invalid custom menu command.");
            string token = scope == "FolderBackground" ? "\"%V\"" : "\"%1\"";
            string text = input.Trim();
            if (text.EndsWith(" " + token, StringComparison.Ordinal))
                text = text[..^(token.Length + 1)].TrimEnd();
            string executable = text.Length >= 2 && text[0] == '"' && text[^1] == '"'
                ? text[1..^1] : text;
            if (executable.IndexOfAny(new[] { '"', '%', '&', '|', '<', '>', '^', '\0' }) >= 0 ||
                !Regex.IsMatch(executable, @"\A[A-Za-z]:\\") || !Path.IsPathFullyQualified(executable) ||
                !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(executable) != executable)
                throw new ArgumentException("Choose the absolute path of an executable, without script or additional arguments.");
            string name = Path.GetFileName(executable);
            if (new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe",
                "rundll32.exe", "regsvr32.exe", "msiexec.exe", "bash.exe", "wsl.exe", "python.exe", "pythonw.exe" }
                .Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Command interpreters and script hosts are not supported.");
            if (checkFile && (!File.Exists(executable) || !MaintenancePaths.IsOrdinaryLocalPath(executable)))
                throw new IOException("Executable must be an existing ordinary local file.");
            return "\"" + executable + "\" " + token;
        }

        internal static string RegPath(string scope, string id)
        {
            if (!ValidId(id)) throw new ArgumentException("Invalid custom menu ID.");
            return scope switch
            {
                "FolderBackground" => $@"Software\Classes\Directory\Background\shell\{id}",
                "Files" => $@"Software\Classes\*\shell\{id}",
                "Directory" => $@"Software\Classes\Directory\shell\{id}",
                _ => throw new ArgumentException("Invalid custom menu scope.")
            };
        }

        internal static bool ValidItem(CustomContextMenuItem? item)
        {
            if (item == null || !ValidId(item.Id) || !ValidScope(item.Scope) || !ValidText(item.MenuText, 128))
                return false;
            try { return BuildCommand(item.Command, item.Scope, false) == item.Command; }
            catch { return false; }
        }

        internal static RegTweak[] Values(CustomContextMenuItem item)
        {
            if (!ValidItem(item)) throw new ArgumentException("Invalid custom menu item.");
            string path = RegPath(item.Scope, item.Id);
            return new[]
            {
                new RegTweak("", "", RegistryHive.CurrentUser, path, "", item.MenuText, RegistryValueKind.String, item.Id + ".Label"),
                new RegTweak("", "", RegistryHive.CurrentUser, path + @"\command", "", item.Command, RegistryValueKind.String, item.Id + ".Command")
            };
        }

        private static List<CustomContextMenuItem> Parse(CustomMenuIndex index)
        {
            if (string.IsNullOrEmpty(index.Entries) || index.Entries.Length > 24000)
                throw new InvalidDataException("Invalid menu index.");
            var items = JsonSerializer.Deserialize<List<CustomContextMenuItem>>(index.Entries)
                ?? throw new InvalidDataException("Invalid menu index.");
            if (items.Count > MaxItems || items.Any(x => !ValidItem(x)) ||
                items.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
                throw new InvalidDataException("Invalid or ambiguous menu index.");
            return items;
        }

        private static bool ValidIndex(CustomMenuIndex index)
        {
            try { _ = Parse(index); return true; } catch { return false; }
        }

        private static (List<CustomContextMenuItem> Items, bool Exists) Read()
        {
            var index = ProtectedOperationJournal.Read<CustomMenuIndex>(IndexPath, ValidIndex);
            return index == null ? (new(), false) : (Parse(index), true);
        }

        private static void Save(List<CustomContextMenuItem> items, bool replace) =>
            ProtectedOperationJournal.Save(IndexPath,
                new CustomMenuIndex(JsonSerializer.Serialize(items)), ValidIndex, replace);

        public static bool HasLegacyEntries => File.Exists(LegacyPath);

        public static List<CustomContextMenuItem> Load()
        {
            try { return Read().Items; }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Custom menu index: " + ex.Message); return new(); }
        }

        public static CustomContextMenuItem Add(string menuText, string command, string scope)
        {
            var item = new CustomContextMenuItem("OptimizerCustom_" + Guid.NewGuid().ToString("N"),
                menuText.Trim(), BuildCommand(command, scope), scope);
            if (!ValidItem(item)) throw new ArgumentException("Invalid menu label.");
            using var lease = Lease();
            var (items, exists) = Read();
            if (items.Count >= MaxItems) throw new InvalidOperationException("Custom menu limit reached.");
            var values = Values(item);
            using (var key = Registry.ClassesRoot.OpenSubKey(RegPath(scope, item.Id)[@"Software\Classes\".Length..]))
                if (key != null) throw new IOException("Shell verb already exists.");
            _ = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, false));
            items.Add(item);
            // Save the durable plan first, so an interrupted apply remains
            // recoverable even if the UI never received its return value.
            Save(items, exists);
            GroupedRegistryTweaks.Change(values, item.Id, false);
            return item;
        }

        public static void Remove(CustomContextMenuItem item)
        {
            using var lease = Lease();
            var (items, exists) = Read();
            var ownedItem = items.SingleOrDefault(x => x.Id == item.Id);
            if (ownedItem == null || ownedItem != item)
                throw new InvalidOperationException("No verified original menu record; existing settings left unchanged.");
            var values = Values(ownedItem);
            var owned = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, true));
            foreach (var t in owned) TweakRegistryJournal.Restore(t);
            // If an interrupted add never reached the writes, it is safe to
            // remove only the index entry. Do not remove any key by name.
            if (values.Any(TweakRegistryJournal.IsApplied))
                throw new IOException("A shell value remains outside GearWin ownership; manual review required.");
            items.Remove(ownedItem);
            Save(items, exists);
            // Empty registry containers deliberately remain. Shell visibility
            // and legacy entries require separate Windows UI acceptance.
        }
    }
}
