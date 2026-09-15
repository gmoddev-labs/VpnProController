# Changelog

## 0.2.0

- Add per-user Windows installer with bundled runtimes, uninstall and stable upgrade identity.
- Add optional minimized launch at sign-in from the installer and UI.
- Add fixed-purpose elevated service recovery; a stalled stop can terminate only the verified VPNProService process.
- Re-register and restore event subscription after recovery.
- Report the observed empty Connect reply instead of silently returning to Disconnected.
- Add private-release workflow, SHA-256 checksums, version manifest and releases link.

Automatic authenticated update download/install and code signing are future work.
