using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MusicWidget;

public partial class App : Application
{
    private const string MutexName = "MusicWidget.SingleInstance.v1";
    private const string ShowEventName = "MusicWidget.Show.v1";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isNew);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _ownsMutex = isNew;

        if (!isNew)
        {
            // Another instance is already running: ask it to show, then exit.
            _showEvent.Set();
            Shutdown();
            return;
        }

        var window = new MainWindow();

        // Background thread waits for "show" signals from future launches.
        var listener = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                Dispatcher.Invoke(() => window.SummonFromTray());
            }
        })
        { IsBackground = true };
        listener.Start();

        // We manage visibility ourselves; don't quit when the window hides.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) { try { _mutex?.ReleaseMutex(); } catch { } }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
