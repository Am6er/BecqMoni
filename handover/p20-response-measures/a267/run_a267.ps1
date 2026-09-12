# П20 12.09.2026, замер `A267`: пила якоря света на умолчаниях склада.
# Пробы — из worktree C:\Users\moroz\bqp20 (HEAD 98cb5532), сборка Release_P20.
# Плечи:
#   ctrl0   — нулевой допуск, энергии П1 (14.5…32.5 шаг 2, бин 2): ПОЛОЖИТЕЛЬНЫЙ
#             контроль — обязан снова показать пилу ~6.5 % на 30.5 кэВ (П1 10.09);
#   store_p1 — умолчания склада (--peakb --store, бин 2) на тех же энергиях П1;
#   store_nodes — умолчания склада на УЗЛАХ сетки склада 10…70 кэВ (42 узла);
#   peakw_nodes — допуск из геометрии на тех же узлах: контроль, класс обязан быть пуст.
# Сцены — копии в handover\p20-response-measures\scenes (проба склад не пишет).
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = 'C:\Users\moroz\bqp20\tools\effmaker\probes\build_p20'
$scenes = "$root\handover\p20-response-measures\scenes"
$out = "$root\handover\p20-response-measures\a267"
$p1 = '14.5,16.5,18.5,20.5,22.5,24.5,26.5,28.5,30.5,32.5,59.5,122,661.657'
Push-Location $bin
foreach ($g in @('AS80_point0', 'ASN16_lu_side')) {
    $geo = "$scenes\$g.in"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --n=400000 "--energies=$p1" *> "$out\${g}_ctrl0.txt"
    "ctrl0 $g code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --n=400000 --peakb --store "--energies=$p1" *> "$out\${g}_store_p1.txt"
    "store_p1 $g code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --n=400000 --peakb --store --nodes=10-70 *> "$out\${g}_store_nodes.txt"
    "store_nodes $g code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" --bin=2 --n=400000 --peakw --store --nodes=10-70 *> "$out\${g}_peakw_nodes.txt"
    "peakw_nodes $g code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
