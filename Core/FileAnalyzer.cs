using System.Buffers.Binary;
using System.Text;

namespace BinaryViewer.Core;

public sealed record CarveHit(long Offset, string Name);

public sealed record FieldGuess(int Offset, string Type, string Value, string Note);

public sealed class AnalysisResult
{
    public string Path = "";
    public long Length;
    public List<string> HeaderMatches = [];
    public List<CarveHit> Carved = [];
    public List<FieldGuess> Fields = [];
    public double HeaderEntropy;
    public double OverallEntropy;
    public double PrintableRatio;
    public double ZeroRatio;
    public string TextEncodingGuess = "";
    public ZipInfo Zip = new();
    public byte[] Head = [];
    public string Report = "";
}

public static class FileAnalyzer
{
    public const int HeadSize = 512;

    public static AnalysisResult Analyze(IByteSource src, string path, bool carve = true,
                                         CancellationToken ct = default)
    {
        var r = new AnalysisResult { Path = path, Length = src.Length };

        var head = new byte[(int)Math.Min(HeadSize, src.Length)];
        src.Read(0, head, 0, head.Length);
        r.Head = head;

        // 1) magic numbers anchored at fixed offsets
        foreach (var sig in Signatures.All)
        {
            var probe = new byte[sig.Magic.Length];
            if (sig.Offset + probe.Length > src.Length) continue;
            src.Read(sig.Offset, probe, 0, probe.Length);
            if (!probe.AsSpan().SequenceEqual(sig.Magic)) continue;

            string ext = sig.Ext.Length > 0 ? $" ({sig.Ext})" : "";
            r.HeaderMatches.Add(sig.Offset == 0 ? sig.Name + ext : $"{sig.Name} @0x{sig.Offset:X}");
        }

        // 2) statistics
        r.HeaderEntropy = Entropy(head);
        var (overall, printable, zero) = SampleStats(src, ct);
        r.OverallEntropy = overall;
        r.PrintableRatio = printable;
        r.ZeroRatio = zero;
        r.TextEncodingGuess = GuessEncoding(head, printable);

        // 3) plausible header fields in the first 64 bytes
        r.Fields = GuessFields(head, src.Length);

        // 4) container structure: a ZIP central directory stays readable even when entries are encrypted
        r.Zip = ZipInspector.Inspect(src);

        // 5) embedded signatures elsewhere in the file
        if (carve) r.Carved = Carve(src, ct);

        r.Report = BuildReport(r);
        return r;
    }

