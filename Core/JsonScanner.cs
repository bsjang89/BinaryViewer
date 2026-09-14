using System.Text;
using System.Text.Json;

namespace BinaryViewer.Core;

public sealed record JsonHit(long Offset, long Length, char Kind, bool Utf16, int Depth, string Preview)
{
    public string Encoding => Utf16 ? "UTF-16LE" : "UTF-8";
    public string KindName => Kind == '{' ? "object" : "array";
}

/// <summary>
/// Sequentially scans a file for regions that parse as strictly valid JSON.
/// Every '{' / '[' starts a candidate parse that aborts at the first byte violating the grammar,
/// so junk costs only a few bytes of work while real JSON is validated end to end.
/// </summary>
public sealed class JsonScanner
{
    /// <summary>Candidates shorter than this are ignored (binary noise produces many tiny "[]" hits).</summary>
    public int MinLength { get; set; } = 24;

    /// <summary>Also detect JSON stored as UTF-16 little endian (common in Windows apps).</summary>
    public bool ScanUtf16 { get; set; } = true;

    /// <summary>Upper bound on one candidate, guards against runaway parses.</summary>
    public long MaxCandidateLength { get; set; } = 256L * 1024 * 1024;

    public int MaxHits { get; set; } = 50_000;

    public List<JsonHit> Scan(IByteSource src, long from, long to,
                              IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var hits = new List<JsonHit>();
        long len = src.Length;
        to = Math.Min(to, len);

        var scan = new SourceReader(src);
        var parser = new Parser(new SourceReader(src));

        long pos = from;
        long nextReport = from;

        while (pos < to && hits.Count < MaxHits)
        {
            if (pos >= nextReport)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(to > from ? (double)(pos - from) / (to - from) : 1);
                nextReport = pos + (1 << 20);
            }

            int b = scan.At(pos);
            if (b != '{' && b != '[')
            {
                if (b < 0) break;
                pos++;
                continue;
            }

            long limit = Math.Min(len, pos + MaxCandidateLength);
            bool utf16 = ScanUtf16 && scan.At(pos + 1) == 0;

            if (parser.TryParse(pos, limit, utf16 ? 2 : 1, out long end, out int depth) &&
                end - pos >= MinLength && !parser.TopLevelEmpty)
            {
                hits.Add(new JsonHit(pos, end - pos, (char)b, utf16, depth,
                                     Preview(src, pos, end - pos, utf16)));
                pos = end;
                continue;
            }

            // A UTF-16 guess that failed may still be valid UTF-8 JSON, so retry once.
            if (utf16 && parser.TryParse(pos, limit, 1, out end, out depth) &&
                end - pos >= MinLength && !parser.TopLevelEmpty)
            {
                hits.Add(new JsonHit(pos, end - pos, (char)b, false, depth,
                                     Preview(src, pos, end - pos, false)));
                pos = end;
                continue;
            }

            pos++;
        }

