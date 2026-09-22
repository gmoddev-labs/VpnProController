using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VpnPro.Core;

namespace VpnPro.Windows;

internal sealed class MainWindow : Window
{
    private readonly IVpnService Service;
    private TrayIcon? Tray;
    private readonly TextBlock StatusText = new() { Text = "Checking service", FontSize = 34, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock DetailText = new() { Text = "Reading Opera VPN Pro…", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private readonly TextBlock AccountText = new() { FontSize = 12, Opacity = 0.7 };
    private readonly ComboBox LocationPicker = new() { PlaceholderText = "Choose a location", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Name" };
    private readonly Button ConnectButton = new() { Content = "Connect", HorizontalAlignment = HorizontalAlignment.Stretch, Height = 44, IsEnabled = false };
    private readonly Button RefreshButton = new() { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button OptimalButton = new() { Content = "Find optimal location", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock OptimalText = new() { Text = "Uses Opera’s recommendation; no measured ping is returned.", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Button RecoveryButton = new() { Content = "Force restart VPN Pro service", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly InfoBar ErrorBar = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Warning };
    private readonly ProgressRing Progress = new() { Width = 20, Height = 20, IsActive = true };
    private bool Busy;
    private bool Closing;
    private IReadOnlyList<VpnLocation>? DisplayedLocations;

    public MainWindow() : this(new OperaVpnService(AppLog.Write)) { }
    internal MainWindow(IVpnService Service)
    {
        this.Service = Service;
        Title = "VPN Pro Controller";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(470, 760));
        SystemBackdrop = new MicaBackdrop();
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Connected.ico"));
            Tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this),
                () => DispatcherQueue.TryEnqueue(() =>
                {
                    if (Closing) return;
                    if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter Presenter) Presenter.Restore();
                    Activate();
                }),
                () => DispatcherQueue.TryEnqueue(() => { if (!Closing) Close(); }));
        }
        catch (Exception Error) { AppLog.Write($"[VPNPro:Tray] {Error.Message}"); }
        var Root = new StackPanel { Padding = new Thickness(28), Spacing = 22 };
        var Heading = new StackPanel { Spacing = 5 };
        Heading.Children.Add(new TextBlock { Text = "VPN PRO", FontSize = 13, FontWeight = FontWeights.SemiBold, CharacterSpacing = 150 });
        Heading.Children.Add(new TextBlock { Text = "Your connection, at a glance.", FontSize = 14, Opacity = 0.65 });
        Root.Children.Add(Heading);

        var Card = new StackPanel { Spacing = 12, Padding = new Thickness(20) };
        var StatusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        StatusRow.Children.Add(Progress);
        StatusRow.Children.Add(new TextBlock { Text = "Opera VPN Pro service", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        Card.Children.Add(StatusRow);
        Card.Children.Add(StatusText);
        Card.Children.Add(DetailText);
        Root.Children.Add(new Border { Child = Card, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.2 }, Background = new SolidColorBrush(Colors.Gray) { Opacity = 0.08 } });

        var Locations = new StackPanel { Spacing = 8 };
        Locations.Children.Add(new TextBlock { Text = "Location", FontWeight = FontWeights.SemiBold });
        Locations.Children.Add(LocationPicker);
        Locations.Children.Add(OptimalButton);
        Locations.Children.Add(OptimalText);
        Locations.Children.Add(new TextBlock { Text = "Disconnect before switching locations.", FontSize = 12, Opacity = 0.65 });
        Root.Children.Add(Locations);
        ConnectButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        Root.Children.Add(ConnectButton);
        Root.Children.Add(ErrorBar);
        var Footer = new Grid();
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AccountText.VerticalAlignment = VerticalAlignment.Center;
        Footer.Children.Add(AccountText);
        Grid.SetColumn(RefreshButton, 1);
        Footer.Children.Add(RefreshButton);
        Root.Children.Add(Footer);
        var StartupToggle = new ToggleSwitch { Header = "Launch at Windows sign-in", OffContent = "Off", OnContent = "Start minimized" };
        try { StartupToggle.IsOn = DesktopIntegration.StartupEnabled; }
        catch (Exception Error) { AppLog.Write($"[VPNPro:Startup] {Error.Message}"); }
        StartupToggle.Toggled += (_, _) =>
        {
            try { DesktopIntegration.StartupEnabled = StartupToggle.IsOn; }
            catch (Exception Error) { ShowError(Error.Message); }
        };
        Root.Children.Add(StartupToggle);
        var DebugPanel = new StackPanel { Spacing = 10 };
        DebugPanel.Children.Add(new TextBlock { Text = "Restarts Opera’s service and interrupts the VPN. If normal stop hangs, force-stops only the verified service process. Windows administrator approval is required.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        DebugPanel.Children.Add(RecoveryButton);
        RecoveryButton.Click += async (_, _) => await Execute(async () =>
        {
            await DesktopIntegration.RestartServiceAsync();
            await Service.ReconnectAsync();
        });
        Root.Children.Add(new Expander { Header = "Debug / recovery", Content = DebugPanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var Version = typeof(MainWindow).Assembly.GetName().Version;
        var UpdatesButton = new HyperlinkButton { Content = $"Releases & updates · v{Version?.Major}.{Version?.Minor}.{Version?.Build}", Padding = new Thickness(0) };
        UpdatesButton.Click += (_, _) => { try { DesktopIntegration.OpenReleases(); } catch (Exception Error) { ShowError(Error.Message); } };
        Root.Children.Add(UpdatesButton);
        Root.Children.Add(new TextBlock { Text = "Uses your installed Opera VPN Pro service.\nClosing this window leaves the VPN running.", FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        Content = new ScrollViewer { Content = Root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Service.SnapshotChanged += OnSnapshot;
        OptimalButton.Click += async (_, _) => await Execute(async () =>
        {
            OptimalText.Text = "Asking Opera for its optimal location…";
            try
            {
                var Recommended = await Service.GetRecommendedLocationAsync();
                if (Closing) return;
                Render(Service.Snapshot);
                LocationPicker.SelectedItem = Service.Snapshot.Locations.First(Location => Location.Id == Recommended.Id);
                OptimalText.Text = $"Opera recommends {Recommended.Name}. Selected for your next connection.";
            }
            catch
            {
                if (!Closing) OptimalText.Text = "No recommendation received. Your connection was not changed.";
                throw;
            }
        });
        RefreshButton.Click += async (_, _) => await Execute(async () => { await Service.RefreshAsync(); });
        ConnectButton.Click += async (_, _) => await Execute(async () =>
        {
            if (Service.Snapshot.Status == VpnStatus.Connected) await Service.DisconnectAsync();
            else if (LocationPicker.SelectedItem is VpnLocation Location) await Service.ConnectAsync(Location.Id);
        });
        LocationPicker.SelectionChanged += (_, _) => Render(Service.Snapshot);
        Closed += async (_, _) =>
        {
            Closing = true;
            Tray?.Dispose();
            Service.SnapshotChanged -= OnSnapshot;
            await Service.DisposeAsync();
        };
        Root.Loaded += async (_, _) => await Execute(async () => { await Service.RefreshAsync(); });
    }

    private void OnSnapshot(VpnSnapshot Snapshot) => DispatcherQueue.TryEnqueue(() => { if (!Closing) Render(Snapshot); });
    private async Task Execute(Func<Task> Action)
    {
        if (Busy || Closing) return;
        Busy = true;
        ErrorBar.IsOpen = false;
        Render(Service.Snapshot);
        try { await Action(); }
        catch (Exception Error) { if (!Closing) ShowError(Error.Message); }
        finally { Busy = false; if (!Closing) Render(Service.Snapshot); }
    }
    public void ShowError(string Message) { ErrorBar.Message = Message; ErrorBar.IsOpen = true; }
    private void Render(VpnSnapshot Snapshot)
    {
        Tray?.Update(Snapshot);
        StatusText.Text = Snapshot.ServiceAvailable ? Snapshot.Status.ToString() : Busy ? "Checking service" : "Service unavailable";
        StatusText.FontSize = Snapshot.ServiceAvailable ? 34 : 27;
        Progress.IsActive = Busy || Snapshot.Status is VpnStatus.Connecting or VpnStatus.Disconnecting;
        Progress.Visibility = Progress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        var CurrentLocation = Snapshot.Locations.FirstOrDefault(Location => Location.Id == Snapshot.LocationId)?.Name;
        DetailText.Text = Snapshot.Status == VpnStatus.Connected
            ? $"{CurrentLocation ?? "Connected location"}\n{Snapshot.IpAddress}"
            : Snapshot.ServiceAvailable ? "Choose a location when you’re ready." : "Make sure Opera VPN Pro is installed and its service is running.";
        AccountText.Text = Snapshot.ServiceAvailable ? Snapshot.CredentialsValid ? "Opera credentials valid" : "Sign in through Opera" : "Waiting for service";
        if (!ReferenceEquals(DisplayedLocations, Snapshot.Locations))
        {
            var SelectedId = (LocationPicker.SelectedItem as VpnLocation)?.Id ?? Snapshot.LocationId;
            DisplayedLocations = Snapshot.Locations;
            LocationPicker.ItemsSource = Snapshot.Locations;
            LocationPicker.SelectedItem = Snapshot.Locations.FirstOrDefault(Location => Location.Id == SelectedId) ?? Snapshot.Locations.FirstOrDefault();
        }
        LocationPicker.IsEnabled = !Busy && Snapshot.ServiceAvailable && Snapshot.Status == VpnStatus.Disconnected;
        OptimalButton.IsEnabled = !Busy && Snapshot.ServiceAvailable && Snapshot.Status == VpnStatus.Disconnected;
        ConnectButton.Content = Snapshot.Status == VpnStatus.Connected ? "Disconnect" : Snapshot.Status == VpnStatus.Connecting ? "Connecting…" : Snapshot.Status == VpnStatus.Disconnecting ? "Disconnecting…" : "Connect";
        ConnectButton.IsEnabled = !Busy && Snapshot.ServiceAvailable && (Snapshot.Status == VpnStatus.Connected ||
            Snapshot.Status == VpnStatus.Disconnected && Snapshot.CredentialsValid && LocationPicker.SelectedItem is VpnLocation);
        RefreshButton.IsEnabled = !Busy;
        RecoveryButton.IsEnabled = !Busy;
        if (Snapshot.Error is { } Error) ShowError(Error);
        else if (Snapshot.ServiceAvailable && !Snapshot.CredentialsValid) ShowError("Open Opera to sign in or renew VPN Pro. This controller does not manage your account.");
    }
}
