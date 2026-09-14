# П31 12.09.2026, `A308` — стенд: чистое рабочее дерево HEAD 3dd0da47 (`T209`) в C:\Users\moroz\bqp31
# + рабочая копия FsaStackShot.cs (чужая правка соседа + ключ --huber= П29) + мои FsaAnalyzer.cs
# (прибор NnlsTraceSink в NnlsSolve) и FsaNnlsDumpProbe.cs. Основное дерево несёт незакоммиченные
# правки П27 (EfficiencyMaker, .csproj) — собирать там нельзя. Здесь: копия склада .rmx (в git его нет),
# Restore + Release_P31 + build_all -Out build_p31. Лог — build_wt.log рядом.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp31'
$here = "$root\handover\p31-a308-nnls"
$log = "$here\build_wt.log"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File $log
$n1 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\*.rmx" "$wt\tools\CORPUS\corpus\geometries\" -PassThru).Count
New-Item -ItemType Directory -Force "$wt\tools\CORPUS\corpus\geometries\response" | Out-Null
$n2 = (Copy-Item "$root\tools\CORPUS\corpus\geometries\response\*.rmx" "$wt\tools\CORPUS\corpus\geometries\response\" -PassThru).Count
"rmx copied: geometries $n1, response $n2" | Out-File -Append $log
"FsaStackShot.cs sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\FsaStackShot.cs").Hash | Out-File -Append $log
"FsaAnalyzer.cs sha256: " + (Get-FileHash "$wt\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs").Hash | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:SignManifests=false /p:GenerateManifests=false /v:m /nologo *>> "$here\msbuild_wt_restore.log"
"restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P31\' /p:IntermediateOutputPath='obj\Release_P31\' `
    /v:m /nologo *>> "$here\msbuild_wt.log"
$code = $LASTEXITCODE
"msbuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($code -ne 0) { exit $code }
Set-Location $wt
& pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P31' -Out 'tools\effmaker\probes\build_p31' *>> "$here\build_all_wt.log"
$code = $LASTEXITCODE
"build_all code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
(Get-FileHash "$wt\BecquerelMonitor\bin\Release_P31\BecquerelMonitor.exe").Hash | Out-File -Append $log
(Get-FileHash "$wt\tools\effmaker\probes\build_p31\BecquerelMonitor.exe").Hash | Out-File -Append $log
exit $code
