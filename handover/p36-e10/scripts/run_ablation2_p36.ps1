$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$out = Join-Path $repo 'handover\p36-e10\ablation'
$log = Join-Path $out 'run_ablation2.log'
$exe = "$repo\tools\effmaker\probes\build_p36\MarinelliSelfAbsProbeE10.exe"
"exe sha256: $((Get-FileHash $exe -Algorithm SHA256).Hash)" | Tee-Object -FilePath $log
$common = @('--scenes=J', '--energies=238.6,911.2,2614.5', '--rhos=1.6,2.8', "--out=$out", '--threads=12')
$variants = @(
    @{ tag = 'crystal20_noscatter'; args = @('--n=20000000', '--crystal=20', '--no-scatter') },
    @{ tag = 'crystal40_noscatter'; args = @('--n=8000000', '--crystal=40', '--no-scatter') }
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
