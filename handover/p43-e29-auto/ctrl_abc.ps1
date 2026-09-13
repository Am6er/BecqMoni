# П43 13.09.2026 — контроли (а)(б)(в) пути КРИВОЙ (EfficiencyCalculation.Run, тот же, что у кнопки
# «Посчитать из геометрии»; читатель — CorpusEffProbe, --log печатает журнал приложения как есть).
#   (а) сосуд корпуса ASN16_lu_side БЕЗ ключа, в scratch-копии (.in, .rmx, index, оба спектра .xml),
#       НЕ --dry: кривая пишется в копии спектров, и её 39 узлов сверяются с живым корпусом
#       ПО ПОЛНОЙ ТОЧНОСТИ (cmp_curve.py); клеймо обязано быть посимвольно прежним (без imp=1),
#       строки «розыгрыш включён: полевая сцена» быть НЕ должно;
#   (б) полевая сцена ASN16_ground (SceneCostProbe --save-ground, П41; DS_Scene = GROUND) БЕЗ ключа —
#       автоматика: строка «включён: полевая сцена» в журнале, клеймо «; imp=1», медиана ≈ 2 %,
#       предупреждения нет, код 0;
#   (в) та же сцена с явным --imp=0 — абляция: медиана ~25 %, предупреждение, код 1
#       (положительный контроль: явное сильнее автоматики, признак отказывает там, где обязан).
# Матрица-заглушка ASN16_ground.rmx — копия AS80_point0.rmx П41: проба проверяет лишь НАЛИЧИЕ файла.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p43"
$art = "$root\handover\p43-e29-auto"
$scratch = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad'
$corpus = "$root\tools\CORPUS\corpus"
Set-Location $root

# --- (а) сосуд ---
$v = "$scratch\p43\vessel"
New-Item -ItemType Directory -Force "$v\spectra" | Out-Null
Remove-Item "$v\spectra\*" -Force -ErrorAction SilentlyContinue
"geometry,spectrum,preset,vessel`nASN16_lu_side,ASN16_Lu176,Atom Spectra Nano 16,`"банка 50 мл Ø40×h15, СБОКУ у широкой грани`"`nASN16_lu_side,ASN16_Lu176_P0,Atom Spectra Nano 16,`"банка 50 мл Ø40×h15, СБОКУ у широкой грани`"" | Set-Content -Encoding UTF8 "$v\index.csv"
Copy-Item "$corpus\geometries\ASN16_lu_side.in"  "$v\ASN16_lu_side.in"  -Force
Copy-Item "$corpus\geometries\ASN16_lu_side.rmx" "$v\ASN16_lu_side.rmx" -Force
Copy-Item "$corpus\spectra\ASN16_Lu176.xml"    "$v\spectra\ASN16_Lu176.xml"    -Force
Copy-Item "$corpus\spectra\ASN16_Lu176_P0.xml" "$v\spectra\ASN16_Lu176_P0.xml" -Force
"ctrl_a start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_a sha256 corpus ASN16_Lu176.xml BEFORE $((Get-FileHash "$corpus\spectra\ASN16_Lu176.xml").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
& "$bin\CorpusEffProbe.exe" "--dir=$v" "--spectra=$v\spectra" --force --log > "$art\ctrl_a_vessel_auto.log" 2>&1
"ctrl_a vessel AUTO (без ключа) code=$LASTEXITCODE (ОБЯЗАН быть 0) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_a sha256 corpus ASN16_Lu176.xml AFTER  $((Get-FileHash "$corpus\spectra\ASN16_Lu176.xml").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
python "$art\cmp_curve.py" "$corpus\spectra\ASN16_Lu176.xml" "$v\spectra\ASN16_Lu176.xml" > "$art\ctrl_a_cmp.log" 2>&1
"ctrl_a cmp_curve code=$LASTEXITCODE (ОБЯЗАН быть 0: 39/39 узлов побитово, клеймо то же) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# --- (б) полевая сцена, автоматика ---
$g = "$scratch\p43\ground"
New-Item -ItemType Directory -Force "$g\spectra" | Out-Null
"geometry,spectrum,preset,vessel`nASN16_ground,ASN16_Lu176,Atom Spectra Nano 16,`"детектор на земле, грунт`"" | Set-Content -Encoding UTF8 "$g\index.csv"
Copy-Item "$scratch\p41\ground\ASN16_ground.in" "$g\ASN16_ground.in" -Force
Copy-Item "$corpus\spectra\ASN16_Lu176.xml" "$g\spectra\ASN16_Lu176.xml" -Force
Copy-Item "$scratch\p41\ctrl_a\AS80_point0.rmx" "$g\ASN16_ground.rmx" -Force
"ctrl_b start $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\CorpusEffProbe.exe" "--dir=$g" "--spectra=$g\spectra" --dry --force --log > "$art\ctrl_b_ground_auto.log" 2>&1
"ctrl_b ground AUTO (без ключа) code=$LASTEXITCODE (ОБЯЗАН быть 0: розыгрыш включён сценой) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"

# --- (в) полевая сцена, явный --imp=0 ---
& "$bin\CorpusEffProbe.exe" "--dir=$g" "--spectra=$g\spectra" --dry --force --log --imp=0 > "$art\ctrl_c_ground_imp0.log" 2>&1
"ctrl_c ground --imp=0 code=$LASTEXITCODE (ОБЯЗАН быть 1: явное ВЫКЛ сильнее автоматики, кривая шумная) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_abc end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
