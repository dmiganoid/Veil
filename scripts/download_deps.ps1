# Downloads the patched VeilEngine runtime and Wintun into client/.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\download_deps.ps1 [-Force]

param(
    [switch]$Force,
    [string]$EngineRepository = "dmiganoid/Veil",
    [string]$EngineVersion = "latest"
)

$ErrorActionPreference = "Stop"
$headers = @{ "User-Agent" = "VeilDependencyDownloader" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$clientDir = Join-Path $PSScriptRoot "..\client"
$exePath = Join-Path $clientDir "trusttunnel_client.exe"
$dllPath = Join-Path $clientDir "wintun.dll"

if (-not (Test-Path -LiteralPath $clientDir)) {
    New-Item -ItemType Directory -Force -Path $clientDir | Out-Null
}

if (-not $Force -and (Test-Path -LiteralPath $exePath) -and (Test-Path -LiteralPath $dllPath)) {
    Write-Host "VeilEngine runtime already exists, skipping. Use -Force to re-download." -ForegroundColor Yellow
    return
}

function Find-GitHubCli {
    $command = Get-Command gh -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $defaultPath = "C:\Program Files\GitHub CLI\gh.exe"
    if (Test-Path -LiteralPath $defaultPath) {
        return $defaultPath
    }

    return $null
}

function Download-EngineWithGitHubApi {
    param(
        [string]$DestinationPath
    )

    if ($EngineVersion -eq "latest") {
        $releaseUri = "https://api.github.com/repos/$EngineRepository/releases/latest"
    }
    else {
        $releaseUri = "https://api.github.com/repos/$EngineRepository/releases/tags/$EngineVersion"
    }

    $release = Invoke-RestMethod -Headers $headers -Uri $releaseUri
    $asset = $release.assets |
        Where-Object { $_.name -eq "VeilEngine-win-x64.zip" } |
        Select-Object -First 1

    if (-not $asset) {
        throw "Could not find VeilEngine-win-x64.zip in $EngineRepository release $EngineVersion."
    }

    Invoke-WebRequest -Headers $headers -Uri $asset.browser_download_url -OutFile $DestinationPath
}

function Download-EngineWithGitHubCli {
    param(
        [string]$DestinationPath
    )

    $gh = Find-GitHubCli
    if (-not $gh) {
        throw "GitHub API download failed and GitHub CLI was not found."
    }

    $downloadDir = Split-Path -Parent $DestinationPath
    $arguments = @(
        "release",
        "download",
        "--repo", $EngineRepository,
        "--pattern", "VeilEngine-win-x64.zip",
        "--dir", $downloadDir,
        "--clobber"
    )

    if ($EngineVersion -ne "latest") {
        $arguments = @("release", "download", $EngineVersion) + $arguments[2..($arguments.Length - 1)]
    }

    & $gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed to download VeilEngine runtime."
    }

    $downloaded = Join-Path $downloadDir "VeilEngine-win-x64.zip"
    if (-not (Test-Path -LiteralPath $downloaded)) {
        throw "GitHub CLI did not create $downloaded."
    }

    Move-Item -LiteralPath $downloaded -Destination $DestinationPath -Force
}

Write-Host "Downloading VeilEngine runtime..." -ForegroundColor Cyan

$zipPath = Join-Path $env:TEMP "veil_engine_win_x64.zip"
$extractDir = Join-Path $env:TEMP "veil_engine_win_x64"

try {
    Download-EngineWithGitHubApi -DestinationPath $zipPath
}
catch {
    Write-Host "GitHub API download failed, trying GitHub CLI fallback..." -ForegroundColor Yellow
    Download-EngineWithGitHubCli -DestinationPath $zipPath
}
if (Test-Path -LiteralPath $extractDir) {
    Remove-Item -LiteralPath $extractDir -Recurse -Force
}

Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

$foundExe = Get-ChildItem -Path $extractDir -Filter "trusttunnel_client.exe" -Recurse | Select-Object -First 1
$foundDll = Get-ChildItem -Path $extractDir -Filter "wintun.dll" -Recurse | Select-Object -First 1

if (-not $foundExe) {
    throw "Could not find trusttunnel_client.exe in VeilEngine runtime asset."
}

if (-not $foundDll) {
    throw "Could not find wintun.dll in VeilEngine runtime asset."
}

Copy-Item -LiteralPath $foundExe.FullName -Destination $exePath -Force
Copy-Item -LiteralPath $foundDll.FullName -Destination $dllPath -Force

Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Dependencies ready in $clientDir" -ForegroundColor Green
