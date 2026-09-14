# П65: полный счёт матрицы сцены ASN16_rn_side (широкая грань, вата ρ 0.15) — физика 18 умолчаниями,
# как у живого склада (П50); сборка плеча А (worktree HEAD 847a8799): перенос правкой П65 не трогается.
#   pwsh -File handover\p65-s172\count.ps1 [-Check]   # -Check: контроль A77 на двух узлах в store_check
param([switch]$Check)
Set-Location 'D:\BqMoni_Claude\p65\wt\tools\effmaker\probes\build_p65a'
$sw = [Diagnostics.Stopwatch]::StartNew()
if ($Check) {
    & .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p65\store_check --nodes=2 --emin=30 --emax=31 --n=400000 --threads=10 --target=0 --jnodes=0 *> D:\BqMoni_Claude\p65\logs\count_check_a77.log
    "code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p65\logs\count_check_done.txt
} else {
    & .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p65\store --threads=10 --target=0 *> D:\BqMoni_Claude\p65\logs\count_ASN16_rn_side.log
    "code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p65\logs\count_done.txt
}
