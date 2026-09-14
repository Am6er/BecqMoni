# П69 (S174): плечо А — HEAD 6c2833cf в worktree D:\BqMoni_Claude\p69\wt; свои каталоги Release_p69a / build_p69a.
# Звать: pwsh -File D:\BqMoni_Claude\p69\build_a.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p69\wt'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p69a\' /p:IntermediateOutputPath='obj\Release_p69a\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p69a" -Out "$wt\tools\effmaker\probes\build_p69a"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\build_p69a\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p