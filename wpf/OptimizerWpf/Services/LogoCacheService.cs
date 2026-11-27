using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OptimizerWpf.Services
{
    // Product-specific, build-time assets only. Old favicon cache is deliberately ignored:
    // neither publisher favicons nor user-writable files are authoritative product identities.
    public static class LogoCacheService
    {
        internal sealed record IconSource(string Id, string Name, string Url, string? Sha256);

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chrome"] = "Google.Chrome",
            ["Edge"] = "Microsoft.Edge",
            ["Brave"] = "BraveSoftware.BraveBrowser",
            ["Opera"] = "Opera.Opera",
            ["Firefox"] = "Mozilla.Firefox",
            ["OperaGX"] = "Opera.OperaGX",
            ["Vivaldi"] = "VivaldiTechnologies.Vivaldi"
        };

        private static readonly Lazy<Dictionary<string, IconSource>> Catalog = new(() =>
        {
            using var stream = typeof(LogoCacheService).Assembly.GetManifestResourceStream("recommended-icons.json");
            if (stream == null) return new(StringComparer.OrdinalIgnoreCase);
            var items = JsonSerializer.Deserialize<IconSource[]>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<IconSource>();
            return items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        });

        private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> Images =
            new(StringComparer.OrdinalIgnoreCase);

        internal static IconSource? SourceFor(string key)
        {
            if (Aliases.TryGetValue(key, out var id)) key = id;
            return Catalog.Value.TryGetValue(key, out var source) ? source : null;
        }

        public static bool HasLogoSource(string key) => SourceFor(key) != null;

        public static Task<ImageSource?> GetLogoAsync(string key)
        {
            var source = SourceFor(key);
            if (source == null) return Task.FromResult<ImageSource?>(null);
            return Images.GetOrAdd(source.Id, id => new Lazy<Task<ImageSource?>>(
                () => Task.Run(() => Decode(id)))).Value;
        }

        private static ImageSource? Decode(string id)
        {
            try
            {
                using var stream = typeof(LogoCacheService).Assembly.GetManifestResourceStream(
                    "recommended-icon." + id.ToLowerInvariant() + ".png");
                if (stream == null) return null; // development build without prepared assets
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.DecodePixelWidth = 64;
                bmp.EndInit();
                if (bmp.PixelWidth < 16 || bmp.PixelHeight < 16) return null;
                bmp.Freeze();
                return bmp;
            }
            catch { return null; } // neutral fallback, never substitute another product
        }
    }
}
