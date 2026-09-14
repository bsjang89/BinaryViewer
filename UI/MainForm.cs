using System.Diagnostics;
using System.Text;
using BinaryViewer.Core;

namespace BinaryViewer.UI;

public sealed class MainForm : Form
{
    private readonly HexViewControl _hex = new() { Dock = DockStyle.Fill };
    private readonly DataInspector _inspector = new() { Dock = DockStyle.Fill };

    private readonly TextBox _report = NewMonoBox();
    private readonly ListView _jsonList = NewVirtualList();
    private readonly TextBox _jsonText = NewMonoBox();
    private readonly ListView _stringList = NewVirtualList();
    private readonly TextBox _stringFilter = new() { Width = 160 };

    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 140 };
    private readonly ToolStripButton _cancelButton = new("중지") { Visible = false };
    private readonly ToolStripStatusLabel _statusFile = new("파일 없음") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusOffset = new("-") { AutoSize = true };
    private readonly ToolStripStatusLabel _statusInfo = new("") { AutoSize = true };
    private readonly ToolStripComboBox _bprCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ToolStripMenuItem _autoScan = new("열면 JSON 자동 스캔") { CheckOnClick = true, Checked = true };
    private readonly NumericUpDown _jsonMinLen = new() { Minimum = 8, Maximum = 100000, Value = 24, Width = 70 };
    private readonly CheckBox _jsonUtf16 = new() { Text = "UTF-16LE 포함", Checked = true, AutoSize = true };
    private readonly NumericUpDown _strMinLen = new() { Minimum = 3, Maximum = 500, Value = 6, Width = 60 };

    private SplitContainer? _jsonSplit;
    private IByteSource? _source;
    private string _path = "";
    private CancellationTokenSource? _cts;
    private FindDialog? _find;
    private byte[]? _lastPattern;
    private bool _lastIgnoreCase;

    private List<JsonHit> _jsonHits = [];
    private List<StringHit> _strings = [];
    private List<StringHit> _stringsView = [];

    public MainForm(string? initialFile)
    {
        Text = "Binary Viewer";
        ClientSize = new Size(1420, 880);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        BuildUi();

        DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenFile(files[0]);
        };

        _hex.CaretChanged += (_, _) => UpdateCaretStatus();

        // opening has to wait until the window exists, so the layout is already sized
        if (!string.IsNullOrEmpty(initialFile))
            Shown += (_, _) => BeginInvoke(() => OpenFile(initialFile));
    }

    private static TextBox NewMonoBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 9.5f),
        BackColor = Color.White
    };

    private static ListView NewVirtualList() => new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        VirtualMode = true,
        FullRowSelect = true,
        HideSelection = false,
        GridLines = true,
        Font = new Font("Consolas", 9f)
    };

    private void BuildUi()
    {
        // ---- toolbar ----------------------------------------------------
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Renderer = new ToolStripSystemRenderer() };
        var openButton = new ToolStripButton("열기 (Ctrl+O)");
        openButton.Click += (_, _) => PromptOpen();
        var gotoButton = new ToolStripButton("이동 (Ctrl+G)");
        gotoButton.Click += (_, _) => PromptGoto();
        var findButton = new ToolStripButton("찾기 (Ctrl+F)");
        findButton.Click += (_, _) => ShowFind();
        var scanButton = new ToolStripButton("JSON 스캔 (Ctrl+J)");
        scanButton.Click += async (_, _) => await ScanJsonAsync();
        var stringsButton = new ToolStripButton("문자열 추출");
        stringsButton.Click += async (_, _) => await ScanStringsAsync();
        var optionsMenu = new ToolStripDropDownButton("옵션");
        optionsMenu.DropDownItems.Add(_autoScan);

        _bprCombo.Items.AddRange(["자동", "8", "16", "24", "32", "48", "64"]);
        _bprCombo.SelectedIndex = 0;
        _bprCombo.SelectedIndexChanged += (_, _) =>
            _hex.BytesPerRow = _bprCombo.SelectedIndex == 0 ? 0 : int.Parse((string)_bprCombo.SelectedItem!);

        _cancelButton.Click += (_, _) => _cts?.Cancel();

        tools.Items.AddRange([
            openButton, new ToolStripSeparator(), gotoButton, findButton, new ToolStripSeparator(),
            scanButton, stringsButton, new ToolStripSeparator(),
            new ToolStripLabel("줄당 바이트:"), _bprCombo, optionsMenu,
            new ToolStripSeparator(), _progress, _cancelButton
        ]);

        // ---- status bar -------------------------------------------------
        var status = new StatusStrip();
        status.Items.AddRange([_statusFile, _statusOffset, _statusInfo]);

        // ---- right hand panel ------------------------------------------
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildAnalysisTab());
        tabs.TabPages.Add(BuildJsonTab());
        tabs.TabPages.Add(BuildStringsTab());

        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(tabs);
        rightSplit.Panel2.Controls.Add(_inspector);
        rightSplit.Panel2.Controls.Add(new Label
        {
            Text = " 데이터 인스펙터 (커서 위치 해석)",
            Dock = DockStyle.Top,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray
        });
        rightSplit.Panel2MinSize = 120;

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill };
        mainSplit.Panel1.Controls.Add(_hex);
        mainSplit.Panel2.Controls.Add(rightSplit);
        mainSplit.Panel1MinSize = 360;

        Controls.Add(mainSplit);
        Controls.Add(tools);
        Controls.Add(status);

        Shown += (_, _) =>
        {
            SetSplit(mainSplit, (int)(mainSplit.Width * 0.56));
            SetSplit(rightSplit, (int)(rightSplit.Height * 0.64));
            if (_jsonSplit != null) SetSplit(_jsonSplit, (int)(_jsonSplit.Height * 0.45));
        };
    }

    private static void SetSplit(SplitContainer split, int distance)
    {
        int span = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        int max = span - split.SplitterWidth - split.Panel2MinSize;
        if (max <= split.Panel1MinSize) return;
        split.SplitterDistance = Math.Clamp(distance, split.Panel1MinSize, max);
    }

    private TabPage BuildAnalysisTab()
    {
        var page = new TabPage("헤더 분석");
        var refreshButton = new Button { Text = "다시 분석", Dock = DockStyle.Right, Width = 90 };
        refreshButton.Click += async (_, _) => await AnalyzeAsync();

        var bar = new Panel { Dock = DockStyle.Top, Height = 30 };
        bar.Controls.Add(refreshButton);
        page.Controls.Add(_report);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildJsonTab()
    {
        var page = new TabPage("JSON");

        _jsonList.Columns.Add("오프셋", 100);
        _jsonList.Columns.Add("길이", 90);
        _jsonList.Columns.Add("종류", 60);
        _jsonList.Columns.Add("인코딩", 70);
        _jsonList.Columns.Add("깊이", 45);
        _jsonList.Columns.Add("미리보기", 900);
        _jsonList.RetrieveVirtualItem += (_, e) =>
        {
            var h = _jsonHits[e.ItemIndex];
            e.Item = new ListViewItem([
                $"0x{h.Offset:X8}", $"{h.Length:N0}", h.KindName, h.Encoding, h.Depth.ToString(), h.Preview
            ]);
        };
        _jsonList.SelectedIndexChanged += (_, _) =>
        {
            if (_jsonList.SelectedIndices.Count > 0) ShowJson(_jsonHits[_jsonList.SelectedIndices[0]], navigate: true);
        };

        var scan = new Button { Text = "스캔", Width = 70 };
        scan.Click += async (_, _) => await ScanJsonAsync();
        var save = new Button { Text = "선택 영역 저장", Width = 110 };
        save.Click += (_, _) => SaveSelectedJson();
        var copy = new Button { Text = "복사", Width = 60 };
        copy.Click += (_, _) =>
        {
            if (_jsonText.TextLength > 0)
                try { Clipboard.SetText(_jsonText.Text); } catch { }
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
        bar.Controls.AddRange([
            scan,
            new Label { Text = "최소 길이", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _jsonMinLen,
            _jsonUtf16, save, copy
        ]);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_jsonList);
        split.Panel2.Controls.Add(_jsonText);

        page.Controls.Add(split);
        page.Controls.Add(bar);
        _jsonSplit = split;
        return page;
    }

    private TabPage BuildStringsTab()
    {
        var page = new TabPage("문자열");

        _stringList.Columns.Add("오프셋", 100);
        _stringList.Columns.Add("인코딩", 70);
        _stringList.Columns.Add("길이", 60);
        _stringList.Columns.Add("내용", 900);
        _stringList.RetrieveVirtualItem += (_, e) =>
        {
            var s = _stringsView[e.ItemIndex];
            e.Item = new ListViewItem([
                $"0x{s.Offset:X8}", s.Utf16 ? "UTF-16" : "ASCII", s.ByteLength.ToString(),
                s.Text.Length > 300 ? s.Text[..300] + " ..." : s.Text
            ]);
        };
        _stringList.SelectedIndexChanged += (_, _) =>
        {
            if (_stringList.SelectedIndices.Count == 0) return;
            var s = _stringsView[_stringList.SelectedIndices[0]];
            _hex.Select(s.Offset, s.ByteLength);
        };

        var extract = new Button { Text = "추출", Width = 70 };
        extract.Click += async (_, _) => await ScanStringsAsync();
        _stringFilter.TextChanged += (_, _) => ApplyStringFilter();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
        bar.Controls.AddRange([
            extract,
            new Label { Text = "최소 길이", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _strMinLen,
            new Label { Text = "필터", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _stringFilter
        ]);

        page.Controls.Add(_stringList);
        page.Controls.Add(bar);
        return page;
    }

    // ------------------------------------------------------------------ file

    private void PromptOpen()
    {
        using var dlg = new OpenFileDialog { Title = "바이너리 파일 열기", Filter = "모든 파일 (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    public async void OpenFile(string path)
    {
        try
        {
            _cts?.Cancel();
            var sw = Stopwatch.StartNew();
            var source = ByteSourceFactory.Open(path);
            sw.Stop();

            _source?.Dispose();
            _source = source;
            _path = path;
            _hex.Source = source;

            _jsonHits = [];
            _strings = [];
            _stringsView = [];
            _jsonList.VirtualListSize = 0;
            _stringList.VirtualListSize = 0;
            _jsonText.Clear();
            _hex.ClearHighlights();

            bool mapped = source is MappedByteSource;
            Text = $"Binary Viewer - {Path.GetFileName(path)}";
            _statusFile.Text = path;
            _statusInfo.Text = $"{FileAnalyzer.Human(source.Length)} / 열기 {sw.Elapsed.TotalMilliseconds:F1} ms" +
                               (mapped ? " (메모리 맵)" : "");
            UpdateCaretStatus();
            _hex.Focus();

            await AnalyzeAsync();
            if (_autoScan.Checked && source.Length > 0) await ScanJsonAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------------ work

    private CancellationToken StartWork(string label)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _progress.Visible = true;
        _progress.Value = 0;
        _cancelButton.Visible = true;
        _statusOffset.Text = label;
        return _cts.Token;
    }

    private void EndWork()
    {
        _progress.Visible = false;
        _cancelButton.Visible = false;
        UpdateCaretStatus();
    }

    private async Task AnalyzeAsync()
    {
        if (_source is not { } src) return;
        var ct = StartWork("분석 중...");
        var sw = Stopwatch.StartNew();
        try
        {
            string path = _path;
            var result = await Task.Run(() => FileAnalyzer.Analyze(src, path, carve: true, ct), ct);
            _report.Text = result.Report + $"\r\n(분석 {sw.Elapsed.TotalMilliseconds:F0} ms)";
            _report.SelectionStart = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _report.Text = ex.Message; }
        finally { EndWork(); }
    }

    private async Task ScanJsonAsync()
    {
        if (_source is not { } src || src.Length == 0) return;
        var ct = StartWork("JSON 스캔 중...");
        var sw = Stopwatch.StartNew();
        var progress = new Progress<double>(p => _progress.Value = (int)Math.Clamp(p * 100, 0, 100));

        var scanner = new JsonScanner
        {
            MinLength = (int)_jsonMinLen.Value,
            ScanUtf16 = _jsonUtf16.Checked
        };

        try
        {
            var hits = await Task.Run(() => scanner.Scan(src, 0, src.Length, progress, ct), ct);
            _jsonHits = hits;
            _jsonList.VirtualListSize = hits.Count;
            _jsonList.Invalidate();
            _jsonText.Clear();

            _hex.SetHighlights(hits.Take(2000).Select(h =>
                new HighlightRange(h.Offset, h.Length, Color.FromArgb(0xD8, 0xF5, 0xD8), "json")));

            long bytes = hits.Sum(h => h.Length);
            _statusInfo.Text = hits.Count == 0
                ? $"JSON 없음 (스캔 {sw.Elapsed.TotalMilliseconds:F0} ms)"
                : $"JSON {hits.Count:N0}건 / {FileAnalyzer.Human(bytes)} (스캔 {sw.Elapsed.TotalMilliseconds:F0} ms)";

            // the tab may not be realised yet, so fill the pane directly instead of relying on selection
            if (hits.Count > 0) ShowJson(hits[0], navigate: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "JSON 스캔", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { EndWork(); }
    }

    private async Task ScanStringsAsync()
    {
        if (_source is not { } src || src.Length == 0) return;
        var ct = StartWork("문자열 추출 중...");
        var sw = Stopwatch.StartNew();
        var progress = new Progress<double>(p => _progress.Value = (int)Math.Clamp(p * 100, 0, 100));
        int min = (int)_strMinLen.Value;

        try
        {
            var hits = await Task.Run(() => StringScanner.Scan(src, min, true, 200_000, progress, ct), ct);
            _strings = hits;
            ApplyStringFilter();
            _statusInfo.Text = $"문자열 {hits.Count:N0}건 (추출 {sw.Elapsed.TotalMilliseconds:F0} ms)";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "문자열 추출", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { EndWork(); }
    }

    private void ApplyStringFilter()
    {
        string f = _stringFilter.Text.Trim();
        _stringsView = f.Length == 0
            ? _strings
            : _strings.Where(s => s.Text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _stringList.VirtualListSize = _stringsView.Count;
        _stringList.Invalidate();
    }

    // ------------------------------------------------------------------ json pane

    private void ShowJson(JsonHit hit, bool navigate)
    {
        if (_source is not { } src) return;

        if (navigate) _hex.Select(hit.Offset, hit.Length);
        _hex.SetHighlights(
            _jsonHits.Take(2000)
                     .Select(h => new HighlightRange(h.Offset, h.Length,
                         h.Offset == hit.Offset ? Color.FromArgb(0xB6, 0xEA, 0xB6) : Color.FromArgb(0xE6, 0xF7, 0xE6))));

        const long PrettyLimit = 4L * 1024 * 1024;
        if (hit.Length > PrettyLimit)
        {
            _jsonText.Text = JsonScanner.ReadText(src, hit.Offset, PrettyLimit, hit.Utf16) +
                             $"\r\n\r\n... ({FileAnalyzer.Human(hit.Length)} 중 앞부분 4 MB만 표시)";
            return;
        }

        string raw = JsonScanner.ReadText(src, hit.Offset, hit.Length, hit.Utf16);
        _jsonText.Text = JsonScanner.PrettyPrint(raw).ReplaceLineEndings("\r\n");
        _jsonText.SelectionStart = 0;
    }

    private void SaveSelectedJson()
    {
        if (_source is not { } src || _jsonList.SelectedIndices.Count == 0) return;
        var hit = _jsonHits[_jsonList.SelectedIndices[0]];

        using var dlg = new SaveFileDialog
        {
            Title = "JSON 영역 저장",
            Filter = "JSON (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(_path)}_0x{hit.Offset:X}.json"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string text = JsonScanner.ReadText(src, hit.Offset, hit.Length, hit.Utf16);
        File.WriteAllText(dlg.FileName, text, new UTF8Encoding(false));
        _statusInfo.Text = $"저장됨: {dlg.FileName}";
    }

    // ------------------------------------------------------------------ navigation

    private void PromptGoto()
    {
        if (_source is not { } src) return;
        using var dlg = new GoToDialog(_hex.CaretOffset, src.Length);
        if (dlg.ShowDialog(this) == DialogResult.OK) _hex.SetCaret(dlg.Offset, center: true);
    }

    private void ShowFind()
    {
        _find ??= CreateFindDialog();
        _find.Show(this);
        _find.BringToFront();
    }

    private FindDialog CreateFindDialog()
    {
        var dlg = new FindDialog();
        dlg.Find += (pattern, ignoreCase, backward) =>
        {
            _lastPattern = pattern;
            _lastIgnoreCase = ignoreCase;
            DoFind(pattern, ignoreCase, backward);
        };
        return dlg;
    }

    private void DoFind(byte[] pattern, bool ignoreCase, bool backward)
    {
        if (_source is not { } src) return;
        Cursor = Cursors.WaitCursor;
        try
        {
            long from = _hex.CaretOffset;
            long hit = backward
                ? ByteSearcher.FindBackward(src, pattern, from, ignoreCase)
                : ByteSearcher.Find(src, pattern, from + 1, ignoreCase);

            if (hit < 0 && !backward) hit = ByteSearcher.Find(src, pattern, 0, ignoreCase);   // wrap around

            if (hit < 0)
            {
                _statusInfo.Text = "찾는 패턴 없음";
                return;
            }
            _hex.Select(hit, pattern.Length);
            _statusInfo.Text = $"찾음: 0x{hit:X}";
        }
        finally { Cursor = Cursors.Default; }
    }

    private void UpdateCaretStatus()
    {
        long off = _hex.CaretOffset;
        _statusOffset.Text = _source == null
            ? "-"
            : $"오프셋 0x{off:X8} ({off:N0})" +
              (_hex.SelectionLength > 1 ? $"  선택 {_hex.SelectionLength:N0} B" : "");
        _inspector.Update(_source, off);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O: PromptOpen(); return true;
            case Keys.Control | Keys.G: PromptGoto(); return true;
            case Keys.Control | Keys.F: ShowFind(); return true;
            case Keys.Control | Keys.J: _ = ScanJsonAsync(); return true;
            case Keys.F3:
                if (_lastPattern != null) DoFind(_lastPattern, _lastIgnoreCase, false);
                else ShowFind();
                return true;
            case Keys.Shift | Keys.F3:
                if (_lastPattern != null) DoFind(_lastPattern, _lastIgnoreCase, true);
                return true;
            case Keys.Escape: _cts?.Cancel(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _source?.Dispose();
        base.OnFormClosed(e);
    }
}
