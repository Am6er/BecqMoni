# П69 (S174): плечо Б — рабочее дерево (правка S174); свои каталоги Release_p69 / obj\Release_p69 / build_p69.
# Звать: pwsh -File D:\BqMoni_Claude\p69\build_b.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p69\' /p:IntermediateOutputPath='obj\Release_p69\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $repo
& pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p69" -Out "$repo\tools\effmaker\probes\build_p69"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$repo\tools\effmaker\probes\build_p69\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p