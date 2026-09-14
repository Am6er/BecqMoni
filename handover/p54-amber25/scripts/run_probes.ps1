# Прогон приёмочных проб П54 из каталога проб build_p54: -Tag before|after.
param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $repo 'tools\effmaker\probes\build_p54'
$out = "D:\BqMoni_Claude\p54\$Tag"
New-Item -ItemType Directory -Force $out | Out-Null

# Каталог приёмочной пробы: копия рантайма из каталога проб (без чужих exe).
$accept = "D:\BqMoni_Claude\p54\accept_$Tag"
if (Test-Path $accept) { Remove-Item -Recurse -Force $accept }
New-Item -ItemType Directory -Force $accept | Out-Null
Get-ChildItem $build -File | Where-Object { $_.Name -eq 'BecquerelMonitor.exe' -or $_.Extension -in '.dll','.sqlite','.config','.pdb' -and $_.Name -notlike '*Probe*' -and $_.Name -notlike '*Shot*' } |
    Copy-Item -Destination $accept -Force
foreach ($d in 'runtimes','ru','config') { Copy-Item -Recurse -Force (Join-Path $build $d) (Join-Path $accept $d) }
Copy-Item (Join-Path $build 'BecquerelMonitor.exe.config') (Join-Path $accept 'AcceptP54.exe.config') -Force

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
& $csc /nologo /target:exe /langversion:7.3 "/out:$accept\AcceptP54.exe" `
    "/r:$accept\BecquerelMonitor.exe" /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
    /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$accept\Microsoft.Data.Sqlite.dll" `
    "/r:$accept\WeifenLuo.WinFormsUI.Docking.dll" "/r:$facades\netstandard.dll" `
    'D:\BqMoni_Claude\p54\AcceptP54.cs' 2>&1 | Tee-Object -FilePath "$out\accept_csc.log"
"csc exit=$LASTEXITCODE" | Tee-Object -FilePath "$out\accept_csc.log" -Append

Push-Location $accept
& "$accept\AcceptP54.exe" "--geometry=$repo\tools\CORPUS\corpus\geometries\AS80_point0.in" "--out=$out\curve_AS80_point0.txt" *> "$out\accept.log"
"AcceptP54 exit=$LASTEXITCODE" | Add-Content "$out\accept.log"
Pop-Location

Push-Location $build
$list = @(
    @{ Exe = 'MakerSaveProbe.exe';        Args = @() },
    @{ Exe = 'CalcRestoreProbe.exe';      Args = @() },
    @{ Exe = 'GeometryLayoutProbe.exe';   Args = @() },
    @{ Exe = 'FwhmReaderProbeF62.exe';    Args = @('--mode=effmaker') },
    @{ Exe = 'BoundProbeF59.exe';         Args = @("--repo=$repo") },
    @{ Exe = 'ChainProbe.exe';            Args = @() },
    @{ Exe = 'XrayLinesProbe.exe';        Args = @() },
    @{ Exe = 'CurveGenerationProbe.exe';  Args = @() },
    @{ Exe = 'DoseRateFromCurveProbe.exe'; Args = @("--dir=$repo\tools\CORPUS\corpus") }
)
foreach ($p in $list) {
    $name = [IO.Path]::GetFileNameWithoutExtension($p.Exe)
    if (-not (Test-Path $p.Exe)) { "$name : НЕТ EXE" | Add-Content "$out\summary.txt"; continue }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & ".\$($p.Exe)" @($p.Args) *> "$out\$name.log"
    "$name : exit=$LASTEXITCODE  $([int]$sw.Elapsed.TotalSeconds) s" | Add-Content "$out\summary.txt"
}
Pop-Location
Get-Content "$out\summary.txt"
