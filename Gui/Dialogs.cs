using System.Globalization;
using Avalonia;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BinaryViewer.Core;

namespace BinaryViewer.Gui;

public sealed record FindRequest(byte[] Pattern, bool IgnoreCase, bool Backward);

internal static class DialogParts
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Monospace");

    public static Window Shell(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false
    };

    public static StackPanel Buttons(params Control[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }
}

public static class MessageDialog
{
    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var window = DialogParts.Shell(title, 460, 190);
        var ok = new Button { Content = S.Ok, MinWidth = 80, IsDefault = true };
        ok.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                DialogParts.Buttons(ok)
            }
        };
        await window.ShowDialog(owner);
    }
}

public static class GotoDialog
{
    public static async Task<long?> ShowAsync(Window owner, long current, long max)
    {
        var window = DialogParts.Shell(S.GoToTitle, 360, 200);
        long? result = null;

        var input = new TextBox { Text = current.ToString("X"), FontFamily = DialogParts.Mono };
        var hex = new RadioButton { Content = S.Hexadecimal, IsChecked = true, GroupName = "radix" };
        var dec = new RadioButton { Content = S.Decimal, GroupName = "radix" };
        var error = new TextBlock { Foreground = Brushes.Firebrick };

        var ok = new Button { Content = S.GoTo, MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = S.Cancel, MinWidth = 80, IsCancel = true };

        ok.Click += (_, _) =>
        {
            string text = (input.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            bool parsed = hex.IsChecked == true
                ? long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)
                : long.TryParse(text, out value);

            if (!parsed || value < 0 || value > Math.Max(0, max - 1))
            {
                error.Text = S.OutOfRange(Math.Max(0, max - 1));
                return;
            }
            result = value;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { hex, dec } },
                error,
                DialogParts.Buttons(cancel, ok)
            }
        };

        await window.ShowDialog(owner);
        return result;
    }
}

public static class FindDialog
{
    private static string _lastText = "";
    private static bool _lastHexMode;

    public static async Task<FindRequest?> ShowAsync(Window owner)
    {
        var window = DialogParts.Shell(S.FindTitle, 440, 230);
        FindRequest? result = null;

        var input = new TextBox { Text = _lastText, FontFamily = DialogParts.Mono };
        var asText = new RadioButton { Content = S.AsText, IsChecked = !_lastHexMode, GroupName = "mode" };
        var asHex = new RadioButton { Content = S.AsHex, IsChecked = _lastHexMode, GroupName = "mode" };
        var utf16 = new CheckBox { Content = "UTF-16LE" };
        var ignoreCase = new CheckBox { Content = S.IgnoreCase, IsChecked = true };
        var error = new TextBlock { Foreground = Brushes.Firebrick };

        var next = new Button { Content = S.FindNext, MinWidth = 90, IsDefault = true };
        var prev = new Button { Content = S.FindPrevious, MinWidth = 90 };
        var cancel = new Button { Content = S.Close, MinWidth = 70, IsCancel = true };

        void Submit(bool backward)
        {
            string text = input.Text ?? "";
            byte[]? pattern;

            if (asHex.IsChecked == true)
            {
                pattern = ByteSearcher.ParseHex(text);
                if (pattern == null)
                {
                    error.Text = S.NotHex;
                    return;
                }
            }
            else
            {
                if (text.Length == 0) return;
                pattern = utf16.IsChecked == true ? Encoding.Unicode.GetBytes(text) : Encoding.UTF8.GetBytes(text);
            }

            _lastText = text;
            _lastHexMode = asHex.IsChecked == true;
            result = new FindRequest(pattern, asHex.IsChecked != true && ignoreCase.IsChecked == true, backward);
            window.Close();
        }

        next.Click += (_, _) => Submit(false);
        prev.Click += (_, _) => Submit(true);
        cancel.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { asText, asHex } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { utf16, ignoreCase } },
                error,
                DialogParts.Buttons(cancel, prev, next)
            }
        };

        await window.ShowDialog(owner);
        return result;
    }
}
