# П26 12.09.2026, `AMBER22` — стенд: чистое рабочее дерево HEAD (`T209`) в C:\Users\moroz\bqp26.
# Сосед (AMBER23) пишет в основное дерево новую пробу FwhmOverflowProbe.cs — build_all.ps1 там
# отказывает по несовпадению плана (build_all.log). Здесь: копия склада .rmx (в git его нет),
# Restore + Release + build_all. Лог — build_wt.log рядом.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp26'
$log = "$root\handover\p26-amber22\build_wt.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File $log
# склад матриц: geometries\*.rmx и geometries\response\*.rmx, только копия (основной склад не трогается)
$n1 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\*.rmx" "$wt\tools\CORPUS\corpus\geometries\" -PassThru).Count
New-Item -ItemType Directory -Force "$wt\tools\CORPUS\corpus\geometries\response" | Out-Null
$n2 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\response\*.rmx" "$wt\tools\CORPUS\corpus\geometries\response\" -PassThru).Count
"rmx copied: geometries $n1, response $n2" | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:SignManifests=false /p:GenerateManifests=false /v:m /nologo *>> "$root\handover\p26-amber22\msbuild_wt_restore.log"
"restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P26\' /p:IntermediateOutputPath='obj\Release_P26\' `
    /v:m /nologo *>> "$root\handover\p26-amber22\msbuild_wt.log"
$code = $LASTEXITCODE
"msbuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($code -ne 0) { exit $code }
Set-Location $wt
& pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P26' -Out 'tools\effmaker\probes\build_p26' *>> "$root\handover\p26-amber22\build_all_wt.log"
$code = $LASTEXITCODE
"build_all code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
(Get-FileHash "$wt\BecquerelMonitor\bin\Release_P26\BecquerelMonitor.exe").Hash | Out-File -Append $log
(Get-FileHash "$wt\tools\effmaker\probes\build_p26\BecquerelMonitor.exe").Hash | Out-File -Append $log
exit $code
