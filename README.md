<p align="center">
  <img src="Assets/icon.png" alt="Veil app icon" width="128" height="128">
</p>

# Veil

[![CI](https://github.com/dmiganoid/Veil/actions/workflows/ci.yml/badge.svg)](https://github.com/dmiganoid/Veil/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/dmiganoid/Veil?label=release)](https://github.com/dmiganoid/Veil/releases/latest)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

Veil is a Windows desktop client for TrustTunnel VPN. It wraps the TrustTunnel runtime in a polished WPF app with split tunneling, server setup tools, logs, and a tray-first workflow.

The tunneling engine integration was refined alongside the app, so Veil treats connection setup, split tunneling, diagnostics, and cleanup as one cohesive desktop workflow.

## Features

- Connect and disconnect from a private TrustTunnel VPN.
- Windows tray menu with show, connect/disconnect, and exit actions.
- Split tunnel by domains, installed applications, manual executable names, and GeoIP countries.
- Installed app discovery with executable icons.
- Log-based split-tunnel suggestions.
- Remote server setup over SSH with certificate and service checks.
- JSON import/export for client settings.
- Single-instance protection and graceful VPN process cleanup.

## Requirements

- Windows 10/11 x64.
- .NET SDK 9.0 for development.
- Administrator approval when running Veil, because Wintun adapter creation requires elevation.
- VeilEngine Windows runtime and `wintun.dll` in `client/`.

Veil depends on a patched TrustTunnel client runtime. The engine patch is included in [engine/patches](engine/patches), and the dependency script below downloads the matching Windows runtime from Veil releases.

Download runtime dependencies:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download_deps.ps1
```

The downloaded files are intentionally ignored by git:

```text
client/trusttunnel_client.exe
client/wintun.dll
```

## Development

```powershell
dotnet build Veil.sln
dotnet run --project Veil.csproj
dotnet run --project Veil.Tests\Veil.Tests.csproj
```

## Release

Build a portable zip, a VeilEngine runtime zip, and a Windows installer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build_release.ps1
```

Artifacts are written to:

```text
dist/Veil-portable-win-x64.zip
dist/VeilEngine-win-x64.zip
dist/Veil-Setup-win-x64.exe
```

The installer is per-user and installs to:

```text
%LOCALAPPDATA%\Programs\Veil
```

Runtime settings are stored separately in:

```text
%APPDATA%\Veil
```

## License

Veil is released under the Apache License 2.0. See [LICENSE](LICENSE).
