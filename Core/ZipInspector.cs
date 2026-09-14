using System.Buffers.Binary;
using System.Text;

namespace BinaryViewer.Core;

public sealed record ZipEntry(
    string Name, long Offset, long CompressedSize, long UncompressedSize,
    int Method, int Flags, DateTime? Modified, string Encryption)
{
    public bool Encrypted => (Flags & 1) != 0;
    public string MethodName => Method switch
    {
        0 => "store",
        8 => "deflate",
        9 => "deflate64",
        12 => "bzip2",
        14 => "lzma",
        93 => "zstd",
        99 => "AES",
        _ => Method.ToString()
    };
}

public sealed class ZipInfo
{
    public bool IsZip;
    public bool Zip64;
    public List<ZipEntry> Entries = [];
    public string Comment = "";
    public bool AllEncrypted => Entries.Count > 0 && Entries.TrueForAll(e => e.Encrypted);
    public bool AnyEncrypted => Entries.Exists(e => e.Encrypted);
}

/// <summary>
/// Reads a ZIP central directory straight off the byte source, so the entry list, sizes and
/// timestamps are visible even when every entry inside is encrypted.
/// </summary>
public static class ZipInspector
{
    private const int EocdSig = 0x06054B50;
    private const int Eocd64Sig = 0x06064B50;
    private const int CenSig = 0x02014B50;

