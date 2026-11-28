using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

internal static class CatalogPackage
{
    internal const long MaxBytes = 1024L * 1024 * 1024;
    internal const int BufferBytes = 128 * 1024;
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dl.delivery.mp.microsoft.com", "download.windowsupdate.com",
        "catalog.s.download.windowsupdate.com", "www.download.windowsupdate.com"
    };
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    internal static Uri? Normalize(string? value, bool fromCatalog = false)
    {
        if (value == null || value.Length > 4096 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !Hosts.Contains(uri.IdnHost) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort) return null;
        if (uri.Scheme == "http" && fromCatalog)
            uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
        if (uri.Scheme != "https" ||
            !uri.AbsolutePath.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)) return null;
        return uri;
    }

    internal static async Task<string> CopyHashAsync(Stream input, Stream output, long? expected,
        long limit, CancellationToken ct)
    {
        if (limit <= 0 || expected is <= 0 || expected > limit) throw new InvalidDataException("Invalid package size.");
        var buffer = new byte[BufferBytes];
        long total = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (read > limit - total || (expected.HasValue && read > expected.Value - total))
                throw new InvalidDataException("Package exceeded its allowed size.");
            total += read;
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        if (total == 0 || (expected.HasValue && total != expected.Value))
            throw new InvalidDataException("Empty or incomplete package.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // Cap the bytes actually received, including responses without Content-Length.
    // Only the small, bounded HTML response is buffered; CABs use streaming.
    internal static async Task<string> ReadBoundedHtmlAsync(Stream input, int limit,
        Encoding encoding, CancellationToken ct)
    {
        if (limit <= 0 || limit > 2 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limit));
        ct.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - output.Length + 1)), ct)
                .ConfigureAwait(false);
            if (read == 0) break;
            if (read > limit - output.Length) throw new InvalidDataException("Catalog HTML exceeds its byte limit.");
            output.Write(buffer, 0, read);
        }
        ct.ThrowIfCancellationRequested();
        output.Position = 0;
        using var reader = new StreamReader(output, encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static async Task<string> DownloadAsync(Uri uri, string target, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        for (int hop = 0; hop <= 3; hop++)
        {
            uri = Normalize(uri.AbsoluteUri) ?? throw new InvalidDataException("Untrusted package URL.");
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (hop == 3 || response.Headers.Location == null)
                    throw new IOException("Package redirect limit reached.");
                uri = new Uri(uri, response.Headers.Location);
                continue; // validate new URI BEFORE the next request
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentEncoding.Count != 0)
                throw new InvalidDataException("Unexpected package encoding.");
            var length = response.Content.Headers.ContentLength;
            if (length is <= 0 || length > MaxBytes) throw new InvalidDataException("Invalid package size.");
            using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            string hash = await CopyHashAsync(input, output, length, MaxBytes, deadline.Token).ConfigureAwait(false);
            output.Flush(true);
            return hash;
        }
        throw new IOException("Package redirect limit reached.");
    }

    // Conservative INF manufacturer/model parser. Only exact hardware IDs
    // on declared model-section rows qualify; no substring search or wildcard.
    // Architecture/driver ranking is still enforced by Windows PnPUtil.
    internal static bool MatchesInf(string text, string hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId) || hardwareId.Length > 512 ||
            hardwareId.Any(c => char.IsControl(c) || c is ',' or '"' or ';') ||
            text.Length > 1024 * 1024) return false;
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Split(';', 2)[0].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var name = line[1..^1].Trim();
                if (sections.ContainsKey(name)) return false;
                sections[name] = current = new();
            }
            else current?.Add(line);
        }
        if (!sections.TryGetValue("Manufacturer", out var manufacturers)) return false;
        foreach (var row in manufacturers)
        {
            int eq = row.IndexOf('=');
            if (eq < 0) continue;
            var parts = row[(eq + 1)..].Split(',').Select(s => s.Trim().Trim('"')).ToArray();
            if (parts.Length == 0 || parts[0].Length == 0) continue;
            var names = parts.Length == 1 ? new[] { parts[0] } : parts.Skip(1)
                .Where(x => x.Length != 0).Select(x => parts[0] + "." + x).ToArray();
            foreach (string name in names)
            {
                if (!sections.TryGetValue(name, out var models)) continue;
                foreach (string model in models)
                {
                    int split = model.IndexOf('=');
                    if (split < 0) continue;
                    var fields = model[(split + 1)..].Split(',');
                    if (fields.Length >= 2 && fields.Skip(1).Any(x =>
                        string.Equals(x.Trim().Trim('"'), hardwareId, StringComparison.OrdinalIgnoreCase))) return true;
                }
            }
        }
        return false;
    }

    // Conservative structural preflight based on MS-CAB (CFHEADER, CFFOLDER,
    // CFFILE and CFDATA). This does not decompress or verify checksums; Windows
    // remains responsible for decoding. Signature and post-extraction checks stay mandatory.
    internal static void ValidateCabinet(Stream stream, long maxExpanded = 2 * MaxBytes)
    {
        if (!stream.CanSeek || stream.Position != 0 || stream.Length < 36 ||
            stream.Length > MaxBytes || maxExpanded <= 0 || maxExpanded > 2 * MaxBytes)
            throw new InvalidDataException("Invalid CAB size or stream.");
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        long end = stream.Length;
        void Need(long count)
        {
            if (count < 0 || stream.Position > end || count > end - stream.Position)
                throw new InvalidDataException("Truncated CAB structure.");
        }
        void Skip(long count) { Need(count); stream.Position += count; }
        if (reader.ReadUInt32() != 0x4643534d) throw new InvalidDataException("Not a CAB.");
        Skip(4);
        long cabinetSize = reader.ReadUInt32();
        if (cabinetSize < 36 || cabinetSize > end) throw new InvalidDataException("Invalid CAB declared size.");
        end = cabinetSize; // signed CABs may have a trailing signature
        Skip(4);
        long fileTable = reader.ReadUInt32();
        Skip(4);
        int minor = reader.ReadByte(), major = reader.ReadByte();
        int folderCount = reader.ReadUInt16(), fileCount = reader.ReadUInt16(), flags = reader.ReadUInt16();
        Skip(2);
        int cabinetIndex = reader.ReadUInt16();
        if (minor != 3 || major != 1 || folderCount is < 1 or > 256 ||
            fileCount is < 1 or > 4096 || (flags & ~4) != 0 || cabinetIndex != 0)
            throw new InvalidDataException("Unsupported CAB layout or spanning.");
        int folderReserve = 0, dataReserve = 0;
        if ((flags & 4) != 0)
        {
            Need(4);
            int headerReserve = reader.ReadUInt16();
            folderReserve = reader.ReadByte();
            dataReserve = reader.ReadByte();
            Skip(headerReserve);
        }
        var folders = new (long Offset, int Blocks, int Compression)[folderCount];
        for (int i = 0; i < folderCount; i++)
        {
            Need(8);
            folders[i] = (reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16());
            if ((folders[i].Compression & 15) > 3) throw new InvalidDataException("Unsupported CAB compression.");
            Skip(folderReserve);
        }
        if (fileTable < stream.Position || fileTable > end) throw new InvalidDataException("Invalid CAB file table.");
        stream.Position = fileTable;
        var entries = new List<(int Folder, long Offset, long Size)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;
        for (int i = 0; i < fileCount; i++)
        {
            Need(16);
            long size = reader.ReadUInt32(), offset = reader.ReadUInt32();
            int folder = reader.ReadUInt16();
            Skip(6);
            if (folder >= folderCount || size > maxExpanded - declaredTotal)
                throw new InvalidDataException("CAB file quota or folder exceeded.");
            declaredTotal += size;
            var name = new List<byte>();
            while (true)
            {
                Need(1);
                byte b = reader.ReadByte();
                if (b == 0) break;
                if (name.Count >= 240 || b < 32 || b >= 127)
                    throw new InvalidDataException("Unsupported CAB filename.");
                name.Add(b);
            }
            string path = Encoding.ASCII.GetString(name.ToArray()).Replace('/', '\\');
            if (!SafeCabinetName(path) || !names.Add(path) || !leaves.Add(path.Split('\\')[^1]))
                throw new InvalidDataException("Unsafe or ambiguous CAB filename.");
            entries.Add((folder, offset, size));
        }
        foreach (var name in names)
        {
            int slash = name.IndexOf('\\');
            while (slash >= 0)
            {
                if (names.Contains(name[..slash])) throw new InvalidDataException("Conflicting CAB paths.");
                slash = name.IndexOf('\\', slash + 1);
            }
        }
        long tableEnd = stream.Position, previousEnd = tableEnd, expanded = 0;
        var sizes = new long[folderCount];
        foreach (int i in Enumerable.Range(0, folderCount).OrderBy(i => folders[i].Offset))
        {
            var f = folders[i];
            if (f.Offset < previousEnd || f.Offset > end) throw new InvalidDataException("Overlapping CAB folder data.");
            stream.Position = f.Offset;
            for (int j = 0; j < f.Blocks; j++)
            {
                Need(8);
                Skip(4);
                int packed = reader.ReadUInt16(), unpacked = reader.ReadUInt16();
                if (packed == 0 || unpacked is < 1 or > 32768 || unpacked > maxExpanded - expanded ||
                    ((f.Compression & 15) == 0 && packed != unpacked))
                    throw new InvalidDataException("Invalid CAB block or expanded quota exceeded.");
                expanded += unpacked;
                sizes[i] += unpacked;
                Skip(dataReserve + packed);
            }
            previousEnd = stream.Position;
        }
        foreach (var entry in entries)
            if (entry.Offset > sizes[entry.Folder] || entry.Size > sizes[entry.Folder] - entry.Offset)
                throw new InvalidDataException("CAB file exceeds folder data.");
    }

    internal static bool SafeCabinetName(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240) return false;
        foreach (string part in path.Split('\\', '/'))
        {
            if (part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || c >= 127 || c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
                return false;
            string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                    stem[3] is >= '0' and <= '9')) return false;
        }
        return true;
    }

    internal static string? SelectInf(string directory, string hardwareId)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        int entries = 0;
        long bytes = 0;
        while (pending.Count != 0)
        {
            var dir = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                if (++entries > 4096 || !MaintenancePaths.IsOrdinaryLocalPath(path))
                    throw new IOException("Unexpected extracted package content.");
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                long length = new FileInfo(path).Length;
                if (length < 0 || length > 2 * MaxBytes - bytes) throw new IOException("Extracted package too large.");
                bytes += length;
                if (!path.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) continue;
                if (length > 1024 * 1024) throw new IOException("INF exceeds size limit.");
                using var input = new StreamReader(path, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
                if (MatchesInf(input.ReadToEnd(), hardwareId)) files.Add(path);
            }
        }
        return files.Count == 1 ? files[0] : null;
    }
}
