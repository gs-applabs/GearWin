using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class TrayLifetimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(512)]
    public void TooltipIsBoundedToLegacyNotifyIconLimit(int length)
    {
        string input = new('x', length);
        string output = TrayIconService.SafeTooltip(input);
        Assert.Equal(System.Math.Min(length, 63), output.Length);
        Assert.StartsWith(output, input);
    }

    [Fact]
    public void TooltipDoesNotSplitSurrogatePair()
    {
        string input = new string('x', 62) + "\U0001F600";
        Assert.Equal(new string('x', 62), TrayIconService.SafeTooltip(input));
    }
}
