# Changelog

## 0.5.1

- Remove obsolete app-local runtime loaders when upgrading from bundled-runtime versions, so both the controller and recovery helper locate the installed shared .NET runtime.

## 0.5.0

- Stop bundling the .NET runtime. Setup detects an installed x64 .NET 10 runtime, otherwise downloads a hash-verified Microsoft installer and installs it with no automatic restart. WinUI dependencies remain bundled.

- Give shortcuts an explicit shield icon and repair the existing controller taskbar pin on upgrade, avoiding stale executable icon caching.
- Add Connect/Disconnect and Connect/Switch to optimal region to the tray menu. Commands are disabled during changes or when required service/credential state is unavailable.
- Optimal switching waits for disconnection, queries the current recommendation, and sends one connect request. Closing the controller cancels the remaining switch steps.

## 0.4.0

- Use the supplied red shield as the executable, installer and window icon.
- Add stateful tray icons: gray disconnected, amber connecting/disconnecting, red connected, and the supplied information shield for errors/unavailable state.
- Add tray Show and Exit actions, keyboard activation, tooltip status and Explorer-restart recovery. Closing/exiting still leaves the VPN running.
- Package all four icons at 16, 20, 24, 32, 48 and 256 pixels, with no new application runtime dependency.

## 0.3.0

- Add **Find optimal location** while disconnected, using Opera's native recommendation query.
- Refresh the location catalog before selecting the recommended ID for the next manual connection.
- Expose the recommendation through the independent core API and read-only probe.
- Validate captured request/reply bytes, refreshed location IDs, missing replies and absence of connection mutations.

The service returns a recommended location, not measured ping or a guarantee of lowest latency.

## 0.2.0

- Add per-user Windows installer with bundled runtimes, uninstall and stable upgrade identity.
- Add optional minimized launch at sign-in from the installer and UI.
- Add fixed-purpose elevated service recovery; a stalled stop can terminate only the verified VPNProService process.
- Re-register and restore event subscription after recovery.
- Report the observed empty Connect reply instead of silently returning to Disconnected.
- Add private-release workflow, SHA-256 checksums, version manifest and releases link.

Automatic authenticated update download/install and code signing are future work.
