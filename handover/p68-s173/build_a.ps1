# П68 (S173): плечо А — HEAD 198500a7 в worktree D:\BqMoni_Claude\p68\wt; свои каталоги Release_p68a / build_p68a.
# Звать: pwsh -File D:\BqMoni_Claude\p68\build_a.ps1   (копия без пробелов в пути — грабля Start-Process, П65 §0)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p68\wt'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p68a\' /p:IntermediateOutputPath='obj\Release_p68a\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p68a" -Out "$wt\tools\effmaker\probes\build_p68a"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\build_p68a\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p
