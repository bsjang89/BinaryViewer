using System.Text;

namespace BinaryViewer.Core;

public sealed record Signature(string Name, byte[] Magic, int Offset = 0, string Ext = "")
{
    public bool MatchesAt(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + Magic.Length > data.Length) return false;
        for (int i = 0; i < Magic.Length; i++)
            if (data[pos + i] != Magic[i]) return false;
        return true;
    }
}

public static class Signatures
{
    private static byte[] A(string s) => Encoding.ASCII.GetBytes(s);
    private static byte[] H(params int[] b) => Array.ConvertAll(b, x => (byte)x);

    /// <summary>Magic numbers anchored at a fixed offset in the file.</summary>
    public static readonly Signature[] All =
    [
        new("PNG image",                H(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), 0, ".png"),
        new("JPEG image",               H(0xFF, 0xD8, 0xFF), 0, ".jpg"),
        new("GIF image (87a)",          A("GIF87a"), 0, ".gif"),
        new("GIF image (89a)",          A("GIF89a"), 0, ".gif"),
        new("BMP image",                A("BM"), 0, ".bmp"),
        new("TIFF (little endian)",     H(0x49, 0x49, 0x2A, 0x00), 0, ".tif"),
        new("TIFF (big endian)",        H(0x4D, 0x4D, 0x00, 0x2A), 0, ".tif"),
        new("WebP/RIFF container",      A("RIFF"), 0, ".riff"),
        new("ICO/CUR icon",             H(0x00, 0x00, 0x01, 0x00), 0, ".ico"),
        new("PSD (Photoshop)",          A("8BPS"), 0, ".psd"),

        new("ZIP / OOXML / JAR / APK",  H(0x50, 0x4B, 0x03, 0x04), 0, ".zip"),
        new("ZIP (empty archive)",      H(0x50, 0x4B, 0x05, 0x06), 0, ".zip"),
        new("ZIP (spanned)",            H(0x50, 0x4B, 0x07, 0x08), 0, ".zip"),
        new("GZIP",                     H(0x1F, 0x8B), 0, ".gz"),
        new("BZIP2",                    A("BZh"), 0, ".bz2"),
        new("XZ",                       H(0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00), 0, ".xz"),
        new("7-Zip",                    H(0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C), 0, ".7z"),
        new("RAR v1.5-4.0",             H(0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00), 0, ".rar"),
        new("RAR v5.0+",                H(0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00), 0, ".rar"),
        new("ZSTD",                     H(0x28, 0xB5, 0x2F, 0xFD), 0, ".zst"),
        new("LZ4 frame",                H(0x04, 0x22, 0x4D, 0x18), 0, ".lz4"),
        new("CAB (MS Cabinet)",         A("MSCF"), 0, ".cab"),
        new("TAR archive",              A("ustar"), 257, ".tar"),

        new("PDF document",             A("%PDF-"), 0, ".pdf"),
        new("RTF document",             A("{\rtf"), 0, ".rtf"),
        new("OLE2 / MS Office (CFB)",   H(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1), 0, ".doc"),
        new("SQLite 3 database",        A("SQLite format 3\0"), 0, ".db"),
        new("Windows shortcut (LNK)",   H(0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00), 0, ".lnk"),
        new("Windows registry hive",    A("regf"), 0, ".hive"),
        new("Windows registry export",  A("Windows Registry Editor"), 0, ".reg"),
        new("Event log (EVTX)",         A("ElfFile\0"), 0, ".evtx"),
        new("Prefetch (SCCA)",          A("SCCA"), 4, ".pf"),

        new("PE / DOS executable (MZ)", A("MZ"), 0, ".exe"),
        new("ELF executable",           H(0x7F, 0x45, 0x4C, 0x46), 0, ".elf"),
        new("Mach-O 64 (LE)",           H(0xCF, 0xFA, 0xED, 0xFE), 0, ""),
        new("Mach-O 32 (LE)",           H(0xCE, 0xFA, 0xED, 0xFE), 0, ""),
        new("Java class",               H(0xCA, 0xFE, 0xBA, 0xBE), 0, ".class"),
        new("WebAssembly",              H(0x00, 0x61, 0x73, 0x6D), 0, ".wasm"),
        new(".NET/Windows PDB",         A("Microsoft C/C++ MSF"), 0, ".pdb"),
        new("Portable PDB",             A("BSJB"), 0, ".pdb"),

        new("MP4/MOV (ftyp)",           A("ftyp"), 4, ".mp4"),
        new("Matroska / WebM",          H(0x1A, 0x45, 0xDF, 0xA3), 0, ".mkv"),
        new("OGG media",                A("OggS"), 0, ".ogg"),
        new("FLAC audio",               A("fLaC"), 0, ".flac"),
        new("MP3 (ID3 tag)",            A("ID3"), 0, ".mp3"),
        new("ASF/WMV",                  H(0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11), 0, ".wmv"),

        new("PCAP capture (LE)",        H(0xD4, 0xC3, 0xB2, 0xA1), 0, ".pcap"),
        new("PCAP capture (BE)",        H(0xA1, 0xB2, 0xC3, 0xD4), 0, ".pcap"),
        new("PCAPNG capture",           H(0x0A, 0x0D, 0x0D, 0x0A), 0, ".pcapng"),


        new("UTF-8 BOM",                H(0xEF, 0xBB, 0xBF), 0, ".txt"),
        new("UTF-16 LE BOM",            H(0xFF, 0xFE), 0, ".txt"),
        new("UTF-16 BE BOM",            H(0xFE, 0xFF), 0, ".txt"),
        new("UTF-32 LE BOM",            H(0xFF, 0xFE, 0x00, 0x00), 0, ".txt"),
    ];

    /// <summary>Signatures worth carving for anywhere inside the file (long enough to be meaningful).</summary>
    public static readonly Signature[] Carvable =
        All.Where(s => s.Offset == 0 && s.Magic.Length >= 4
                    && s.Name is not ("UTF-32 LE BOM" or "ICO/CUR icon")).ToArray();
}
