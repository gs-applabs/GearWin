using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Shared bounded pipe reader. Drain excess without keeping it in memory;
// consumers must not parse a partial result as a complete inventory.
internal static class BoundedProcessOutput
{
    internal const int InventoryChars = 4 * 1024 * 1024;
    internal const int ErrorChars = 64 * 1024;

    internal static async Task<(string Text, bool Exceeded)> ReadAsync(
        TextReader reader, int maxChars, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);
        token.ThrowIfCancellationRequested();
        var buffer = new char[4096];
        var text = new StringBuilder(Math.Min(maxChars, buffer.Length));
        bool exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (count > maxChars - text.Length) exceeded = true;
            if (!exceeded) text.Append(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return (text.ToString(), exceeded);
    }
}
