<#
  OpenMirror - engine setup / verification.
  Vendors nothing: fetches the mirroring engine (scrcpy, Apache-2.0) and adb
  into .\engine, verifies them, and reports. Idempotent; safe to re-run.
#>
[CmdletBinding()]
param(
    [string] $EngineRoot = '',
    [string] $ScrcpyVersion = '5.0.1',
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# $PSScriptRoot is not always populated while param defaults are bound, so resolve here.
if ([string]::IsNullOrEmpty($EngineRoot)) { $EngineRoot = Join-Path $PSScriptRoot 'bin\engine' }

function Write-Step([string] $text) { Write-Host ("==> " + $text) -ForegroundColor Cyan }
function Write-Ok([string] $text)   { Write-Host ("    OK  " + $text) -ForegroundColor Green }
function Write-Warn2([string] $text) { Write-Host ("    !!  " + $text) -ForegroundColor Yellow }

$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'winarm64' } else { 'win64' }
$zipName = "scrcpy-$arch-v$ScrcpyVersion.zip"
$zipUrl = "https://github.com/Genymobile/scrcpy/releases/download/v$ScrcpyVersion/$zipName"

New-Item -ItemType Directory -Force -Path $EngineRoot | Out-Null
$zipPath = Join-Path $EngineRoot $zipName
$scrcpyDir = Join-Path $EngineRoot 'scrcpy'

Write-Step "Engine root: $EngineRoot"
Write-Step "Target: scrcpy $ScrcpyVersion ($arch)"

$exe = $null
if (-not $Force) {
    $found = Get-ChildItem -LiteralPath $scrcpyDir -Recurse -Filter 'scrcpy.exe' -ErrorAction SilentlyContinue |
             Where-Object { $_.DirectoryName -like "*scrcpy-$arch-v$ScrcpyVersion*" } | Select-Object -First 1
    if ($found) { $exe = $found.FullName }
}

if (-not $exe) {
    if (-not (Test-Path $zipPath)) {
        $cacheCandidates = @(
            (Join-Path $PSScriptRoot ('engine\' + $zipName)),
            (Join-Path $env:USERPROFILE ('tools\openmirror\' + $zipName))
        )
        foreach ($cached in $cacheCandidates) {
            if (Test-Path $cached) {
                Write-Step "Reusing cached archive $cached"
                Copy-Item -LiteralPath $cached -Destination $zipPath -Force
                break
            }
        }
    }
    if ($Force -or -not (Test-Path $zipPath)) {
        Write-Step "Downloading $zipUrl"
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing -TimeoutSec 900
    }
    Write-Ok ("archive: {0:N0} bytes" -f (Get-Item $zipPath).Length)
    Write-Step 'Extracting'
    Expand-Archive -LiteralPath $zipPath -DestinationPath $scrcpyDir -Force
    $exe = (Get-ChildItem -LiteralPath $scrcpyDir -Recurse -Filter 'scrcpy.exe' | Select-Object -First 1).FullName
}
Write-Ok "scrcpy.exe -> $exe"
$engineDir = Split-Path -Parent $exe

$verLine = (& $exe --version 2>&1 | Select-Object -First 1)
Write-Ok ("version: " + $verLine)

$serverJar = Join-Path $engineDir 'scrcpy-server'
if (Test-Path $serverJar) { Write-Ok ("server payload: {0:N0} bytes" -f (Get-Item $serverJar).Length) }
else { Write-Warn2 'scrcpy-server payload missing from the archive' }

# adb: prefer the engine's own copy so we never fight another vendor's adb server.
$adb = Join-Path $engineDir 'adb.exe'
if (-not (Test-Path $adb)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
        'C:\Program Files\Android\Android Studio\platform-tools\adb.exe'
    )
    $adb = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($adb) { Write-Ok "adb -> $adb" } else { Write-Warn2 'adb not found; set it in the app settings' }

$manifest = [ordered]@{
    schema           = 1
    engine           = 'scrcpy'
    engine_version   = $ScrcpyVersion
    architecture     = $arch
    scrcpy_exe       = $exe
    engine_dir       = $engineDir
    server_payload   = $serverJar
    adb_exe          = $adb
    installed_utc    = (Get-Date).ToUniversalTime().ToString('o')
}
$manifestPath = Join-Path $EngineRoot 'engine.json'
($manifest | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Ok "manifest -> $manifestPath"

if ($adb) {
    Write-Step 'adb connectivity probe'
    & $adb start-server 2>&1 | Out-Null
    $devices = (& $adb devices -l 2>&1)
    foreach ($line in $devices) { Write-Host ("    " + $line) }
    $count = @($devices | Select-String -Pattern '\sdevice\s').Count
    if ($count -gt 0) { Write-Ok "$count device(s) ready" } else { Write-Warn2 'no authorized device yet (plug in / enable USB debugging, or use wireless)' }
}

Write-Host ''
Write-Host 'Engine ready. Launch the app with:  .\bin\OpenMirror.exe' -ForegroundColor Cyan
