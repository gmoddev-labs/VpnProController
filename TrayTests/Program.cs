using System.Reflection;
using System.Runtime.InteropServices;
using VpnPro.Core;
using VpnPro.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // The form is never shown or activated. This test never contacts the VPN.
        using var Host = new Form();
        var Handle = Host.Handle;
        var Shows = 0;
        using var Tray = new TrayIcon(Handle, () => Shows++, () => { });
        var Identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = Handle, Id = 1 };
        Check(Shell_NotifyIconGetRect(ref Identifier, out _) == 0, "native tray registration");
        var Snapshot = new VpnSnapshot(true, true, VpnStatus.Disconnected, [], null, null, null);
        foreach (var (Status, Expected) in new[] { (VpnStatus.Disconnected, "Disconnected"), (VpnStatus.Connecting, "Connecting"),
            (VpnStatus.Connected, "Connected"), (VpnStatus.Disconnecting, "Connecting"), (VpnStatus.Unknown, "Error") })
        {
            Tray.Update(Snapshot with { Status = Status });
            Check(State(Tray) == Expected && Shell_NotifyIconGetRect(ref Identifier, out _) == 0, $"native icon update: {Status}");
        }
        Tray.Update(Snapshot with { Error = "Test error" });
        Check(State(Tray) == "Error", "reported service error");
        Tray.Update(Snapshot with { ServiceAvailable = false });
        Check(State(Tray) == "Error", "unavailable service");
        Tray.Update(Snapshot with { CredentialsValid = false });
        Check(State(Tray) == "Error", "invalid credentials while disconnected");
        SendMessage(Handle, 0x8001, 0, 0x400);
        SendMessage(Handle, 0x8001, 0, 0x401);
        Check(Shows == 2, "mouse and keyboard activation callbacks");
        // Remove only our test icon, then deliver Explorer's restart message.
        var DataField = typeof(TrayIcon).GetField("Data", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var Data = DataField.GetValue(Tray)!;
        typeof(TrayIcon).GetMethod("Shell_NotifyIcon", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [2u, Data]);
        Check(Shell_NotifyIconGetRect(ref Identifier, out _) != 0, "simulated shell loss");
        SendMessage(Handle, RegisterWindowMessage("TaskbarCreated"), 0, 0);
        Check(Shell_NotifyIconGetRect(ref Identifier, out _) == 0, "registration restored after shell restart message");
        Tray.Dispose();
        Tray.Dispose();
        Check(Shell_NotifyIconGetRect(ref Identifier, out _) != 0, "idempotent disposal removes tray icon");
        return 0;
    }
    private static string State(TrayIcon Tray) => (string)typeof(TrayIcon).GetField("CurrentState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Tray)!;
    private static void Check(bool Passed, string Description)
    {
        if (!Passed) throw new InvalidOperationException(Description);
        Console.WriteLine($"[VPNPro:TrayTest] PASS {Description}");
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier Identifier, out Rect Rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string Name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint Window, uint Message, nuint WParam, nint LParam);
}

namespace VpnPro.Windows
{
    internal static class AppLog { public static void Write(string Message) => Console.Error.WriteLine(Message); }
}
