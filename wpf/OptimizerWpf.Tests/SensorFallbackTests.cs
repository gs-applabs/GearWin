using System;
using System.Buffers.Binary;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class SensorFallbackTests
{
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-273.0, false)]
    [InlineData(45.0, true)]
    [InlineData(150.0, true)]
    [InlineData(151.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void InvalidSensorValuesAreNotDisplayed(double value, bool expected)
        => Assert.Equal(expected, SensorService.IsValidTemperature(value));

    [Theory]
    [InlineData("DIMM #0", true)]
    [InlineData("Composite Temperature", true)]
    [InlineData("Warning Temperature", false)]
    [InlineData("Critical Temperature", false)]
    [InlineData("Thermal Sensor High Limit", false)]
    [InlineData("Temperature Sensor Resolution", false)]
    public void LiveSensorIsNotConfusedWithLimit(string name, bool expected)
        => Assert.Equal(expected, SensorService.IsLiveTemperatureName(name));

    private static byte[] Descriptor(short temperature, ushort index = 0)
    {
        var bytes = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), index);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), temperature);
        return bytes;
    }

    [Theory]
    [InlineData(42, 42)]
    [InlineData(0, null)]
    [InlineData(-32768, null)]
    [InlineData(999, null)]
    public void NativeDescriptorReturnsOnlyValidCompositeTemperature(int temperature, int? expected)
        => Assert.Equal(expected, StorageTemperatureService.ParseDescriptor(Descriptor((short)temperature)));

    [Fact]
    public void NativeDescriptorRejectsNonCompositeAndTruncatedData()
    {
        Assert.Null(StorageTemperatureService.ParseDescriptor(Descriptor(44, 1)));
        Assert.Null(StorageTemperatureService.ParseDescriptor(Descriptor(44).AsSpan(0, 27)));
        var malformed = Descriptor(44);
        BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(12), 99);
        Assert.Null(StorageTemperatureService.ParseDescriptor(malformed));
    }

    [Fact]
    public void VolumeExtentMappingRejectsMultipleDisksAndShortBuffers()
    {
        var extents = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(extents, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(extents.AsSpan(8), 3);
        Assert.Equal(3u, StorageTemperatureService.ParseDiskNumber(extents));
        Assert.Null(StorageTemperatureService.ParseDiskNumber(extents.AsSpan(0, 12)));
        BinaryPrimitives.WriteUInt32LittleEndian(extents, 2);
        Assert.Null(StorageTemperatureService.ParseDiskNumber(extents));
    }
}
