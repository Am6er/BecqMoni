# П37 13.09.2026 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (а): новый код (физика 17) ОБРАТНЫМИ ключами — все семь
# ВЫКЛ (--lbin=0 --pkch=0 --lys=0 --etr=0 --positron=0 --posoffset=0 --rayl2=0) — обязан дать
# матрицу, ТОЖДЕСТВЕННУЮ живому складу физики 16 по телу (MatrixDiffProbe: тела тождественны по
# отпечатку, 0.000/0.000/0.00), при клейме, отличном только phys=. Сцена — AS80_point0 (есть в
# складе; самая быстрая из его сцен: 326 с на 10 потоках у П20). Рецепт склада: 140 узлов,
# 3 млн историй, --threads=10 --target=0 (плоский счёт). С 06.09.2026 (A104 закрыта) счёт на
# нескольких потоках детерминирован — иначе контроль против склада был бы невозможен.
# Проба ПИШЕТ .rmx в --dir — поэтому копия сцены в C:\Users\moroz\bqp37_ctrl\a\, живой склад
# только читается (MatrixDiffProbe --a=).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp37'
$bin = "$wt\tools\effmaker\probes\build_p37"
$art = "$root\handover\p37-store"
$dir = 'C:\Users\moroz\bqp37_ctrl\a'
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$root\tools\CORPUS\corpus\geometries\$scene.in" "$dir\$scene.in" -Force
"ctrl_a start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force `
    --lbin=0 --pkch=0 --lys=0 --etr=0 --positron=0 --posoffset=0 --rayl2=0 > "$art\ctrl_a_matrix.log" 2>&1
"ctrl_a matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$root\tools\CORPUS\corpus\geometries\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl_a_diff.log" 2>&1
"ctrl_a diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
