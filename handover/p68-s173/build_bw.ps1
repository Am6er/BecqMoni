# П68 (S173): плечо Б для МАЛОЙ БАЗЫ — worktree D:\BqMoni_Claude\p68\wt_b (HEAD 198500a7 + правка S173 через git apply,
# файлы байт в байт те же, что в основном дереве); свои каталоги Release_p68b / build_p68b.
# Зачем отдельный worktree: в общем дереве П66 переписала корпус (`tools/CORPUS/corpus/*`, в том числе
# ПШПВ-калибровки спектров малой базы G1S24_* и прибор `Gamma-1S UDS-GC 63x63 1024`), и плечо Б из
# основного дерева считало бы ДРУГОЙ корпус, чем плечо А (worktree, чистый HEAD).
# Звать: pwsh -File D:\BqMoni_Claude\p68\build_bw.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p68\wt_b'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p68b\' /p:IntermediateOutputPath='obj\Release_p68b\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p68b" -Out "$wt\tools\effmaker\probes\build_p68b"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\build_p68b\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p
