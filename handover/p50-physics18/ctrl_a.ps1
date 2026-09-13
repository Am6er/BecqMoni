# П50 13.09.2026 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (а): новый код (физика 18) ОБРАТНЫМИ ключами
# --ecomp=0 --bpath=0 обязан дать матрицу, ТОЖДЕСТВЕННУЮ живому складу физики 17 по телу
# (MatrixDiffProbe: тела тождественны по отпечатку, 0.000/0.000/0.00), при клейме, отличном
# только phys= (17 → 18). Сцена — AS80_point0 (самая быстрая из склада: 318…353 с на 10 потоках
# у П37). Рецепт склада: 140 узлов, 3 млн историй, --threads=10 --target=0 (плоский счёт).
# Проба ПИШЕТ .rmx в --dir — поэтому копия сцены в D:\BqMoni_Claude\p50\ctrl\a\, живой склад
# только читается (MatrixDiffProbe --a=).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p50\wt'
$bin = "$wt\tools\effmaker\probes\build_p50"
$art = 'D:\BqMoni_Claude\p50\art'
$dir = 'D:\BqMoni_Claude\p50\ctrl\a'
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$root\tools\CORPUS\corpus\geometries\$scene.in" "$dir\$scene.in" -Force
"ctrl_a start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force `
    --ecomp=0 --bpath=0 > "$art\ctrl_a_matrix.log" 2>&1
"ctrl_a matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$root\tools\CORPUS\corpus\geometries\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl_a_diff.log" 2>&1
"ctrl_a diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
$env:PYTHONIOENCODING = 'utf-8'
python "$root\handover\p50-physics18\rmx_tails.py" "$root\tools\CORPUS\corpus\geometries\$scene.rmx" "$dir\$scene.rmx" > "$art\ctrl_a_tails.txt" 2>&1
"ctrl_a tails code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_a end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
