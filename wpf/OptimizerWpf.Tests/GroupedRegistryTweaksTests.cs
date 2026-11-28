using System;
using System.Linq;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class GroupedRegistryTweaksTests
{
    [Fact]
    public void MouseValuesAreTypedAndDistinct()
    {
        var group = GroupedRegistryTweaks.Mouse;
        Assert.Equal(3, group.Count);
        Assert.Equal(3, group.Select(x => x.BackupKey).Distinct().Count());
        Assert.All(group, x =>
        {
            Assert.Equal(RegistryHive.CurrentUser, x.Hive);
            Assert.Equal(RegistryValueKind.String, x.Kind);
            Assert.Equal("0", x.OnValue);
        });
    }

    [Fact]
    public void GameBarPolicyAndUserValueRemainSeparate()
    {
        var group = GroupedRegistryTweaks.GameBar;
        Assert.Equal(2, group.Count);
        Assert.Contains(group, x => x.Hive == RegistryHive.LocalMachine && x.Name == "AllowGameDVR");
        Assert.Contains(group, x => x.Hive == RegistryHive.CurrentUser && x.Name == "GameDVR_Enabled");
        Assert.All(group, x => Assert.Equal(RegistryValueKind.DWord, x.Kind));
    }

    [Fact]
    public void ClassicMenuTargetsOnlyDefaultValueNotEntireTree()
    {
        var value = Assert.Single(GroupedRegistryTweaks.ClassicMenu);
        Assert.Equal("", value.Name);
        Assert.Equal("", value.OnValue);
        Assert.EndsWith(@"\InprocServer32", value.Path);
    }

    [Fact]
    public void SelectOwnedSkipsUnownedMembers()
    {
        var group = GroupedRegistryTweaks.Mouse;
        var selected = GroupedRegistryTweaks.SelectOwned(group, t => t.Name == "MouseThreshold1");
        Assert.Equal("MouseThreshold1", Assert.Single(selected).Name);
    }

    [Fact]
    public void PreflightChecksAllMembersBeforeReturningAny()
    {
        var group = GroupedRegistryTweaks.Mouse;
        int seen = 0;
        Assert.Throws<InvalidOperationException>(() => GroupedRegistryTweaks.SelectOwned(group, t =>
        {
            seen++;
            if (seen == 3) throw new InvalidOperationException("conflict");
            return true;
        }));
        Assert.Equal(3, seen);
    }

    [Fact]
    public void BulkGroupsHaveUniqueIdsAndValues()
    {
        var groups = GroupedRegistryTweaks.Groups().ToArray();
        Assert.Equal(3, groups.Select(x => x.Id).Distinct().Count());
        var values = groups.SelectMany(x => x.Values).ToArray();
        Assert.Equal(6, values.Length);
        Assert.Equal(6, values.Select(x => x.BackupKey).Distinct().Count());
    }
}
