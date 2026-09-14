# П71 (S170): сборка плеча — worktree wt (HEAD 311c98b0) или wt_b (HEAD + правка).
#   pwsh -File D:\BqMoni_Claude\p71\wt_build.ps1 -Arm a|b
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = if ($Arm -eq 'a') { 'D:\BqMoni_Claude\p71\wt' } else { 'D:\BqMoni_Claude\p71\wt_b' }
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p71$Arm\" "/p:IntermediateOutputPath=obj\Release_p71$Arm\" /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
"exe sha256: " + (Get-FileHash "$wt\BecquerelMonitor\bin\Release_p71$Arm\BecquerelMonitor.exe" -Algorithm SHA256).Hash
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p71$Arm" -Out "$wt\tools\effmaker\probes\build_p71$Arm"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
exit $p
