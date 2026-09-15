using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Security.Principal;

// Fixed-purpose helper. No caller-supplied service name, PID, executable, or command.
// --check is read-only; --restart is called only by an explicit UI action with RunAs.
if (args.Length != 1 || args[0] is not ("--restart" or "--check")) return 2;
try
{
    Native.SetErrorMode(3);
    using var Service = new ServiceController("VPNProService");
    Service.Refresh();
    if (args[0] == "--check") return 0;
    using var Identity = WindowsIdentity.GetCurrent();
    if (!new WindowsPrincipal(Identity).IsInRole(WindowsBuiltInRole.Administrator)) return 5;

    var OriginalId = Native.GetProcessId(Service);
    using var OriginalProcess = OriginalId == 0 ? null : Process.GetProcessById(OriginalId);
    if (OriginalProcess is not null) VerifyProcess(OriginalProcess);
    if (Service.Status != ServiceControllerStatus.Stopped)
    {
        if (Service.Status != ServiceControllerStatus.StopPending) Service.Stop();
        try { Service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8)); }
        catch (System.ServiceProcess.TimeoutException)
        {
            // Hold the original process handle to avoid killing a reused PID.
            Service.Refresh();
            var CurrentId = Native.GetProcessId(Service);
            if (OriginalProcess is null || OriginalProcess.HasExited || CurrentId != OriginalId) return 6;
            VerifyProcess(OriginalProcess);
            OriginalProcess.Kill(entireProcessTree: false);
            if (!OriginalProcess.WaitForExit(5000)) return 7;
            Service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
        }
    }
    Service.Refresh();
    if (Service.Status == ServiceControllerStatus.Stopped) Service.Start();
    Service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
    return 0;
}
catch { return 1; } // Parent shows a nonmodal error. No system crash/console dialog.

static void VerifyProcess(Process Candidate)
{
    var Expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Opera Norway AS", "VPNPro", "WindowsService.exe");
    if (!string.Equals(Candidate.MainModule?.FileName, Expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Service executable does not match the installed Opera VPN Pro path.");
    _ = Candidate.Handle;
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, State, Accepted, Win32Exit, ServiceExit, Checkpoint, WaitHint, ProcessId, Flags;
    }
    [DllImport("kernel32.dll")] internal static extern uint SetErrorMode(uint Mode);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr Handle, int Level, out ServiceStatus Status, int Size, out int Needed);
    internal static int GetProcessId(ServiceController Service)
    {
        var Handle = Service.ServiceHandle; // Owned by ServiceController.
        if (!QueryServiceStatusEx(Handle.DangerousGetHandle(), 0, out var Status, Marshal.SizeOf<ServiceStatus>(), out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return checked((int)Status.ProcessId);
    }
}
