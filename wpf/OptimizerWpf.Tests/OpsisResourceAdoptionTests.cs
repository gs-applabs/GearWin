using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OptimizerWpf.Tests;

public class OpsisResourceAdoptionTests
{
    [Fact]
    public void OpsisResourceDictionaryIsPackagedInWpfAssembly()
    {
        var assembly = typeof(OptimizerWpf.App).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new System.Resources.ResourceReader(stream);
        var keys = reader.GetEnumerator();
        bool found = false;
        while (keys.MoveNext())
            if (string.Equals(keys.Key as string, "themes/opsisui6midnight.baml", StringComparison.OrdinalIgnoreCase))
                found = true;
        Assert.True(found);
    }
}
