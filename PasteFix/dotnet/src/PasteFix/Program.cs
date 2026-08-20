using System;
using System.IO;
using System.Threading;

namespace PasteFix;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, "PasteFix_App_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Log("Another instance of PasteFix is already running. Exiting.");
            return;
        }

        Log("boot");
        Application.ThreadException += (_, e) => Log("ThreadException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UnhandledException: " + e.ExceptionObject);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication());
        Log("exit");
        _mutex.ReleaseMutex();
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

