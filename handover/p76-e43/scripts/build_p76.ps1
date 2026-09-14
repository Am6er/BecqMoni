# П76 (E43): сборка стенда из worktree на 97ccef9f. Образец — handover/p73-v10/scripts/build_p73.ps1.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p76\build_p76.ps1 -Wt D:\BqMoni_Claude\p76\wt   -Tag p76    # чистый 97ccef9f: стенд FSA, матрицы, кривые
#   pwsh -NoProfile -File D:\BqMoni_Claude\p76\build_p76.ps1 -Wt D:\BqMoni_Claude\p76\wt_b -Tag p76b   # 97ccef9f + правка полосы: генератор, пробы приёмки
# Restore -> Build Release (bin\Release_<Tag>, obj\Release_<Tag>) -> пробы build_all.ps1 -Out build_<Tag>.
# Коды возврата — в build_codes_<Tag>.txt. Ключ /p:GenerateManifests=false обязателен (T75) и у Restore, и у Build.
param([Parameter(Mandatory)][string]$Wt, [Parameter(Mandatory)][string]$Tag)
$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
$p  = 'D:\BqMoni_Claude\p76'
$ms = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$Wt\BecquerelMonitor\BecquerelMonitor.csproj"
$codes = @()
"start restore $(Get-Date -Format 'HH:mm:ss')"
& $ms $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /v:m /nologo > "$p\build_restore_$Tag.log" 2>&1
$c1 = $LASTEXITCODE; $codes += "restore=$c1"; "restore code=$c1 $(Get-Date -Format 'HH:mm:ss')"
if ($c1 -ne 0) { "RESTORE FAILED"; $codes | Set-Content "$p\build_codes_$Tag.txt"; exit 1 }
& $ms $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_$Tag\" "/p:IntermediateOutputPath=obj\Release_$Tag\" /v:m /nologo > "$p\build_app_$Tag.log" 2>&1
$c2 = $LASTEXITCODE; $codes += "build=$c2"; "build code=$c2 $(Get-Date -Format 'HH:mm:ss')"
if ($c2 -ne 0) { "BUILD FAILED"; $codes | Set-Content "$p\build_codes_$Tag.txt"; exit 2 }
& pwsh -NoProfile -File "$Wt\tools\effmaker\probes\build_all.ps1" -Bin "$Wt\BecquerelMonitor\bin\Release_$Tag" -Out "$Wt\tools\effmaker\probes\build_$Tag" > "$p\build_probes_$Tag.log" 2>&1
$c3 = $LASTEXITCODE; $codes += "probes=$c3"; "probes code=$c3 $(Get-Date -Format 'HH:mm:ss')"
if ($c3 -ne 0) { "PROBES FAILED"; $codes | Set-Content "$p\build_codes_$Tag.txt"; exit 3 }
$h1 = (Get-FileHash "$Wt\BecquerelMonitor\bin\Release_$Tag\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$h2 = (Get-FileHash "$Wt\tools\effmaker\probes\build_$Tag\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$codes += "sha_app=$h1"; $codes += "sha_probes=$h2"; $codes += "same=$($h1 -eq $h2)"
$codes | Set-Content "$p\build_codes_$Tag.txt"
"ALL OK; sha app=$($h1.Substring(0,16)) probes=$($h2.Substring(0,16)) same=$($h1 -eq $h2)"
exit 0
