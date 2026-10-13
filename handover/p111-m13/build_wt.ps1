# П111 (M13, 19.09.2026): сборка worktree — Release в свои каталоги (bin\p111, obj\p111), пробы build_all.ps1 в build_p111.
# Код возврата каждого шага — в codes_build.txt (правило A77). Образец — handover/p106-m13/build_wt.ps1.
#   -Wt   каталог worktree (D:\BqMoni_Claude\p111\wt)
#   -Tag  суффикс каталогов: bin\<Tag>\, obj\<Tag>\, probes\build_<Tag>
param([string]$Wt = 'D:\BqMoni_Claude\p111\wt', [string]$Tag = 'p111', [string]$Note = '', [switch]$Rebuild)
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$root = 'D:\BqMoni_Claude\p111'
$log = "$root\codes_build.txt"
New-Item -ItemType Directory -Force "$root\logs" | Out-Null
$outDir = "bin\$Tag\"
$objDir = "obj\$Tag\"
$stamp = Get-Date -Format 'HHmmss'
$target = if ($Rebuild) { 'Rebuild' } else { 'Build' }
# (сторож check_build_recipe: ключ /p:GenerateManifests=false обязан стоять в восьми строках после строки с MSBuild.exe)
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$Wt\BecquerelMonitor\BecquerelMonitor.csproj"

"=== build $Tag ($Wt) $Note $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
& $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "$root\logs\build_${Tag}_${stamp}_restore.log"
"restore $Tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log

& $msb $proj /t:$target /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$outDir" "/p:BaseIntermediateOutputPath=$objDir" /v:m /nologo 2>&1 | Out-File "$root\logs\build_${Tag}_${stamp}_app.log"
$appCode = $LASTEXITCODE
"app $Tag code=$appCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
if ($appCode -ne 0) { "STOP: app build failed" | Out-File -Append $log; exit $appCode }

Set-Location $Wt
pwsh -File "$Wt\tools\effmaker\probes\build_all.ps1" -Bin "$Wt\BecquerelMonitor\bin\$Tag" -Out "$Wt\tools\effmaker\probes\build_$Tag" 2>&1 | Out-File "$root\logs\build_${Tag}_${stamp}_probes.log"
$probeCode = $LASTEXITCODE
"probes $Tag code=$probeCode $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
$shaApp = (Get-FileHash "$Wt\BecquerelMonitor\bin\$Tag\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$shaProbe = (Get-FileHash "$Wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe" -Algorithm SHA256).Hash
"sha app=$shaApp probes=$shaProbe same=$($shaApp -eq $shaProbe)" | Out-File -Append $log
"done $Tag $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
exit $probeCode
