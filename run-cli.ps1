<#
  OpenMirror - run a CLI mode and show its output.
  The exe is a GUI-subsystem binary on purpose, so Windows does not wait for it and
  its stdout is not attached to your shell prompt. This wrapper waits properly and
  prints the transcript the app mirrors to bin\cli-transcript.txt.

  Mode takes the switch name WITHOUT leading dashes, so PowerShell cannot mistake it
  for a parameter:

    .\run-cli.ps1                       # same as -Mode probe
    .\run-cli.ps1 -Mode selftest
    .\run-cli.ps1 -Mode headless -Extra 6
    .\run-cli.ps1 -Mode screenshot -Extra 'C:\Users\me\Pictures\phone.png'
#>
[CmdletBinding()]
param(
    [string] $Mode = 'probe',
    [string] $Extra = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe = Join-Path $root 'bin\OpenMirror.exe'
$transcript = Join-Path $root 'bin\cli-transcript.txt'

if (-not (Test-Path $exe)) { throw "Not built yet: run .\build.ps1 first ($exe missing)" }

$argList = @('--' + $Mode)
if ($Extra.Trim().Length -gt 0) { $argList += $Extra.Split(' ') }

Remove-Item -LiteralPath $transcript -ErrorAction SilentlyContinue
Write-Host ("running: OpenMirror.exe " + ($argList -join ' ')) -ForegroundColor Cyan
$proc = Start-Process -FilePath $exe -ArgumentList $argList -Wait -PassThru
Write-Host ''

if (Test-Path $transcript) { Get-Content -LiteralPath $transcript }
else { Write-Host 'no transcript produced' -ForegroundColor Yellow }

Write-Host ''
Write-Host ("exit code: " + $proc.ExitCode) -ForegroundColor Cyan
exit $proc.ExitCode
