# П88: сборка приложения Release в worktree D:\BqMoni_Claude\p88\wt (Restore -> Build). Пробы — build_probes.ps1 отдельно.
$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
$wt = 'D:\BqMoni_Claude\p88\wt'
$ms = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
"start restore $(Get-Date -Format 'HH:mm:ss')"
& $ms $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo > "D:\BqMoni_Claude\p88\build_restore.log" 2>&1
$c1 = $LASTEXITCODE; "restore code=$c1 $(Get-Date -Format 'HH:mm:ss')"
if ($c1 -ne 0) { "RESTORE FAILED"; exit 1 }
& $ms $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p88\' /p:IntermediateOutputPath='obj\Release_p88\' /v:m /nologo > "D:\BqMoni_Claude\p88\build_app.log" 2>&1
$c2 = $LASTEXITCODE; "build code=$c2 $(Get-Date -Format 'HH:mm:ss')"
if ($c2 -ne 0) { "BUILD FAILED"; exit 2 }
"APP OK"
exit 0
