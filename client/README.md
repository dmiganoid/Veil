# Runtime Client Files

This directory is populated locally by:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ..\scripts\download_deps.ps1
```

Expected runtime files:

```text
trusttunnel_client.exe
wintun.dll
```

These binaries are required for local runs and release artifacts, but they are intentionally not committed to the repository. They come from the patched VeilEngine runtime attached to Veil releases, not from the upstream TrustTunnelClient release.
