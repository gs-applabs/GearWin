using System.Text;

namespace GearWin.Migration;

public static class BoundedText
{
    public static string ReadFile(string path, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        using var stream = File.OpenRead(path);
        if (!stream.CanSeek || stream.Length > maxBytes)
            throw new IOException($"Input exceeds the {maxBytes}-byte limit or is not seekable.");

        // Count actual bytes, even if a writer extends the file after Length.
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, (long)maxBytes - bytes.Length + 1))) != 0)
        {
            if (count > maxBytes - bytes.Length)
                throw new IOException($"Input exceeds the {maxBytes}-byte limit.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
