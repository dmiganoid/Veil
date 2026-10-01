# Runtime Client Files

This directory holds the VeilEngine runtime that Veil starts:

```text
trusttunnel_client.exe
wintun.dll
```

Populate it with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ..\scripts\download_deps.ps1
```

or copy a VeilEngine build here (see [../engine/README.md](../engine/README.md)). These binaries are required for local runs and release builds, but they are not committed to the repository. They come from the patched VeilEngine runtime, not from the upstream TrustTunnelClient release.

Veil writes the engine configuration to `%APPDATA%\Veil\trusttunnel_client.toml`, not to this folder, because it contains the VPN password.
