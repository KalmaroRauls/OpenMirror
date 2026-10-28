<#
  OpenMirror - icon generator.
  Draws the OpenMirror mark and writes:
      assets\icon.ico      multi-size app icon (16..256 px)
      assets\logo.png      512 px preview of the mark
      assets\wordmark.png  mark + name, for the README header
  Only needs the .NET Framework compiler that build.ps1 already uses; re-run it after
  editing tools\IconGen.cs (the draw calls are all in there).
#>
[CmdletBinding()]
param([string] $Size = '')

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$root = if ((Split-Path -Leaf $here) -ieq 'tools') { Split-Path -Parent $here } else { $here }
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw 'No C# compiler found. Install .NET Framework 4.x and retry.' }

$work = Join-Path $env:TEMP ('openmirror-icon-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.exe')
try {
    $source = Join-Path $here 'IconGen.cs'
    Write-Host ('compiler: ' + $csc) -ForegroundColor Cyan
    & $csc /nologo /target:exe /langversion:5 ('/out:' + $work) `
        /reference:System.dll /reference:System.Drawing.dll $source 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'IconGen.cs did not compile' }

    & $work (Join-Path $assets 'icon.ico') (Join-Path $assets 'logo.png') (Join-Path $assets 'wordmark.png') (Join-Path $assets 'sizes.png')
    if ($LASTEXITCODE -ne 0) { throw "Icon generator failed with exit code $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $work -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Get-ChildItem -LiteralPath $assets -File | ForEach-Object {
    Write-Host ('ICON OK -> ' + $_.FullName + '  (' + $_.Length + ' bytes)') -ForegroundColor Green
}
