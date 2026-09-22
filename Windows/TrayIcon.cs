using System.Runtime.InteropServices;
using VpnPro.Core;

namespace VpnPro.Windows;

// Owned by the WinUI thread. Native handles and the subclass delegate live together.
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly nint Window;
    private readonly Action Show;
    private readonly Action Exit;
    private readonly Action? ToggleConnection;
    private readonly Action? SwitchOptimal;
    private VpnSnapshot Snapshot = VpnSnapshot.Initial;
    private bool Busy;
    private bool HasLocation;
    private readonly SubclassProc Callback;
    private readonly Dictionary<string, nint> Icons = new();
    private readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData Data;
    private bool Added;
    private bool Disposed;
    private string? CurrentState;
    private string? CurrentTip;

    public TrayIcon(nint Window, Action Show, Action Exit, Action? ToggleConnection = null, Action? SwitchOptimal = null)
    {
        this.Window = Window;
        this.Show = Show;
        this.Exit = Exit;
        this.ToggleConnection = ToggleConnection;
        this.SwitchOptimal = SwitchOptimal;
        Callback = HandleMessage;
        Data = new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = Window, Id = 1,
            Flags = 1 | 2 | 4 | 0x80, CallbackMessage = CallbackMessage, Tip = "VPN Pro Controller",
            Info = "", InfoTitle = "" };
        try
        {
            var Size = GetSystemMetricsForDpi(49, GetDpiForWindow(Window)); // SM_CXSMICON
            foreach (var State in new[] { "Connected", "Disconnected", "Connecting", "Error" })
            {
                var Handle = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", State + ".ico"), 1, Size, Size, 0x10);
                if (Handle == 0) throw new InvalidOperationException($"Cannot load {State} tray icon.");
                Icons.Add(State, Handle);
            }
            if (!SetWindowSubclass(Window, Callback, 1, 0)) throw new InvalidOperationException("Cannot attach tray callback.");
            Update(VpnSnapshot.Initial);
        }
        catch { Dispose(); throw; }
    }

    public void Update(VpnSnapshot Snapshot, bool Busy = false, bool HasLocation = false)
    {
        if (Disposed) return;
        this.Snapshot = Snapshot;
        this.Busy = Busy;
        this.HasLocation = HasLocation;
        var Failed = !Snapshot.ServiceAvailable || Snapshot.Error is not null || Snapshot.Status == VpnStatus.Unknown
            || (!Snapshot.CredentialsValid && Snapshot.Status == VpnStatus.Disconnected);
        var State = Failed ? "Error" : Snapshot.Status switch
        {
            VpnStatus.Connected => "Connected",
            VpnStatus.Connecting or VpnStatus.Disconnecting => "Connecting",
            _ => "Disconnected"
        };
        var Detail = !Snapshot.ServiceAvailable ? "Service unavailable / checking" : Snapshot.Error is not null ? "Error - open controller"
            : !Snapshot.CredentialsValid && Snapshot.Status == VpnStatus.Disconnected ? "Sign in through Opera" : Snapshot.Status.ToString();
        var Tip = $"VPN Pro Controller - {Detail}";
        if (Added && State == CurrentState && Tip == CurrentTip) return;
        CurrentState = State;
        CurrentTip = Tip;
        Data.Icon = Icons[State];
        Data.Tip = Tip;
        if (Added && Shell_NotifyIcon(1, ref Data)) return; // NIM_MODIFY
        Added = Shell_NotifyIcon(0, ref Data); // NIM_ADD; retry after shell startup/restart
        if (Added)
        {
            Data.Version = 4;
            Shell_NotifyIcon(4, ref Data); // NIM_SETVERSION
        }
        else AppLog.Write("[VPNPro:Tray] Notification area unavailable; will retry on the next state refresh.");
    }

    private nint HandleMessage(nint Handle, uint Message, nuint WParam, nint LParam, nuint Id, nuint Reference)
    {
        try
        {
            if (Message == TaskbarCreated)
            {
                Added = false;
                if (!Disposed)
                {
                    Added = Shell_NotifyIcon(0, ref Data);
                    if (Added) { Data.Version = 4; Shell_NotifyIcon(4, ref Data); }
                }
            }
            if (Message == CallbackMessage && !Disposed)
            {
                var Event = (uint)(LParam.ToInt64() & 0xffff);
                if (Event is 0x400 or 0x401) Show(); // NIN_SELECT / NIN_KEYSELECT
                else if (Event == 0x7b) ShowMenu(WParam); // WM_CONTEXTMENU, v4 screen coordinates
                return 0;
            }
        }
        catch (Exception Error) { AppLog.Write($"[VPNPro:Tray] {Error.Message}"); }
        return DefSubclassProc(Handle, Message, WParam, LParam);
    }

    private bool CanToggle => !Busy && Snapshot.ServiceAvailable && ToggleConnection is not null &&
        (Snapshot.Status == VpnStatus.Connected || Snapshot.Status == VpnStatus.Disconnected && Snapshot.CredentialsValid && HasLocation);
    private bool CanSwitch => !Busy && Snapshot.ServiceAvailable && Snapshot.CredentialsValid && SwitchOptimal is not null &&
        Snapshot.Status is VpnStatus.Connected or VpnStatus.Disconnected;

    private nint BuildMenu()
    {
        var Menu = CreatePopupMenu();
        if (Menu == 0) return 0;
        AppendMenu(Menu, 0, 1, "Show controller");
        AppendMenu(Menu, CanToggle ? 0u : 1u, 3, Snapshot.Status switch
        {
            VpnStatus.Connected => "Disconnect",
            VpnStatus.Connecting => "Connecting…",
            VpnStatus.Disconnecting => "Disconnecting…",
            _ => "Connect"
        });
        AppendMenu(Menu, CanSwitch ? 0u : 1u, 4, Snapshot.Status == VpnStatus.Connected ? "Switch to optimal region" : "Connect to optimal region");
        AppendMenu(Menu, 0x800, 0, "");
        AppendMenu(Menu, 0, 2, "Exit controller (leave VPN running)");
        return Menu;
    }

    private void ShowMenu(nuint Position)
    {
        var Menu = BuildMenu();
        if (Menu == 0) return;
        try
        {
            SetForegroundWindow(Window);
            var X = unchecked((short)((ulong)Position & 0xffff));
            var Y = unchecked((short)(((ulong)Position >> 16) & 0xffff));
            var Command = TrackPopupMenu(Menu, 0x100 | 0x2, X, Y, 0, Window, 0);
            PostMessage(Window, 0, 0, 0);
            if (Command == 1) Show();
            else if (Command == 2) Exit();
            else if (Command == 3 && CanToggle) ToggleConnection?.Invoke();
            else if (Command == 4 && CanSwitch) SwitchOptimal?.Invoke();
        }
        finally { DestroyMenu(Menu); }
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        if (Added) Shell_NotifyIcon(2, ref Data); // NIM_DELETE
        RemoveWindowSubclass(Window, Callback, 1);
        foreach (var Icon in Icons.Values) DestroyIcon(Icon);
        Icons.Clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id; public uint Flags; public uint CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint Window, uint Message, nuint WParam, nint LParam, nuint Id, nuint Reference);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint Message, ref NotifyIconData Data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint Window, SubclassProc Callback, nuint Id, nuint Reference);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint Window, SubclassProc Callback, nuint Id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint Window, uint Message, nuint WParam, nint LParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string Name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint Instance, string Name, uint Type, int Width, int Height, uint Flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint Icon);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint Window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int Index, uint Dpi);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint Menu, uint Flags, nuint Id, string Text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint Menu, uint Flags, int X, int Y, int Reserved, nint Window, nint Rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint Menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint Window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint Window, uint Message, nuint WParam, nint LParam);
}
