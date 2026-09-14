using System.ComponentModel;
using System.Globalization;
using System.Text;
using BinaryViewer.Core;

namespace BinaryViewer.UI;

public sealed class GoToDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _hex = new() { Text = "16진수", Checked = true, AutoSize = true };
    private readonly RadioButton _dec = new() { Text = "10진수", AutoSize = true };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long Offset { get; private set; }

    public GoToDialog(long current, long max)
    {
        Text = "오프셋으로 이동";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(330, 110);

        _input.Text = current.ToString("X");
        _input.Font = new Font("Consolas", 10f);

        var radios = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        radios.Controls.AddRange([_hex, _dec]);

        var ok = new Button { Text = "이동", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 80 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([cancel, ok]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_input, 0, 0);
        layout.Controls.Add(radios, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            string t = _input.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            bool parsed = _hex.Checked
                ? long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)
                : long.TryParse(t, out v);
            if (!parsed || v < 0 || v > Math.Max(0, max - 1))
            {
                MessageBox.Show(this, "유효한 오프셋이 아닙니다.", "이동", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            Offset = v;
        };
    }
}

public sealed class FindDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _text = new() { Text = "텍스트", Checked = true, AutoSize = true };
    private readonly RadioButton _hexMode = new() { Text = "Hex (예: 50 4B 03 04)", AutoSize = true };
    private readonly CheckBox _utf16 = new() { Text = "UTF-16LE", AutoSize = true };
    private readonly CheckBox _ignoreCase = new() { Text = "대소문자 무시", AutoSize = true, Checked = true };

    /// <summary>Raised with the pattern to look for, and the direction.</summary>
    public event Action<byte[], bool, bool>? Find;

    public FindDialog()
    {
        Text = "찾기";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 130);
        _input.Font = new Font("Consolas", 10f);

        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        opts.Controls.AddRange([_text, _hexMode, _utf16, _ignoreCase]);

        var next = new Button { Text = "다음 찾기 (F3)", Width = 120 };
        var prev = new Button { Text = "이전 찾기", Width = 90 };
        var close = new Button { Text = "닫기", Width = 70, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([close, prev, next]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_input, 0, 0);
        layout.Controls.Add(opts, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        AcceptButton = next;
        CancelButton = close;

        next.Click += (_, _) => Raise(false);
        prev.Click += (_, _) => Raise(true);
        close.Click += (_, _) => Hide();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.FormOwnerClosing) { e.Cancel = true; Hide(); }
        };
    }

    public void FindNext() => Raise(false);

    public new void Show(IWin32Window owner)
    {
        base.Show(owner);
        _input.Focus();
        _input.SelectAll();
    }

    private void Raise(bool backward)
    {
        byte[]? pattern;
        if (_hexMode.Checked)
        {
            pattern = ByteSearcher.ParseHex(_input.Text);
            if (pattern == null)
            {
                MessageBox.Show(this, "16진수 형식이 아닙니다.", "찾기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            if (_input.Text.Length == 0) return;
            pattern = _utf16.Checked ? Encoding.Unicode.GetBytes(_input.Text) : Encoding.UTF8.GetBytes(_input.Text);
        }
        Find?.Invoke(pattern, !_hexMode.Checked && _ignoreCase.Checked, backward);
    }
}
