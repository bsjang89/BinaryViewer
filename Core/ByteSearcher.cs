using System.Text;

namespace BinaryViewer.Core;

/// <summary>Boyer-Moore-Horspool search over an <see cref="IByteSource"/>, chunked with overlap.</summary>
public static class ByteSearcher
{
    public static long Find(IByteSource src, byte[] pattern, long startAt, bool ignoreCase,
                            CancellationToken ct = default)
    {
        if (pattern.Length == 0 || src.Length == 0) return -1;
        if (startAt < 0) startAt = 0;

        byte[] pat = ignoreCase ? Lower(pattern) : pattern;
        var skip = new int[256];
        for (int i = 0; i < 256; i++) skip[i] = pat.Length;
        for (int i = 0; i < pat.Length - 1; i++) skip[pat[i]] = pat.Length - 1 - i;

        const int Chunk = 1 << 20;
        int overlap = pat.Length - 1;
        var buf = new byte[Chunk + overlap];
        long pos = startAt;

        while (pos < src.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = src.Read(pos, buf, 0, buf.Length);
            if (n < pat.Length) return -1;
            if (ignoreCase) LowerInPlace(buf, n);

            int i = 0;
            while (i + pat.Length <= n)
            {
                int j = pat.Length - 1;
                while (j >= 0 && buf[i + j] == pat[j]) j--;
                if (j < 0) return pos + i;
                i += skip[buf[i + pat.Length - 1]];
            }
            pos += n - overlap;
        }
        return -1;
    }

    public static long FindBackward(IByteSource src, byte[] pattern, long before, bool ignoreCase,
                                    CancellationToken ct = default)
    {
        if (pattern.Length == 0) return -1;
        const int Window = 1 << 20;
        long end = Math.Min(before + pattern.Length - 1, src.Length);

        while (end > 0)
        {
            ct.ThrowIfCancellationRequested();
            long start = Math.Max(0, end - Window);
            long found = -1, from = start;
            while (true)
            {
                long hit = Find(src, pattern, from, ignoreCase, ct);
                if (hit < 0 || hit + pattern.Length > end || hit >= before) break;
                found = hit;
                from = hit + 1;
            }
            if (found >= 0) return found;
            if (start == 0) break;
            end = start + pattern.Length - 1;
        }
        return -1;
    }

    /// <summary>Parses "50 4B 03 04", "504B0304" or "\x50\x4b" into bytes. Returns null when malformed.</summary>
    public static byte[]? ParseHex(string text)
    {
        var clean = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c is ',' or '-') continue;
            if (c == '\\' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X')) { i++; continue; }
            if (c == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X') && clean.Length % 2 == 0) { i++; continue; }
            if (!Uri.IsHexDigit(c)) return null;
            clean.Append(c);
        }
        if (clean.Length == 0 || clean.Length % 2 != 0) return null;

        var result = new byte[clean.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(clean.ToString(i * 2, 2), 16);
        return result;
    }

    private static byte[] Lower(byte[] src)
    {
        var d = new byte[src.Length];
        for (int i = 0; i < src.Length; i++) d[i] = ToLower(src[i]);
        return d;
    }

    private static void LowerInPlace(byte[] buf, int n)
    {
        for (int i = 0; i < n; i++) buf[i] = ToLower(buf[i]);
    }

    private static byte ToLower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
}
