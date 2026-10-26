using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace L_Mechrevo.WpfP0;

public partial class App : Application
{
    private static readonly string InstanceMutexName =
        Environment.GetEnvironmentVariable("L_MECHREVO_WPF_INSTANCE") is { Length: > 0 } customInstance
            ? @"Local\" + customInstance
            : @"Local\L-Mechrevo.WpfP0";
    private static readonly string ShowMainEventName = InstanceMutexName + ".ShowMain";
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _showMainEvent;
    private RegisteredWaitHandle? _showMainWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool createdNew);
        _ownsInstanceMutex = createdNew;
        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowMainEventName, out EventWaitHandle? existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
            }
            Shutdown();
            return;
        }
        _showMainEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowMainEventName);
        _showMainWait = ThreadPool.RegisterWaitForSingleObject(
            _showMainEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is OriginalStyleWindow owner)
                {
                    owner.ShowMainFromExternal();
                }
                else if (MainWindow is Window window)
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                    window.Activate();
                }
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        ApplyFont();
        base.OnStartup(e);
        OriginalStyleWindow window = new();
        MainWindow = window;
        if (string.Equals(MechrevoLite.AppConfig.GetString("last_window_mode"), "wide", StringComparison.OrdinalIgnoreCase))
        {
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = -20000;
        }
        else
        {
            window.PositionCompactBottomRight();
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showMainWait?.Unregister(null);
        _showMainEvent?.Dispose();
        if (_ownsInstanceMutex)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch
            {
            }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ApplyFont()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
            string regular = Path.Combine(dir, "HarmonyOS_Sans_SC_Regular.ttf");
            string bold = Path.Combine(dir, "HarmonyOS_Sans_SC_Bold.ttf");
            bool installed = UserFontInstaller.EnsureInstalled(regular, bold);
            FontFamily regularFamily;
            FontFamily boldFamily;
            if (installed)
            {
                regularFamily = new FontFamily("HarmonyOS Sans SC");
                boldFamily = new FontFamily("HarmonyOS Sans SC");
            }
            else
            {
                regularFamily = File.Exists(regular)
                    ? new FontFamily(regular + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
                boldFamily = File.Exists(bold)
                    ? new FontFamily(bold + "#HarmonyOS Sans SC")
                    : new FontFamily("HarmonyOS Sans SC");
            }
            Application.Current.Resources["AppFontFamily"] = regularFamily;
            Application.Current.Resources["AppFontBoldFamily"] = boldFamily;
        }
        catch
        {
        }
    }
}
