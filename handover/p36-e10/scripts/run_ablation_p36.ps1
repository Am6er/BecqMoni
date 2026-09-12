$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$out = Join-Path $repo 'handover\p36-e10\ablation'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $out 'run_ablation.log'
$exe = "$repo\tools\effmaker\probes\build_p36\MarinelliSelfAbsProbeE10.exe"
"exe sha256: $((Get-FileHash $exe -Algorithm SHA256).Hash)" | Tee-Object -FilePath $log
"app sha256: $((Get-FileHash "$repo\tools\effmaker\probes\build_p36\BecquerelMonitor.exe" -Algorithm SHA256).Hash)" | Tee-Object -FilePath $log -Append
$common = @('--scenes=J', '--energies=238.6,911.2,2614.5', '--rhos=1.6,2.8', "--out=$out", '--threads=12')
$variants = @(
    @{ tag = 'base';      args = @('--n=4000000') },
    @{ tag = 'noscatter'; args = @('--n=4000000', '--no-scatter') },
    @{ tag = 'nocohpass'; args = @('--n=4000000', '--no-cohpass') },
    @{ tag = 'crystal20'; args = @('--n=20000000', '--crystal=20') }
)
$total = 0
foreach ($v in $variants) {
    "##### вариант $($v.tag) $(Get-Date -Format 'HH:mm:ss')" | Tee-Object -FilePath $log -Append
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $exe @common @($v.args) "--tag=$($v.tag)" 2>&1 | Where-Object { $_ -notmatch '^  \[' } | Tee-Object -FilePath $log -Append
    "EXIT_$($v.tag)=$LASTEXITCODE seconds=$([int]$sw.Elapsed.TotalSeconds)" | Tee-Object -FilePath $log -Append
    $total += $LASTEXITCODE
}
"EXIT=$total" | Tee-Object -FilePath $log -Append
exit $total