    private static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return 0;
        Span<int> hist = stackalloc int[256];
        foreach (byte b in data) hist[b]++;
        double e = 0, n = data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (hist[i] == 0) continue;
            double p = hist[i] / n;
            e -= p * Math.Log2(p);
        }
        return e;
    }

    /// <summary>Whole-file histogram, sampled in 1 MB chunks for very large files.</summary>
    private static (double entropy, double printable, double zero) SampleStats(IByteSource src, CancellationToken ct)
    {
        long len = src.Length;
        if (len == 0) return (0, 0, 0);

        const int Chunk = 1 << 20;
        long step = len <= 64L * 1024 * 1024 ? Chunk : Math.Max(Chunk, len / 64 / Chunk * Chunk);
        var buf = new byte[Chunk];
        var hist = new long[256];
        long total = 0;

        for (long pos = 0; pos < len; pos += step)
        {
            ct.ThrowIfCancellationRequested();
            int n = src.Read(pos, buf, 0, Chunk);
            if (n <= 0) break;
            for (int i = 0; i < n; i++) hist[buf[i]]++;
            total += n;
        }
        if (total == 0) return (0, 0, 0);

        double e = 0;
        long printable = 0;
        for (int i = 0; i < 256; i++)
        {
            if (hist[i] == 0) continue;
            double p = (double)hist[i] / total;
            e -= p * Math.Log2(p);
            if (i is (>= 0x20 and < 0x7F) or 0x09 or 0x0A or 0x0D) printable += hist[i];
        }
        return (e, (double)printable / total, (double)hist[0] / total);
    }

    private static string GuessEncoding(ReadOnlySpan<byte> head, double printable)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return S.EncUtf8Bom;
        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xFE && head[2] == 0 && head[3] == 0) return S.EncUtf32Le;
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE) return S.EncUtf16Le;
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF) return S.EncUtf16Be;

        int evenZero = 0, oddZero = 0, pairs = 0;
        for (int i = 0; i + 1 < Math.Min(head.Length, 256); i += 2)
        {
            pairs++;
            if (head[i] == 0) evenZero++;
            if (head[i + 1] == 0) oddZero++;
        }
        if (pairs > 8 && oddZero > pairs * 0.8 && evenZero < pairs * 0.2) return S.EncUtf16LeNoBom;
        if (pairs > 8 && evenZero > pairs * 0.8 && oddZero < pairs * 0.2) return S.EncUtf16BeNoBom;

        if (printable > 0.95) return IsValidUtf8(head) ? S.EncTextUtf8 : S.EncTextAscii;
        return S.EncBinary;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            int extra = b < 0x80 ? 0 : (b & 0xE0) == 0xC0 ? 1 : (b & 0xF0) == 0xE0 ? 2 : (b & 0xF8) == 0xF0 ? 3 : -1;
            if (extra < 0) return false;
            if (i + extra >= data.Length) return true;   // truncated at the sample edge: do not penalise
            for (int k = 1; k <= extra; k++)
                if ((data[i + k] & 0xC0) != 0x80) return false;
            i += extra + 1;
        }
        return true;
    }

    /// <summary>
    /// Interprets the first 64 bytes as integers and flags values that look like header fields:
    /// file size, remaining length, offsets pointing inside the file, timestamps, ASCII tags.
    /// </summary>
    private static List<FieldGuess> GuessFields(byte[] head, long fileLen)
    {
        var list = new List<FieldGuess>();
        int limit = Math.Min(head.Length, 64);

        for (int off = 0; off + 4 <= limit; off += 4)
        {
            var span = head.AsSpan(off, 4);
            uint le = BinaryPrimitives.ReadUInt32LittleEndian(span);
            uint be = BinaryPrimitives.ReadUInt32BigEndian(span);

            if (IsAsciiTag(span))
            {
                list.Add(new FieldGuess(off, "ascii", Encoding.ASCII.GetString(span), S.FieldAsciiTag));
                continue;                                  // a printable tag is not a number field
            }

            AddIfInteresting(list, off, "u32 LE", le, fileLen);
            if (be != le) AddIfInteresting(list, off, "u32 BE", be, fileLen);
        }
        return list;
    }

    private static void AddIfInteresting(List<FieldGuess> list, int off, string type, uint v, long fileLen)
    {
        string? note = null;
        if (v == fileLen) note = S.FieldTotalSize;
        else if (v == fileLen - off - 4) note = S.FieldRemaining;
        else if (v == fileLen - 8) note = S.FieldSizeMinus8;
        else if (v is >= 0x30000000 and <= 0x7FFFFFFF)
            note = S.FieldUnixTime($"{DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime:yyyy-MM-dd HH:mm:ss}");
        else if (v >= 8 && v < fileLen && fileLen > 64 && v > (uint)off) note = S.FieldOffset;

        if (note != null)
            list.Add(new FieldGuess(off, type, $"0x{v:X8} ({v})", note));
    }

    private static bool IsAsciiTag(ReadOnlySpan<byte> s)
    {
        foreach (byte b in s)
            if (b is < 0x20 or > 0x7E) return false;
        return true;
    }

    private static List<CarveHit> Carve(IByteSource src, CancellationToken ct)
    {
        var hits = new List<CarveHit>();
        var byFirst = new List<Signature>?[256];
        foreach (var sig in Signatures.Carvable)
            (byFirst[sig.Magic[0]] ??= []).Add(sig);

        const int Chunk = 1 << 20;
        const int Overlap = 16;
        var buf = new byte[Chunk + Overlap];
        long pos = 0;

        while (pos < src.Length && hits.Count < 2000)
        {
            ct.ThrowIfCancellationRequested();
            int n = src.Read(pos, buf, 0, buf.Length);
            if (n <= 0) break;
            int scanTo = pos + n >= src.Length ? n : n - Overlap;

            for (int i = 0; i < scanTo; i++)
            {
                var cands = byFirst[buf[i]];
                if (cands == null) continue;
                foreach (var sig in cands)
                {
                    if (!sig.MatchesAt(buf.AsSpan(0, n), i)) continue;
                    if (pos + i != 0) hits.Add(new CarveHit(pos + i, sig.Name));   // offset 0 is the header itself
                    break;
                }
            }
            pos += scanTo;
        }
        return hits;
    }

    private static string BuildReport(AnalysisResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{S.RepFile}: {r.Path}");
        sb.AppendLine($"{S.RepSize}: {r.Length:N0} bytes ({Human(r.Length)})");
        sb.AppendLine();

        sb.AppendLine(S.RepSignatures);
        if (r.HeaderMatches.Count == 0)
            sb.AppendLine(S.RepNoSignature);
        else
            foreach (var m in r.HeaderMatches) sb.AppendLine("  * " + m);
        sb.AppendLine();

        sb.AppendLine(S.RepStats);
        sb.AppendLine($"{S.RepHeaderEntropy(r.Head.Length)}{r.HeaderEntropy:F2} / 8.00");
        sb.AppendLine($"{S.RepOverallEntropy}{r.OverallEntropy:F2} / 8.00  {EntropyNote(r.OverallEntropy)}");
        sb.AppendLine($"{S.RepPrintable}{r.PrintableRatio:P1}");
        sb.AppendLine($"{S.RepZeros}{r.ZeroRatio:P1}");
        sb.AppendLine($"{S.RepEncoding}{r.TextEncodingGuess}");
        ZipInspector.AppendReport(sb, r.Zip, r.Length);
        sb.AppendLine();

        sb.AppendLine(S.RepFields);
        if (r.Fields.Count == 0)
            sb.AppendLine(S.RepNoFields);
        else
            foreach (var f in r.Fields)
                sb.AppendLine($"  +0x{f.Offset:X2}  {f.Type,-7} {f.Value,-22} {f.Note}");
        sb.AppendLine();

        sb.AppendLine(S.RepCarved);
        if (r.Carved.Count == 0)
            sb.AppendLine(S.RepNone);
        else
            foreach (var g in r.Carved.GroupBy(c => c.Name).OrderByDescending(g => g.Count()))
            {
                var first = g.Take(6).Select(c => $"0x{c.Offset:X}");
                string more = g.Count() > 6 ? " ..." : "";
                sb.AppendLine($"  {g.Key} x{g.Count()} : {string.Join(", ", first)}{more}");
            }

        return sb.ToString();
    }

    private static string EntropyNote(double e) => e switch
    {
        > 7.8 => S.EntropyPacked,
        > 6.5 => S.EntropyMixedHigh,
        > 4.5 => S.EntropyMixed,
        _ => S.EntropyStructured
    };

    public static string Human(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
