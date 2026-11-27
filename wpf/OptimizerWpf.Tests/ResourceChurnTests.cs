using System;
using System.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class ResourceChurnTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    [InlineData(10000, false)]
    public void GpuCounterAdmissionIsBounded(int count, bool accepted)
    {
        var selected = GpuInfoService.SelectEngineInstances(
            Enumerable.Range(0, count).Select(i => "pid_" + i + "_engtype_3D"));
        Assert.Equal(accepted, selected != null);
        if (accepted) Assert.Equal(count, selected!.Length);
    }

    [Fact]
    public void GpuCounterAdmissionDeduplicatesAndExcludesOtherEngines()
    {
        Assert.Equal(new[] { "pid_1_engtype_3D" }, GpuInfoService.SelectEngineInstances(
            new[] { "pid_1_engtype_3D", "PID_1_ENGTYPE_3D", "pid_2_engtype_Copy" }));
    }

    [Fact]
    public void InvalidGpuSamplesAreNotReportedAsIdle()
    {
        Assert.Null(GpuInfoService.AggregateUsage(Array.Empty<float>()));
        Assert.Null(GpuInfoService.AggregateUsage(new[] { float.NaN, float.PositiveInfinity, -1f }));
        Assert.Equal(0, GpuInfoService.AggregateUsage(new[] { 0f }));
        Assert.Equal(100, GpuInfoService.AggregateUsage(new[] { 80f, 60f }));
        Assert.Equal(15, GpuInfoService.AggregateUsage(new[] { 15f, float.NaN }));
    }

    [Fact]
    public void DuplicateStatusUpdatesDoNotPublishThousandsOfEvents()
    {
        using var scope = StatusService.BeginScope("unchanged status");
        int events = 0;
        void Observe(string message, bool busy) { events++; }
        StatusService.Changed += Observe;
        try
        {
            for (int i = 0; i < 10000; i++) scope.Update("unchanged status");
            Assert.Equal(0, events);
            scope.Update("different status");
            Assert.Equal(1, events);
            StatusService.Report("notice");
            scope.Update("different status"); // clears notice, so publish
            Assert.Equal(3, events);
        }
        finally { StatusService.Changed -= Observe; }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2048)]
    [InlineData(100000)]
    public void RetainedActionLogMessageHasSizeBound(int length)
    {
        var result = ActionLogService.BoundMessage(new string('x', length));
        Assert.Equal(Math.Min(length, ActionLogService.MaxMessageChars), result.Length);
        if (length > ActionLogService.MaxMessageChars) Assert.EndsWith("…", result);
    }
}
