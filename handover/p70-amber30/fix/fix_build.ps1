# П70 (AMBER30): плечо правки — worktree D:\BqMoni_Claude\p70\wt_fix (HEAD a6710dcd + правка):
# /t:Restore → /t:Build Release → build_all.ps1 (все пробы) в свои каталоги.
#   pwsh -File D:\BqMoni_Claude\p70\fix_build.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = 'D:\BqMoni_Claude\p70\wt_fix'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p70\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
"exe sha256: " + (Get-FileHash "$wt\BecquerelMonitor\bin\Release_p70\BecquerelMonitor.exe" -Algorithm SHA256).Hash + "  " + (Get-Item "$wt\BecquerelMonitor\bin\Release_p70\BecquerelMonitor.exe").LastWriteTime.ToString('HH:mm:ss')
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p70" -Out "$wt\tools\effmaker\probes\build_p70"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
exit $p
