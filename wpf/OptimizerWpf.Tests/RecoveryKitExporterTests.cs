using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OptimizerWpf.Services;

namespace OptimizerWpf.Tests;

public sealed class RecoveryKitExporterTests
{
    [Fact]
    public void ExportContainsOnlyRequiredAssetsAndReplacesOldZipAfterValidation()
    {
        var root = Directory.CreateTempSubdirectory("gw-kit-");
        try
        {
            string target = Path.Combine(root.FullName, "kit.zip");
            File.WriteAllText(target, "previous");
            foreach (string name in new[] { "gearwin_clone.py", "partclone_adapter.py", "README.md", "ignored.txt" })
                File.WriteAllText(Path.Combine(root.FullName, name), "fixture " + name);
            RecoveryKitExporter.Export(root.FullName, target);
            using var zip = ZipFile.OpenRead(target);
            Assert.Equal(new[] { "README.md", "gearwin_clone.py", "partclone_adapter.py" },
                zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            using var reader = new StreamReader(zip.GetEntry("README.md")!.Open());
            Assert.Equal("fixture README.md", reader.ReadToEnd());
            Assert.Empty(Directory.GetFiles(root.FullName, ".gearwin-kit-*.tmp"));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void MissingAssetDoesNotTruncateExistingDestination()
    {
        var root = Directory.CreateTempSubdirectory("gw-kit-");
        try
        {
            string target = Path.Combine(root.FullName, "kit.zip");
            File.WriteAllText(target, "previous valid destination");
            File.WriteAllText(Path.Combine(root.FullName, "gearwin_clone.py"), "fixture");
            Assert.Throws<IOException>(() => RecoveryKitExporter.Export(root.FullName, target));
            Assert.Equal("previous valid destination", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(root.FullName, ".gearwin-kit-*.tmp"));
        }
        finally { root.Delete(true); }
    }
}
