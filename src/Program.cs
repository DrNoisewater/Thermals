using Thermals.Core;
using Thermals.Native;
using Thermals.UI;

namespace Thermals;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\NoisewaterLabs.Thermals", out bool firstInstance);
        if (!firstInstance)
        {
            // Already running: ask the existing instance to show its window.
            int msg = NativeMethods.RegisterWindowMessage(AppHost.ShowMessageName);
            if (msg != 0) NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        bool minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        using var host = new AppHost(minimized);
        Application.Run(host);
    }
}
