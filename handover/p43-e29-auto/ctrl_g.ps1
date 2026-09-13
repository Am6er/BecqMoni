# П43 13.09.2026 — контроль (г): путь МАТРИЦЫ автоматики не получает. Сборка build_p43 считает
# AS80_point0 УМОЛЧАНИЯМИ пробы (ни одного ключа физики) и обязана дать матрицу, ТОЖДЕСТВЕННУЮ
# живому складу по телу (MatrixDiffProbe 0.000/0.000/0.00, клеймо то же). Рецепт склада: 140 узлов,
# 3 млн историй, --threads=10 --target=0. Проба ПИШЕТ .rmx в --dir — потому копия сцены в scratch,
# живой склад только читается (sha256 до/после).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p43"
$art = "$root\handover\p43-e29-auto"
$dir = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad\p43\ctrl_g'
$store = "$root\tools\CORPUS\corpus\geometries"
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$store\$scene.in" "$dir\$scene.in" -Force
"ctrl_g start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_g sha256 store BEFORE $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force > "$art\ctrl_g_matrix.log" 2>&1
"ctrl_g matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$store\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl_g_diff.log" 2>&1
"ctrl_g diff code=$LASTEXITCODE (ОБЯЗАН быть 0: тело тождественно) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"ctrl_g sha256 store AFTER  $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"ctrl_g sha256 ctrl         $((Get-FileHash "$dir\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"ctrl_g end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
