# П65: плечо Б — рабочее дерево (правка S172); свои каталоги Release_p65 / build_p65.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p65\' /p:IntermediateOutputPath='obj\Release_p65\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $repo
& pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p65" -Out "$repo\tools\effmaker\probes\build_p65"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
exit $p
