namespace VpnPro.Core;

public enum VpnStatus { Unknown = -1, Disconnected = 0, Connecting = 1, Connected = 2, Disconnecting = 3 }
public sealed record VpnLocation(string Id, string Name, string CountryCode);
public sealed record VpnSnapshot(bool ServiceAvailable, bool CredentialsValid, VpnStatus Status,
    IReadOnlyList<VpnLocation> Locations, string? LocationId, string? IpAddress, string? Error)
{
    public static VpnSnapshot Initial { get; } = new(false, false, VpnStatus.Unknown, [], null, null, null);
}

// No UI types, global service locator, local HTTP server or administrator requirement.
// Events arrive on the transport worker. Shells marshal them to their UI thread.
public interface IVpnService : IAsyncDisposable
{
    VpnSnapshot Snapshot { get; }
    event Action<VpnSnapshot>? SnapshotChanged;
    Task<VpnSnapshot> RefreshAsync();
    Task<VpnSnapshot> ReconnectAsync();
    Task<VpnLocation> GetRecommendedLocationAsync();
    Task ConnectAsync(string LocationId);
    Task DisconnectAsync();
}
