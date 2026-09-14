# П69 (S174): счёт матрицы сцены ASN16_rn_side (широкая грань, вата ρ 0.15) — физика 18 умолчаниями,
# сборка плеча А (worktree HEAD 6c2833cf); правка S174 переноса не касается. Приём П65/П68.
#   pwsh -File D:\BqMoni_Claude\p69\count.ps1 [-Check]   # -Check: контроль A77 на двух узлах в store_check
param([switch]$Check)
Set-Location 'D:\BqMoni_Claude\p69\wt\tools\effmaker\probes\build_p69a'
$sw = [Diagnostics.Stopwatch]::StartNew()
if ($Check) {
    & .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p69\store_check --nodes=2 --emin=30 --emax=31 --n=400000 --threads=10 --target=0 --jnodes=0 *> D:\BqMoni_Claude\p69\logs\count_check_a77.log
    "code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p69\logs\count_check_done.txt
} else {
    & .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p69\store --threads=10 --target=0 *> D:\BqMoni_Claude\p69\logs\count_ASN16_rn_side.log
    "code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p69\logs\count_done.txt
}