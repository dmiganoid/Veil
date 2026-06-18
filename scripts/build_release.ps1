param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$DownloadDependencies,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$ProjectDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$DistDir = Join-Path $ProjectDir "dist"
$PortableDir = Join-Path $DistDir "Veil-portable-$Runtime"
$PortableZip = Join-Path $DistDir "Veil-portable-$Runtime.zip"
$EngineZip = Join-Path $DistDir "VeilEngine-$Runtime.zip"
$InstallerExe = Join-Path $DistDir "Veil-Setup-$Runtime.exe"
$InstallerStage = Join-Path $DistDir "installer-stage"

function Assert-UnderProject {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $projectPath = [System.IO.Path]::GetFullPath($ProjectDir)
    if (-not $fullPath.StartsWith($projectPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch path outside project: $fullPath"
    }
}

function Reset-Directory {
    param([string]$Path)

    Assert-UnderProject $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE. Arguments: $($Arguments -join ' ')"
    }
}

function Assert-RuntimeDependencies {
    $clientExe = Join-Path $ProjectDir "client\trusttunnel_client.exe"
    $wintunDll = Join-Path $ProjectDir "client\wintun.dll"

    if ($DownloadDependencies) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectDir "scripts\download_deps.ps1")
    }

    if (-not (Test-Path -LiteralPath $clientExe)) {
        throw "Missing $clientExe. Run scripts\download_deps.ps1 before building a release."
    }

    if (-not (Test-Path -LiteralPath $wintunDll)) {
        throw "Missing $wintunDll. Run scripts\download_deps.ps1 before building a release."
    }
}

function Write-PortableReadme {
    $text = @"
Veil portable build

Run Veil.exe to start the app. Windows will ask for administrator approval because Veil uses Wintun to create a VPN adapter.

Runtime settings are stored in:
%APPDATA%\Veil

Bundled runtime files:
- client\trusttunnel_client.exe
- client\wintun.dll
"@

    Set-Content -LiteralPath (Join-Path $PortableDir "README.txt") -Value $text -Encoding UTF8
}

function New-EngineRuntimeZip {
    $engineStage = Join-Path $DistDir "engine-runtime"
    Reset-Directory $engineStage

    Copy-Item -LiteralPath (Join-Path $ProjectDir "client\trusttunnel_client.exe") -Destination (Join-Path $engineStage "trusttunnel_client.exe") -Force
    Copy-Item -LiteralPath (Join-Path $ProjectDir "client\wintun.dll") -Destination (Join-Path $engineStage "wintun.dll") -Force

    $text = @"
VeilEngine Windows x64 runtime

This runtime is built from the Veil-patched TrustTunnel client source.
Source patch: engine/patches/veil-trusttunnel-client.patch

Files:
- trusttunnel_client.exe
- wintun.dll
"@

    Set-Content -LiteralPath (Join-Path $engineStage "README.txt") -Value $text -Encoding UTF8

    if (Test-Path -LiteralPath $EngineZip) {
        Remove-Item -LiteralPath $EngineZip -Force
    }

    Compress-Archive -Path (Join-Path $engineStage "*") -DestinationPath $EngineZip -Force
}

function New-InstallerSupportFiles {
    Reset-Directory $InstallerStage

    $payloadZip = Join-Path $InstallerStage "payload.zip"
    Compress-Archive -Path (Join-Path $PortableDir "*") -DestinationPath $payloadZip -Force

    $installScript = @'
$ErrorActionPreference = "Stop"

$installDir = if ($env:VEIL_INSTALL_DIR) {
    $env:VEIL_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "Programs\Veil"
}

$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$payloadZip = Join-Path $sourceDir "payload.zip"
$extractDir = Join-Path $env:TEMP ("VeilInstall_" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
Expand-Archive -Path $payloadZip -DestinationPath $extractDir -Force
Copy-Item -Path (Join-Path $extractDir "*") -Destination $installDir -Recurse -Force
Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Veil"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $startMenuDir "Veil.lnk"))
$shortcut.TargetPath = Join-Path $installDir "Veil.exe"
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = Join-Path $installDir "Veil.exe"
$shortcut.Save()

$uninstallScript = @"
`$ErrorActionPreference = "Stop"
`$installDir = "$installDir"
`$startMenuDir = "$startMenuDir"
Remove-Item -LiteralPath `$startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$installDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Veil was removed."
"@

Set-Content -LiteralPath (Join-Path $installDir "Uninstall-Veil.ps1") -Value $uninstallScript -Encoding UTF8

$uninstallShortcut = $shell.CreateShortcut((Join-Path $startMenuDir "Uninstall Veil.lnk"))
$uninstallShortcut.TargetPath = "powershell.exe"
$uninstallShortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"" + (Join-Path $installDir "Uninstall-Veil.ps1") + "`""
$uninstallShortcut.WorkingDirectory = $installDir
$uninstallShortcut.IconLocation = "powershell.exe"
$uninstallShortcut.Save()

Write-Host "Veil installed to $installDir"
'@

    Set-Content -LiteralPath (Join-Path $InstallerStage "install.ps1") -Value $installScript -Encoding UTF8

    $installCmd = @'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
exit /b %ERRORLEVEL%
'@

    Set-Content -LiteralPath (Join-Path $InstallerStage "install.cmd") -Value $installCmd -Encoding ASCII
}

function Find-SevenZip {
    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($command) {
        $sfx = Join-Path (Split-Path -Parent $command.Source) "7z.sfx"
        if (Test-Path -LiteralPath $sfx) {
            return @{
                Exe = $command.Source
                Sfx = $sfx
            }
        }
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "7-Zip"),
        (Join-Path ${env:ProgramFiles(x86)} "7-Zip")
    )

    foreach ($candidate in $candidates) {
        $exe = Join-Path $candidate "7z.exe"
        $sfx = Join-Path $candidate "7z.sfx"
        if ((Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $sfx)) {
            return @{
                Exe = $exe
                Sfx = $sfx
            }
        }
    }

    return $null
}

