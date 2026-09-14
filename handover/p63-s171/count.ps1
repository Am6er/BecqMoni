# П63: полный счёт матрицы сцены ASN16_rn_side (широкая грань, вата ρ 0.15) — физика 18 умолчаниями,
# как у живого склада (П50); сборка плеча А (worktree HEAD 449335e4): перенос правкой П63 не трогается.
Set-Location 'D:\BqMoni_Claude\p63\wt\tools\effmaker\probes\build_p63a'
$sw = [Diagnostics.Stopwatch]::StartNew()
& .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p63\store --threads=10 --target=0 *> D:\BqMoni_Claude\p63\logs\count_ASN16_rn_side.log
"code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p63\logs\count_done.txt