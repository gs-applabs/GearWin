using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class VisualAndHibernateTests
{
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    [InlineData(2, 0, true)]
    [InlineData(0, 0, false)]
    [InlineData(2, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(-1, 0, false)]
    public void OnlyExistingThreePresetsAreAccepted(int mode, int enabled, bool valid)
    {
        var plan = new VisualPresetPlan(mode, enabled);
        Assert.Equal(valid, plan.IsValid);
        if (!valid) Assert.Throws<ArgumentException>(() => VisualEffectsJournal.Values(plan));
    }

    [Fact]
    public void PresetsUseSameIdentitiesAndExactTypes()
    {
        var a = VisualEffectsJournal.Values(new(1, 1));
        var b = VisualEffectsJournal.Values(new(2, 0));
        Assert.Equal(7, a.Length);
        Assert.Equal(a.Select(x => x.BackupKey), b.Select(x => x.BackupKey));
        Assert.All(a, x => Assert.Equal(RegistryHive.CurrentUser, x.Hive));
        Assert.Equal(2, a.Count(x => x.Kind == RegistryValueKind.String));
        Assert.Equal(5, a.Count(x => x.Kind == RegistryValueKind.DWord));
        Assert.All(b.Where(x => x.Kind == RegistryValueKind.String), x => Assert.Equal("0", x.OnValue));
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public void HibernateUsesOnlyAbsoluteSystemCommand(bool enabled, string arg)
    {
        var info = HibernateSettings.Command(enabled);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), info.FileName);
        Assert.Equal(new[] { "/hibernate", arg }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput && info.RedirectStandardError);
    }

    [Fact]
    public void UnknownHibernateStateNeverBecomesFalseByCoercion()
    {
        Assert.True(HibernateSettings.Decode(1, RegistryValueKind.DWord));
        Assert.False(HibernateSettings.Decode(0, RegistryValueKind.DWord));
        Assert.Null(HibernateSettings.Decode(2, RegistryValueKind.DWord));
        Assert.Null(HibernateSettings.Decode("1", RegistryValueKind.String));
        Assert.Null(HibernateSettings.Decode(null, RegistryValueKind.DWord));
    }
}
