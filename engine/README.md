# VeilEngine Patch

Veil uses a patched TrustTunnel client runtime for Windows process-aware split tunneling.

The source patch is stored in [patches/veil-trusttunnel-client.patch](patches/veil-trusttunnel-client.patch). Apply it to the upstream `TrustTunnel/TrustTunnelClient` source at commit `dbd076b` to reproduce the runtime changes used by Veil.

For normal Veil development, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download_deps.ps1
```

That downloads the prebuilt `VeilEngine-win-x64.zip` asset from the latest Veil release and places `trusttunnel_client.exe` plus `wintun.dll` into `client/`.
