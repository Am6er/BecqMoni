# П35 12.09.2026 (S66): сборка в ОСНОВНОМ дереве — Release, свои каталоги:
#   приложение bin\Release_p35 (obj\Release_p35) -> пробы tools\effmaker\probes\build_p35.
# Каждый шаг по коду возврата (A77); при отказе — стоп. Оснастка корпуса (mk_appwd) НЕ нужна:
# FsaInferProbeF51 ищет пики и выводит состав, фита и склада матриц не зовёт.
#   pwsh -NoProfile -File "<repo>\handover\p35-s66\p35_build.ps1"
param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "$root\BecquerelMonitor\bin\Release_p35"
$prb  = "$root\tools\effmaker\probes\build_p35"
$here = "$root\handover\p35-s66"
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
$null = Step 'msbuild' { & $msbuild $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p35\" "/p:IntermediateOutputPath=obj\Release_p35\" /v:m /nologo }
Get-Item "$bin\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  exe: {0} байт, {1}, sha256 {2}" -f $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,12)) }
$null = Step 'probes' { & "$root\tools\effmaker\probes\build_all.ps1" -Bin $bin -Out $prb }
Get-Item "$prb\FsaInferProbeF51.exe", "$prb\BecquerelMonitor.exe" | ForEach-Object { Write-Output ("  {0}: {1} байт, {2}, sha256 {3}" -f $_.Name, $_.Length, $_.LastWriteTime, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,12)) }
Write-Output 'СБОРКА ПРОШЛА'
