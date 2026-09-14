# П60: полный счёт матрицы сцены ASN16_rn_side (широкая грань, вата ρ 0.15) — физика 18 умолчаниями,
# как у живого склада (П50). Сборка плеча А (HEAD 1b8663d4): код переноса правкой П60 не трогается.
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_p60a'
$sw = [Diagnostics.Stopwatch]::StartNew()
& .\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p60\store --threads=10 --target=0 *> D:\BqMoni_Claude\p60\count.log
"code $LASTEXITCODE, $([int]$sw.Elapsed.TotalSeconds) s" | Out-File D:\BqMoni_Claude\p60\count_done.txt
