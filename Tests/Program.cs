using System.Collections.Concurrent;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;
using VpnPro.Core;
using VpnPro.Core.Wire;

static void Check(bool Condition, string Description)
{
    if (!Condition) throw new Exception(Description);
    Console.WriteLine($"[VPNPro:Test] PASS {Description}");
}

var Registration = Envelope.Parser.ParseFrom(Convert.FromHexString("82010a120808ace002101d1804"));
Check(Registration.ClientConnect.Reply.EventPort == 45100 && Registration.ClientConnect.Reply.Version == 4, "captured registration reply");
var Connect = new Envelope { Connect = new() { Request = new() { LocationId = "75" } } };
Check(Convert.ToHexString(Connect.ToByteArray()).Equals("ba01060a040a023735", StringComparison.OrdinalIgnoreCase), "connect matches capture");
Check(Convert.ToHexString(new Envelope { Disconnect = new() { Request = new() } }.ToByteArray()).Equals("c201020a00", StringComparison.OrdinalIgnoreCase), "disconnect matches capture");
Check(Envelope.Parser.ParseFrom(Convert.FromHexString("ca01021200")).GetConnectionStatus.Reply.Status == 0, "omitted enum means Disconnected");
Check(Envelope.Parser.ParseFrom(Convert.FromHexString("1a0412020802")).Event.ConnectionChanged.Status == 2, "connected event decodes");
Check(Envelope.Parser.ParseFrom(Convert.FromHexString("a2010412021001")).GetStoredCredentialsStatus.Reply.Valid, "captured credential validity");
Check(Convert.ToHexString(new Envelope { GetRecommendedLocation = new() { Request = new() } }.ToByteArray()) == "D201020A00", "optimal location request matches Opera capture");
var Recommendation = Envelope.Parser.ParseFrom(Convert.FromHexString("d2012912270a250a03353634121043616e616461202d20546f726f6e746f1a0243412596c39ec22dbe9f2e42"));
Check(Recommendation.GetRecommendedLocation.Reply.Location.Id == "564" && Recommendation.GetRecommendedLocation.Reply.Location.Name == "Canada - Toronto", "captured optimal location reply decodes");
var Last = Envelope.Parser.ParseFrom(Convert.FromHexString("da011312110a0f0a093139322e302e322e3112023735"));
Check(Last.GetLastUsedConnectionData.Reply.Data.LocationId == "75", "captured Express last connection has direct Data, not Nord optional wrapper");

