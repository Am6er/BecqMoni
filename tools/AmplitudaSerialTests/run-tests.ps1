# Builds the console test runner with Roslyn csc and runs it.
# Usage:  run-tests.ps1                      - unit tests on a fake block
#         run-tests.ps1 -Live COM2 -Address 0 -Seconds 60 [-Pour 2000]   - real hardware, CHANGES BLOCK STATE
param([string]$Live = "", [int]$Address = 0, [int]$Seconds = 60, [int]$Pour = 0)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$src  = Join-Path $here '..\..\BecquerelMonitor'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$csc = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $csc) { throw 'Roslyn csc.exe not found (VS Build Tools required).' }

# Dependency-free device sources: compile whichever already exist.
$sources = @(@('AmplitudaSerialProtocol.cs', 'AmplitudaSerialPort.cs', 'AmplitudaSerialLine.cs',
               'AmplitudaSerialSession.cs', 'AmplitudaSerialAcquisition.cs') |
    ForEach-Object { Join-Path $src $_ } | Where-Object { Test-Path $_ })
$tests = @(Get-ChildItem -Path $here -Filter *.cs | ForEach-Object { $_.FullName })
$exe = Join-Path $here 'AmplitudaSerialTests.exe'

& $csc /nologo /langversion:7.3 /debug+ "/out:$exe" @tests @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runArgs = @()
if ($Live) { $runArgs += @('--live', $Live, "$Address", "$Seconds", "$Pour") }
& $exe @runArgs
exit $LASTEXITCODE
