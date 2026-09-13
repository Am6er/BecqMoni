# П46 13.09.2026 (S106): сборка Release в основном дереве — свои каталоги
#   приложение bin\Release_p46 (obj\Release_p46) -> пробы probes\build_p46 -> оснастка scripts\wd_p46.
# Каждый шаг по коду возврата (A77); при отказе — стоп. Склад матриц — живой (только чтение).
#   pwsh -NoProfile -File "C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p46-s106\p46_build.ps1" [-SkipApp]
param([switch]$SkipApp, [switch]$AppOnly)
$ErrorActionPreference = 'Stop'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Release_p46"
$prb  = "$root\tools\effmaker\probes\build_p46"
$wd   = "$root\tools\CORPUS\scripts\wd_p46"
$store = "$root\tools\CORPUS\corpus\geometries"
$here = "$root\handover\p46-s106"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
[System.IO.Directory]::SetCurrentDirectory($root)
Set-Location $root

function Step($name, $sb) {
    $t0 = Get-Date
    $log = & $sb 2>&1
    $rc = $LASTEXITCODE
    $log | Out-File -Encoding utf8 "$here\build.$name.log"
    $line = "{0,-8} exit={1} ({2} s)" -f $name, $rc, [int]((Get-Date)-$t0).TotalSeconds
    Write-Output $line
    Add-Content -Encoding utf8 "$here\build.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
    if ($rc -ne 0) { $log | Select-Object -Last 25; throw "шаг $name отказал кодом $rc" }
    return $log
}

if (-not $SkipApp) {
    $null = Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p46\" "/p:IntermediateOutputPath=obj\Release_p46\" /v:m /nologo }
}
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,12)) }
if ($AppOnly) { Write-Output 'ПРИЛОЖЕНИЕ СОБРАНО'; exit 0 }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Write-Output ("  NuclideDefinition.xml в оснастке: {0}" -f (Test-Path "$wd\config\NuclideDefinition.xml"))
Write-Output 'СБОРКА ПРОШЛА'
