# П94 (AMBER44 + занос M12, 17.09.2026): сборка worktree D:\BqMoni_Claude\p94\wt — Release в свои
# каталоги, пробы build_all.ps1 в свой -Out. Код возврата каждого шага — в codes_build.txt (правило A77).
param([string]$Tag = 'p94', [string]$Note = '')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$wt = 'D:\BqMoni_Claude\p94\wt'
$log = "D:\BqMoni_Claude\p94\codes_build.txt"
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
$outDir = "bin\Release_$Tag\"
$objDir = "obj\Release_$Tag\"
$stamp = Get-Date -Format 'HHmmss'

"=== build $Tag $Note $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
& $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p94\logs\build_${Tag}_${stamp}_restore.log"
"restore $Tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log

& $msb $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p94\logs\build_${Tag}_${stamp}_app.log"
$appCode = $LASTEXITCODE
"app $Tag code=$appCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($appCode -ne 0) { "STOP: app build failed" | Out-File -Append $log; exit $appCode }

Set-Location $wt
pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_$Tag" -Out "$wt\tools\effmaker\probes\build_$Tag" 2>&1 | Out-File "D:\BqMoni_Claude\p94\logs\build_${Tag}_${stamp}_probes.log"
$probeCode = $LASTEXITCODE
"probes $Tag code=$probeCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
"done $Tag $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
exit $probeCode
