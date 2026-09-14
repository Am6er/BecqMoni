# П63: плечо А — HEAD 449335e4 в worktree D:\BqMoni_Claude\p63\wt; свои каталоги Release_p63a / build_p63a.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p63\wt'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p63a\' /p:IntermediateOutputPath='obj\Release_p63a\' /v:m /nologo
"build code $LASTEXITCODE  ($([int]$sw.Elapsed.TotalSeconds) s)"
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p63a" -Out "$wt\tools\effmaker\probes\build_p63a"
"build_all code $LASTEXITCODE  ($([int]$sw.Elapsed.TotalSeconds) s)"