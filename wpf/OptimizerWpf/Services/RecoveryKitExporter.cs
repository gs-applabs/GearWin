using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OptimizerWpf.Services;

// Export the existing kit; never execute or modify the recovery scripts.
internal static class RecoveryKitExporter
{
    private static readonly string[] Assets = { "gearwin_clone.py", "partclone_adapter.py", "README.md" };
    private const long MaxAssetBytes = 8 * 1024 * 1024;

    internal static void Export(string sourceFolder, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a .zip destination.");
        var sources = Assets.Select(name => Path.Combine(sourceFolder, name)).ToArray();
        foreach (var source in sources)
        {
            var info = new FileInfo(source);
            if (!info.Exists || info.Length == 0 || info.Length > MaxAssetBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Required recovery asset is missing or invalid: " + info.Name);
        }
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Invalid ZIP destination.");
        var stage = Path.Combine(parent, ".gearwin-kit-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool ownedStage = false;
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                ownedStage = true;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var buffer = new byte[32768];
                    for (int i = 0; i < Assets.Length; i++)
                    {
                        using var input = new FileStream(sources[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (input.Length <= 0 || input.Length > MaxAssetBytes)
                            throw new IOException("Recovery asset changed: " + Assets[i]);
                        using var entry = zip.CreateEntry(Assets[i], CompressionLevel.Optimal).Open();
                        long total = 0;
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            total += count;
                            if (total > MaxAssetBytes) throw new IOException("Recovery asset exceeds its limit.");
                            entry.Write(buffer, 0, count);
                        }
                        if (total != input.Length || total == 0) throw new IOException("Recovery asset changed during export.");
                    }
                }
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                using var verify = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                if (verify.Entries.Count != Assets.Length ||
                    Assets.Any(name => verify.GetEntry(name) is not { Length: > 0 }))
                    throw new IOException("Recovery kit ZIP validation failed.");
                foreach (var entry in verify.Entries)
                {
                    using var content = entry.Open();
                    content.CopyTo(Stream.Null);
                }
            }
            File.Move(stage, destination, overwrite: true);
            ownedStage = false;
        }
        finally
        {
            if (ownedStage) try { File.Delete(stage); } catch { }
        }
    }
}
