using System.Collections.Concurrent;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;
using VpnPro.Core.Wire;

namespace VpnPro.Core;

public sealed class OperaVpnService : IVpnService
{
    private readonly BlockingCollection<Action> Queue = new(32);
    private readonly Thread Worker;
    private readonly TaskCompletionSource Stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string Endpoint;
    private readonly TimeSpan Timeout;
    private readonly Action<string>? Log;
    private RequestSocket? Request;
    private SubscriberSocket? Subscriber;
    private VpnSnapshot Current = VpnSnapshot.Initial;
    private int Disposed;
    private DateTime NextRefresh = DateTime.MaxValue;
    public VpnSnapshot Snapshot => Volatile.Read(ref Current);
    public event Action<VpnSnapshot>? SnapshotChanged;

    public OperaVpnService(Action<string>? Log = null, int Port = 45000, TimeSpan? Timeout = null)
    {
        if (Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
        this.Log = Log;
        this.Timeout = Timeout ?? TimeSpan.FromSeconds(4);
        Endpoint = $"tcp://127.0.0.1:{Port}";
        Worker = new Thread(Run) { IsBackground = true, Name = "VPNPro transport" };
        Worker.Start();
    }

    public Task<VpnSnapshot> RefreshAsync() => Submit(() => { Refresh(); return Snapshot; });
    public Task<VpnLocation> GetRecommendedLocationAsync() => Submit(() =>
    {
        Refresh();
        var Location = Exchange(new Envelope { GetRecommendedLocation = new() { Request = new() } })
            .GetRecommendedLocation?.Reply?.Location;
        if (Location is null || string.IsNullOrWhiteSpace(Location.Id) || string.IsNullOrWhiteSpace(Location.Name))
            throw new InvalidDataException("Opera VPN Pro did not return an optimal location. Try again later.");
        // IDs can change independently of this client's cached inventory.
        var Inventory = Exchange(new Envelope { GetLocations = new() { Request = new() } }).GetLocations?.Reply
            ?? throw new InvalidDataException("Locations reply is missing.");
        var Locations = Array.AsReadOnly(Inventory.Locations.Select(Item => new VpnLocation(Item.Id, Item.Name, Item.CountryCode)).ToArray());
        var Recommended = Locations.FirstOrDefault(Item => Item.Id == Location.Id)
            ?? throw new InvalidDataException("Opera's recommended location is no longer available. Try again.");
        Publish(Snapshot with { Locations = Locations, Error = null });
        return Recommended;
    });
    public Task<VpnSnapshot> ReconnectAsync() => Submit(() =>
    {
        CloseSockets();
        Publish(VpnSnapshot.Initial);
        Refresh();
        return Snapshot;
    });
    public Task ConnectAsync(string LocationId) => Submit(() =>
    {
        Refresh();
        if (!Snapshot.CredentialsValid) throw new InvalidOperationException("Open Opera to renew or sign in to VPN Pro.");
        if (Snapshot.Status != VpnStatus.Disconnected) throw new InvalidOperationException("Disconnect before choosing a new connection.");
        if (!Snapshot.Locations.Any(Location => Location.Id == LocationId)) throw new ArgumentException("Choose an available location.");
        var Reply = Exchange(new Envelope { Connect = new() { Request = new() { LocationId = LocationId } } });
        if (Reply.Connect?.Reply is null) throw new InvalidDataException("Connect reply is missing.");
        Publish(Snapshot with { Status = VpnStatus.Connecting, LocationId = LocationId, Error = null });
        Refresh(); // Command acknowledgement is not proof of a completed tunnel.
        if (string.IsNullOrEmpty(Reply.Connect.Reply.IpAddress) && Snapshot.Status == VpnStatus.Disconnected)
        {
            Publish(Snapshot with { Error = "Opera VPN Pro did not establish a connection. Try another location or restart the service from Debug." });
        }
        return true;
    });
    public Task DisconnectAsync() => Submit(() =>
    {
        Refresh();
        if (Snapshot.Status == VpnStatus.Disconnected) return true;
        var Reply = Exchange(new Envelope { Disconnect = new() { Request = new() } });
        if (Reply.Disconnect?.Reply is null) throw new InvalidDataException("Disconnect reply is missing.");
        Refresh();
        return true;
    });

    private Task<T> Submit<T>(Func<T> Operation)
    {
        var Completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref Disposed) != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(OperaVpnService)));
        try
        {
            if (!Queue.TryAdd(() =>
            {
                try { Completion.TrySetResult(Operation()); }
                catch (Exception Error) { Fail(Error); Completion.TrySetException(Error); }
            })) Completion.TrySetException(new InvalidOperationException("Controller is busy. Try again shortly."));
        }
        catch (InvalidOperationException) { Completion.TrySetException(new ObjectDisposedException(nameof(OperaVpnService))); }
        return Completion.Task;
    }

    private void Run()
    {
        try
        {
            while (!Queue.IsCompleted)
            {
                if (Queue.TryTake(out var Operation, 50)) Operation();
                if (Volatile.Read(ref Disposed) != 0) continue;
                try
                {
                    DrainEvents();
                    if (DateTime.UtcNow >= NextRefresh) Refresh();
                }
                catch (Exception Error) { Fail(Error); }
            }
        }
        finally { CloseSockets(); Stopped.TrySetResult(); }
    }

    private void EnsureSession()
    {
        if (Request is not null) return;
        Request = new RequestSocket();
        Request.Options.Linger = TimeSpan.Zero;
        Request.Options.ReceiveHighWatermark = 64;
        Request.Connect(Endpoint);
        var Reply = Exchange(new Envelope { ClientConnect = new() { Request = new() { ClientName = "VPNProController", Version = 4 } } });
        var Registration = Reply.ClientConnect?.Reply ?? throw new InvalidDataException("Registration reply is missing.");
        if (Registration.Version != 4 || Registration.EventPort is < 1 or > 65535)
            throw new InvalidDataException("Unsupported VPN Pro service protocol. Update the controller.");
        Subscriber = new SubscriberSocket();
        Subscriber.Options.Linger = TimeSpan.Zero;
        Subscriber.Options.ReceiveHighWatermark = 256;
        Subscriber.Connect($"tcp://127.0.0.1:{Registration.EventPort}");
        Subscriber.Subscribe("vpnpro_event");
        WriteLog($"Registered client {Registration.ClientId}, protocol {Registration.Version}.");
    }

    private Envelope Exchange(Envelope Message)
    {
        if (Request is null) throw new InvalidOperationException("No service session.");
        if (!Request.TrySendFrame(Timeout, Message.ToByteArray())) throw new TimeoutException("VPN Pro service did not accept the request.");
        var Frames = new NetMQMessage();
        if (!Request.TryReceiveMultipartMessage(Timeout, ref Frames))
            throw new TimeoutException("VPN Pro service did not reply. Refresh status before retrying a connection change.");
        if (Frames.FrameCount != 1 || Frames[0].MessageSize > 1024 * 1024) throw new InvalidDataException("Unexpected service message framing.");
        var Reply = Envelope.Parser.ParseFrom(Frames[0].ToByteArray());
        if (Reply.Error is not null) throw new InvalidOperationException($"VPN Pro service rejected the request (code {Reply.Error.ErrorCode}). Open Opera to check your account.");
        if (Reply.DetailsCase != Message.DetailsCase) throw new InvalidDataException("Service reply does not match the request.");
        return Reply;
    }

    private void Refresh()
    {
        EnsureSession();
        var Credentials = Exchange(new Envelope { GetStoredCredentialsStatus = new() { Request = new() } }).GetStoredCredentialsStatus?.Reply
            ?? throw new InvalidDataException("Credential status reply is missing.");
        var Status = Exchange(new Envelope { GetConnectionStatus = new() { Request = new() } }).GetConnectionStatus?.Reply
            ?? throw new InvalidDataException("Connection status reply is missing.");
        var Locations = Snapshot.Locations;
        if (Locations.Count == 0)
        {
            var Reply = Exchange(new Envelope { GetLocations = new() { Request = new() } }).GetLocations?.Reply
                ?? throw new InvalidDataException("Locations reply is missing.");
            Locations = Array.AsReadOnly(Reply.Locations.Select(Location => new VpnLocation(Location.Id, Location.Name, Location.CountryCode)).ToArray());
        }
        var Last = Exchange(new Envelope { GetLastUsedConnectionData = new() { Request = new() } }).GetLastUsedConnectionData?.Reply
            ?? throw new InvalidDataException("Last connection reply is missing.");
        Publish(new(true, Credentials.ResultCase == CredentialsReply.ResultOneofCase.Valid && Credentials.Valid,
            ParseStatus(Status.Status), Locations, Last.Data?.LocationId, Last.Data?.IpAddress, null));
        NextRefresh = DateTime.UtcNow.AddSeconds(10);
    }

    private void DrainEvents()
    {
        if (Subscriber is null) return;
        for (var Count = 0; Count < 32; Count++)
        {
            var Frames = new NetMQMessage();
            if (!Subscriber.TryReceiveMultipartMessage(TimeSpan.Zero, ref Frames)) return;
            if (Frames.FrameCount != 2 || Frames[0].ConvertToString() != "vpnpro_event" || Frames[1].MessageSize > 1024 * 1024)
                throw new InvalidDataException("Unexpected event framing.");
            var Message = Envelope.Parser.ParseFrom(Frames[1].ToByteArray());
            if (Message.Event?.ServiceStop is not null) throw new IOException("VPN Pro service stopped.");
            if (Message.Event?.ConnectionChanged is { } Change)
                Publish(Snapshot with { Status = ParseStatus(Change.Status) });
            // Re-query after an event burst; unknown future event fields are safely ignored.
            NextRefresh = DateTime.UtcNow.AddMilliseconds(250);
        }
    }

    private static VpnStatus ParseStatus(int Value) => Value is >= 0 and <= 3 ? (VpnStatus)Value : VpnStatus.Unknown;
    private void Publish(VpnSnapshot Value)
    {
        Volatile.Write(ref Current, Value);
        if (SnapshotChanged is not { } Handlers) return;
        foreach (Action<VpnSnapshot> Handler in Handlers.GetInvocationList())
            try { Handler(Value); } catch (Exception Error) { WriteLog($"Shell callback failed: {Error.GetType().Name}"); }
    }
    private void Fail(Exception Error)
    {
        CloseSockets();
        NextRefresh = DateTime.MaxValue; // Explicit refresh re-registers; never replay a mutation.
        Publish(Snapshot with { ServiceAvailable = false, CredentialsValid = false, Status = VpnStatus.Unknown, Error = Error.Message });
        WriteLog(Error.Message);
    }
    private void WriteLog(string Message) { try { Log?.Invoke($"[VPNPro:Transport] {Message}"); } catch { } }
    private void CloseSockets()
    {
        Subscriber?.Dispose(); Subscriber = null;
        Request?.Dispose(); Request = null;
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref Disposed, 1) == 0) Queue.CompleteAdding();
        await Stopped.Task.ConfigureAwait(false);
        // Closing this shell never sends VPN Disconnect or stops Opera's service.
    }
}
