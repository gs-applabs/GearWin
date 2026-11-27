using System;
using System.IO;
using System.Text;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class UltimatePlanJournalTests
{
    private static readonly Guid Duplicate = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Original = new("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void ConfirmedPlanOnlyRestoresOwnedOrAlreadyRestoredState()
    {
        var snapshot = new UltimatePlanSnapshot(Duplicate, Original, true);
        Assert.True(snapshot.CanRestore(Duplicate));
        Assert.True(snapshot.CanRestore(Original));
        Assert.False(snapshot.CanRestore(UltimatePlanJournal.Template));
        Assert.False(snapshot.CanRestore(Guid.Empty));
    }

    [Fact]
    public void UnconfirmedDuplicateIsNotOwned()
    {
        var snapshot = new UltimatePlanSnapshot(Duplicate, Original, false);
        Assert.True(snapshot.IsValid);
        Assert.False(snapshot.CanRestore(Duplicate));
        Assert.True(snapshot.CanRestore(Original));
    }

    [Fact]
    public void InactiveReusablePlanCannotRestoreAnOldPlan()
    {
        var snapshot = new UltimatePlanSnapshot(Duplicate, null, true);
        Assert.True(snapshot.IsValid);
        Assert.False(snapshot.CanRestore(Duplicate));
        Assert.False(snapshot.CanRestore(Original));
    }

    [Fact]
    public void RejectsEmptyTemplateAndSelfReferencingIdentities()
    {
        Assert.False(new UltimatePlanSnapshot(Guid.Empty, Original, true).IsValid);
        Assert.False(new UltimatePlanSnapshot(UltimatePlanJournal.Template, Original, true).IsValid);
        Assert.False(new UltimatePlanSnapshot(Duplicate, Duplicate, true).IsValid);
        Assert.False(new UltimatePlanSnapshot(Duplicate, Guid.Empty, true).IsValid);
        Assert.False(new UltimatePlanSnapshot(Duplicate, null, false).IsValid);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"SVC_Test\":null}")]
    [InlineData("{\"SVC_Test\":1}")]
    [InlineData("{\"SVC_Test\":\"Auto\",\"svc_test\":\"Manual\"}")]
    public void MalformedLegacyBackupNeverBecomesEmptyState(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => TweakBackupService.ParseLegacy(stream));
    }

    [Fact]
    public void LegacyBackupRemainsAdvisoryAndCaseInsensitive()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"SVC_Test\":\"Auto\"}"));
        Assert.Equal("Auto", TweakBackupService.ParseLegacy(stream)["svc_test"]);
    }
}
