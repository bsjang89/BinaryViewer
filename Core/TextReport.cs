using System.Diagnostics;

namespace BinaryViewer.Core;

/// <summary>
/// The headless analysis output, shared by the Windows app's console mode and the cross platform CLI.
/// </summary>
public static class TextReport
{
    public static int Write(TextWriter output, TextWriter error, string path,
                            bool report, bool json, bool strings, int minStringLength = 6)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var src = ByteSourceFactory.Open(path);
            double openMs = sw.Elapsed.TotalMilliseconds;

            if (report)
            {
                var result = FileAnalyzer.Analyze(src, path);
                output.WriteLine(result.Report);
                output.WriteLine(S.ConsoleTiming(openMs, sw.Elapsed.TotalMilliseconds));
                output.WriteLine(HexDump(src, 0, (int)Math.Min(256, src.Length)));
            }

            if (json)
            {
                sw.Restart();
                var hits = new JsonScanner().Scan(src, 0, src.Length);
                output.WriteLine(S.ConsoleJson(hits.Count, sw.Elapsed.TotalMilliseconds));
                foreach (var h in hits.Take(50))
                    output.WriteLine($"  0x{h.Offset:X8}  {h.Length,10:N0} B  {h.KindName,-6} {h.Encoding,-8} depth {h.Depth,-3} {h.Preview}");
                if (hits.Count > 50) output.WriteLine(S.ConsoleMore(hits.Count - 50));
            }

            if (strings)
            {
                sw.Restart();
                var found = StringScanner.Scan(src, minStringLength);
                output.WriteLine(S.ConsoleStrings(found.Count, sw.Elapsed.TotalMilliseconds));
                foreach (var s in found.Take(40))
                    output.WriteLine($"  0x{s.Offset:X8}  {(s.Utf16 ? "UTF-16" : "ASCII "),-7} {s.Text}");
                if (found.Count > 40) output.WriteLine(S.ConsoleMore(found.Count - 40));
            }
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static string HexDump(IByteSource src, long offset, int count)
    {
        var buf = new byte[count];
        int n = src.Read(offset, buf, 0, count);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(S.ConsoleFirstBytes(n));
        for (int i = 0; i < n; i += 16)
        {
            sb.Append($"{offset + i:X8}  ");
            for (int j = 0; j < 16; j++)
            {
                sb.Append(i + j < n ? buf[i + j].ToString("X2") : "  ");
                sb.Append(j == 7 ? "  " : " ");
            }
            sb.Append(' ');
            for (int j = 0; j < 16 && i + j < n; j++)
            {
                byte b = buf[i + j];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
