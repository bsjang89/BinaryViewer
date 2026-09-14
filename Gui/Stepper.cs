using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BinaryViewer.Gui;

/// <summary>
/// A macOS style stepper: minus on the left, the editable value in the middle, plus on the right.
/// Replaces NumericUpDown, whose default template stacks both chevrons on one side and draws
/// different corner radii per visual state.
/// </summary>
public sealed class Stepper : Border
{
    private readonly TextBox _input;
    private readonly Button _minus;
    private readonly Button _plus;
    private int _value;
    private bool _updatingText;

    public event EventHandler? ValueChanged;

    public int Minimum { get; set; } = int.MinValue;
    public int Maximum { get; set; } = int.MaxValue;
    public int Step { get; set; } = 1;

    public int Value
    {
        get => _value;
        set => Apply(value, updateText: true);
    }

    public Stepper()
    {
        CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1);
        MinHeight = 28;
        ClipToBounds = true;

        _minus = MakeButton("−", new CornerRadius(6, 0, 0, 6));
        _plus = MakeButton("+", new CornerRadius(0, 6, 6, 0));

        _input = new TextBox
        {
            Text = "0",
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0, 3, 0, 3),
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12.5
        };

        _minus.Click += (_, _) => Apply(_value - Step, updateText: true);
        _plus.Click += (_, _) => Apply(_value + Step, updateText: true);

        _input.TextChanged += (_, _) =>
        {
            if (_updatingText) return;
            if (int.TryParse(_input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                Apply(parsed, updateText: false);
        };
        _input.LostFocus += (_, _) => Apply(_value, updateText: true);
        _input.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Up: Apply(_value + Step, updateText: true); e.Handled = true; break;
                case Key.Down: Apply(_value - Step, updateText: true); e.Handled = true; break;
                case Key.Enter: Apply(_value, updateText: true); e.Handled = true; break;
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_minus, 0);
        Grid.SetColumn(_input, 1);
        Grid.SetColumn(_plus, 2);
        grid.Children.Add(_minus);
        grid.Children.Add(_input);
        grid.Children.Add(_plus);
        Child = grid;
    }

    private static Button MakeButton(string glyph, CornerRadius corners) => new()
    {
        Content = glyph,
        Classes = { "stepper" },
        CornerRadius = corners,
        Width = 30,
        FontSize = 14,
        Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private void Apply(int candidate, bool updateText)
    {
        int clamped = Math.Clamp(candidate, Minimum, Maximum);
        bool changed = clamped != _value;
        _value = clamped;

        if (updateText)
        {
            _updatingText = true;
            _input.Text = clamped.ToString(CultureInfo.InvariantCulture);
            _updatingText = false;
        }

        _minus.IsEnabled = clamped > Minimum;
        _plus.IsEnabled = clamped < Maximum;
        if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Apply(_value, updateText: true);      // clamp once Minimum/Maximum have been set from XAML
    }
}
