using System.Diagnostics;
using Microsoft.Win32;

namespace VpnPro.Windows;

internal static class DesktopIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ReleasesUrl = "https://github.com/gmoddev/VpnProController/releases";
    public static bool StartupEnabled
    {
        get { using var Key = Registry.CurrentUser.OpenSubKey(RunKey); return Key?.GetValue("VpnProController") is string; }
        set
        {
            using var Key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) Key.SetValue("VpnProController", $"\"{Environment.ProcessPath}\" --startup");
            else Key.DeleteValue("VpnProController", throwOnMissingValue: false);
        }
    }

    public static async Task RestartServiceAsync()
    {
        var Helper = Path.Combine(AppContext.BaseDirectory, "Recovery", "VpnPro.Recovery.exe");
        if (!File.Exists(Helper)) throw new FileNotFoundException("Service recovery helper is missing. Reinstall the controller.");
        try
        {
            using var Process = System.Diagnostics.Process.Start(new ProcessStartInfo(Helper, "--restart")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(Helper)!
            }) ?? throw new IOException("Could not launch service recovery.");
            await Process.WaitForExitAsync();
            if (Process.ExitCode != 0) throw new IOException($"VPN Pro service recovery failed (code {Process.ExitCode}). Check that Opera VPN Pro is installed.");
            await Task.Delay(750);
        }
        catch (System.ComponentModel.Win32Exception Error) when (Error.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Service recovery was cancelled at the Windows permission prompt."); }
    }

    public static void OpenReleases() => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
}
