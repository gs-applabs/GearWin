using System;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class CustomMenuSafetyTests
{
    private const string Id = "OptimizerCustom_0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("Directory", "%1")]
    [InlineData("Files", "%1")]
    [InlineData("FolderBackground", "%V")]
    public void DirectExecutableUsesOneQuotedSelection(string scope, string token)
    {
        string path = @"C:\Program Files\Editor\Editor.exe";
        string command = CustomContextMenuService.BuildCommand(path, scope, false);
        Assert.Equal($"\"{path}\" \"{token}\"", command);
        Assert.Equal(command, CustomContextMenuService.BuildCommand(command, scope, false));
    }

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\Tools\run.bat")]
    [InlineData(@"C:\Tools\Editor.exe /unsafe")]
    [InlineData(@"C:\Tools\..\Editor.exe")]
    [InlineData(@"\\server\share\Editor.exe")]
    [InlineData("C:\\Tools\\Editor.exe\nbad")]
    public void UnsafeOrAmbiguousExecutableIsRefused(string input) =>
        Assert.Throws<ArgumentException>(() => CustomContextMenuService.BuildCommand(input, "Files", false));

    [Theory]
    [InlineData("OptimizerCustom_1234567890")]
    [InlineData("..\\Other")]
    [InlineData("Other")]
    public void UnownedOrMalformedIdCannotSelectRegistryPath(string id) =>
        Assert.Throws<ArgumentException>(() => CustomContextMenuService.RegPath("Files", id));

    [Fact]
    public void UnknownScopeIsNotMappedToDirectory()
    {
        Assert.Throws<ArgumentException>(() => CustomContextMenuService.RegPath("Other", Id));
        Assert.Throws<ArgumentException>(() => CustomContextMenuService.BuildCommand(@"C:\Tools\Editor.exe", "Other", false));
    }

    [Fact]
    public void OnlyUserLabelAndCommandAreJournaled()
    {
        var item = new CustomContextMenuItem(Id, "Open editor", "\"C:\\Tools\\Editor.exe\" \"%1\"", "Files");
        Assert.True(CustomContextMenuService.ValidItem(item));
        var values = CustomContextMenuService.Values(item);
        Assert.Equal(2, values.Length);
        Assert.All(values, v =>
        {
            Assert.Equal(RegistryHive.CurrentUser, v.Hive);
            Assert.Equal("", v.Name);
            Assert.Equal(RegistryValueKind.String, v.Kind);
            Assert.StartsWith(@"Software\Classes\*\shell\" + Id, v.Path);
        });
    }
}
