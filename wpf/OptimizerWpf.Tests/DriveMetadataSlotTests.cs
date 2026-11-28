using System;
using System.Threading;
using System.Threading.Tasks;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class DriveMetadataSlotTests
{
    [Theory]
    [InlineData("c:\\", "C:")]
    [InlineData("D:", "D:")]
    [InlineData("z:\\", "Z:")]
    [InlineData("C:\\Windows", null)]
    [InlineData("\\\\server\\share", null)]
    [InlineData("1:", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void KeysAreBoundedToDriveLetters(string? input, string? expected)
        => Assert.Equal(expected, HomeDiskMetadataService.NormalizeDrive(input));

    [Fact]
    public async Task RapidNavigationSharesOneReadAndDoesNotQueueOtherDrives()
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var slot = new DriveMetadataSlot<string>(async _ =>
        {
            Interlocked.Increment(ref reads);
            return await gate.Task;
        });
        var first = slot.ReadAsync("C:");
        try
        {
            for (int i = 0; i < 1000; i++)
            {
                Assert.Same(first, slot.ReadAsync("c:\\"));
                Assert.Null(await slot.ReadAsync("D:"));
            }
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(cancel.Token));
            Assert.False(first.IsCompleted);
            Assert.Equal(1, reads);
        }
        finally { gate.TrySetResult("SSD"); }
        Assert.Equal("SSD", await first);
        Assert.Equal("SSD", await slot.ReadAsync("C:"));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task InvalidationDiscardsPendingReadWithoutStartingAnother()
    {
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var slot = new DriveMetadataSlot<string>(_ => ++reads == 1 ? gate.Task : Task.FromResult("new"));
        var old = slot.ReadAsync("C:");
        slot.Invalidate();
        Assert.Null(await slot.ReadAsync("C:"));
        Assert.Null(await slot.ReadAsync("D:"));
        Assert.Equal(1, reads);
        gate.SetResult("old");
        Assert.Null(await old);
        Assert.Equal(0, slot.CachedCount);
        Assert.Equal("new", await slot.ReadAsync("C:"));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task InvalidationClearsPreviouslyCachedLetters()
    {
        int reads = 0;
        var slot = new DriveMetadataSlot<string>(_ => Task.FromResult((++reads).ToString()));
        Assert.Equal("1", await slot.ReadAsync("C:"));
        Assert.Equal("2", await slot.ReadAsync("D:"));
        slot.Invalidate();
        Assert.Equal(0, slot.CachedCount);
        Assert.Equal("3", await slot.ReadAsync("C:"));
    }

    [Theory]
    [InlineData(0x0219, 0x0007L, true)]
    [InlineData(0x0219, 0x8000L, true)]
    [InlineData(0x0219, 0x8004L, true)]
    [InlineData(0x0219, 0x8001L, false)]
    [InlineData(0x0218, 0x0007L, true)]
    [InlineData(0x0218, 0x0012L, true)]
    [InlineData(0x0218, 0x0004L, false)]
    [InlineData(0x000F, 0x8000L, false)]
    public void OnlyTopologyOrResumeBroadcastsInvalidate(int msg, long change, bool expected) =>
        Assert.Equal(expected, OptimizerWpf.Views.HomeView.IsDiskTopologyMessage(msg, change));

    [Fact]
    public async Task CacheExpiresAndCannotGrowBeyondAlphabet()
    {
        long clock = 0;
        int reads = 0;
        var slot = new DriveMetadataSlot<string>(key => { reads++; return Task.FromResult(key); }, () => clock);
        for (char letter = 'A'; letter <= 'Z'; letter++)
            Assert.Equal(letter + ":", await slot.ReadAsync(letter + ":"));
        Assert.Equal(26, slot.CachedCount);
        for (int i = 0; i < 1000; i++) Assert.Null(await slot.ReadAsync("invalid-" + i));
        Assert.Equal(26, reads);
        clock = 119999;
        Assert.Equal("C:", await slot.ReadAsync("C:"));
        Assert.Equal(26, reads);
        clock = 120000;
        Assert.Equal("C:", await slot.ReadAsync("C:"));
        Assert.Equal(27, reads);
        Assert.Equal(26, slot.CachedCount);
    }
}
