<#
  OpenMirror - build script.
  Uses the .NET Framework compiler already present on Windows (no SDK install).
  Produces .\bin\OpenMirror.exe (WinForms, C# 5 language level).
#>
[CmdletBinding()]
param([switch] $Run, [string] $RunArgs = '--probe')
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    $roslyn = Get-Command csc.exe -ErrorAction SilentlyContinue
    if ($roslyn) { $csc = $roslyn.Source }
}
if (-not $csc) { throw 'No C# compiler found. Install .NET Framework 4.x or a .NET SDK and retry.' }

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' | Sort-Object Name | Select-Object -ExpandProperty FullName
if (-not $sources) { throw "No sources found in $src" }

$out = Join-Path $bin 'OpenMirror.exe'
# Compile to a staging name and move it into place afterwards: replacing the output
# directly can fail with "used by another process" when something (antivirus, the search
# indexer) happens to hold a read handle on the previous build, while a rename over it works.
$staging = Join-Path $bin 'OpenMirror.staging.exe'
Remove-Item -LiteralPath $staging -Force -ErrorAction SilentlyContinue
$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll'
)

Write-Host ("compiler: " + $csc) -ForegroundColor Cyan
Write-Host ("sources : " + $sources.Count) -ForegroundColor Cyan

# The app icon, when it has been generated (tools\MakeIcon.ps1): /win32icon gives Explorer
# and the taskbar their icon, /resource embeds the very same file as app.ico for the windows.
$icon = Join-Path $root 'assets\icon.ico'
$iconArgs = @()
if (Test-Path $icon) {
    $iconArgs = @(('/win32icon:' + $icon), ('/resource:' + $icon + ',app.ico'))
    Write-Host ('icon    : ' + $icon) -ForegroundColor Cyan
} else {
    Write-Host 'icon    : none - run .\tools\MakeIcon.ps1 to generate assets\icon.ico' -ForegroundColor Yellow
}

$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    '/langversion:5', ('/out:' + $staging)
) + $refs + $iconArgs + $sources

& $csc @compilerArgs 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $staging -Force -ErrorAction SilentlyContinue
    throw "Build failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path $staging)) { throw 'Build reported success but produced no output' }

# Something (antivirus, the search indexer) can hold the previous exe open for a moment, so
# replacing it is retried a few times before giving up.
$replaced = $false
for ($attempt = 1; $attempt -le 10 -and -not $replaced; $attempt++) {
    try {
        [System.IO.File]::Copy($staging, $out, $true)
        $replaced = $true
    }
    catch {
        Start-Sleep -Milliseconds 700
    }
}
Remove-Item -LiteralPath $staging -Force -ErrorAction SilentlyContinue
if (-not $replaced) {
    throw ('Could not replace ' + $out + ' after several attempts. Close OpenMirror if it is ' +
           'running, or exclude this folder from antivirus scanning, then build again.')
}

Write-Host ''
Write-Host ("BUILD OK -> " + $out + "  (" + (Get-Item $out).Length + " bytes)") -ForegroundColor Green
if ($Run) {
    Write-Host ("running: OpenMirror.exe " + $RunArgs) -ForegroundColor Cyan
    $transcript = Join-Path $bin 'cli-transcript.txt'
    Remove-Item -LiteralPath $transcript -ErrorAction SilentlyContinue
    $proc = Start-Process -FilePath $out -ArgumentList $RunArgs.Split(' ') -Wait -PassThru
    if (Test-Path $transcript) { Get-Content -LiteralPath $transcript }
    exit $proc.ExitCode
}
