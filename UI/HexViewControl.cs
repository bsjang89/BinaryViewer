using System.ComponentModel;
using BinaryViewer.Core;

namespace BinaryViewer.UI;

public sealed record HighlightRange(long Offset, long Length, Color Color, string Tag = "");

/// <summary>
/// Virtualised hex/ASCII view: only the visible rows are ever read or drawn, so opening and
/// scrolling cost the same whether the file is 10 KB or 10 GB.
/// </summary>
public sealed class HexViewControl : Control
{
    public event EventHandler? CaretChanged;

    private IByteSource? _source;
    private int _bytesPerRow = 16;
    private bool _autoBytesPerRow = true;
    private long _topRow;
    private long _caret;
    private long _anchor;
    private long _scrollScale = 1;
    private int _xOffset;

    private readonly VScrollBar _vScroll = new() { Dock = DockStyle.Right };
    private readonly HScrollBar _hScroll = new() { Dock = DockStyle.Bottom, Visible = false };

    private byte[] _buf = [];
    private long _bufStart = -1;
    private int _bufLen;

    private int _charW = 8;
    private int _rowH = 15;
    private int _offsetDigits = 8;

    private readonly List<HighlightRange> _highlights = [];
    private char[] _hexLine = [];
    private char[] _asciiLine = [];

    private const int Pad = 6;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OffsetColor { get; set; } = Color.FromArgb(0x80, 0x80, 0x90);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HexColor { get; set; } = Color.FromArgb(0x1E, 0x1E, 0x1E);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AsciiColor { get; set; } = Color.FromArgb(0x1B, 0x5E, 0x20);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color NonPrintableColor { get; set; } = Color.FromArgb(0xB0, 0xB0, 0xB8);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HeaderColor { get; set; } = Color.FromArgb(0x55, 0x55, 0x66);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectionBack { get; set; } = Color.FromArgb(0xCC, 0xE0, 0xFF);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridColor { get; set; } = Color.FromArgb(0xE4, 0xE4, 0xEA);

    public HexViewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.White;
        Font = PickFont();
        TabStop = true;

