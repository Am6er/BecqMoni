# Builds the console test runner with Roslyn csc and runs it.
# Usage:  run-tests.ps1 [-Dump <path to 64-byte-record dump>] [-Live <seconds>]
param([string]$Dump = "", [int]$Live = 0)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$src  = Join-Path $here '..\..\BecquerelMonitor'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $csc) { throw 'Roslyn csc.exe not found (VS Build Tools required).' }

# Dependency-free device sources: compile whichever already exist.
# Wrapped in @(...): a pipeline that yields exactly one match collapses to a scalar string in
# PowerShell, and splatting a scalar with @ passes it to csc.exe one character at a time.
$sources = @(@('AmplitudaUsbProtocol.cs', 'AmplitudaUsbAccumulator.cs', 'HidNative.cs', 'AmplitudaUsbReader.cs') |
    ForEach-Object { Join-Path $src $_ } | Where-Object { Test-Path $_ })
$tests = @(Get-ChildItem -Path $here -Filter *.cs | ForEach-Object { $_.FullName })
$exe = Join-Path $here 'AmplitudaUsbTests.exe'

& $csc /nologo /langversion:7.3 /debug+ "/out:$exe" @tests @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runArgs = @()
if ($Dump) { $runArgs += @('--dump', $Dump) }
if ($Live -gt 0) { $runArgs += @('--live', "$Live") }
& $exe @runArgs
exit $LASTEXITCODE
