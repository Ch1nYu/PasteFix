using System;
using System.IO;

namespace PasteFix;

static class Program
{
    [STAThread]
    static void Main()
    {
        Log("boot");
        Application.ThreadException += (_, e) => Log("ThreadException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UnhandledException: " + e.ExceptionObject);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication());
        Log("exit");
    }

    internal static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PasteFix");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "pastefix.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} PID={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}