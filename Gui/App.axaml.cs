using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BinaryViewer.Gui;

public partial class App : Application
{
    /// <summary>File passed on the command line, opened once the window exists.</summary>
    public static string? StartupFile;

    /// <summary>Language and theme remembered from the last run.</summary>
    public static Settings Settings = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Settings.StoredTheme is { } theme) RequestedThemeVariant = theme;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(StartupFile);

        base.OnFrameworkInitializationCompleted();
    }
}