        _vScroll.Scroll += (_, _) => { _topRow = (long)_vScroll.Value * _scrollScale; Invalidate(); };
        _hScroll.Scroll += (_, _) => { _xOffset = _hScroll.Value; Invalidate(); };
        Controls.Add(_vScroll);
        Controls.Add(_hScroll);
    }

    private static Font PickFont()
    {
        foreach (string name in new[] { "Cascadia Mono", "Consolas", "Courier New" })
        {
            var f = new Font(name, 9.75f, FontStyle.Regular, GraphicsUnit.Point);
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        return new Font(FontFamily.GenericMonospace, 9.75f);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IByteSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            _bufStart = -1;
            _bufLen = 0;
            _topRow = 0;
            _caret = _anchor = 0;
            _highlights.Clear();
            _offsetDigits = (value?.Length ?? 0) > 0xFFFFFFFFL ? 12 : 8;
            UpdateMetrics();
            Invalidate();
            CaretChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BytesPerRow
    {
        get => _bytesPerRow;
        set
        {
            _autoBytesPerRow = value <= 0;
            if (!_autoBytesPerRow) _bytesPerRow = Math.Max(1, value);
            UpdateMetrics();
            Invalidate();
        }
    }

    public long CaretOffset => _caret;
    public long SelectionStart => Math.Min(_caret, _anchor);
    public long SelectionEnd => Math.Max(_caret, _anchor);
    public long SelectionLength => _anchor == _caret ? 0 : SelectionEnd - SelectionStart + 1;
    public long Length => _source?.Length ?? 0;

    private long TotalRows => Length == 0 ? 1 : (Length + _bytesPerRow - 1) / _bytesPerRow;
    private int HeaderHeight => _rowH + 4;
    private int ViewHeight => Math.Max(0, ClientSize.Height - HeaderHeight - (_hScroll.Visible ? _hScroll.Height : 0));
    private int VisibleRows => Math.Max(1, ViewHeight / _rowH);

    private int HexX => Pad + (_offsetDigits + 2) * _charW;
    private int ByteX(int i) => HexX + (i * 3 + i / 8) * _charW;
    private int AsciiX => ByteX(_bytesPerRow) + _charW;
    private int ContentWidth => AsciiX + _bytesPerRow * _charW + Pad;

    public void SetHighlights(IEnumerable<HighlightRange> ranges)
    {
        _highlights.Clear();
        _highlights.AddRange(ranges);
        Invalidate();
    }

    public void ClearHighlights()
    {
        _highlights.Clear();
        Invalidate();
    }

    public void Select(long offset, long length, bool center = true)
    {
        if (Length == 0) return;
        offset = Math.Clamp(offset, 0, Length - 1);
        long end = Math.Clamp(offset + Math.Max(1, length) - 1, 0, Length - 1);
        _anchor = offset;
        _caret = end;
        EnsureVisible(offset, center);
        Invalidate();
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCaret(long offset, bool extend = false, bool center = false)
    {
        if (Length == 0) return;
        _caret = Math.Clamp(offset, 0, Length - 1);
        if (!extend) _anchor = _caret;
        EnsureVisible(_caret, center);
        Invalidate();
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureVisible(long offset, bool center = false)
    {
        long row = offset / _bytesPerRow;
        int vis = VisibleRows;
        if (center) _topRow = Math.Max(0, row - vis / 3);
        else if (row < _topRow) _topRow = row;
        else if (row >= _topRow + vis) _topRow = row - vis + 1;
        ClampTop();
        SyncScrollBar();
    }

    private void ClampTop()
    {
        long maxTop = Math.Max(0, TotalRows - VisibleRows);
        _topRow = Math.Clamp(_topRow, 0, maxTop);
    }

    private void UpdateMetrics()
    {
        using var g = CreateGraphics();
        var size = TextRenderer.MeasureText(g, "0000000000", Font, new Size(int.MaxValue, int.MaxValue),
                                            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        _charW = Math.Max(4, size.Width / 10);
        _rowH = Math.Max(10, size.Height + 1);

        if (_autoBytesPerRow)
        {
            int avail = ClientSize.Width - _vScroll.Width - Pad * 2 - (_offsetDigits + 2) * _charW;
            // each byte costs 3 chars of hex + 1 char of ASCII, plus a group gap every 8 bytes
            int perByte = 4 * _charW;
            int fit = Math.Max(8, (avail - 2 * _charW) / perByte);
            _bytesPerRow = Math.Max(8, fit / 8 * 8);
        }

        int cells = _bytesPerRow * 3 + _bytesPerRow / 8 + 1;
        if (_hexLine.Length != cells) _hexLine = new char[cells];
        if (_asciiLine.Length != _bytesPerRow) _asciiLine = new char[_bytesPerRow];

        SyncScrollBar();
    }

    private void SyncScrollBar()
    {
        long rows = TotalRows;
        _scrollScale = Math.Max(1, rows / int.MaxValue + 1);

        int max = (int)(rows / _scrollScale);
        int large = (int)Math.Max(1, VisibleRows / _scrollScale);
        _vScroll.Maximum = Math.Max(0, max + large - 1);
        _vScroll.LargeChange = large;
        _vScroll.SmallChange = 1;
        ClampTop();
        int val = (int)(_topRow / _scrollScale);
        _vScroll.Value = Math.Clamp(val, _vScroll.Minimum, Math.Max(_vScroll.Minimum, _vScroll.Maximum - large + 1));

        int content = ContentWidth;
        int viewW = ClientSize.Width - _vScroll.Width;
        bool needH = content > viewW;
        if (needH != _hScroll.Visible) _hScroll.Visible = needH;
        if (needH)
        {
            _hScroll.Maximum = content - viewW + _hScroll.LargeChange;
            _hScroll.Value = Math.Clamp(_xOffset, 0, Math.Max(0, _hScroll.Maximum - _hScroll.LargeChange + 1));
            _xOffset = _hScroll.Value;
        }
        else _xOffset = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateMetrics();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMetrics();
    }

    private void EnsureBuffer(long start, int count)
    {
        if (_bufStart >= 0 && start >= _bufStart && start + count <= _bufStart + _bufLen) return;
        if (_buf.Length < count) _buf = new byte[Math.Max(count, 64 * 1024)];
        _bufLen = _source!.Read(start, _buf, 0, count);
        _bufStart = start;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        DrawHeader(g);
        if (_source == null || Length == 0) return;

        long startOff = _topRow * _bytesPerRow;
        int rows = Math.Min(VisibleRows + 1, (int)(TotalRows - _topRow));
        int need = (int)Math.Min((long)rows * _bytesPerRow, Length - startOff);
        if (need <= 0) return;
        EnsureBuffer(startOff, need);

        long selStart = SelectionStart, selEnd = SelectionEnd;
        bool hasSel = _anchor != _caret;

        for (int r = 0; r < rows; r++)
        {
            long rowOff = startOff + (long)r * _bytesPerRow;
            if (rowOff >= Length) break;
            int n = (int)Math.Min(_bytesPerRow, Length - rowOff);
            int y = HeaderHeight + r * _rowH;

            // backgrounds first: text is drawn transparently on top
            foreach (var h in _highlights)
                PaintRange(g, rowOff, n, y, h.Offset, h.Offset + h.Length - 1, h.Color);
            if (hasSel) PaintRange(g, rowOff, n, y, selStart, selEnd, SelectionBack);

            TextRenderer.DrawText(g, rowOff.ToString("X" + _offsetDigits), Font,
                                  new Point(Pad - _xOffset, y), OffsetColor, Flags);

            Array.Fill(_hexLine, ' ');
            bool allPrintable = true;
            for (int i = 0; i < n; i++)
            {
                byte b = _buf[rowOff - _bufStart + i];
                int idx = i * 3 + i / 8;
                _hexLine[idx] = HexDigit(b >> 4);
                _hexLine[idx + 1] = HexDigit(b & 0xF);
                bool printable = b is >= 0x20 and < 0x7F;
                _asciiLine[i] = printable ? (char)b : '.';
                if (!printable) allPrintable = false;
            }

            TextRenderer.DrawText(g, new string(_hexLine, 0, _bytesPerRow * 3 + (_bytesPerRow - 1) / 8), Font,
                                  new Point(HexX - _xOffset, y), HexColor, Flags);
            TextRenderer.DrawText(g, new string(_asciiLine, 0, n), Font,
                                  new Point(AsciiX - _xOffset, y), allPrintable ? AsciiColor : NonPrintableColor, Flags);
        }

        DrawCaret(g, startOff, rows);
    }

    private void PaintRange(Graphics g, long rowOff, int n, int y, long from, long to, Color color)
    {
        long s = Math.Max(rowOff, from);
        long e = Math.Min(rowOff + n - 1, to);
        if (s > e) return;

        int i0 = (int)(s - rowOff), i1 = (int)(e - rowOff);
        using var brush = new SolidBrush(color);
        int x0 = ByteX(i0) - _xOffset;
        int x1 = ByteX(i1) + 2 * _charW - _xOffset;
        g.FillRectangle(brush, x0, y, x1 - x0, _rowH);
        g.FillRectangle(brush, AsciiX + i0 * _charW - _xOffset, y, (i1 - i0 + 1) * _charW, _rowH);
    }

    private void DrawCaret(Graphics g, long startOff, int rows)
    {
        long endOff = startOff + (long)rows * _bytesPerRow;
        if (_caret < startOff || _caret >= endOff) return;

        int r = (int)((_caret - startOff) / _bytesPerRow);
        int i = (int)(_caret % _bytesPerRow);
        int y = HeaderHeight + r * _rowH;
        using var pen = new Pen(Color.FromArgb(0x20, 0x6B, 0xD0));
        g.DrawRectangle(pen, ByteX(i) - _xOffset - 1, y, 2 * _charW + 1, _rowH - 1);
        g.DrawRectangle(pen, AsciiX + i * _charW - _xOffset - 1, y, _charW + 1, _rowH - 1);
    }

    private void DrawHeader(Graphics g)
    {
        const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using (var back = new SolidBrush(Color.FromArgb(0xF6, 0xF6, 0xF9)))
            g.FillRectangle(back, 0, 0, ClientSize.Width, HeaderHeight);
        using (var pen = new Pen(GridColor))
            g.DrawLine(pen, 0, HeaderHeight - 1, ClientSize.Width, HeaderHeight - 1);

        TextRenderer.DrawText(g, "Offset".PadRight(_offsetDigits), Font, new Point(Pad - _xOffset, 2), HeaderColor, F);
        for (int i = 0; i < _bytesPerRow; i++)
            TextRenderer.DrawText(g, i.ToString("X2"), Font, new Point(ByteX(i) - _xOffset, 2), HeaderColor, F);
        TextRenderer.DrawText(g, "ASCII", Font, new Point(AsciiX - _xOffset, 2), HeaderColor, F);
    }

    private static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        long? hit = HitTest(e.Location);
        if (hit.HasValue) SetCaret(hit.Value, (ModifierKeys & Keys.Shift) != 0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left) return;
        long? hit = HitTest(e.Location);
        if (hit.HasValue) SetCaret(hit.Value, extend: true);
    }

    private long? HitTest(Point p)
    {
        if (_source == null || Length == 0 || p.Y < HeaderHeight) return null;
        long row = _topRow + (p.Y - HeaderHeight) / _rowH;
        int x = p.X + _xOffset;

        int index;
        if (x >= AsciiX) index = (x - AsciiX) / _charW;
        else if (x >= HexX)
        {
            index = _bytesPerRow - 1;
            for (int i = 0; i < _bytesPerRow; i++)
                if (x < ByteX(i) + 3 * _charW) { index = i; break; }
        }
        else index = 0;

        index = Math.Clamp(index, 0, _bytesPerRow - 1);
        long off = row * _bytesPerRow + index;
        return off >= Length ? Length - 1 : off;
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_source == null || Length == 0) return;

        bool shift = e.Shift;
        bool ctrl = e.Control;
        long c = _caret;

        switch (e.KeyCode)
        {
            case Keys.Left: SetCaret(c - 1, shift); break;
            case Keys.Right: SetCaret(c + 1, shift); break;
            case Keys.Up: SetCaret(c - _bytesPerRow, shift); break;
            case Keys.Down: SetCaret(c + _bytesPerRow, shift); break;
            case Keys.PageUp: SetCaret(c - (long)VisibleRows * _bytesPerRow, shift); break;
            case Keys.PageDown: SetCaret(c + (long)VisibleRows * _bytesPerRow, shift); break;
            case Keys.Home: SetCaret(ctrl ? 0 : c - c % _bytesPerRow, shift); break;
            case Keys.End: SetCaret(ctrl ? Length - 1 : c - c % _bytesPerRow + _bytesPerRow - 1, shift); break;
            case Keys.A when ctrl: _anchor = 0; SetCaret(Length - 1, extend: true); break;
            case Keys.C when ctrl: CopySelection(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0) lines = 3;
        _topRow -= e.Delta / 120 * lines;
        ClampTop();
        SyncScrollBar();
        Invalidate();
    }

    public void CopySelection(bool asHex = true)
    {
        if (_source == null || Length == 0) return;
        long start = SelectionStart;
        long len = SelectionLength == 0 ? 1 : SelectionLength;
        len = Math.Min(len, 4 * 1024 * 1024);

        var data = new byte[len];
        _source.Read(start, data, 0, (int)len);
        string text = asHex
            ? string.Join(" ", data.Select(b => b.ToString("X2")))
            : new string(data.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.').ToArray());
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }

    public byte[] GetSelectedBytes(int max = 1 << 20)
    {
        if (_source == null || Length == 0) return [];
        long len = Math.Min(SelectionLength == 0 ? 1 : SelectionLength, max);
        var data = new byte[len];
        _source.Read(SelectionStart, data, 0, (int)len);
        return data;
    }
}
