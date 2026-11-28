using System;
using System.Linq;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class BuiltInShellJournalTests
{
    private const string Script = @"C:\Program Files\GearWin\Shell\ShellContextHelper.ps1";

    [Theory]
    [InlineData("Open")]
    [InlineData("Own")]
    public void ShellUsesFixedFileModeAndCurrentUserValues(string kind)
    {
        var rows = BuiltInShellJournal.Values(new(kind, "Menu", Script));
        Assert.Equal(4, rows.Length);
        Assert.All(rows, t => Assert.Equal(RegistryHive.CurrentUser, t.Hive));
        foreach (var row in rows.Where(t => t.Path.EndsWith(@"\command")))
        {
            string command = Assert.IsType<string>(row.OnValue);
            Assert.Contains("-NoProfile -NoExit -File", command);
            Assert.Contains("\"" + Script + "\"", command);
            Assert.DoesNotContain("-Command", command);
            Assert.DoesNotContain("cmd.exe", command);
            Assert.DoesNotContain("ExecutionPolicy", command);
        }
    }

    [Fact]
    public void OwnershipDoesNotRegisterDriveRootVerb()
    {
        Assert.DoesNotContain(BuiltInShellJournal.Roots("Own"), x => x.StartsWith(@"Drive\"));
    }

    [Theory]
    [InlineData("Other", Script)]
    [InlineData("Own", "ShellContextHelper.ps1")]
    [InlineData("Open", @"C:\Temp\another.ps1")]
    [InlineData("Open", @"C:\Temp\%V\ShellContextHelper.ps1")]
    public void InvalidPlanCannotGenerateValues(string kind, string script) =>
        Assert.Throws<ArgumentException>(() => BuiltInShellJournal.Values(new(kind, "Menu", script)));
}
