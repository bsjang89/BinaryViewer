using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BinaryViewer.Core;

namespace BinaryViewer.Gui;

public sealed record HighlightRange(long Offset, long Length, Color Color);

/// <summary>
/// Virtualised hex/ASCII view. Only the visible rows are read from the source and drawn, so
/// scrolling costs the same on a 10 KB file and a 10 GB one.
/// Colours come from the theme dictionary, so it follows the light/dark switch.
/// </summary>
public sealed class HexView : Grid
{
    public event EventHandler? CaretChanged;

    private readonly Surface _surface;
    private readonly ScrollBar _scroll;
    private readonly ScrollBar _hScroll;
    private double _xOffset;

    private IByteSource? _source;
    private int _bytesPerRow = 16;
    private bool _autoBytesPerRow = true;
    private long _topRow;
    private long _caret;
    private long _anchor;

    private byte[] _buf = [];
    private long _bufStart = -1;
    private int _bufLen;

    private readonly Typeface _typeface;
    private const double FontSize = 12.5;
    private double _charW = 8;
    private double _rowH = 18;
    private int _offsetDigits = 8;

    private readonly List<HighlightRange> _highlights = [];
    private readonly char[] _hexChars = new char[3 * 64 + 8];
    private readonly char[] _zeroChars = new char[3 * 64 + 8];
    private readonly char[] _asciiChars = new char[64];
    private readonly char[] _asciiDimChars = new char[64];

    private const double Pad = 14;
    private const double RowGap = 4;

    private IBrush _surfaceBrush = Brushes.White;
    private IBrush _offsetBrush = Brushes.Gray;
    private IBrush _byteBrush = Brushes.Black;
    private IBrush _zeroBrush = Brushes.LightGray;
    private IBrush _asciiBrush = Brushes.Green;
    private IBrush _asciiDimBrush = Brushes.LightGray;
    private IBrush _selectionBrush = Brushes.LightBlue;
    private IBrush _groupTintBrush = Brushes.Transparent;
    private IBrush _headerBgBrush = Brushes.WhiteSmoke;
    private IBrush _hairlineBrush = Brushes.Gainsboro;
    private IPen _hairlinePen = new Pen(Brushes.Gainsboro);
    private IPen _caretPen = new Pen(Brushes.DodgerBlue, 1.5);

    public HexView()
    {
        _typeface = new Typeface(new FontFamily("SF Mono, Menlo, Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"));

        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("*,Auto");
        _surface = new Surface(this) { ClipToBounds = true, Focusable = true };
        _scroll = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 0,
            ViewportSize = 1,
            AllowAutoHide = false,
            Margin = new Thickness(0, 4, 2, 4)
        };
        _scroll.Scroll += (_, _) => { _topRow = (long)_scroll.Value; _surface.InvalidateVisual(); };