await using (var Mock = new MockService())
await using (var Service = new OperaVpnService(Port: await Mock.Port.Task, Timeout: TimeSpan.FromMilliseconds(600)))
{
    var Snapshot = await Service.RefreshAsync();
    Check(Snapshot.ServiceAvailable && Snapshot.CredentialsValid && Snapshot.Locations.Count == 1, "read-only startup against mock");
    Check(Mock.ClientName == "VPNProController", "honest standalone client identity");
    Check(!Mock.Operations.Contains(Envelope.DetailsOneofCase.Connect) && !Mock.Operations.Contains(Envelope.DetailsOneofCase.Disconnect), "startup sends no mutation");
    var Recommended = await Service.GetRecommendedLocationAsync();
    Check(Recommended.Id == "564" && Service.Snapshot.Locations.Any(Location => Location.Id == "564"), "recommendation refreshes stale location IDs");
    Check(!Mock.Operations.Contains(Envelope.DetailsOneofCase.Connect) && !Mock.Operations.Contains(Envelope.DetailsOneofCase.Disconnect), "finding optimal location sends no VPN mutation");
    Mock.MissingRecommendation = true;
    try { await Service.GetRecommendedLocationAsync(); throw new Exception("Expected missing recommendation rejection"); }
    catch (InvalidDataException) { Check(true, "empty recommendation is rejected"); }
    Mock.MissingRecommendation = false;
    await Service.RefreshAsync();
    await Task.WhenAll(Service.RefreshAsync(), Service.RefreshAsync(), Service.RefreshAsync());
    Check(Service.Snapshot.ServiceAvailable, "concurrent shell calls serialized");
    var RegistrationsBefore = Mock.Operations.Count(Operation => Operation == Envelope.DetailsOneofCase.ClientConnect);
    await Service.ReconnectAsync();
    Check(Mock.Operations.Count(Operation => Operation == Envelope.DetailsOneofCase.ClientConnect) == RegistrationsBefore + 1, "recovery creates a new registration and event subscription");
    var EventSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Service.SnapshotChanged += State => { if (State.Status == VpnStatus.Connected) EventSeen.TrySetResult(); };
    await Task.Delay(150);
    Mock.EmitConnected = true;
    await EventSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(true, "event subscription updates snapshot");
    await Service.DisconnectAsync();
    Check(Service.Snapshot.Status == VpnStatus.Disconnected, "explicit mock disconnect");
    Mock.EmptyConnect = true;
    await Service.ConnectAsync("75");
    Check(Service.Snapshot.Status == VpnStatus.Disconnected && Service.Snapshot.Error is not null, "empty connect reply reports the observed stuck-service failure");
    Mock.EmptyConnect = false;
    await Service.ConnectAsync("75");
    Check(Service.Snapshot.Status == VpnStatus.Connected, "explicit mock connect");
    var BeforeSwitch = Mock.Operations.Count;
    await VpnCommands.ConnectOptimalAsync(Service);
    var SwitchOperations = Mock.Operations.Skip(BeforeSwitch).Where(Operation => Operation is Envelope.DetailsOneofCase.Disconnect or Envelope.DetailsOneofCase.GetRecommendedLocation or Envelope.DetailsOneofCase.Connect).ToArray();
    Check(SwitchOperations.SequenceEqual(new[] { Envelope.DetailsOneofCase.Disconnect, Envelope.DetailsOneofCase.GetRecommendedLocation, Envelope.DetailsOneofCase.Connect }), "optimal switch disconnects then queries then connects exactly once");
    Mock.Valid = false;
    BeforeSwitch = Mock.Operations.Count;
    try { await VpnCommands.ConnectOptimalAsync(Service); throw new Exception("Expected invalid credentials rejection"); }
    catch (InvalidOperationException) { Check(!Mock.Operations.Skip(BeforeSwitch).Any(Operation => Operation is Envelope.DetailsOneofCase.Disconnect or Envelope.DetailsOneofCase.Connect), "invalid credentials do not interrupt current connection"); }
    Mock.Valid = true;
    BeforeSwitch = Mock.Operations.Count;
    try { await VpnCommands.ConnectOptimalAsync(Service, new CancellationToken(true)); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(Mock.Operations.Count == BeforeSwitch, "cancelled optimal switch sends no commands"); }
    Mock.MissingRecommendation = true;
    BeforeSwitch = Mock.Operations.Count;
    try { await VpnCommands.ConnectOptimalAsync(Service); throw new Exception("Expected missing recommendation rejection"); }
    catch (InvalidDataException) { Check(!Mock.Operations.Skip(BeforeSwitch).Contains(Envelope.DetailsOneofCase.Connect), "failed recommendation never sends a guessed connect"); }
    Mock.MissingRecommendation = false;
    BeforeSwitch = Mock.Operations.Count;
    await VpnCommands.ConnectOptimalAsync(Service);
    Check(!Mock.Operations.Skip(BeforeSwitch).Contains(Envelope.DetailsOneofCase.Disconnect) && Service.Snapshot.Status == VpnStatus.Connected, "optimal connect while disconnected skips disconnect");
    Mock.DropNext = true;
    try { await Service.RefreshAsync(); throw new Exception("Expected timeout"); }
    catch (TimeoutException) { Check(!Service.Snapshot.ServiceAvailable && Service.Snapshot.Status == VpnStatus.Unknown, "timeout marks stale status unknown"); }
    var Recovered = await Service.RefreshAsync();
    Check(Recovered.ServiceAvailable, "explicit refresh recovers REQ socket after timeout");
    Mock.WrongNext = true;
    try { await Service.RefreshAsync(); throw new Exception("Expected mismatched reply"); }
    catch (InvalidDataException) { Check(true, "mismatched reply rejected"); }
    await Service.RefreshAsync();
    await Service.DisconnectAsync();
    Mock.Valid = false;
    var ConnectsBefore = Mock.Operations.Count(Operation => Operation == Envelope.DetailsOneofCase.Connect);
    try { await Service.ConnectAsync("75"); throw new Exception("Expected invalid credentials rejection"); }
    catch (InvalidOperationException) { Check(Mock.Operations.Count(Operation => Operation == Envelope.DetailsOneofCase.Connect) == ConnectsBefore, "invalid credentials prevent connect"); }
    var MutationsBefore = Mock.Operations.Count(Operation => Operation is Envelope.DetailsOneofCase.Connect or Envelope.DetailsOneofCase.Disconnect);
    await Service.DisposeAsync();
    Check(Mock.Operations.Count(Operation => Operation is Envelope.DetailsOneofCase.Connect or Envelope.DetailsOneofCase.Disconnect) == MutationsBefore, "disposing shell sends no disconnect");
}
Console.WriteLine("[VPNPro:Test] All tests passed. Only random-port mock services were controlled.");
return 0;

