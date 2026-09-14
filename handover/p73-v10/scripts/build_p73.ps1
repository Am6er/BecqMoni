# П73 (V10): сборка стенда из worktree D:\BqMoni_Claude\p73\wt на 311c98b0.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\build_p73.ps1
# Restore -> Build Release (bin\Release_p73, obj\Release_p73) -> пробы build_all.ps1 -Out build_p73. Коды возврата — в build_codes.txt.
$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
$p  = 'D:\BqMoni_Claude\p73'
$wt = "$p\wt"
$ms = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = "$wt\BecquerelMonitor\BecquerelMonitor.csproj"
$codes = @()
"start restore $(Get-Date -Format 'HH:mm:ss')"
& $ms $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /v:m /nologo > "$p\build_restore.log" 2>&1
$c1 = $LASTEXITCODE; $codes += "restore=$c1"; "restore code=$c1 $(Get-Date -Format 'HH:mm:ss')"
if ($c1 -ne 0) { "RESTORE FAILED"; $codes | Set-Content "$p\build_codes.txt"; exit 1 }
& $ms $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p73\' /p:IntermediateOutputPath='obj\Release_p73\' /v:m /nologo > "$p\build_app.log" 2>&1
$c2 = $LASTEXITCODE; $codes += "build=$c2"; "build code=$c2 $(Get-Date -Format 'HH:mm:ss')"
if ($c2 -ne 0) { "BUILD FAILED"; $codes | Set-Content "$p\build_codes.txt"; exit 2 }
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p73" -Out "$wt\tools\effmaker\probes\build_p73" > "$p\build_probes.log" 2>&1
$c3 = $LASTEXITCODE; $codes += "probes=$c3"; "probes code=$c3 $(Get-Date -Format 'HH:mm:ss')"
if ($c3 -ne 0) { "PROBES FAILED"; $codes | Set-Content "$p\build_codes.txt"; exit 3 }
$h1 = (Get-FileHash "$wt\BecquerelMonitor\bin\Release_p73\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$h2 = (Get-FileHash "$wt\tools\effmaker\probes\build_p73\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$codes += "sha_app=$h1"; $codes += "sha_probes=$h2"; $codes += "same=$($h1 -eq $h2)"
$codes | Set-Content "$p\build_codes.txt"
"ALL OK; sha app=$($h1.Substring(0,16)) probes=$($h2.Substring(0,16)) same=$($h1 -eq $h2)"
exit 0
