# П96 (AMBER43, остаток: поставочная раскладка как умолчание, 17.09.2026): сборка worktree —
# Debug в свои каталоги (bin\Debug_p96, obj\Debug_p96), пробы build_all.ps1 в свой -Out (build_p96).
# Код возврата каждого шага — в codes_build.txt (правило A77: смотреть код, а не «запустил»).
#   -Wt   — какой worktree собирать (wt — правка, wt_h — контроль на чистом 0f738825)
#   -Tag  — суффикс каталогов сборки
param([string]$Wt = 'D:\BqMoni_Claude\p96\wt', [string]$Tag = 'p96', [string]$Note = '')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$log = "D:\BqMoni_Claude\p96\codes_build.txt"
$stamp = Get-Date -Format 'HHmmss'
$wtName = Split-Path $Wt -Leaf
$outDir = "bin\Debug_$Tag\"
$objDir = "obj\Debug_$Tag\"
"=== build $Tag ($wtName) $Note $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$Wt\BecquerelMonitor\BecquerelMonitor.csproj"
# Restore ОБЯЗАТЕЛЕН в свежем worktree (PackageReference, project.assets.json в obj\) — иначе сотни CS0246.
& $msb $proj /t:Restore /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p96\logs\build_${Tag}_${wtName}_${stamp}_restore.log"
"restore $Tag ($wtName) code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log

& $msb $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "D:\BqMoni_Claude\p96\logs\build_${Tag}_${wtName}_${stamp}_app.log"
$appCode = $LASTEXITCODE
"app $Tag ($wtName) code=$appCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($appCode -ne 0) { "STOP: app build failed" | Out-File -Append $log; Write-Host "app code=$appCode"; exit $appCode }

Set-Location $Wt
pwsh -File "$Wt\tools\effmaker\probes\build_all.ps1" -Bin "$Wt\BecquerelMonitor\bin\Debug_$Tag" -Out "$Wt\tools\effmaker\probes\build_$Tag" 2>&1 | Out-File "D:\BqMoni_Claude\p96\logs\build_${Tag}_${wtName}_${stamp}_probes.log"
$probeCode = $LASTEXITCODE
"probes $Tag ($wtName) code=$probeCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
"done $Tag ($wtName) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Write-Host "app code=$appCode probes code=$probeCode"
exit $probeCode
