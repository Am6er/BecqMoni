# П68 (S173): плечо Б — рабочее дерево (правка S173); свои каталоги Release_p68 / build_p68.
# Звать: pwsh -File D:\BqMoni_Claude\p68\build_b.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p68\' /p:IntermediateOutputPath='obj\Release_p68\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $repo
& pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p68" -Out "$repo\tools\effmaker\probes\build_p68"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$repo\tools\effmaker\probes\build_p68\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p
