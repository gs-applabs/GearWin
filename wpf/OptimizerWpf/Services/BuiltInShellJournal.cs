using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace OptimizerWpf.Services;

internal sealed record BuiltInVerbPlan(string Kind, string Label, string ScriptPath)
{
    internal bool IsValid => BuiltInShellJournal.ValidPlan(this);
}

internal static class BuiltInShellJournal
{
    internal const string Own = "Own", Open = "Open";
    private const string Classes = @"Software\Classes\";
    internal static bool ValidPlan(BuiltInVerbPlan p)
    {
        if (p.Kind is not (Own or Open) || string.IsNullOrWhiteSpace(p.Label) ||
            p.Label.Length > 256 || p.Label.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(p.ScriptPath) || p.ScriptPath.Length > 512 ||
            p.ScriptPath.IndexOfAny(new[] { '"', '%', '\0' }) >= 0 || p.ScriptPath.Any(char.IsControl))
            return false;
        try
        {
            return Path.IsPathFullyQualified(p.ScriptPath) &&
                p.ScriptPath.Length > 3 && char.IsAsciiLetter(p.ScriptPath[0]) &&
                p.ScriptPath[1] == ':' && p.ScriptPath[2] == '\\' &&
                Path.GetFullPath(p.ScriptPath) == p.ScriptPath &&
                Path.GetFileName(p.ScriptPath) == "ShellContextHelper.ps1";
        }
        catch { return false; }
    }

    internal static string[] Roots(string kind) => kind switch
    {
        Own => new[] { @"*\shell\OptimizerTakeOwnership", @"Directory\shell\OptimizerTakeOwnership" },
        Open => new[] { @"Directory\shell\OptimizerOpenPS", @"Directory\Background\shell\OptimizerOpenPS" },
        _ => throw new ArgumentException("Unknown shell action.")
    };

    internal static RegTweak[] Values(BuiltInVerbPlan plan)
    {
        if (!plan.IsValid) throw new ArgumentException("Invalid shell action plan.");
        string exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return Roots(plan.Kind).SelectMany(root =>
        {
            // Append a literal dot for directory paths to avoid a trailing
            // backslash escaping the closing quote at drive roots.
            string target = root.StartsWith(@"*\", StringComparison.Ordinal) ? "%1" :
                root.Contains(@"\Background\", StringComparison.Ordinal) ? @"%V\." : @"%1\.";
            string command = $"\"{exe}\" -NoProfile -NoExit -File \"{plan.ScriptPath}\" -Action {plan.Kind} -Target \"{target}\"";
            return new[]
            {
                new RegTweak("", "", RegistryHive.CurrentUser, Classes + root, "", plan.Label, RegistryValueKind.String, root + ".Label"),
                new RegTweak("", "", RegistryHive.CurrentUser, Classes + root + @"\command", "", command, RegistryValueKind.String, root + ".Command")
            };
        }).ToArray();
    }

    private static string UserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.User?.Value ??
            throw new InvalidOperationException("Unknown user."))));
    }

    private static string JournalPath(string kind) => ProtectedOperationJournal.PathFor("shell-" + kind + "-" + UserId());
    private static BuiltInVerbPlan? Read(string kind) =>
        ProtectedOperationJournal.Read<BuiltInVerbPlan>(JournalPath(kind), p => p.Kind == kind && p.IsValid);

    private static IDisposable Lease(string kind) =>
        CoreProcessLease.TryAcquire(@"Global\GearWin.ShellVerb.v1." + kind + "." + UserId())
        ?? throw new IOException("Shell action is busy.");

    // Permit only vacant or previously emptied containers; never overwrite a
    // legacy or third-party verb, even when it uses the old product name.
    private static bool IsVacant(string root)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(Classes + root);
            if (key == null) continue;
            if (key.ValueCount != 0 || key.GetSubKeyNames().Any(n => !n.Equals("command", StringComparison.OrdinalIgnoreCase))) return false;
            using var command = key.OpenSubKey("command");
            if (command != null && (command.ValueCount != 0 || command.SubKeyCount != 0)) return false;
        }
        return true;
    }

    internal static bool IsOn(string kind)
    {
        try
        {
            var plan = Read(kind);
            return plan != null && Values(plan).All(TweakRegistryJournal.IsApplied);
        }
        catch { return false; }
    }

    internal static bool CanRestore(string kind)
    {
        try { var plan = Read(kind); return plan != null && GroupedRegistryTweaks.CanRestore(Values(plan)); }
        catch { return false; }
    }

    internal static void Change(string kind, string label, bool restore)
    {
        _ = Roots(kind);
        using var lease = Lease(kind);
        var plan = Read(kind);
        if (restore)
        {
            if (plan == null) throw new InvalidOperationException("Legacy or unowned shell action; manual review required.");
            var values = Values(plan);
            var owned = GroupedRegistryTweaks.SelectOwned(values, t => TweakRegistryJournal.Preflight(t, true));
            foreach (var t in owned) TweakRegistryJournal.Restore(t);
            if (values.Any(TweakRegistryJournal.IsApplied))
                throw new IOException("Shell value remains outside GearWin ownership; manual review required.");
            File.Delete(JournalPath(kind));
            return;
        }

        string script = Path.Combine(AppContext.BaseDirectory, "Shell", "ShellContextHelper.ps1");
        if (!SecureStorage.IsProtectedFile(script))
            throw new IOException("Shell helper must be installed in a protected application folder.");
        if (plan == null)
        {
            if (Roots(kind).Any(root => !IsVacant(root)))
                throw new InvalidOperationException("An existing shell action is not owned by GearWin; manual review required.");
            plan = new BuiltInVerbPlan(kind, label, script);
            _ = GroupedRegistryTweaks.SelectOwned(Values(plan), t => TweakRegistryJournal.Preflight(t, false));
            ProtectedOperationJournal.Save(JournalPath(kind), plan, p => p.Kind == kind && p.IsValid);
        }
        if (!string.Equals(plan.ScriptPath, script, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Shell helper location changed; restore the previous plan before installing.");
        GroupedRegistryTweaks.Change(Values(plan), "ShellVerb." + kind + "." + UserId(), false);
    }
}
