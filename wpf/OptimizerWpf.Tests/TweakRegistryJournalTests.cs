using System;
using System.IO;
using System.Text;
using Microsoft.Win32;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class TweakRegistryJournalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void DwordRoundTripsExactly(int number)
    {
        var value = TweakValue.Capture(number, RegistryValueKind.DWord);
        Assert.True(value.IsValid);
        Assert.Equal(RegistryValueKind.DWord, value.RegistryKind);
        Assert.Equal(number, Assert.IsType<int>(value.ToRegistryValue()));
    }

    [Fact]
    public void MissingEmptyStringAndNumericStringAreDistinct()
    {
        var empty = TweakValue.Capture("", RegistryValueKind.String);
        var numeric = TweakValue.Capture("1", RegistryValueKind.String);
        Assert.NotEqual(TweakValue.Missing, empty);
        Assert.NotEqual(numeric, TweakValue.Capture(1, RegistryValueKind.DWord));
        Assert.Equal("", empty.ToRegistryValue());
    }

    [Fact]
    public void UnsupportedKindsAreRejectedRatherThanCoerced()
    {
        Assert.Throws<InvalidDataException>(() => TweakValue.Capture(new byte[] { 1 }, RegistryValueKind.Binary));
        Assert.Throws<InvalidDataException>(() => TweakValue.Capture("%TEMP%", RegistryValueKind.ExpandString));
        Assert.Throws<InvalidDataException>(() => TweakValue.Capture(1L, RegistryValueKind.QWord));
    }

    [Fact]
    public void RestoreOnlyOwnedOrAlreadyRestoredValue()
    {
        var original = TweakValue.Missing;
        var applied = TweakValue.Capture(1, RegistryValueKind.DWord);
        var snapshot = new TweakRegistrySnapshot(original, applied);
        Assert.True(snapshot.CanRestore(original));
        Assert.True(snapshot.CanRestore(applied));
        Assert.False(snapshot.CanRestore(TweakValue.Capture(2, RegistryValueKind.DWord)));
        Assert.False(snapshot.CanRestore(TweakValue.Capture("1", RegistryValueKind.String)));
    }

    [Fact]
    public void JournalIdentitySeparatesUsersAndSharesMachineScope()
    {
        string Key(RegistryHive hive, string path, string name, string sid) =>
            TweakRegistryJournal.Identity(hive, path, name, sid);
        Assert.NotEqual(Key(RegistryHive.CurrentUser, "Software", "Test", "A"), Key(RegistryHive.CurrentUser, "Software", "Test", "B"));
        Assert.Equal(Key(RegistryHive.LocalMachine, "Software", "Test", "A"), Key(RegistryHive.LocalMachine, "software", "test", "B"));
        Assert.NotEqual(Key(RegistryHive.LocalMachine, "Software", "Test", "A"), Key(RegistryHive.LocalMachine, "Software", "Other", "A"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Original\":{\"Kind\":\"missing\",\"Data\":null},\"Applied\":{\"Kind\":\"dword\",\"Data\":\"01\"}}")]
    [InlineData("{\"Original\":{\"Kind\":\"missing\"},\"Applied\":{\"Kind\":\"dword\",\"Data\":\"1\"}}")]
    [InlineData("{\"Original\":{\"Kind\":\"missing\",\"Kind\":\"missing\",\"Data\":null},\"Applied\":{\"Kind\":\"dword\",\"Data\":\"1\"}}")]
    [InlineData("{\"Original\":{\"Kind\":\"dword\",\"Data\":\"1\"},\"Applied\":{\"Kind\":\"dword\",\"Data\":\"1\"}}")]
    [InlineData("{\"Original\":null,\"Applied\":{\"Kind\":\"dword\",\"Data\":\"1\"}}")]
    public void InvalidJournalIsNeverTreatedAsMissing(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => TweakRegistrySnapshot.Parse(stream));
    }

    [Fact]
    public void ValidJournalRoundTripsExplicitAbsence()
    {
        var original = new TweakRegistrySnapshot(TweakValue.Missing, TweakValue.Capture(1, RegistryValueKind.DWord));
        using var stream = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original));
        Assert.Equal(original, TweakRegistrySnapshot.Parse(stream));
    }
}
