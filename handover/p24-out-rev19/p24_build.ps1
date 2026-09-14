# П24 12.09.2026: сборка СТЕНДА (worktree C:\Users\moroz\bqp24, HEAD d0aeafcb) — Release:
#   приложение (bin\Release_P24) -> пробы (probes\build_p24) -> оснастка (scripts\wd_p24).
# Каждый шаг по коду возврата (A77); при отказе — стоп. Склад матриц — копия живого (только чтение).
#   pwsh -NoProfile -File C:\Users\moroz\bqp24\handover\p24-out-rev19\p24_build.ps1
param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\bqp24'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Release_P24"
$prb  = "$root\tools\effmaker\probes\build_p24"
$wd   = "$root\tools\CORPUS\scripts\wd_p24"
$store = "$root\tools\CORPUS\corpus\geometries"
$here = "$root\handover\p24-out-rev19"
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

if (-not $SkipRestore) {
    $null = Step 'restore' { & $msbuild $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo }
}
$null = Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_P24\" "/p:IntermediateOutputPath=obj\Release_P24\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,12)) }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Get-Item "$prb\CorpusFsaProbe.exe", "$prb\BecquerelMonitor.exe", "$prb\CorpusEffProbe.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
$null = Step 'mkwd' { & "$root\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $prb -Store $store }
Write-Output ("  матриц в оснастке: {0}" -f (Get-ChildItem "$wd\config\device\response\*.rmx").Count)
Write-Output ("  NuclideDefinition.xml в оснастке: {0}" -f (Test-Path "$wd\config\NuclideDefinition.xml"))
Write-Output 'СБОРКА ПРОШЛА'
