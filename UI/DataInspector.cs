using System.Buffers.Binary;
using System.Text;
using BinaryViewer.Core;

namespace BinaryViewer.UI;

/// <summary>Decodes the bytes at the caret as every common scalar type, LE and BE side by side.</summary>
public sealed class DataInspector : ListView
{
    private static readonly string[] Rows =
    [
        "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
        "float", "double", "bits", "char", "UTF-16", "unix time", "FILETIME"
    ];

    public DataInspector()
    {
        View = View.Details;
        FullRowSelect = true;
        GridLines = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        Columns.Add("형식", 78);
        Columns.Add("리틀 엔디언", 190);
        Columns.Add("빅 엔디언", 190);
        foreach (string r in Rows) Items.Add(new ListViewItem([r, "", ""]));
    }

    private delegate string Dec(ReadOnlySpan<byte> bytes);

    public void Update(IByteSource? src, long offset)
    {
        var b = new byte[16];
        int n = src == null ? 0 : src.Read(offset, b, 0, 16);

        string LE(int need, Dec f) => n >= need ? f(b) : "";
        string BE(int need, Dec f) => n >= need ? f(b) : "";

        BeginUpdate();
        Set(0, LE(1, s => ((sbyte)s[0]).ToString()), "");
        Set(1, LE(1, s => s[0].ToString()), "");
        Set(2, LE(2, s => BinaryPrimitives.ReadInt16LittleEndian(s).ToString()),
               BE(2, s => BinaryPrimitives.ReadInt16BigEndian(s).ToString()));
        Set(3, LE(2, s => BinaryPrimitives.ReadUInt16LittleEndian(s).ToString()),
               BE(2, s => BinaryPrimitives.ReadUInt16BigEndian(s).ToString()));
        Set(4, LE(4, s => BinaryPrimitives.ReadInt32LittleEndian(s).ToString()),
               BE(4, s => BinaryPrimitives.ReadInt32BigEndian(s).ToString()));
        Set(5, LE(4, s => $"{BinaryPrimitives.ReadUInt32LittleEndian(s)} (0x{BinaryPrimitives.ReadUInt32LittleEndian(s):X8})"),
               BE(4, s => $"{BinaryPrimitives.ReadUInt32BigEndian(s)} (0x{BinaryPrimitives.ReadUInt32BigEndian(s):X8})"));
        Set(6, LE(8, s => BinaryPrimitives.ReadInt64LittleEndian(s).ToString()),
               BE(8, s => BinaryPrimitives.ReadInt64BigEndian(s).ToString()));
        Set(7, LE(8, s => BinaryPrimitives.ReadUInt64LittleEndian(s).ToString()),
               BE(8, s => BinaryPrimitives.ReadUInt64BigEndian(s).ToString()));
        Set(8, LE(4, s => BinaryPrimitives.ReadSingleLittleEndian(s).ToString("G7")),
               BE(4, s => BinaryPrimitives.ReadSingleBigEndian(s).ToString("G7")));
        Set(9, LE(8, s => BinaryPrimitives.ReadDoubleLittleEndian(s).ToString("G15")),
               BE(8, s => BinaryPrimitives.ReadDoubleBigEndian(s).ToString("G15")));
        Set(10, LE(1, s => Convert.ToString(s[0], 2).PadLeft(8, '0')), "");
        Set(11, LE(1, s => s[0] is >= 0x20 and < 0x7F ? $"'{(char)s[0]}'" : $"0x{s[0]:X2}"), "");
        Set(12, LE(2, s => Printable(Encoding.Unicode.GetString(s[..2]))),
                BE(2, s => Printable(Encoding.BigEndianUnicode.GetString(s[..2]))));
        Set(13, LE(4, s => UnixTime(BinaryPrimitives.ReadUInt32LittleEndian(s))),
                BE(4, s => UnixTime(BinaryPrimitives.ReadUInt32BigEndian(s))));
        Set(14, LE(8, s => FileTime(BinaryPrimitives.ReadInt64LittleEndian(s))), "");
        EndUpdate();
    }

    private void Set(int row, string le, string be)
    {
        Items[row].SubItems[1].Text = le;
        Items[row].SubItems[2].Text = be;
    }

    private static string Printable(string s) =>
        s.Length > 0 && !char.IsControl(s[0]) ? $"'{s}'" : "";

    private static string UnixTime(uint v)
    {
        if (v is < 0x10000000 or > 0x7FFFFFFF) return "";
        return DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
    }

    private static string FileTime(long v)
    {
        if (v <= 0 || v > 2650467743999999999) return "";
        try { return DateTime.FromFileTimeUtc(v).ToString("yyyy-MM-dd HH:mm:ss") + "Z"; }
        catch { return ""; }
    }
}
