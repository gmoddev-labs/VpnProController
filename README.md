# VPN Pro Controller

A small WinUI 3 shell over an independent .NET controller for the legitimately installed Opera VPN Pro service. Opera continues to own credentials, subscription enforcement, tunnel operation and updates. This is an experimental interoperability client, not an official Opera product.

## Run

Download the versioned x64 installer from [private releases](https://github.com/gmoddev/VpnProController/releases), then run it. Installs for the current user under `%LOCALAPPDATA%/Programs/VpnProController`; includes Start-menu shortcuts and an uninstaller. The installer bundles .NET 10 and WinUI dependencies (approximately 103 MB download) so separate runtime installation is unnecessary. Opera VPN Pro itself must already be legitimately installed/provisioned.

For development or the smaller shared-runtime build, run `Build.ps1` without `-Installer` and open `App/VpnPro.Windows.exe`. That build requires .NET 10 and Windows App Runtime 1.8. Keep all its files together. The controller does not install another service or run a web server.

The app registers under **VPNProController**, reads current state, and subscribes to events. Connect and Disconnect happen only after an explicit button press. Choose a location while disconnected, or click **Find optimal location** to select the current Opera recommendation. This refreshes the catalog and selects the location for your next manual Connect; it never switches the VPN automatically. Opera does not return measured ping, so this is not a latency benchmark. Close the window to stop the controller; it does not send VPN Disconnect. Open Opera for sign-in, renewal or account problems. After an unavailable-service error, use Refresh to establish a fresh session.

Errors are displayed in the app's nonmodal InfoBar. The app does not show MessageBox dialogs. Diagnostic logs rotate at roughly 1 MB under `%LOCALAPPDATA%/VpnProController/Controller.log`. The executable requests normal user privileges and suppresses Windows process fault dialogs once Main starts. Missing runtime/OS failures before Main are outside managed error handling.

## Startup, recovery and updates

- The permanent app, window and installer icon is the red shield. The tray icon follows service state: gray disconnected, amber connecting/disconnecting, red connected, and the supplied information shield for errors, unavailable state or invalid credentials while disconnected. Hover for status; click or use the tray menu to show the window. **Exit controller** removes the tray icon and leaves the VPN running, as does closing the window. No balloon notifications are shown. Explorer controls whether the tray icon appears directly or in its overflow area.

- Enable **Launch at Windows sign-in** in the UI or installer. It starts minimized; it never automatically connects the VPN. The toggle writes only this app's HKCU Run entry. Uninstall removes that entry.
- Expand **Debug / recovery** and click **Force restart VPN Pro service** if both Opera and the controller cannot connect. Approve Windows UAC. The fixed-purpose helper stops only `VPNProService`, waits up to eight seconds, and force-stops its original process if necessary after verifying both the current service PID and the exact Opera service executable path. It then starts the service and the UI creates a new registration/event subscription. It interrupts an active VPN; it never kills Opera GX or arbitrary caller-supplied PIDs. Declined elevation/errors stay in the UI.
- **Releases & updates** opens the private release page in your authenticated browser. Re-running a newer installer upgrades the same install identity and location. Versioned tags, release assets, SHA-256 checksums, and `update.json` provide the foundation for a future updater. There is **no background updater or automatic download/install yet**. Private assets require authenticated GitHub access; no token is embedded in the application.
- Future authenticated updaters can fetch `update.json` from a GitHub release, validate version/platform and the setup SHA-256, then run that release's installer. Do not infer authenticity from a checksum alone; use authenticated HTTPS and plan code signing before wider distribution. This initial installer is unsigned.

## Architecture

- **Core/Contracts.cs**: `IVpnService`, immutable `VpnSnapshot`, `VpnLocation` and `VpnStatus`. No WinUI dependency.
- **Core/OperaVpnService.cs**: transport implementation. One background thread owns both NetMQ sockets. It serializes REQ/REP calls, consumes events, refreshes state every 10 seconds while healthy, and tears down a timed-out socket before a new session. It never automatically retries a mutation.
- **Core/Protocol.proto**: minimal independently reconstructed protobuf schema. Generated classes stay inside the Core implementation; shells do not need to use them. No copied Opera DLLs or credential-provisioning messages.
- **Windows/**: WinUI 3 shell. It receives an `IVpnService` and marshals events to the UI dispatcher. Native .NET shells can directly reference Core. Other languages can add a small adapter around the same contract; no network server is required by the architecture.
- **Probe/**: headless read-only CLI, also demonstrating a second shell over the same Core.
- **Recovery/**: independent Windows helper with fixed `--restart` and read-only `--check` modes. Ordinary controller processes remain unelevated.
- **Installer/**: Inno Setup script, stable AppId and optional startup/desktop tasks.
- **Tests/**: captured-wire fixtures plus random-port NetMQ mock integration tests. They do not connect to port 45000 or change the real VPN.

```csharp
await using IVpnService Service = new OperaVpnService();
Service.SnapshotChanged += Snapshot => { /* dispatch to your shell */ };
var Snapshot = await Service.RefreshAsync();
// Only from an explicit user action:
// await Service.ConnectAsync(SelectedLocation.Id);
// await Service.DisconnectAsync();
```

`SnapshotChanged` runs on the transport worker; marshal to your UI thread and do not synchronously block the handler waiting on another service request. Dispose asynchronously. The snapshot reports `Unknown` after an unavailable service or timeout, rather than claiming the VPN is disconnected. A connect acknowledgement is followed by a status read; the service is authoritative for tunnel state.

## Build and test

Run `Build.ps1` from PowerShell. It runs the mock tests and publishes the WinUI app with bounded build concurrency. Requires a .NET 10 SDK and Windows build prerequisites; versions are pinned in project files and NuGet lockfiles.

For a distributable installer, install Inno Setup 6.7+ and run:

```powershell
./Build.ps1 -Installer -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -Version 0.4.0
```

Output: `dist/VpnProController-0.4.0-Setup-x64.exe`, `dist/SHA256SUMS.txt`, and `dist/update.json`. Push a `vMAJOR.MINOR.PATCH` tag to trigger the pinned GitHub Actions workflow: tests, Windows build, installer, then private release. Manual workflow dispatch builds artifacts without publishing a release. Update the default version in Directory.Build.props/Build.ps1 with each release; the UI reads its actual assembly version.

On a Windows desktop with Explorer running, `dotnet run --project TrayTests/VpnPro.TrayTests.csproj -c Release` validates native tray registration, state updates, activation callbacks, simulated Explorer restart and cleanup. It uses an invisible test window and never contacts the VPN. Icon assets are generated from the supplied PNG pack using `Windows/Assets/BuildIcons.ps1 -SourceDirectory <extracted-pack>`; the resulting ICO files are checked in.

```powershell
dotnet run --project Probe/VpnPro.Probe.csproj -c Release -- --read-only
```

The probe performs registration, credential validity, status, locations and last-connection queries only. Use `--recommendation` instead of `--read-only` to also query the recommended location. It does not connect/disconnect the VPN.

## Validation

The developer's installed service accepted this standalone client name, returned credential validity/status/locations, and the user confirmed a successful connection through the controller. A later failure affected Opera and this controller equally; both sent identical requests and received empty replies. A manual service restart restored both. The UI now reports that empty-reply failure and exposes service recovery.

Tests cover protobuf fixture bytes, serialized calls, event delivery, re-registration, timeout recovery, wrong-operation rejection, invalid-credential gating, empty connect failures, and no mutation on startup/disposal. They control only a random-port mock, not the real VPN. The recovery helper's forced termination branch is deliberately not exercised against a healthy live VPN.

Credential expiry/renewal, service restart while connected, operation with Opera fully closed, simultaneous mutation from other clients, and kill-switch behavior remain unqualified. The controller does not touch routes, DNS, firewall, adapters, credentials, licensing or update settings. It does not claim that the browser is never needed again.

Raw packet captures, local logs, and private-machine diagnostic reports are excluded from this repository. Test addresses use documentation-only address space.

## Dependencies

NetMQ 4.0.4.3; Google.Protobuf 3.36.1; Grpc.Tools 2.84.0 (build only); Windows App SDK 1.8.260804001. Their transitive dependencies and hashes are recorded in `packages.lock.json`. Respect their individual licenses when redistributing.

References: [ZMTP 3.0](https://rfc.zeromq.org/spec/23/), [Windows unpackaged deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app), [NetMQ package](https://www.nuget.org/packages/NetMQ/4.0.4.3).
