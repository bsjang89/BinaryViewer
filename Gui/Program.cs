using System.Text;
using Avalonia;
using BinaryViewer.Core;

namespace BinaryViewer.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? langArg = args.FirstOrDefault(a => a.StartsWith("--lang="));
        bool langFromCommandLine = langArg != null && S.TryParse(langArg[7..], out var language) && Set(language);

        App.Settings = Settings.Load();
        App.Settings.ApplyLanguage(langFromCommandLine);

        var files = args.Where(a => !a.StartsWith('-')).ToArray();
        bool report = args.Any(a => a is "--report" or "-r");
        bool json = args.Any(a => a is "--json" or "-j");
        bool strings = args.Any(a => a is "--strings" or "-s");

        // same headless output as the binview CLI, so the GUI binary is usable over SSH too
        if ((report || json || strings) && files.Length > 0)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            return TextReport.Write(Console.Out, Console.Error, files[0], report, json, strings);
        }

        App.StartupFile = files.FirstOrDefault();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static bool Set(Language language)
    {
        S.Current = language;
        return true;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .WithInterFont()
                  .LogToTrace();
}
