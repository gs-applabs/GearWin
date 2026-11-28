using System;
using System.IO;
using System.Windows;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public sealed class ClipboardPrivacyTests
{
    [Theory]
    [InlineData("CanIncludeInClipboardHistory", 0, false)]
    [InlineData("CanIncludeInClipboardHistory", 1, true)]
    [InlineData("CanIncludeInClipboardHistory", 2, false)]
    [InlineData("CanUploadToCloudClipboard", 0, false)]
    [InlineData("CanUploadToCloudClipboard", 1, true)]
    public void PrivacyMarkerIsExplicitAndFailClosed(string marker, int value, bool expected)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "ordinary fixture");
        data.SetData(marker, new MemoryStream(BitConverter.GetBytes(value)));
        Assert.Equal(expected, ClipboardManagerService.MayCapture(data));
    }

    [Fact]
    public void ExclusionAndMalformedMarkerPreventCapture()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "fixture");
        Assert.True(ClipboardManagerService.MayCapture(data));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[1]));
        Assert.False(ClipboardManagerService.MayCapture(data));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        Assert.False(ClipboardManagerService.MayCapture(data));
    }

    [Theory]
    [InlineData(12u, 12u, true)]
    [InlineData(12u, 13u, false)]
    [InlineData(0u, 0u, false)]
    [InlineData(12u, 0u, false)]
    public void OnlyOwnedClipboardGenerationCanBeCleared(uint own, uint current, bool expected) =>
        Assert.Equal(expected, SecureClipboardService.ShouldClear(own, current));
}