        // wide rows (32/48/64 bytes) do not fit a narrow pane, so the view scrolls sideways too
        _hScroll = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            Maximum = 0,
            ViewportSize = 1,
            AllowAutoHide = false,
            IsVisible = false,
            Margin = new Thickness(4, 0, 4, 2)
        };
        _hScroll.Scroll += (_, _) => { _xOffset = _hScroll.Value; _surface.InvalidateVisual(); };

        SetColumn(_surface, 0);
        SetColumn(_scroll, 1);
        SetRow(_hScroll, 1);
        SetColumn(_hScroll, 0);
        Children.Add(_surface);
        Children.Add(_scroll);
        Children.Add(_hScroll);

        MeasureFont();
        ActualThemeVariantChanged += (_, _) => ReloadBrushes();
        ReloadBrushes();
    }

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
            UpdateLayoutMetrics();
            _surface.InvalidateVisual();
            CaretChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Zero or less selects an automatic width that fills the viewport.</summary>
    public int BytesPerRow
    {
        get => _bytesPerRow;
        set
        {
            _autoBytesPerRow = value <= 0;
            if (!_autoBytesPerRow) _bytesPerRow = Math.Clamp(value, 1, 64);
            UpdateLayoutMetrics();
            _surface.InvalidateVisual();
        }
    }

    public long CaretOffset => _caret;
    public long SelectionStart => Math.Min(_caret, _anchor);
    public long SelectionEnd => Math.Max(_caret, _anchor);
    public long SelectionLength => _anchor == _caret ? 0 : SelectionEnd - SelectionStart + 1;
    public long Length => _source?.Length ?? 0;

    private long TotalRows => Length == 0 ? 1 : (Length + _bytesPerRow - 1) / _bytesPerRow;
    private double HeaderHeight => _rowH + 10;
    private int VisibleRows => Math.Max(1, (int)((_surface.Bounds.Height - HeaderHeight - RowGap) / _rowH));


    private double HexX => Pad + (_offsetDigits + 2) * _charW;
    private double ByteX(int i) => HexX + (i * 3 + i / 8 * 0.8) * _charW;
    private double AsciiX => ByteX(_bytesPerRow) + 1.4 * _charW;
    private double AsciiWidth => _bytesPerRow * _charW;
    private double ContentWidth => AsciiX + AsciiWidth + Pad;

    private void ReloadBrushes()
    {
        _surfaceBrush = ThemeBrush("SurfaceBgColor", Colors.White);
        _offsetBrush = ThemeBrush("HexOffsetColor", Color.FromRgb(0x9A, 0x9A, 0xA0));
        _byteBrush = ThemeBrush("HexByteColor", Color.FromRgb(0x1D, 0x1D, 0x1F));
        _zeroBrush = ThemeBrush("HexZeroColor", Color.FromRgb(0xC7, 0xC7, 0xCC));
        _asciiBrush = ThemeBrush("HexAsciiColor", Color.FromRgb(0x2A, 0x7D, 0x52));
        _asciiDimBrush = ThemeBrush("HexAsciiDimColor", Color.FromRgb(0xB0, 0xB0, 0xB8));
        _selectionBrush = ThemeBrush("HexSelectionColor", Color.FromRgb(0xCF, 0xE3, 0xFB));
        _groupTintBrush = ThemeBrush("HexGroupTintColor", Color.FromRgb(0xFA, 0xFA, 0xFC));
        _headerBgBrush = ThemeBrush("HexHeaderBgColor", Color.FromRgb(0xFB, 0xFB, 0xFD));
        _hairlineBrush = ThemeBrush("HairlineColor", Color.FromRgb(0xDC, 0xDC, 0xE0));
        _hairlinePen = new Pen(_hairlineBrush);
        _caretPen = new Pen(ThemeBrush("AccentColor", Color.FromRgb(0x00, 0x71, 0xE3)), 1.4);
        _surface.InvalidateVisual();
    }

    private IBrush ThemeBrush(string key, Color fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out object? value) && value is Color color)
            return new SolidColorBrush(color);
        return new SolidColorBrush(fallback);
    }

    public void SetHighlights(IEnumerable<HighlightRange> ranges)
    {
        _highlights.Clear();
        _highlights.AddRange(ranges);
        _surface.InvalidateVisual();
    }

    public void ClearHighlights()
    {
        _highlights.Clear();
        _surface.InvalidateVisual();
    }

    public void Select(long offset, long length, bool center = true)
    {
        if (Length == 0) return;
        _anchor = Math.Clamp(offset, 0, Length - 1);
        _caret = Math.Clamp(offset + Math.Max(1, length) - 1, 0, Length - 1);
        EnsureVisible(_anchor, center);
        _surface.InvalidateVisual();
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCaret(long offset, bool extend = false, bool center = false)
    {
        if (Length == 0) return;
        _caret = Math.Clamp(offset, 0, Length - 1);
        if (!extend) _anchor = _caret;
        EnsureVisible(_caret, center);
        _surface.InvalidateVisual();
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusSurface() => _surface.Focus();

    public byte[] GetSelectedBytes(int max = 1 << 20)
    {
        if (_source == null || Length == 0) return [];
        long len = Math.Min(SelectionLength == 0 ? 1 : SelectionLength, max);
        var data = new byte[len];
        _source.Read(SelectionStart, data, 0, (int)len);
        return data;
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

    private void ClampTop() => _topRow = Math.Clamp(_topRow, 0, Math.Max(0, TotalRows - VisibleRows));

    private void MeasureFont()
    {
        var probe = new FormattedText("0000000000", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                      _typeface, FontSize, Brushes.Black);
        _charW = probe.Width / 10;
        _rowH = Math.Ceiling(probe.Height) + 5;
    }

    private void UpdateLayoutMetrics()
    {
        if (_autoBytesPerRow)
        {
            double avail = _surface.Bounds.Width - Pad * 2 - (_offsetDigits + 2) * _charW;
            int fit = (int)((avail - 3 * _charW) / (4 * _charW));
            _bytesPerRow = Math.Clamp(fit / 8 * 8, 8, 64);
        }
        SyncScrollBar();
    }

    private void SyncScrollBar()
    {
        double viewport = _surface.Bounds.Width;
        double overflow = Math.Max(0, ContentWidth - viewport);
        _hScroll.IsVisible = overflow > 0.5;
        _hScroll.Maximum = overflow;
        _hScroll.ViewportSize = Math.Max(1, viewport);
        _hScroll.LargeChange = Math.Max(1, viewport / 2);
        _hScroll.SmallChange = _charW * 3;
        _xOffset = Math.Clamp(_xOffset, 0, overflow);
        _hScroll.Value = _xOffset;

        int vis = VisibleRows;
        _scroll.Minimum = 0;
        _scroll.Maximum = Math.Max(0, TotalRows - vis);
        _scroll.ViewportSize = vis;
        _scroll.LargeChange = vis;
        _scroll.SmallChange = 1;
        ClampTop();
        _scroll.Value = _topRow;
    }

    private void EnsureBuffer(long start, int count)
    {
        // the cache is only valid for the bytes actually read, not for the whole buffer capacity
        if (_bufStart >= 0 && start >= _bufStart && start + count <= _bufStart + _bufLen) return;

        int want = (int)Math.Min(Math.Max(count, 64 * 1024), _source!.Length - start);
        if (_buf.Length < want) _buf = new byte[want];
        _bufLen = _source.Read(start, _buf, 0, want);
        _bufStart = start;
    }

    private void Paint(DrawingContext ctx, Size size)
    {
        ctx.FillRectangle(_surfaceBrush, new Rect(size));
        if (_source == null || Length == 0)
        {
            PaintHeader(ctx, size);
            return;
        }

        // one scope for the horizontal offset; the header is drawn afterwards on its own
        using (ctx.PushTransform(Matrix.CreateTranslation(-_xOffset, 0)))
            PaintRows(ctx, size);

        PaintHeader(ctx, size);
    }

    private void PaintRows(DrawingContext ctx, Size size)
    {
        long startOff = _topRow * _bytesPerRow;
        int rows = (int)Math.Min(VisibleRows + 1, TotalRows - _topRow);
        int need = (int)Math.Min((long)rows * _bytesPerRow, Length - startOff);
        if (need <= 0) return;
        EnsureBuffer(startOff, need);

        // soft band behind the ASCII column, the way native mac tables tint a trailing column
        double bandTop = HeaderHeight;
        ctx.FillRectangle(_groupTintBrush,
                          new Rect(AsciiX - 0.7 * _charW, bandTop, AsciiWidth + 1.4 * _charW, size.Height - bandTop),
                          6);

        long selStart = SelectionStart, selEnd = SelectionEnd;
        bool hasSelection = _anchor != _caret;

        for (int r = 0; r < rows; r++)
        {
            long rowOff = startOff + (long)r * _bytesPerRow;
            if (rowOff >= Length) break;
            int n = (int)Math.Min(_bytesPerRow, Length - rowOff);
            double y = HeaderHeight + RowGap + r * _rowH;

            foreach (var h in _highlights)
                PaintRange(ctx, rowOff, n, y, h.Offset, h.Offset + h.Length - 1, new SolidColorBrush(h.Color));
            if (hasSelection) PaintRange(ctx, rowOff, n, y, selStart, selEnd, _selectionBrush);

            Draw(ctx, rowOff.ToString("X" + _offsetDigits), Pad, y, _offsetBrush);

            Array.Fill(_hexChars, ' ');
            Array.Fill(_zeroChars, ' ');
            for (int i = 0; i < n; i++)
            {
                byte b = _buf[rowOff - _bufStart + i];
                int idx = i * 3 + i / 8;
                // zero bytes go to a dimmed pass so real data stands out of the padding
                var target = b == 0 ? _zeroChars : _hexChars;
                target[idx] = HexDigit(b >> 4);
                target[idx + 1] = HexDigit(b & 0xF);

                bool printable = b is >= 0x20 and < 0x7F;
                _asciiChars[i] = printable ? (char)b : ' ';
                _asciiDimChars[i] = printable ? ' ' : '·';
            }

            int hexLen = _bytesPerRow * 3 + (_bytesPerRow - 1) / 8;
            DrawHexRow(ctx, _zeroChars, hexLen, y, _zeroBrush);
            DrawHexRow(ctx, _hexChars, hexLen, y, _byteBrush);
            Draw(ctx, new string(_asciiDimChars, 0, n), AsciiX, y, _asciiDimBrush);
            Draw(ctx, new string(_asciiChars, 0, n), AsciiX, y, _asciiBrush);
        }

        PaintCaret(ctx, startOff, rows);
    }

    /// <summary>Draws one hex line, honouring the wider gap between 8 byte groups.</summary>
    private void DrawHexRow(DrawingContext ctx, char[] chars, int length, double y, IBrush brush)
    {
        for (int g = 0; g * 8 < _bytesPerRow; g++)
        {
            int start = g * 8 * 3 + g;
            int count = Math.Min(24, length - start);
            if (count <= 0) break;
            var text = new string(chars, start, count);
            if (text.AsSpan().IsWhiteSpace()) continue;
            Draw(ctx, text, ByteX(g * 8), y, brush);
        }
    }

    private void PaintRange(DrawingContext ctx, long rowOff, int n, double y, long from, long to, IBrush brush)
    {
        long s = Math.Max(rowOff, from);
        long e = Math.Min(rowOff + n - 1, to);
        if (s > e) return;

        int i0 = (int)(s - rowOff), i1 = (int)(e - rowOff);
        double x0 = ByteX(i0) - 0.25 * _charW;
        double x1 = ByteX(i1) + 2.25 * _charW;
        ctx.FillRectangle(brush, new Rect(x0, y - 1, x1 - x0, _rowH), 4);
        ctx.FillRectangle(brush, new Rect(AsciiX + i0 * _charW - 1, y - 1, (i1 - i0 + 1) * _charW + 2, _rowH), 4);
    }

    private void PaintCaret(DrawingContext ctx, long startOff, int rows)
    {
        long endOff = startOff + (long)rows * _bytesPerRow;
        if (_caret < startOff || _caret >= endOff) return;

        int r = (int)((_caret - startOff) / _bytesPerRow);
        int i = (int)(_caret % _bytesPerRow);
        double y = HeaderHeight + RowGap + r * _rowH - 1;
        ctx.DrawRectangle(null, _caretPen, new Rect(ByteX(i) - 0.25 * _charW, y, 2.5 * _charW, _rowH), 4, 4);
        ctx.DrawRectangle(null, _caretPen, new Rect(AsciiX + i * _charW - 1.5, y, _charW + 3, _rowH), 4, 4);
    }

    private void PaintHeader(DrawingContext ctx, Size size)
    {
        ctx.FillRectangle(_headerBgBrush, new Rect(0, 0, size.Width, HeaderHeight));
        ctx.DrawLine(_hairlinePen, new Point(0, HeaderHeight - 0.5), new Point(size.Width, HeaderHeight - 0.5));

        using var shift = ctx.PushTransform(Matrix.CreateTranslation(-_xOffset, 0));
        double y = (HeaderHeight - _rowH) / 2 + 1;
        Draw(ctx, "OFFSET", Pad, y, _offsetBrush);
        for (int i = 0; i < _bytesPerRow; i++)
            Draw(ctx, i.ToString("X2"), ByteX(i), y, i % 8 == 0 ? _byteBrush : _offsetBrush);
        Draw(ctx, "ASCII", AsciiX, y, _offsetBrush);
    }

    private void Draw(DrawingContext ctx, string text, double x, double y, IBrush brush) =>
        ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                       _typeface, FontSize, brush), new Point(x, y + 1));

    private static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

    private long? HitTest(Point p)
    {
        if (_source == null || Length == 0 || p.Y < HeaderHeight) return null;
        long row = _topRow + (long)((p.Y - HeaderHeight - RowGap) / _rowH);
        double x = p.X + _xOffset;

        int index;
        if (x >= AsciiX) index = (int)((x - AsciiX) / _charW);
        else if (x >= HexX)
        {
            index = _bytesPerRow - 1;
            for (int i = 0; i < _bytesPerRow; i++)
                if (x < ByteX(i) + 3 * _charW) { index = i; break; }
        }
        else index = 0;

        long off = row * _bytesPerRow + Math.Clamp(index, 0, _bytesPerRow - 1);
        return off >= Length ? Length - 1 : Math.Max(0, off);
    }

    private void Scroll(int rows)
    {
        _topRow += rows;
        ClampTop();
        SyncScrollBar();
        _surface.InvalidateVisual();
    }

    private void HandleKey(KeyEventArgs e)
    {
        if (_source == null || Length == 0) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        long c = _caret;

        switch (e.Key)
        {
            case Key.Left: SetCaret(c - 1, shift); break;
            case Key.Right: SetCaret(c + 1, shift); break;
            case Key.Up: SetCaret(c - _bytesPerRow, shift); break;
            case Key.Down: SetCaret(c + _bytesPerRow, shift); break;
            case Key.PageUp: SetCaret(c - (long)VisibleRows * _bytesPerRow, shift); break;
            case Key.PageDown: SetCaret(c + (long)VisibleRows * _bytesPerRow, shift); break;
            case Key.Home: SetCaret(ctrl ? 0 : c - c % _bytesPerRow, shift); break;
            case Key.End: SetCaret(ctrl ? Length - 1 : c - c % _bytesPerRow + _bytesPerRow - 1, shift); break;
            case Key.A when ctrl: _anchor = 0; SetCaret(Length - 1, extend: true); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>The drawing surface; the view itself is the grid that also holds the scroll bar.</summary>
    private sealed class Surface(HexView owner) : Control
    {
        private readonly HexView _owner = owner;

        public override void Render(DrawingContext context) => _owner.Paint(context, Bounds.Size);

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = base.ArrangeOverride(finalSize);
            _owner.UpdateLayoutMetrics();
            return size;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            if (_owner.HitTest(e.GetPosition(this)) is { } hit)
                _owner.SetCaret(hit, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (_owner.HitTest(e.GetPosition(this)) is { } hit) _owner.SetCaret(hit, extend: true);
        }

        private double _wheelResidue;

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            // trackpads deliver fractional deltas; accumulate so small gestures still scroll
            _wheelResidue += -e.Delta.Y * 3;
            int rows = (int)_wheelResidue;
            _wheelResidue -= rows;
            if (rows != 0) _owner.Scroll(rows);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            _owner.HandleKey(e);
        }
    }
}
