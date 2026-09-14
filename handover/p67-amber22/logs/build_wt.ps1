$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
$wt = 'D:\BqMoni_Claude\p67\wt'
$ms = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
"start restore $(Get-Date -Format 'HH:mm:ss')"
& $ms $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo > "D:\BqMoni_Claude\p67\build_restore.log" 2>&1
$c1 = $LASTEXITCODE; "restore code=$c1 $(Get-Date -Format 'HH:mm:ss')"
if ($c1 -ne 0) { "RESTORE FAILED"; exit 1 }
& $ms $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p67\' /p:IntermediateOutputPath='obj\Release_p67\' /v:m /nologo > "D:\BqMoni_Claude\p67\build_app.log" 2>&1
$c2 = $LASTEXITCODE; "build code=$c2 $(Get-Date -Format 'HH:mm:ss')"
if ($c2 -ne 0) { "BUILD FAILED"; exit 2 }
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p67" -Out "$wt\tools\effmaker\probes\build_p67" > "D:\BqMoni_Claude\p67\build_probes.log" 2>&1
$c3 = $LASTEXITCODE; "probes code=$c3 $(Get-Date -Format 'HH:mm:ss')"
if ($c3 -ne 0) { "PROBES FAILED"; exit 3 }
"ALL OK"
exit 0