        progress?.Report(1);
        return hits;
    }

    public static string ReadText(IByteSource src, long offset, long length, bool utf16)
    {
        int n = (int)Math.Min(length, 32L * 1024 * 1024);
        var buf = new byte[n];
        src.Read(offset, buf, 0, n);
        return utf16 ? Encoding.Unicode.GetString(buf) : new UTF8Encoding(false).GetString(buf);
    }

    public static string PrettyPrint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                MaxDepth = 512,
                CommentHandling = JsonCommentHandling.Skip
            });
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return json;
        }
    }

    private static string Preview(IByteSource src, long offset, long length, bool utf16)
    {
        string s = ReadText(src, offset, Math.Min(length, utf16 ? 600 : 300), utf16);
        var sb = new StringBuilder(s.Length);
        bool lastWs = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWs) sb.Append(' ');
                lastWs = true;
            }
            else
            {
                sb.Append(char.IsControl(c) ? '.' : c);
                lastWs = false;
            }
        }
        string outp = sb.ToString().Trim();
        return outp.Length > 200 ? outp[..200] + " ..." : outp;
    }

    /// <summary>Strict JSON recogniser over a byte source. Reusable, allocation free per attempt.</summary>
    private sealed class Parser(SourceReader reader)
    {
        private const int MaxDepth = 256;

        private readonly SourceReader _r = reader;
        private long _pos;
        private long _limit;
        private int _step;          // 1 = UTF-8 / ASCII, 2 = UTF-16 LE
        private int _maxDepth;

        public bool TopLevelEmpty { get; private set; }

        public bool TryParse(long start, long limit, int step, out long end, out int depth)
        {
            _pos = start;
            _limit = limit;
            _step = step;
            _maxDepth = 0;
            TopLevelEmpty = false;
            end = start;
            depth = 0;

            int c = Peek();
            bool ok = c == '{' ? ParseObject(1, true) : c == '[' && ParseArray(1, true);
            if (!ok) return false;

            end = _pos;
            depth = _maxDepth;
            return true;
        }

        /// <summary>Current code unit, or -1 past the limit.</summary>
        private int Peek()
        {
            if (_pos + _step > _limit) return -1;
            int b0 = _r.At(_pos);
            if (b0 < 0) return -1;
            if (_step == 1) return b0;
            int b1 = _r.At(_pos + 1);
            return b1 < 0 ? -1 : b0 | (b1 << 8);
        }

        private void Next() => _pos += _step;

        private void SkipWs()
        {
            while (true)
            {
                int c = Peek();
                if (c is ' ' or '\t' or '\n' or '\r') Next();
                else return;
            }
        }

        private bool ParseValue(int depth)
        {
            if (depth > MaxDepth) return false;
            if (depth > _maxDepth) _maxDepth = depth;

            SkipWs();
            int c = Peek();
            switch (c)
            {
                case '{': return ParseObject(depth, false);
                case '[': return ParseArray(depth, false);
                case '"': return ParseString();
                case 't': return Literal("true");
                case 'f': return Literal("false");
                case 'n': return Literal("null");
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                    return false;
            }
        }

        private bool ParseObject(int depth, bool top)
        {
            if (depth > MaxDepth) return false;
            if (depth > _maxDepth) _maxDepth = depth;

            Next();                                  // consume '{'
            SkipWs();
            if (Peek() == '}')
            {
                Next();
                if (top) TopLevelEmpty = true;
                return true;
            }

            while (true)
            {
                SkipWs();
                if (Peek() != '"' || !ParseString()) return false;
                SkipWs();
                if (Peek() != ':') return false;
                Next();
                if (!ParseValue(depth + 1)) return false;
                SkipWs();
                int c = Peek();
                if (c == ',') { Next(); continue; }
                if (c == '}') { Next(); return true; }
                return false;
            }
        }

        private bool ParseArray(int depth, bool top)
        {
            if (depth > MaxDepth) return false;
            if (depth > _maxDepth) _maxDepth = depth;

            Next();                                  // consume '['
            SkipWs();
            if (Peek() == ']')
            {
                Next();
                if (top) TopLevelEmpty = true;
                return true;
            }

            while (true)
            {
                if (!ParseValue(depth + 1)) return false;
                SkipWs();
                int c = Peek();
                if (c == ',') { Next(); continue; }
                if (c == ']') { Next(); return true; }
                return false;
            }
        }

        private bool ParseString()
        {
            Next();                                  // consume opening quote
            while (true)
            {
                int c = Peek();
                if (c < 0) return false;
                if (c == '"') { Next(); return true; }

                if (c == '\\')
                {
                    Next();
                    int e = Peek();
                    switch (e)
                    {
                        case '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't':
                            Next();
                            break;
                        case 'u':
                            Next();
                            for (int i = 0; i < 4; i++)
                            {
                                if (!IsHex(Peek())) return false;
                                Next();
                            }
                            break;
                        default:
                            return false;
                    }
                    continue;
                }

                if (c < 0x20) return false;          // raw control characters are illegal in JSON strings
                Next();
            }
        }

        private static bool IsHex(int c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private bool ParseNumber()
        {
            if (Peek() == '-') Next();

            int c = Peek();
            if (c == '0') Next();
            else if (c >= '1' && c <= '9') { while (IsDigit(Peek())) Next(); }
            else return false;

            if (Peek() == '.')
            {
                Next();
                if (!IsDigit(Peek())) return false;
                while (IsDigit(Peek())) Next();
            }

            c = Peek();
            if (c is 'e' or 'E')
            {
                Next();
                c = Peek();
                if (c is '+' or '-') Next();
                if (!IsDigit(Peek())) return false;
                while (IsDigit(Peek())) Next();
            }
            return true;
        }

        private static bool IsDigit(int c) => c >= '0' && c <= '9';

        private bool Literal(string word)
        {
            foreach (char w in word)
            {
                if (Peek() != w) return false;
                Next();
            }
            return true;
        }
    }
}
