$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$out = Join-Path $repo 'handover\p36-e10\quick'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $PSScriptRoot 'run_quick_p36.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
& "$repo\tools\effmaker\probes\build_p36\MarinelliSelfAbsProbeE10.exe" --quick --n=200000 --threads=2 "--out=$out" 2>&1 | Tee-Object -FilePath $log
$code = $LASTEXITCODE
"EXIT=$code  seconds=$([int]$sw.Elapsed.TotalSeconds)" | Tee-Object -FilePath $log -Append
exit $code
