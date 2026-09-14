# П55 (A72 оценка, 14.09.2026): сборка worktree D:\BqMoni_Claude\p55\wt — Release в свои каталоги,
# пробы build_all.ps1 в свой -Out. Код возврата каждого шага — в codes.txt (правило A77: смотреть на код,
# а не на факт запуска). Параметр -Tag различает эталон HEAD (ref) и вариант (var).
param([string]$Tag = 'ref')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$wt = 'D:\BqMoni_Claude\p55\wt'
$log = "D:\BqMoni_Claude\p55\codes.txt"
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
$outDir = "bin\Release_p55_$Tag\"
$objDir = "obj\Release_p55_$Tag\"

& $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p55\build_${Tag}_restore.log"
"restore $Tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log

& $msb $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p55\build_${Tag}_app.log"
"app $Tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log

Set-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p55_$Tag" -Out "$wt\tools\effmaker\probes\build_p55_$Tag" 2>&1 | Out-File "D:\BqMoni_Claude\p55\build_${Tag}_probes.log"
"probes $Tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