function Join-BinaryFiles {
    param(
        [string[]]$InputFiles,
        [string]$OutputFile
    )

    if (Test-Path -LiteralPath $OutputFile) {
        Remove-Item -LiteralPath $OutputFile -Force
    }

    $output = [System.IO.File]::Open($OutputFile, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    try {
        foreach ($inputFile in $InputFiles) {
            $input = [System.IO.File]::OpenRead($inputFile)
            try {
                $input.CopyTo($output)
            }
            finally {
                $input.Dispose()
            }
        }
    }
    finally {
        $output.Dispose()
    }
}

function New-SevenZipSfxInstaller {
    $sevenZip = Find-SevenZip
    if (-not $sevenZip) {
        return $false
    }

    $archive = Join-Path $InstallerStage "Veil-Setup.7z"
    $config = Join-Path $InstallerStage "Veil-Setup.config.txt"

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    Push-Location $InstallerStage
    try {
        Invoke-Checked $sevenZip.Exe @("a", "-t7z", "-mx=9", $archive, "payload.zip", "install.ps1", "install.cmd")
    }
    finally {
        Pop-Location
    }

    $configText = @"
;!@Install@!UTF-8!
Title="Veil Setup"
BeginPrompt="Install Veil?"
RunProgram="install.cmd"
;!@InstallEnd@!
"@

    [System.IO.File]::WriteAllText($config, $configText, [System.Text.UTF8Encoding]::new($false))
    Join-BinaryFiles @($sevenZip.Sfx, $config, $archive) $InstallerExe
    return (Test-Path -LiteralPath $InstallerExe)
}

function New-IExpressInstaller {
    $iexpressCommand = Get-Command iexpress.exe -ErrorAction SilentlyContinue
    $iexpress = if ($iexpressCommand) { $iexpressCommand.Source } else { $null }
    if (-not $iexpress) {
        Write-Warning "iexpress.exe was not found. Portable zip was created, but installer EXE was skipped."
        return
    }

    $sedPath = Join-Path $InstallerStage "Veil-Setup.sed"
    $sourceDir = $InstallerStage.TrimEnd('\') + "\"
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Veil has been installed.
TargetName=$InstallerExe
FriendlyName=Veil Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd
UserQuietInstCmd=install.cmd
SourceFiles=SourceFiles
[Strings]
FILE0="payload.zip"
FILE1="install.ps1"
FILE2="install.cmd"
[SourceFiles]
SourceFiles0=$sourceDir
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@

    Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII
    Invoke-Checked $iexpress @("/N", "/Q", $sedPath)

    if (-not (Test-Path -LiteralPath $InstallerExe)) {
        throw "IExpress finished without creating $InstallerExe"
    }
}

Push-Location $ProjectDir
try {
    Assert-RuntimeDependencies

    Reset-Directory $DistDir
    New-Item -ItemType Directory -Force -Path $PortableDir | Out-Null

    Invoke-Checked "dotnet" @("restore", "Veil.sln")
    Invoke-Checked "dotnet" @("build", "Veil.sln", "-c", $Configuration, "--no-restore")

    if (-not $SkipTests) {
        Invoke-Checked "dotnet" @("run", "--project", "Veil.Tests\Veil.Tests.csproj", "-c", $Configuration, "--no-build")
    }

    Invoke-Checked "dotnet" @(
        "publish",
        "Veil.csproj",
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:PublishReadyToRun=true",
        "-o", $PortableDir
    )

    Write-PortableReadme

    if (Test-Path -LiteralPath $PortableZip) {
        Remove-Item -LiteralPath $PortableZip -Force
    }
    Compress-Archive -Path (Join-Path $PortableDir "*") -DestinationPath $PortableZip -Force
    New-EngineRuntimeZip

    New-InstallerSupportFiles
    if (-not (New-SevenZipSfxInstaller)) {
        New-IExpressInstaller
    }

    Write-Host ""
    Write-Host "Release artifacts:" -ForegroundColor Green
    Write-Host "  $PortableZip"
    Write-Host "  $EngineZip"
    if (Test-Path -LiteralPath $InstallerExe) {
        Write-Host "  $InstallerExe"
    }
}
finally {
    Pop-Location
}
