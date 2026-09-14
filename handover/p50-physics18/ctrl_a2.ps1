# П50 13.09.2026 — контроль (а) на ВТОРОЙ сцене, тяжёлой по П44 (AS80_th_disk: стеклянный ториевый диск
# перед торцом, ecomp ×1.13): обратными ключами --ecomp=0 --bpath=0 тело обязано совпасть с живым складом
# физики 17 ДО БИТА (у сцены с пробой ключ ecomp при ВЫКЛ обязан быть пустым — здесь это и проверяется),
# а время на часах в ТОТ ЖЕ час даёт честное отношение физики 18 к 17 (контроль (б) дал 351.1 с).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p50\wt'
$bin = "$wt\tools\effmaker\probes\build_p50"
$art = 'D:\BqMoni_Claude\p50\art'
$dir = 'D:\BqMoni_Claude\p50\ctrl\a2'
$scene = 'AS80_th_disk'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$root\tools\CORPUS\corpus\geometries\$scene.in" "$dir\$scene.in" -Force
"ctrl_a2 start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force `
    --ecomp=0 --bpath=0 > "$art\ctrl_a2_matrix.log" 2>&1
"ctrl_a2 matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$root\tools\CORPUS\corpus\geometries\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl_a2_diff.log" 2>&1
"ctrl_a2 diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
$env:PYTHONIOENCODING = 'utf-8'
python "$root\handover\p50-physics18\rmx_tails.py" "$root\tools\CORPUS\corpus\geometries\$scene.rmx" "$dir\$scene.rmx" > "$art\ctrl_a2_tails.txt" 2>&1
"ctrl_a2 tails code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_a2 end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
