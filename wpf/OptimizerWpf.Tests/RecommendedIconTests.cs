using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public class RecommendedIconTests
{
    [Fact]
    public void EveryRecommendationHasAnUnambiguousPinnedAsset()
    {
        var apps = BloatwareService.RecommendedApps;
        Assert.Equal(29, apps.Count);
        var sources = apps.Select(a => LogoCacheService.SourceFor(a.WingetId)).ToArray();
        Assert.All(sources, s => Assert.NotNull(s));
        Assert.Equal(apps.Count, sources.Select(s => s!.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(apps.Count, sources.Select(s => s!.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(sources, s =>
        {
            Assert.Matches(@"^https://raw\.githubusercontent\.com/[^/]+/[^/]+/[0-9a-f]{40}/.+\.png$", s!.Url);
            if (s.Sha256 != null) Assert.Matches(@"^[a-f0-9]{64}$", s.Sha256);
        });
    }

    [Theory]
    [InlineData("Chrome","Google.Chrome")]
    [InlineData("Firefox","Mozilla.Firefox")]
    [InlineData("Opera","Opera.Opera")]
    [InlineData("OperaGX","Opera.OperaGX")]
    [InlineData("Vivaldi","VivaldiTechnologies.Vivaldi")]
    [InlineData("Edge","Microsoft.Edge")]
    [InlineData("Brave","BraveSoftware.BraveBrowser")]
    public void BrowserAliasesMatchTheCorrectProduct(string alias, string id)
        => Assert.Equal(id, LogoCacheService.SourceFor(alias)?.Id);

    [Fact]
    public async Task RequiredBuildContainsAllDecodableProductIcons()
    {
        // Release CI explicitly requires these resources, while offline developer
        // builds without the preparation step still have neutral placeholders.
        var required = Environment.GetEnvironmentVariable("GEARWIN_REQUIRE_RECOMMENDED_ICONS") == "1";
        foreach (var app in BloatwareService.RecommendedApps)
        {
            var icon = await LogoCacheService.GetLogoAsync(app.WingetId);
            if (!required && icon == null) continue;
            var bitmap = Assert.IsType<BitmapImage>(icon);
            Assert.True(bitmap.IsFrozen);
            Assert.InRange(bitmap.PixelWidth, 16, 96);
            Assert.InRange(bitmap.PixelHeight, 16, 96);
            Assert.Same(icon, await LogoCacheService.GetLogoAsync(app.WingetId));
        }
    }

    [Fact]
    public async Task UnknownPackageNeverUsesSomeoneElsesArtwork()
    {
        Assert.False(LogoCacheService.HasLogoSource("Unknown.Product"));
        Assert.Null(await LogoCacheService.GetLogoAsync("Unknown.Product"));
    }
}
