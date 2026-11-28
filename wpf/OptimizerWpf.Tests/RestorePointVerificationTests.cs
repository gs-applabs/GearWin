using System;
using System.Text.Json;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public sealed class RestorePointVerificationTests
{
    [Fact]
    public void EmptyInventoryIsDistinctFromMissingOutput()
    {
        Assert.Empty(SystemService.ParseRestorePoints("[]"));
        Assert.Throws<JsonException>(() => SystemService.ParseRestorePoints(""));
    }

    [Theory]
    [InlineData("[{\"SequenceNumber\":1}]")]
    [InlineData("[{\"SequenceNumber\":0,\"CreationTime\":\"2026-11-28T07:00:00\"}]")]
    [InlineData("[{\"SequenceNumber\":1,\"CreationTime\":\"2026-11-28T07:00:00\"},{\"SequenceNumber\":1,\"CreationTime\":\"2026-11-28T07:00:00\"}]")]
    public void IncompleteOrAmbiguousInventoryFails(string output) =>
        Assert.Throws<JsonException>(() => SystemService.ParseRestorePoints(output));

    [Fact]
    public void ExistingOrFuturePointCannotProveNewCheckpoint()
    {
        var now = new DateTime(2026, 11, 28, 12, 0, 0);
        var age = TimeSpan.FromHours(24);
        var existing = new RestorePointInfo(1, "old", now.AddHours(-2));
        var future = new RestorePointInfo(2, "future", now.AddHours(1));
        var fresh = new RestorePointInfo(3, "new", now.AddMinutes(-1));
        Assert.False(RestoreGuardService.NeedsNewPoint(new[] { existing }, now, age));
        Assert.True(RestoreGuardService.NeedsNewPoint(new[] { future }, now, age));
        Assert.False(RestoreGuardService.HasNewRecentPoint(new[] { existing }, new[] { existing, future }, now, age));
        Assert.True(RestoreGuardService.HasNewRecentPoint(new[] { existing }, new[] { fresh }, now, age));
        Assert.False(RestoreGuardService.HasNewRecentPoint(new[] { existing }, new[] { existing with { CreationTime = now } }, now, age));
    }
}