    public static ZipInfo Inspect(IByteSource src)
    {
        var info = new ZipInfo();
        if (src.Length < 22) return info;

        var head = new byte[4];
        src.Read(0, head, 0, 4);
        uint local = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (local is not (0x04034B50 or 0x06054B50 or 0x08074B50 or 0x02014B50)) return info;
        info.IsZip = true;

        // End of central directory lives in the last 64 KB (comment can push it back)
        int tailLen = (int)Math.Min(65 * 1024, src.Length);
        var tail = new byte[tailLen];
        long tailStart = src.Length - tailLen;
        src.Read(tailStart, tail, 0, tailLen);

        int eocd = LastIndexOfSig(tail, EocdSig);
        if (eocd < 0) return info;

        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
        int entryCount = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10));
        int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 20));
        if (commentLen > 0 && eocd + 22 + commentLen <= tailLen)
            info.Comment = Encoding.UTF8.GetString(tail, eocd + 22, commentLen).Trim();

        int eocd64 = LastIndexOfSig(tail, Eocd64Sig);
        if (eocd64 >= 0 && eocd64 + 56 <= tailLen)
        {
            info.Zip64 = true;
            entryCount = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(eocd64 + 32)));
            cdSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(eocd64 + 40));
            cdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(eocd64 + 48));
        }

        if (cdOffset < 0 || cdOffset >= src.Length || cdSize <= 0) return info;
        cdSize = Math.Min(cdSize, src.Length - cdOffset);

        var cd = new byte[cdSize];
        src.Read(cdOffset, cd, 0, (int)cdSize);

        int pos = 0;
        while (pos + 46 <= cd.Length && info.Entries.Count < 20000)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos)) != CenSig) break;

            int flags = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 8));
            int method = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 10));
            int dosTime = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 12));
            int dosDate = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 14));
            long csize = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 20));
            long usize = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 24));
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 28));
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 30));
            int cmtLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(pos + 32));
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(pos + 42));

            if (pos + 46 + nameLen > cd.Length) break;
            var enc = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.ASCII;
            string name = enc.GetString(cd, pos + 46, nameLen);

            var extra = cd.AsSpan(pos + 46 + nameLen, Math.Min(extraLen, cd.Length - pos - 46 - nameLen));
            string encryption = ReadExtra(extra, ref usize, ref csize, ref offset, method, flags);

            info.Entries.Add(new ZipEntry(name, offset, csize, usize, method, flags,
                                          DosTime(dosDate, dosTime), encryption));
            pos += 46 + nameLen + extraLen + cmtLen;
        }

        if (entryCount > 0 && info.Entries.Count == 0) info.Entries.Capacity = entryCount;
        return info;
    }

    /// <summary>Reads the ZIP64 sizes and the WinZip AES descriptor out of an entry's extra field.</summary>
    private static string ReadExtra(ReadOnlySpan<byte> extra, ref long usize, ref long csize,
                                    ref long offset, int method, int flags)
    {
        string encryption = (flags & 1) != 0 ? S.ZipEncrypted : "";
        int p = 0;
        while (p + 4 <= extra.Length)
        {
            int id = BinaryPrimitives.ReadUInt16LittleEndian(extra[p..]);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(p + 2)..]);
            if (p + 4 + size > extra.Length) break;
            var body = extra.Slice(p + 4, size);

            switch (id)
            {
                case 0x0001:                                    // ZIP64 extended information
                {
                    int q = 0;
                    if (usize == 0xFFFFFFFF && q + 8 <= body.Length) { usize = (long)BinaryPrimitives.ReadUInt64LittleEndian(body[q..]); q += 8; }
                    if (csize == 0xFFFFFFFF && q + 8 <= body.Length) { csize = (long)BinaryPrimitives.ReadUInt64LittleEndian(body[q..]); q += 8; }
                    if (offset == 0xFFFFFFFF && q + 8 <= body.Length) offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(body[q..]);
                    break;
                }
                case 0x9901 when body.Length >= 7:              // WinZip AES
                {
                    int version = BinaryPrimitives.ReadUInt16LittleEndian(body);
                    int strength = body[4];
                    int realMethod = BinaryPrimitives.ReadUInt16LittleEndian(body[5..]);
                    string bits = strength switch { 1 => "AES-128", 2 => "AES-192", 3 => "AES-256", _ => $"AES-?{strength}" };
                    string real = realMethod switch { 0 => "store", 8 => "deflate", _ => realMethod.ToString() };
                    encryption = S.ZipAes(bits, version, real);
                    break;
                }
            }
            p += 4 + size;
        }

        if (encryption == S.ZipEncrypted && method != 99)
            encryption = S.ZipLegacyCrypto;
        return encryption;
    }

    private static DateTime? DosTime(int date, int time)
    {
        if (date == 0) return null;
        try
        {
            return new DateTime(1980 + (date >> 9), (date >> 5) & 0xF, date & 0x1F,
                                time >> 11, (time >> 5) & 0x3F, (time & 0x1F) * 2);
        }
        catch { return null; }
    }

    private static int LastIndexOfSig(byte[] buf, int sig)
    {
        for (int i = buf.Length - 4; i >= 0; i--)
            if (BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(i)) == sig) return i;
        return -1;
    }

    public static void AppendReport(StringBuilder sb, ZipInfo zip, long fileLength)
    {
        if (!zip.IsZip) return;

        sb.AppendLine();
        sb.AppendLine(S.ZipHeader(zip.Zip64, zip.Entries.Count));
        if (zip.Comment.Length > 0) sb.AppendLine(S.ZipComment + zip.Comment);

        if (zip.Entries.Count == 0)
        {
            sb.AppendLine(S.ZipUnreadable);
            return;
        }

        foreach (var e in zip.Entries)
        {
            string when = e.Modified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            string enc = e.Encryption.Length > 0 ? $"  [{e.Encryption}]" : "";
            sb.AppendLine($"  0x{e.Offset:X8}  {e.UncompressedSize,12:N0} -> {e.CompressedSize,12:N0} B  " +
                          $"{e.MethodName,-8} {when}  {e.Name}{enc}");
        }

        if (zip.AllEncrypted)
        {
            sb.AppendLine();
            sb.AppendLine(S.ZipAllEncryptedNote1);
            sb.AppendLine(S.ZipAllEncryptedNote2);
        }
        else if (zip.AnyEncrypted)
            sb.AppendLine();
    }
}
