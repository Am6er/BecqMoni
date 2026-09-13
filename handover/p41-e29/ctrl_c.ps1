# П41 13.09.2026 — приёмка (4): признак разброса на узел в пути КРИВОЙ, с положительным контролем.
# Путь кривой — EfficiencyCalculation.Run, тот же, что у кнопки «Посчитать из геометрии»; читатель —
# CorpusEffProbe (сводка NodeSpread, отказ на шумной кривой) и журнал приложения (--log печатает
# его строки как есть: EfficiencyMakerNodeSpread / EfficiencyMakerNodeSpreadWarning).
#   (1) полевая сцена ВЫКЛ (умолчание приложения) — ОБЯЗАНА дать предупреждение и отказ (код 1);
#   (2) та же сцена --imp=1 — разброс около процента, предупреждения нет;
#   (3) сосудная сцена корпуса (ASN16_lu_side) ВЫКЛ, --dry на живом складе (только чтение) —
#       предупреждения нет, код 0 — отрицательный контроль: признак не кричит на сосуде.
# Сцена «на земле» записана SceneCostProbe --save-ground (ctrl_b); индекс и спектр — копии в scratch.
# Матрица-заглушка ASN16_ground.rmx — копия ctrl_a\AS80_point0.rmx: проба проверяет лишь НАЛИЧИЕ
# файла матрицы (--dry ничего не копирует), а без него ok=false и код 1 ничего не значил бы.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p41"
$art = "$root\handover\p41-e29"
$scratch = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad\p41'
$g = "$scratch\ground"
Set-Location $root
New-Item -ItemType Directory -Force "$g\spectra" | Out-Null
"geometry,spectrum,preset,vessel`nASN16_ground,ASN16_Lu176,Atom Spectra Nano 16,`"детектор на земле, грунт`"" | Set-Content -Encoding UTF8 "$g\index.csv"
Copy-Item "$root\tools\CORPUS\corpus\spectra\ASN16_Lu176.xml" "$g\spectra\ASN16_Lu176.xml" -Force
Copy-Item "$scratch\ctrl_a\AS80_point0.rmx" "$g\ASN16_ground.rmx" -Force
"ctrl_c start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"

& "$bin\CorpusEffProbe.exe" "--dir=$g" "--spectra=$g\spectra" --dry --force --log > "$art\ctrl_c_ground_off.log" 2>&1
"ctrl_c ground OFF code=$LASTEXITCODE (ОБЯЗАН быть 1: шумная кривая) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

& "$bin\CorpusEffProbe.exe" "--dir=$g" "--spectra=$g\spectra" --dry --force --log --imp=1 > "$art\ctrl_c_ground_imp1.log" 2>&1
"ctrl_c ground imp=1 code=$LASTEXITCODE (ОБЯЗАН быть 0) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

& "$bin\CorpusEffProbe.exe" --only=ASN16_lu_side --dry --force --log > "$art\ctrl_c_vessel_off.log" 2>&1
"ctrl_c vessel OFF code=$LASTEXITCODE (ОБЯЗАН быть 0) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_c end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
