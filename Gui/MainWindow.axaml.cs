using System.Buffers.Binary;
using Avalonia;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using BinaryViewer.Core;

namespace BinaryViewer.Gui;

public partial class MainWindow : Window
{
    private readonly HexView _hex = new();

    private IByteSource? _source;
    private string _path = "";
    private CancellationTokenSource? _cts;
    private Task _work = Task.CompletedTask;
    private int _workGeneration;

    private List<JsonHit> _jsonHits = [];
    private List<StringHit> _strings = [];

    private Func<string>? _statusInfo;
    private byte[]? _lastPattern;
    private bool _lastIgnoreCase;

    public MainWindow() : this(null) { }

    public MainWindow(string? initialFile)
    {
        InitializeComponent();

        HexHost.Content = _hex;
        _hex.CaretChanged += (_, _) => UpdateCaretStatus();

        OpenButton.Click += async (_, _) => await PromptOpenAsync();
        GotoButton.Click += async (_, _) => await PromptGotoAsync();
        FindButton.Click += async (_, _) => await PromptFindAsync();
        ScanButton.Click += async (_, _) => await ScanJsonAsync();
        StringsButton.Click += async (_, _) => await ScanStringsAsync();
        ReanalyzeButton.Click += async (_, _) => await AnalyzeAsync();
        JsonScanButton.Click += async (_, _) => await ScanJsonAsync();
        StringScanButton.Click += async (_, _) => await ScanStringsAsync();
        JsonSaveButton.Click += async (_, _) => await SaveSelectedJsonAsync();
        JsonCopyButton.Click += async (_, _) => await CopyJsonAsync();
        CancelButton.Click += (_, _) => _cts?.Cancel();

        BprCombo.SelectionChanged += (_, _) =>
        {
            string text = BprCombo.SelectedItem as string ?? "";
            _hex.BytesPerRow = int.TryParse(text, out int bpr) ? bpr : 0;   // the "auto" row -> 0
        };

        JsonGrid.SelectionChanged += (_, _) =>
        {
            if (JsonGrid.SelectedItem is JsonRow row) ShowJson(row.Hit, navigate: true);
        };
        StringGrid.SelectionChanged += (_, _) =>
        {
            if (StringGrid.SelectedItem is StringRow row) _hex.Select(row.Hit.Offset, row.Hit.ByteLength);
        };
        StringFilter.TextChanged += (_, _) => ApplyStringFilter();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath() is { } file) _ = OpenFileAsync(file);
        });

        ThemeButton.Click += (_, _) => ToggleTheme();
        LanguageButton.Click += (_, _) => ToggleLanguage();
        ActualThemeVariantChanged += (_, _) => { UpdateThemeButton(); ApplySystemTitleBar(); };
        ApplyPlatformChrome();
        ApplyLanguage();
        UpdateThemeButton();
        Opened += (_, _) => ApplySystemTitleBar();
        UpdateInspector();

        if (!string.IsNullOrEmpty(initialFile))
            Dispatcher.UIThread.Post(() => _ = OpenFileAsync(initialFile), DispatcherPriority.Background);
    }

    /// <summary>On macOS the toolbar doubles as the title bar, so it needs room for the traffic lights.</summary>
    private void ApplyPlatformChrome()
    {
        if (!OperatingSystem.IsMacOS()) return;

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        Toolbar.Padding = new Thickness(0, 6, 0, 0);
        ToolbarLeft.Margin = new Thickness(76, 0, 0, 0);
    }

    /// <summary>
    /// Flips between light and dark. The previous three way cycle could land on a variant that
    /// looked identical to the current system theme, so a click appeared to do nothing.
    /// </summary>
    private void ToggleTheme()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
        UpdateThemeButton();
        RefreshJsonHighlights();
        RememberPreferences();
    }

    // vector icons: the ☀ / ☾ characters fall back to a colour emoji font on some systems,
    // which changed the glyph width and shifted the whole toolbar
    private static readonly Geometry SunIcon = Geometry.Parse(
        "M8,4.6 A3.4,3.4 0 1 0 8,11.4 A3.4,3.4 0 1 0 8,4.6 Z " +
        "M7.4,0.6 h1.2 v2.4 h-1.2 z M7.4,13 h1.2 v2.4 h-1.2 z " +
        "M0.6,7.4 h2.4 v1.2 h-2.4 z M13,7.4 h2.4 v1.2 h-2.4 z " +
        "M2.5,3.35 l0.85,-0.85 l1.7,1.7 l-0.85,0.85 z M11.8,12.65 l0.85,-0.85 l-1.7,-1.7 l-0.85,0.85 z " +
        "M12.65,3.35 l-0.85,-0.85 l-1.7,1.7 l0.85,0.85 z M3.35,12.65 l-0.85,-0.85 l1.7,-1.7 l0.85,0.85 z");

    private static readonly Geometry MoonIcon = Geometry.Parse(
        "M13.4,9.9 A5.8,5.8 0 1 1 6.3,2.7 A4.8,4.8 0 0 0 13.4,9.9 Z");

    /// <summary>Shows the mode you are in right now; the tooltip says what a click does.</summary>
    private void UpdateThemeButton()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        ThemeIcon.Data = dark ? MoonIcon : SunIcon;
        ThemeLabel.Text = dark ? "Dark" : "Light";
        ToolTip.SetTip(ThemeButton, dark ? S.TipThemeDark : S.TipThemeLight);
    }

    private void ToggleLanguage()
    {
        S.Current = S.Current == Language.Korean ? Language.English : Language.Korean;
        ApplyLanguage();
        RememberPreferences();
        if (_source != null) _ = AnalyzeAsync();      // the report text is built in the old language
    }

    private void RememberPreferences() => App.Settings.Remember(S.Current, ActualThemeVariant);

    /// <summary>Pushes every visible string for the current language.</summary>
    private void ApplyLanguage()
    {
        OpenButton.Content = S.Open;
        GotoButton.Content = S.GoTo;
        FindButton.Content = S.Find;
        ScanButton.Content = S.ScanJson;
        StringsButton.Content = S.ExtractStrings;
        CancelButton.Content = S.Stop;
        ReanalyzeButton.Content = S.Reanalyze;
        JsonScanButton.Content = S.Scan;
        JsonSaveButton.Content = S.Save;
        JsonCopyButton.Content = S.Copy;
        StringScanButton.Content = S.Extract;

        ToolTip.SetTip(OpenButton, S.TipOpen);
        ToolTip.SetTip(GotoButton, S.TipGoTo);
        ToolTip.SetTip(FindButton, S.TipFind);
        ToolTip.SetTip(ScanButton, S.TipScan);
        ToolTip.SetTip(LanguageButton, S.TipLanguage);

        PerRowLabel.Text = S.PerRow;
        int bprIndex = Math.Max(0, BprCombo.SelectedIndex);
        BprCombo.ItemsSource = new[] { S.AutoRow, "8", "16", "24", "32", "48", "64" };
        BprCombo.SelectedIndex = bprIndex;
        AutoScanCheck.Content = S.AutoScan;
        JsonMinLabel.Text = S.MinLength;
        StringMinLabel.Text = S.MinLength;
        StringFilter.Watermark = S.Filter;
        InspectorLabel.Text = S.InspectorTitle;
        LanguageButton.Content = S.Current == Language.Korean ? "한" : "EN";

        TabAnalysis.Header = S.TabAnalysis;
        TabJson.Header = S.TabJson;
        TabStrings.Header = S.TabStrings;

        JsonGrid.Columns[0].Header = S.ColOffset;
        JsonGrid.Columns[1].Header = S.ColSize;
        JsonGrid.Columns[2].Header = S.ColKind;
        JsonGrid.Columns[3].Header = S.ColEncoding;
        JsonGrid.Columns[4].Header = S.ColPreview;

        StringGrid.Columns[0].Header = S.ColOffset;
        StringGrid.Columns[1].Header = S.ColEncoding;
        StringGrid.Columns[2].Header = S.ColSize;
        StringGrid.Columns[3].Header = S.ColText;

        InspectorGrid.Columns[0].Header = S.ColType;
        InspectorGrid.Columns[1].Header = S.ColLittle;
        InspectorGrid.Columns[2].Header = S.ColBig;

        if (_source == null) StatusFile.Text = S.NoFile;
        if (_statusInfo != null) StatusInfo.Text = _statusInfo();
        UpdateThemeButton();
        UpdateCaretStatus();
    }

    /// <summary>Windows keeps drawing a light title bar unless the app asks for the dark one.</summary>
    private void ApplySystemTitleBar()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;

        int dark = ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
        try { DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); } catch { /* older Windows */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Highlight tint for JSON regions, tuned per theme so it stays readable in dark mode.</summary>
    private Color JsonTint(bool focused)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        if (dark) return focused ? Color.FromRgb(0x2E, 0x5B, 0x3F) : Color.FromRgb(0x24, 0x3A, 0x2C);
        return focused ? Color.FromRgb(0xC8, 0xEE, 0xD2) : Color.FromRgb(0xE8, 0xF7, 0xEC);
    }

    private void RefreshJsonHighlights()
    {
        long focus = JsonGrid.SelectedItem is JsonRow row ? row.Hit.Offset : -1;
        _hex.SetHighlights(_jsonHits.Take(2000)
            .Select(h => new HighlightRange(h.Offset, h.Length, JsonTint(h.Offset == focus))));
    }

    // ------------------------------------------------------------------ file

    private async Task PromptOpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = S.OpenFileTitle,
            AllowMultiple = false
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await OpenFileAsync(path);
    }

    public async Task OpenFileAsync(string path)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var source = ByteSourceFactory.Open(path);
            sw.Stop();

            await DrainWorkAsync();
            _source?.Dispose();
            _source = source;
            _path = path;
            _hex.Source = source;

            _jsonHits = [];
            _strings = [];
            JsonGrid.ItemsSource = null;
            StringGrid.ItemsSource = null;
            JsonText.Text = "";
            _hex.ClearHighlights();

            Title = $"{S.AppTitle} - {Path.GetFileName(path)}";
            StatusFile.Text = path;
            double openMs = sw.Elapsed.TotalMilliseconds;
            bool mapped = source is MappedByteSource;
            SetStatus(() => S.OpenedIn(FileAnalyzer.Human(source.Length), openMs) + (mapped ? S.MemoryMapped : ""));
            UpdateCaretStatus();
            _hex.FocusSurface();

            await AnalyzeAsync();
            if (AutoScanCheck.IsChecked == true && source.Length > 0) await ScanJsonAsync();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, S.OpenFailed, ex.Message);
        }
    }

    // ------------------------------------------------------------------ background work

    private (CancellationToken Token, int Generation) StartWork(string label)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Progress.Value = 0;
        Progress.IsVisible = true;
        CancelButton.IsVisible = true;
        StatusOffset.Text = label;
        return (_cts.Token, ++_workGeneration);
    }

    /// <summary>Only the newest job may clear the toolbar; a cancelled one finishes later.</summary>
    private void EndWork(int generation)
    {
        if (generation != _workGeneration) return;
        Progress.IsVisible = false;
        CancelButton.IsVisible = false;
        UpdateCaretStatus();
    }

    /// <summary>Waits for whatever scan is still unwinding, so its byte source stays alive.</summary>
    private async Task DrainWorkAsync()
    {
        _cts?.Cancel();
        try { await _work; } catch { /* cancelled or already failed */ }
    }

    private async Task AnalyzeAsync()
    {
        if (_source is not { } src) return;
        var work = StartWork(S.Analyzing);
        var ct = work.Token;
        var sw = Stopwatch.StartNew();
        try
        {
            string path = _path;
            var task = Task.Run(() => FileAnalyzer.Analyze(src, path, carve: true, ct), ct);
            _work = task;
            var result = await task;
            ReportBox.Text = result.Report + Environment.NewLine + S.AnalyzedIn(sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportBox.Text = ex.Message; }
        finally { EndWork(work.Generation); }
    }

    private async Task ScanJsonAsync()
    {
        if (_source is not { } src || src.Length == 0) return;
        var work = StartWork(S.ScanningJson);
        var ct = work.Token;
        var sw = Stopwatch.StartNew();
        var progress = new Progress<double>(p => Progress.Value = Math.Clamp(p * 100, 0, 100));

        var scanner = new JsonScanner
        {
            MinLength = JsonMinLen.Value,
            ScanUtf16 = JsonUtf16Check.IsChecked == true
        };

        try
        {
            var task = Task.Run(() => scanner.Scan(src, 0, src.Length, progress, ct), ct);
            _work = task;
            var hits = await task;
            _jsonHits = hits;
            JsonGrid.ItemsSource = hits.Select(h => new JsonRow(h)).ToList();
            JsonText.Text = "";

            _hex.SetHighlights(hits.Take(2000).Select(h => new HighlightRange(h.Offset, h.Length, JsonTint(false))));

            long bytes = hits.Sum(h => h.Length);
            double scanMs = sw.Elapsed.TotalMilliseconds;
            int count = hits.Count;
            SetStatus(() => count == 0
                ? S.JsonNone(scanMs)
                : S.JsonFound(count, FileAnalyzer.Human(bytes), scanMs));

            if (hits.Count > 0) ShowJson(hits[0], navigate: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await MessageDialog.ShowAsync(this, S.ScanJson, ex.Message); }
        finally { EndWork(work.Generation); }
    }

    private async Task ScanStringsAsync()
    {
        if (_source is not { } src || src.Length == 0) return;
        var work = StartWork(S.ExtractingStrings);
        var ct = work.Token;
        var sw = Stopwatch.StartNew();
        var progress = new Progress<double>(p => Progress.Value = Math.Clamp(p * 100, 0, 100));
        int min = StringMinLen.Value;

        try
        {
            var task = Task.Run(() => StringScanner.Scan(src, min, true, 200_000, progress, ct), ct);
            _work = task;
            var hits = await task;
            _strings = hits;
            ApplyStringFilter();
            double extractMs = sw.Elapsed.TotalMilliseconds;
            int found = hits.Count;
            SetStatus(() => S.StringsFound(found, extractMs));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await MessageDialog.ShowAsync(this, S.ExtractStrings, ex.Message); }
        finally { EndWork(work.Generation); }
    }

    private void ApplyStringFilter()
    {
        string f = (StringFilter.Text ?? "").Trim();
        var view = f.Length == 0
            ? _strings
            : _strings.Where(s => s.Text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        StringGrid.ItemsSource = view.Take(100_000).Select(s => new StringRow(s)).ToList();
    }

    // ------------------------------------------------------------------ json pane

    private void ShowJson(JsonHit hit, bool navigate)
    {
        if (_source is not { } src) return;

        if (navigate) _hex.Select(hit.Offset, hit.Length);
        _hex.SetHighlights(_jsonHits.Take(2000)
            .Select(h => new HighlightRange(h.Offset, h.Length, JsonTint(h.Offset == hit.Offset))));

        const long PrettyLimit = 4L * 1024 * 1024;
        if (hit.Length > PrettyLimit)
        {
            JsonText.Text = JsonScanner.ReadText(src, hit.Offset, PrettyLimit, hit.Utf16) +
                            Environment.NewLine + Environment.NewLine + S.TruncatedPreview(FileAnalyzer.Human(hit.Length));
            return;
        }

        string raw = JsonScanner.ReadText(src, hit.Offset, hit.Length, hit.Utf16);
        JsonText.Text = JsonScanner.PrettyPrint(raw).ReplaceLineEndings();
    }

    private async Task SaveSelectedJsonAsync()
    {
        if (_source is not { } src || JsonGrid.SelectedItem is not JsonRow row) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = S.SaveJsonTitle,
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(_path)}_0x{row.Hit.Offset:X}.json",
            DefaultExtension = "json"
        });
        if (file?.TryGetLocalPath() is not { } target) return;

        string text = JsonScanner.ReadText(src, row.Hit.Offset, row.Hit.Length, row.Hit.Utf16);
        await File.WriteAllTextAsync(target, text, new UTF8Encoding(false));
        SetStatus(() => S.SavedTo(target));
    }

    private async Task CopyJsonAsync()
    {
        if (Clipboard is { } clip && !string.IsNullOrEmpty(JsonText.Text))
            await clip.SetTextAsync(JsonText.Text);
    }

    // ------------------------------------------------------------------ navigation

    private async Task PromptGotoAsync()
    {
        if (_source is not { } src) return;
        long? offset = await GotoDialog.ShowAsync(this, _hex.CaretOffset, src.Length);
        if (offset is { } value) _hex.SetCaret(value, center: true);
    }

    private async Task PromptFindAsync()
    {
        var request = await FindDialog.ShowAsync(this);
        if (request is not { } r) return;
        _lastPattern = r.Pattern;
        _lastIgnoreCase = r.IgnoreCase;
        DoFind(r.Pattern, r.IgnoreCase, r.Backward);
    }

    private void DoFind(byte[] pattern, bool ignoreCase, bool backward)
    {
        if (_source is not { } src) return;

        long from = _hex.CaretOffset;
        long hit = backward
            ? ByteSearcher.FindBackward(src, pattern, from, ignoreCase)
            : ByteSearcher.Find(src, pattern, from + 1, ignoreCase);
        if (hit < 0 && !backward) hit = ByteSearcher.Find(src, pattern, 0, ignoreCase);   // wrap around

        if (hit < 0)
        {
            SetStatus(() => S.PatternNotFound);
            return;
        }
        _hex.Select(hit, pattern.Length);
        SetStatus(() => S.FoundAt(hit));
    }

    /// <summary>Keeps the text as a factory so a language switch can re-render it.</summary>
    private void SetStatus(Func<string> text)
    {
        _statusInfo = text;
        StatusInfo.Text = text();
    }

    private void UpdateCaretStatus()
    {
        long off = _hex.CaretOffset;
        StatusOffset.Text = _source == null
            ? "-"
            : S.OffsetStatus(off) + (_hex.SelectionLength > 1 ? S.SelectionStatus(_hex.SelectionLength) : "");
        UpdateInspector();
    }

    // ------------------------------------------------------------------ data inspector

    private void UpdateInspector()
    {
        var b = new byte[16];
        int n = _source?.Read(_hex.CaretOffset, b, 0, 16) ?? 0;
        var rows = new List<InspectorRow>();

        void Add(string name, int need, Func<byte[], string> le, Func<byte[], string>? be = null) =>
            rows.Add(new InspectorRow(name, n >= need ? le(b) : "", n >= need && be != null ? be(b) : ""));

        Add("int8", 1, x => ((sbyte)x[0]).ToString());
        Add("uint8", 1, x => x[0].ToString());
        Add("int16", 2, x => BinaryPrimitives.ReadInt16LittleEndian(x).ToString(),
                        x => BinaryPrimitives.ReadInt16BigEndian(x).ToString());
        Add("uint16", 2, x => BinaryPrimitives.ReadUInt16LittleEndian(x).ToString(),
                         x => BinaryPrimitives.ReadUInt16BigEndian(x).ToString());
        Add("int32", 4, x => BinaryPrimitives.ReadInt32LittleEndian(x).ToString(),
                        x => BinaryPrimitives.ReadInt32BigEndian(x).ToString());
        Add("uint32", 4, x => $"{BinaryPrimitives.ReadUInt32LittleEndian(x)} (0x{BinaryPrimitives.ReadUInt32LittleEndian(x):X8})",
                         x => $"{BinaryPrimitives.ReadUInt32BigEndian(x)} (0x{BinaryPrimitives.ReadUInt32BigEndian(x):X8})");
        Add("int64", 8, x => BinaryPrimitives.ReadInt64LittleEndian(x).ToString(),
                        x => BinaryPrimitives.ReadInt64BigEndian(x).ToString());
        Add("uint64", 8, x => BinaryPrimitives.ReadUInt64LittleEndian(x).ToString(),
                         x => BinaryPrimitives.ReadUInt64BigEndian(x).ToString());
        Add("float", 4, x => BinaryPrimitives.ReadSingleLittleEndian(x).ToString("G7"),
                        x => BinaryPrimitives.ReadSingleBigEndian(x).ToString("G7"));
        Add("double", 8, x => BinaryPrimitives.ReadDoubleLittleEndian(x).ToString("G15"),
                         x => BinaryPrimitives.ReadDoubleBigEndian(x).ToString("G15"));
        Add("bits", 1, x => Convert.ToString(x[0], 2).PadLeft(8, '0'));
        Add("char", 1, x => x[0] is >= 0x20 and < 0x7F ? $"'{(char)x[0]}'" : $"0x{x[0]:X2}");
        Add("UTF-16", 2, x => Printable(Encoding.Unicode.GetString(x, 0, 2)),
                         x => Printable(Encoding.BigEndianUnicode.GetString(x, 0, 2)));
        Add("unix time", 4, x => UnixTime(BinaryPrimitives.ReadUInt32LittleEndian(x)),
                            x => UnixTime(BinaryPrimitives.ReadUInt32BigEndian(x)));
        Add("FILETIME", 8, x => FileTime(BinaryPrimitives.ReadInt64LittleEndian(x)));

        InspectorGrid.ItemsSource = rows;
    }

    private static string Printable(string s) => s.Length > 0 && !char.IsControl(s[0]) ? $"'{s}'" : "";

    private static string UnixTime(uint v) =>
        v is < 0x10000000 or > 0x7FFFFFFF ? "" : DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "Z";

    private static string FileTime(long v)
    {
        if (v <= 0 || v > 2650467743999999999) return "";
        try { return DateTime.FromFileTimeUtc(v).ToString("yyyy-MM-dd HH:mm:ss") + "Z"; }
        catch { return ""; }
    }

    // ------------------------------------------------------------------ shortcuts

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        switch (e.Key)
        {
            case Key.O when cmd: e.Handled = true; await PromptOpenAsync(); break;
            case Key.G when cmd: e.Handled = true; await PromptGotoAsync(); break;
            case Key.F when cmd: e.Handled = true; await PromptFindAsync(); break;
            case Key.J when cmd: e.Handled = true; await ScanJsonAsync(); break;
            case Key.C when cmd && _hex.SelectionLength > 0:
                e.Handled = true;
                if (Clipboard is { } clip)
                    await clip.SetTextAsync(string.Join(" ", _hex.GetSelectedBytes().Select(x => x.ToString("X2"))));
                break;
            case Key.F3:
                e.Handled = true;
                if (_lastPattern != null) DoFind(_lastPattern, _lastIgnoreCase, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                else await PromptFindAsync();
                break;
            case Key.Escape: _cts?.Cancel(); break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _source?.Dispose();
        base.OnClosed(e);
    }
}

public sealed class JsonRow(JsonHit hit)
{
    public JsonHit Hit { get; } = hit;
    public string Offset => $"0x{Hit.Offset:X8}";
    public string Size => $"{Hit.Length:N0}";
    public string Kind => Hit.KindName;
    public string Encoding => Hit.Encoding;
    public string Depth => Hit.Depth.ToString();
    public string Preview => Hit.Preview;
}

public sealed class StringRow(StringHit hit)
{
    public StringHit Hit { get; } = hit;
    public string Offset => $"0x{Hit.Offset:X8}";
    public string Encoding => Hit.Utf16 ? "UTF-16" : "ASCII";
    public string Size => Hit.ByteLength.ToString();
    public string Text => Hit.Text.Length > 300 ? Hit.Text[..300] + " ..." : Hit.Text;
}

public sealed record InspectorRow(string Name, string Le, string Be);
