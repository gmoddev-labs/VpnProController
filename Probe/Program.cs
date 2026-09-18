using System.Text.Json;
using VpnPro.Core;

if (args is not ["--read-only"] && args is not ["--recommendation"])
{
    Console.Error.WriteLine("Usage: VpnPro.Probe --read-only | --recommendation");
    return 2;
}
try
{
    await using var Service = new OperaVpnService(Console.Error.WriteLine);
    var Snapshot = await Service.RefreshAsync();
    if (args is ["--recommendation"])
    {
        var Recommendation = await Service.GetRecommendedLocationAsync();
        Console.WriteLine(JsonSerializer.Serialize(Recommendation));
        return 0;
    }
    Console.WriteLine(JsonSerializer.Serialize(Snapshot, new JsonSerializerOptions { WriteIndented = true }));
    await Task.Delay(1500); // Exercise the event subscriber without sending a VPN mutation.
    return Snapshot.ServiceAvailable ? 0 : 1;
}
catch (Exception Error)
{
    Console.Error.WriteLine($"[VPNPro:Probe] {Error.Message}");
    return 1;
}
