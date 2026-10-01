<#
.SYNOPSIS
Builds Veil release artifacts: an Inno Setup installer, a portable zip and the VeilEngine runtime zip.

.DESCRIPTION
Everything is written to release\<version>\ (or -OutputRoot). Build intermediates go to artifacts\,
so the regular bin\ folders, including a Veil copy running from bin\Release, are never touched.

The installer needs the Inno Setup 6 compiler (ISCC.exe). It is looked up in the ISCC environment
variable, PATH and the standard per-user and per-machine install folders.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build_release.ps1
#>
param(
    [string]$Runtime = "win-x64",
    [string]$OutputRoot,
    [switch]$DownloadDependencies,
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$ProjectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ArtifactsDir = Join-Path $ProjectDir "artifacts"
$Version = (Select-Xml -Path (Join-Path $ProjectDir "Veil.csproj") -XPath "//Version").Node.InnerText
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $ProjectDir "release"
}
$ReleaseDir = Join-Path ([System.IO.Path]::GetFullPath($OutputRoot)) $Version
$AppDir = Join-Path $ReleaseDir "Veil"
$PortableZip = Join-Path $ReleaseDir "Veil-portable-$Runtime.zip"
$EngineZip = Join-Path $ReleaseDir "VeilEngine-$Runtime.zip"
$InstallerName = "Veil-Setup-$Runtime"

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE. Arguments: $($Arguments -join ' ')"
    }
}

function Reset-Directory {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Assert-RuntimeDependencies {
    if ($DownloadDependencies) {
        Invoke-Checked "powershell" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $ProjectDir "scripts\download_deps.ps1"))
    }

    foreach ($file in @("client\trusttunnel_client.exe", "client\wintun.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectDir $file))) {
            throw "Missing $file. Run scripts\download_deps.ps1 or build VeilEngine (see engine\README.md) first."
        }
    }
}

function Find-InnoSetupCompiler {
    $candidates = @(
        $env:ISCC,
        (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )

    return $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

function New-EngineZip {
    $stage = Join-Path $ArtifactsDir "engine-stage"
    Reset-Directory $stage
    Copy-Item -LiteralPath (Join-Path $ProjectDir "client\trusttunnel_client.exe") -Destination $stage
    Copy-Item -LiteralPath (Join-Path $ProjectDir "client\wintun.dll") -Destination $stage
    Set-Content -LiteralPath (Join-Path $stage "README.txt") -Encoding UTF8 -Value @"
VeilEngine $Version, Windows x64 runtime

Built from the TrustTunnel client with the Veil patch (engine/patches/veil-trusttunnel-client.patch):
process-aware split tunneling and !-prefixed domain exceptions.

Files:
- trusttunnel_client.exe
- wintun.dll
"@
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $EngineZip -Force
}

Push-Location $ProjectDir
try {
    Assert-RuntimeDependencies

    Write-Host "Building Veil $Version for $Runtime" -ForegroundColor Cyan
    Reset-Directory $ReleaseDir

    if (-not $SkipTests) {
        Invoke-Checked "dotnet" @("build", "Veil.Tests\Veil.Tests.csproj", "-c", "Release", "--artifacts-path", $ArtifactsDir, "-nologo")
        $testExe = Join-Path $ArtifactsDir "bin\Veil.Tests\release\Veil.Tests.exe"
        Invoke-Checked $testExe @()
    }

    Invoke-Checked "dotnet" @(
        "publish", "Veil.csproj",
        "-c", "Release",
        "-r", $Runtime,
        "--self-contained", "true",
        "--artifacts-path", $ArtifactsDir,
        "-p:PublishReadyToRun=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $AppDir,
        "-nologo"
    )

    Compress-Archive -Path (Join-Path $AppDir "*") -DestinationPath $PortableZip -Force
    New-EngineZip

    if ($SkipInstaller) {
        Write-Warning "Installer skipped (-SkipInstaller)."
    }
    else {
        $iscc = Find-InnoSetupCompiler
        if (-not $iscc) {
            throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php (or 'winget install JRSoftware.InnoSetup'), set ISCC to ISCC.exe, or pass -SkipInstaller."
        }

        Invoke-Checked $iscc @(
            "/Qp",
            "/DAppVersion=$Version",
            "/DSourceDir=$AppDir",
            "/DOutputDir=$ReleaseDir",
            "/F$InstallerName",
            (Join-Path $ProjectDir "installer\Veil.iss")
        )
    }

    $checksums = Get-ChildItem -LiteralPath $ReleaseDir -File |
        Where-Object { $_.Extension -in ".exe", ".zip" } |
        ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    Set-Content -LiteralPath (Join-Path $ReleaseDir "SHA256SUMS.txt") -Value $checksums -Encoding ASCII

    Write-Host ""
    Write-Host "Release artifacts in $($ReleaseDir):" -ForegroundColor Green
    Get-ChildItem -LiteralPath $ReleaseDir | ForEach-Object { Write-Host "  $($_.Name)" }
}
finally {
    Pop-Location
}
