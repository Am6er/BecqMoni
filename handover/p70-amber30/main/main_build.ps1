# П70 (AMBER30): сборка ОСНОВНОГО дерева с правкой — свои каталоги bin\Release_p70, obj\Release_p70, probes\build_p70.
#   pwsh -File D:\BqMoni_Claude\p70\main_build.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p70\' /p:IntermediateOutputPath='obj\Release_p70\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
"exe sha256: " + (Get-FileHash "$repo\BecquerelMonitor\bin\Release_p70\BecquerelMonitor.exe" -Algorithm SHA256).Hash + "  " + (Get-Item "$repo\BecquerelMonitor\bin\Release_p70\BecquerelMonitor.exe").LastWriteTime.ToString('HH:mm:ss')
Set-Location $repo
& pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p70" -Out "$repo\tools\effmaker\probes\build_p70"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
exit $p
