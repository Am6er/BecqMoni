# П29 12.09.2026, `AMBER22` — стенд: чистое рабочее дерево HEAD ebd3bd10 (`T209`) в C:\Users\moroz\bqp29
# + рабочая копия FsaStackShot.cs (чужая правка соседа + мой ключ --huber=). Основное дерево несёт
# незакоммиченные правки П27 (EfficiencyMaker, .csproj) — собирать там нельзя. Здесь: копия склада .rmx
# (в git его нет), Restore + Release + build_all. Лог — build_wt.log рядом.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp29'
$here = "$root\handover\p29-huber"
$log = "$here\build_wt.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File $log
# склад матриц: geometries\*.rmx и geometries\response\*.rmx, только копия (основной склад не трогается)
$n1 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\*.rmx" "$wt\tools\CORPUS\corpus\geometries\" -PassThru).Count
New-Item -ItemType Directory -Force "$wt\tools\CORPUS\corpus\geometries\response" | Out-Null
$n2 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\response\*.rmx" "$wt\tools\CORPUS\corpus\geometries\response\" -PassThru).Count
"rmx copied: geometries $n1, response $n2" | Out-File -Append $log
"FsaStackShot.cs sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\FsaStackShot.cs").Hash | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:SignManifests=false /p:GenerateManifests=false /v:m /nologo *>> "$here\msbuild_wt_restore.log"
"restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P29\' /p:IntermediateOutputPath='obj\Release_P29\' `
    /v:m /nologo *>> "$here\msbuild_wt.log"
$code = $LASTEXITCODE
"msbuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($code -ne 0) { exit $code }
Set-Location $wt
& pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P29' -Out 'tools\effmaker\probes\build_p29' *>> "$here\build_all_wt.log"
$code = $LASTEXITCODE
"build_all code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
(Get-FileHash "$wt\BecquerelMonitor\bin\Release_P29\BecquerelMonitor.exe").Hash | Out-File -Append $log
(Get-FileHash "$wt\tools\effmaker\probes\build_p29\BecquerelMonitor.exe").Hash | Out-File -Append $log
exit $code
