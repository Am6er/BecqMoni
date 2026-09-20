# П87 — контроль на ПЕРВЫХ ДВУХ сценах (A77): G1S_point5 и AS80_point0 новым построителем
# (формат 9, Q_k в матрице) рецептом склада: --threads=10 --target=0 --n=3000000 (S140: рецепт, не умолчание).
# Пишет в СВОЙ склад D:\BqMoni_Claude\p87\store (живому складу --dir не даётся).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p87\wt\tools\effmaker\probes\build_p87'
$store = 'D:\BqMoni_Claude\p87\store'
$art = 'D:\BqMoni_Claude\p87\art'
New-Item -ItemType Directory -Force "$art\dumps" | Out-Null
Push-Location $probes
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\ctrl_first2_codes.txt"
& .\CorpusMatrixProbe.exe "--dir=$store" --only=G1S_point5,AS80_point0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\first2.csv" *> "$art\ctrl_first2_matrix.log"
"matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\ctrl_first2_codes.txt"
Pop-Location
Get-Content "$art\ctrl_first2_codes.txt" | Select-Object -Last 2
Get-Content "$art\ctrl_first2_matrix.log" | Select-String '^== |клеймо|время|счёт  |шум конт|Q_k|файл|СОШЛИСЬ|ШУМНЫЕ|матриц:' | ForEach-Object { $_.Line }
