# П69 (S174): плечо Б для МАЛОЙ БАЗЫ — worktree D:\BqMoni_Claude\p69\wt_b (HEAD 6c2833cf + правка S174 через git apply,
# 11 файлов байт в байт те же, что в основном дереве, два — те же с точностью до CRLF/LF); свои каталоги Release_p69b / build_p69b.
# Зачем отдельный worktree: в общем дереве П66 переписала корпус (tools/CORPUS/corpus/*), и плечо Б из основного
# дерева считало бы ДРУГОЙ корпус, чем плечо А (worktree, чистый HEAD). Образец — П68.
# Звать: pwsh -File D:\BqMoni_Claude\p69\build_bw.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p69\wt_b'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$sw = [Diagnostics.Stopwatch]::StartNew()
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /v:m /nologo
"restore code $LASTEXITCODE"
& $msb "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p69b\' /p:IntermediateOutputPath='obj\Release_p69b\' /v:m /nologo
$b = $LASTEXITCODE
"build code $b  ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($b -ne 0) { exit $b }
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p69b" -Out "$wt\tools\effmaker\probes\build_p69b"
$p = $LASTEXITCODE
"build_all code $p  ($([int]$sw.Elapsed.TotalSeconds) s)"
"exe sha256: " + (Get-FileHash "$wt\tools\effmaker\probes\build_p69b\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16)
exit $p
