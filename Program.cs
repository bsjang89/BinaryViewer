using System.Runtime.InteropServices;
using BinaryViewer.Core;
using BinaryViewer.UI;

namespace BinaryViewer;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    [STAThread]
    private static int Main(string[] args)
    {
        var files = args.Where(a => !a.StartsWith('-')).ToArray();
        bool reportMode = args.Any(a => a is "--report" or "-r");
        bool jsonMode = args.Any(a => a is "--json" or "-j");
        bool stringsMode = args.Any(a => a is "--strings" or "-s");

        if ((reportMode || jsonMode || stringsMode) && files.Length > 0)
            return RunConsole(files[0], reportMode, jsonMode, stringsMode);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => LogCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm(files.FirstOrDefault()));
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            return 1;
        }
        return 0;
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        string log = Path.Combine(Path.GetTempPath(), "BinaryViewer-crash.log");
        try { File.AppendAllText(log, $"{DateTime.Now:s}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}"); }
        catch { }
        MessageBox.Show($"{ex.Message}{Environment.NewLine}{Environment.NewLine}자세한 내용: {log}",
                        "Binary Viewer 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Headless mode, shared with the cross platform `binview` CLI.</summary>
    private static int RunConsole(string path, bool report, bool json, bool strings)
    {
        AttachConsole(-1);
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        return TextReport.Write(Console.Out, Console.Error, path, report, json, strings);
    }
}
