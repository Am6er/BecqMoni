$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$out = Join-Path $repo 'handover\p36-e10\full'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $out 'run_full.log'
$threads = [Math]::Max(2, [Environment]::ProcessorCount - 2)
"exe sha256: $((Get-FileHash "$repo\tools\effmaker\probes\build_p36\MarinelliSelfAbsProbeE10.exe" -Algorithm SHA256).Hash)" | Tee-Object -FilePath $log
"app sha256: $((Get-FileHash "$repo\tools\effmaker\probes\build_p36\BecquerelMonitor.exe" -Algorithm SHA256).Hash)" | Tee-Object -FilePath $log -Append
$sw = [Diagnostics.Stopwatch]::StartNew()
& "$repo\tools\effmaker\probes\build_p36\MarinelliSelfAbsProbeE10.exe" --n=4000000 --threads=$threads "--out=$out" 2>&1 | Tee-Object -FilePath $log -Append
$code = $LASTEXITCODE
"EXIT=$code  seconds=$([int]$sw.Elapsed.TotalSeconds)  threads=$threads" | Tee-Object -FilePath $log -Append
exit $code
