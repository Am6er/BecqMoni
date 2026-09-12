# П23 12.09.2026, приёмка `M9` (ключ LYieldSupply, `--lys=` у G4RawProbe и ResponseRowDumpProbe).
# (а) Мерка ~~A101~~: голый NaI Ø80×80 (копия tools\effmaker\models\AS80_bare.in), 59.541 кэВ,
#     `--no-light --bin=1 --n=2000000`, шесть зёрен 20260901/03/05/07/09/11 (как A85/A101),
#     полосы 55-56 (бины L-линий иода) и 55-59; арбитр Geant4 vacuum: 5.268e-4 / 5.794e-4 на историю.
#     Плечи: off (уровень 0 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, A101: 4.405e-4 в 55+56), lys1, lys2.
#     lys2 — из копии каталога проб build_p23ck, чья matdb.sqlite несёт таблицу coster_kronig
#     (импортёр с --apply на КОПИИ; база дерева не тронута, sha256 в журнале).
# (б) Образ L-серии свинца: сцена ASN16_pbcup (CsI 15×18×60, стакан со стенками Pb 0.1 мм,
#     источник — вода), ResponseRowDumpProbe --geometry= --direct --peakw=0, узлы 59.541 и 661.657,
#     плечи off / lys1 / lys2 — бины 5 и 6 (10 и 12 кэВ) строки 59.541.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$tree = "$root\tools\effmaker\probes\build_p23"
$ck = "$root\tools\effmaker\probes\build_p23ck"
$out = "$root\handover\p23-physics17-keys\m9"
$bare = "$root\handover\p23-physics17-keys\scenes\AS80_bare.in"
$pbcup = "$root\handover\p23-physics17-keys\scenes\ASN16_pbcup.in"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$seeds = 20260901, 20260903, 20260905, 20260907, 20260909, 20260911
foreach ($arm in @(@('off', $tree, '--lys=0'), @('lys1', $tree, '--lys=1'), @('lys2', $ck, '--lys=2'))) {
    $name, $bin, $key = $arm
    Push-Location $bin
    foreach ($seed in $seeds) {
        & "$bin\G4RawProbe.exe" "--geometry=$bare" --energy=59.541 --n=2000000 --bin=1 --no-light "--seed=$seed" $key --bands=55-56,55-59,48-54 "--out=$out\bare_${name}_$seed.csv" > "$out\bare_${name}_$seed.txt" 2>&1
        "bare $name $seed code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    }
    Pop-Location
}
foreach ($arm in @(@('off', $tree, '--lys=0'), @('lys1', $tree, '--lys=1'), @('lys2', $ck, '--lys=2'))) {
    $name, $bin, $key = $arm
    Push-Location $bin
    & "$bin\ResponseRowDumpProbe.exe" "--geometry=$pbcup" --direct --peakw=0 $key --e=59.541,661.657 "--out=$out\pbcup_$name" > "$out\pbcup_$name.txt" 2>&1
    "pbcup $name code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    Pop-Location
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