sealed class MockService : IAsyncDisposable
{
    public readonly TaskCompletionSource<int> Port = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly ConcurrentQueue<Envelope.DetailsOneofCase> Operations = new();
    private readonly Task Worker;
    private volatile bool Stop;
    public volatile bool DropNext;
    public volatile bool WrongNext;
    public volatile bool EmitConnected;
    public volatile bool EmptyConnect;
    public volatile bool Valid = true;
    public volatile bool MissingRecommendation;
    private bool RecommendedRequested;
    public string? ClientName;
    public MockService() => Worker = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);
    private void Run()
    {
        try
        {
            using var Events = new PublisherSocket();
            Events.Options.Linger = TimeSpan.Zero;
            var EventPort = Events.BindRandomPort("tcp://127.0.0.1");
            using var Server = new ResponseSocket();
            Server.Options.Linger = TimeSpan.Zero;
            Port.TrySetResult(Server.BindRandomPort("tcp://127.0.0.1"));
            var State = 0;
            while (!Stop)
            {
                if (EmitConnected)
                {
                    EmitConnected = false; State = 2;
                    Events.SendMoreFrame("vpnpro_event").SendFrame(new Envelope { Event = new() { ConnectionChanged = new() { Status = State } } }.ToByteArray());
                }
                if (!Server.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(20), out var Bytes)) continue;
                var Message = Envelope.Parser.ParseFrom(Bytes);
                Operations.Enqueue(Message.DetailsCase);
                if (DropNext) { DropNext = false; Thread.Sleep(850); }
                Envelope Reply;
                if (WrongNext) { WrongNext = false; Reply = new() { Disconnect = new() { Reply = new() } }; }
                else switch (Message.DetailsCase)
                {
                    case Envelope.DetailsOneofCase.ClientConnect:
                        ClientName = Message.ClientConnect.Request.ClientName;
                        Reply = new() { ClientConnect = new() { Reply = new() { ClientId = 90, EventPort = EventPort, Version = 4 } } }; break;
                    case Envelope.DetailsOneofCase.GetStoredCredentialsStatus:
                        Reply = new() { GetStoredCredentialsStatus = new() { Reply = new() { Valid = Valid } } }; break;
                    case Envelope.DetailsOneofCase.GetConnectionStatus:
                        Reply = new() { GetConnectionStatus = new() { Reply = new() { Status = State } } }; break;
                    case Envelope.DetailsOneofCase.GetLocations:
                        Reply = new() { GetLocations = new() { Reply = new() } };
                        Reply.GetLocations.Reply.Locations.Add(new Location { Id = "75", Name = "USA - New York", CountryCode = "US" });
                        if (RecommendedRequested) Reply.GetLocations.Reply.Locations.Add(new Location { Id = "564", Name = "Canada - Toronto", CountryCode = "CA" });
                        break;
                    case Envelope.DetailsOneofCase.GetRecommendedLocation:
                        RecommendedRequested = true;
                        Reply = new() { GetRecommendedLocation = new() { Reply = new() } };
                        if (!MissingRecommendation) Reply.GetRecommendedLocation.Reply.Location = new Location { Id = "564", Name = "Canada - Toronto", CountryCode = "CA" };
                        break;
                    case Envelope.DetailsOneofCase.GetLastUsedConnectionData:
                        Reply = new() { GetLastUsedConnectionData = new() { Reply = new() { Data = new() { LocationId = "75", IpAddress = "192.0.2.1" } } } }; break;
                    case Envelope.DetailsOneofCase.Connect:
                        State = EmptyConnect ? 0 : 2; Reply = new() { Connect = new() { Reply = new() { IpAddress = EmptyConnect ? "" : "192.0.2.1" } } }; break;
                    case Envelope.DetailsOneofCase.Disconnect:
                        State = 0; Reply = new() { Disconnect = new() { Reply = new() } }; break;
                    default: throw new Exception("Unexpected operation");
                }
                Server.SendFrame(Reply.ToByteArray());
            }
        }
        catch (Exception Error) { Port.TrySetException(Error); throw; }
    }
    public async ValueTask DisposeAsync() { Stop = true; await Worker.WaitAsync(TimeSpan.FromSeconds(4)); }
}
