namespace VpnPro.Core;

public static class VpnCommands
{
    // Only called for an explicit switch/connect-to-optimal action. Never retries mutations.
    public static async Task<VpnLocation> ConnectOptimalAsync(IVpnService Service, CancellationToken Cancellation = default)
    {
        Cancellation.ThrowIfCancellationRequested();
        var Snapshot = await Service.RefreshAsync();
        if (!Snapshot.ServiceAvailable || !Snapshot.CredentialsValid)
            throw new InvalidOperationException("Open Opera to check VPN Pro service and sign-in status.");
        if (Snapshot.Status is not (VpnStatus.Connected or VpnStatus.Disconnected))
            throw new InvalidOperationException("Wait for the current connection change to finish.");
        Cancellation.ThrowIfCancellationRequested();
        if (Snapshot.Status == VpnStatus.Connected)
        {
            await Service.DisconnectAsync();
            var Deadline = DateTime.UtcNow.AddSeconds(15);
            while (Service.Snapshot.Status != VpnStatus.Disconnected)
            {
                Cancellation.ThrowIfCancellationRequested();
                if (!Service.Snapshot.ServiceAvailable || Service.Snapshot.Error is not null || DateTime.UtcNow >= Deadline)
                    throw new InvalidOperationException("VPN did not finish disconnecting. No new connection was requested.");
                await Task.Delay(300, Cancellation);
                await Service.RefreshAsync();
            }
        }
        Cancellation.ThrowIfCancellationRequested();
        var Location = await Service.GetRecommendedLocationAsync();
        Cancellation.ThrowIfCancellationRequested();
        await Service.ConnectAsync(Location.Id);
        return Location;
    }
}
