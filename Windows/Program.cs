using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace VpnPro.Windows;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint Mode);
    [STAThread]
    private static void Main()
    {
        try
        {
            SetErrorMode(0x0001 | 0x0002); // Never raise system fault dialogs over another app.
            AppLog.Write("[VPNPro:Startup] Starting Windows shell.");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(CallbackArgs =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App();
            });
        }
        catch (Exception Error) { AppLog.Write($"[VPNPro:Startup] {Error}"); }
    }
}

public sealed partial class App : Application
{
    private MainWindow? Window;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, Event) =>
        {
            AppLog.Write($"[VPNPro:UI] {Event.Exception}");
            Event.Handled = true;
            Window?.ShowError("An unexpected error occurred. Refresh the service to try again.");
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs Args)
    {
        Window = new MainWindow();
        Window.Activate();
        if (Environment.GetCommandLineArgs().Contains("--startup") && Window.AppWindow.Presenter is OverlappedPresenter Presenter)
            Presenter.Minimize(activateWindow: false);
    }
}

internal static class AppLog
{
    public static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnProController", "Controller.log");
    public static void Write(string Message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > 1024 * 1024) File.Move(Path, Path + ".previous", true);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {Message}{Environment.NewLine}");
        }
        catch { /* Logging must never interrupt the user. */ }
    }
}
