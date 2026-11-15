using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

// Το ViVeTool τρέχει elevated και κατεβαίνει από URL που επιστρέφει το GitHub API: ο έλεγχος host πρέπει να είναι ακριβής
// (το παλιό EndsWith("github.com") δεχόταν και το "evilgithub.com").
public class ViveToolHostTests
{
    [Theory]
    [InlineData("github.com")]
    [InlineData("GitHub.com")]
    [InlineData("api.github.com")]
    [InlineData("objects.githubusercontent.com")]
    [InlineData("release-assets.githubusercontent.com")]
    [InlineData("githubusercontent.com")]
    public void IsGitHubHost_AcceptsGitHubHosts(string host)
    {
        Assert.True(ViveToolService.IsGitHubHost(host));
    }

    [Theory]
    [InlineData("evilgithub.com")]
    [InlineData("notgithub.com")]
    [InlineData("github.com.evil.example")]
    [InlineData("evilgithubusercontent.com")]
    [InlineData("example.com")]
    [InlineData("")]
    public void IsGitHubHost_RejectsLookalikes(string host)
    {
        Assert.False(ViveToolService.IsGitHubHost(host));
    }
}
