# VeilEngine

Veil runs VeilEngine, a build of the [TrustTunnel client](https://github.com/TrustTunnel/TrustTunnelClient) with two additions:

- **Process-aware split tunneling on Windows.** Exclusions such as `deadlock.exe`, `app:deadlock.exe` or `process:deadlock.exe` match connections by the executable that opened them.
- **Domain exceptions.** An exclusion prefixed with `!` cancels broader domain exclusions for the names it matches. Names are checked from the most specific to the least specific, and on the same level an exception beats an exclusion, so `*.net !example.net` treats every `.net` domain as an exclusion except `example.net`. Exceptions accept domain patterns (`!example.net`, `!*.example.net`) only.

The source changes are in [patches/veil-trusttunnel-client.patch](patches/veil-trusttunnel-client.patch), a diff against upstream commit `dbd076b`. It also adds unit tests for both features to `core/test/test_domain_filter.cpp`.

## Getting the runtime

For normal development, download the prebuilt runtime:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\download_deps.ps1
```

It places `trusttunnel_client.exe` and `wintun.dll` from the latest Veil release's `VeilEngine-win-x64.zip` into `client/`. Releases older than Veil 0.2 contain an engine without domain exceptions; with such an engine, `!` entries are logged as malformed and ignored.

## Building from source (Windows)

Prerequisites: Visual Studio 2022 Build Tools with the C++ workload and a Windows SDK (they provide CMake and Ninja), Python 3.13, Git and Rust. Conan and the build tools it needs (NASM, Strawberry Perl) are installed into an isolated location below; nothing is installed system-wide.

```bat
git clone https://github.com/TrustTunnel/TrustTunnelClient.git
cd TrustTunnelClient
git checkout dbd076b
git apply path\to\veil-csharp\engine\patches\veil-trusttunnel-client.patch

py -3.13 -m venv ..\.venv-conan
..\.venv-conan\Scripts\python -m pip install conan

rem Adjust the path to your Visual Studio Build Tools installation.
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
set "CONAN_HOME=%CD%\..\.conan2"
set "PATH=%CD%\..\.venv-conan\Scripts;%USERPROFILE%\.cargo\bin;%PATH%"
conan profile detect
```

`scripts/bootstrap_conan_deps.py` exports AdGuard's packages from their current `master` branches, which no longer match this commit. Export the tags this commit was built against instead (long paths are required for the clones):

```bat
git -c core.longpaths=true clone https://github.com/AdguardTeam/DnsLibs.git ..\dns-libs
git -C ..\dns-libs checkout v2.8.54
conan export ..\dns-libs --user adguard --channel oss --version 2.8.54

git -c core.longpaths=true clone https://github.com/AdguardTeam/NativeLibsCommon.git ..\native-libs-common
git -C ..\native-libs-common checkout v8.1.33
conan export ..\native-libs-common --user adguard --channel oss --version 8.1.33
for /d %R in (..\native-libs-common\conan\recipes\*) do conan export "%R" --user adguard --channel oss
```

BoringSSL needs NASM on x64. Create `%CONAN_HOME%\profiles\veil-extra` with:

```ini
[tool_requires]
openssl/*: nasm/2.16.01
```

Configure (this builds all dependencies the first time), build and test:

```bat
cmake -S . -B build-veil -G Ninja -DCMAKE_BUILD_TYPE=RelWithDebInfo -DCMAKE_C_COMPILER=cl.exe -DCMAKE_CXX_COMPILER=cl.exe "-DCONAN_HOST_PROFILE=%CD%/conan/profiles/windows-msvc.jinja;auto-cmake;%CONAN_HOME%/profiles/veil-extra"
cmake --build build-veil --target trusttunnel_client test_domain_filter
build-veil\core\test_domain_filter.exe
```

Copy `build-veil\trusttunnel\trusttunnel_client.exe` into Veil's `client/` folder. The executable links the MSVC runtime statically and needs only `wintun.dll` next to it.
