using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MusicIsland;

public partial class App : Application
{
    const string InstanceName = "MusicIsland.SingleInstance";

    Mutex? instanceMutex;
    EventWaitHandle? showSettingsSignal;

    /// <summary>Raised (on the UI thread) when someone launches a second copy.</summary>
    public static event Action? SecondInstanceLaunched;

    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex = new Mutex(true, InstanceName, out bool first);

        if (!first)
        {
            // Already running: ask that copy to open Settings instead of adding a second island.
            // We were launched by the user, so we may hand it permission to come to the front.
            try
            {
                AllowSetForegroundWindow(-1);
                EventWaitHandle.OpenExisting(InstanceName + ".Show").Set();
            }
            catch
            {
            }

            Shutdown();
            return;
        }

        showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        new Thread(() =>
        {
            while (showSettingsSignal.WaitOne())
                Dispatcher.BeginInvoke(new Action(() => SecondInstanceLaunched?.Invoke()));
        })
        { IsBackground = true, Name = "SingleInstanceListener" }.Start();

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            Log.Error("Fatal exception", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Starting MusicIsland {UpdateService.CurrentVersion}");

        base.OnStartup(e);

        // Created here rather than via StartupUri so a second instance never builds a window
        new MainWindow().Show();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Exiting");
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
