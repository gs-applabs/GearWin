using System.Text;

namespace GearWin.Migration;

public static class BoundedText
{
    public static string ReadFile(string path, int maxBytes)
    {
        using var stream = File.OpenRead(path);
        if (!stream.CanSeek || stream.Length > maxBytes)
            throw new IOException($"Input exceeds the {maxBytes}-byte limit or is not seekable.");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > maxBytes - result.Length)
                throw new IOException($"Input exceeds the {maxBytes}-character limit.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
