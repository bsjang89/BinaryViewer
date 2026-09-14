using System.Text;

namespace BinaryViewer.Core;

public sealed record StringHit(long Offset, int ByteLength, bool Utf16, string Text);

/// <summary>Extracts printable ASCII and UTF-16LE runs, like `strings` but offset-aware.</summary>
public static class StringScanner
{
    public static List<StringHit> Scan(IByteSource src, int minLength = 4, bool utf16 = true,
                                       int maxHits = 200_000,
                                       IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var hits = new List<StringHit>();
        long len = src.Length;
        const int Chunk = 1 << 20;
        const int Overlap = 1024;
        var buf = new byte[Chunk + Overlap];
        long pos = 0;

        while (pos < len && hits.Count < maxHits)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(len > 0 ? (double)pos / len : 1);

            int n = src.Read(pos, buf, 0, buf.Length);
            if (n <= 0) break;
            bool last = pos + n >= len;
            int scanTo = last ? n : n - Overlap;

            int runStart = -1;
            for (int i = 0; i < n; i++)
            {
                if (IsPrintable(buf[i]))
                {
                    if (runStart < 0) runStart = i;
                    continue;
                }
                TryEmitAscii(hits, buf, runStart, i, pos, minLength, scanTo);
                runStart = -1;
            }
            TryEmitAscii(hits, buf, runStart, n, pos, minLength, scanTo);

            if (utf16) ScanUtf16(hits, buf, n, pos, minLength, scanTo, maxHits);

            pos += scanTo;
        }

        progress?.Report(1);
        hits.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return hits;
    }

    private static void TryEmitAscii(List<StringHit> hits, byte[] buf, int start, int endExclusive,
                                     long basePos, int minLength, int scanTo)
    {
        if (start < 0) return;
        int length = endExclusive - start;
        if (length < minLength || start >= scanTo) return;
        hits.Add(new StringHit(basePos + start, length, false,
                               Encoding.ASCII.GetString(buf, start, Math.Min(length, 4096))));
    }

    private static void ScanUtf16(List<StringHit> hits, byte[] buf, int n, long basePos,
                                  int minLength, int scanTo, int maxHits)
    {
        for (int start = 0; start + 1 < n && hits.Count < maxHits; start++)
        {
            if (!IsPrintable(buf[start]) || buf[start + 1] != 0) continue;

            int i = start;
            var sb = new StringBuilder();
            while (i + 1 < n && IsPrintable(buf[i]) && buf[i + 1] == 0)
            {
                sb.Append((char)buf[i]);
                i += 2;
            }
            if (sb.Length >= minLength && start < scanTo)
                hits.Add(new StringHit(basePos + start, sb.Length * 2, true, sb.ToString()));
            start = i;                                   // skip past the run we consumed
        }
    }

    private static bool IsPrintable(byte b) => b is >= 0x20 and < 0x7F or 0x09;
}
