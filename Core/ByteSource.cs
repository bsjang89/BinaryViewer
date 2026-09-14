using System.IO.MemoryMappedFiles;

namespace BinaryViewer.Core;

/// <summary>Random-access byte provider. Implementations must be thread-safe.</summary>
public interface IByteSource : IDisposable
{
    long Length { get; }
    int Read(long offset, byte[] buffer, int bufferOffset, int count);
    byte ReadByte(long offset);
}

public sealed class MemoryByteSource : IByteSource
{
    private readonly byte[] _data;

    public MemoryByteSource(byte[] data) => _data = data;

    public long Length => _data.LongLength;

    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (offset < 0 || offset >= _data.LongLength) return 0;
        int n = (int)Math.Min(count, _data.LongLength - offset);
        Array.Copy(_data, offset, buffer, bufferOffset, n);
        return n;
    }

    public byte ReadByte(long offset) => _data[offset];

    public void Dispose() { }
}

/// <summary>Memory-mapped source with a sliding view window, for files too large to buffer.</summary>
public sealed class MappedByteSource : IByteSource
{
    private const long WindowSize = 32L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor? _view;
    private long _viewStart = -1;
    private long _viewLen;

    public long Length { get; }

    public MappedByteSource(string path)
    {
        var fi = new FileInfo(path);
        Length = fi.Length;
        _mmf = MemoryMappedFile.CreateFromFile(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
    }

    private void EnsureWindow(long offset)
    {
        if (_view != null && offset >= _viewStart && offset < _viewStart + _viewLen) return;

        _view?.Dispose();
        long start = offset / WindowSize * WindowSize;
        long len = Math.Min(WindowSize * 2, Length - start);
        _view = _mmf.CreateViewAccessor(start, len, MemoryMappedFileAccess.Read);
        _viewStart = start;
        _viewLen = len;
    }

    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (offset < 0 || offset >= Length) return 0;
        count = (int)Math.Min(count, Length - offset);

        lock (_gate)
        {
            int done = 0;
            while (done < count)
            {
                long cur = offset + done;
                EnsureWindow(cur);
                int n = (int)Math.Min(count - done, _viewStart + _viewLen - cur);
                _view!.ReadArray(cur - _viewStart, buffer, bufferOffset + done, n);
                done += n;
            }
            return count;
        }
    }

    public byte ReadByte(long offset)
    {
        lock (_gate)
        {
            EnsureWindow(offset);
            return _view!.ReadByte(offset - _viewStart);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _view?.Dispose();
            _view = null;
            _mmf.Dispose();
        }
    }
}

public static class ByteSourceFactory
{
    /// <summary>Files up to this size are slurped into RAM: fastest possible scrolling and scanning.</summary>
    public const long FullLoadLimit = 128L * 1024 * 1024;

    public static IByteSource Open(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException(path);
        if (fi.Length == 0) return new MemoryByteSource(Array.Empty<byte>());
        return fi.Length <= FullLoadLimit
            ? new MemoryByteSource(File.ReadAllBytes(path))
            : new MappedByteSource(path);
    }
}

/// <summary>Forward-only buffered reader over an <see cref="IByteSource"/>.</summary>
public sealed class SourceReader
{
    private readonly IByteSource _src;
    private readonly byte[] _buf;
    private long _bufStart = -1;
    private int _bufLen;

    public SourceReader(IByteSource src, int bufferSize = 1 << 20)
    {
        _src = src;
        _buf = new byte[bufferSize];
    }

    public long Length => _src.Length;

    /// <summary>Returns -1 past end of file.</summary>
    public int At(long offset)
    {
        if (offset < 0 || offset >= _src.Length) return -1;
        if (_bufStart < 0 || offset < _bufStart || offset >= _bufStart + _bufLen)
        {
            _bufStart = offset;
            _bufLen = _src.Read(offset, _buf, 0, _buf.Length);
            if (_bufLen <= 0) return -1;
        }
        return _buf[offset - _bufStart];
    }
}
