# П23 12.09.2026, `M9` (б'): образ L-серии СВИНЦА — G4RawProbe на сцене ASN16_pbcup
# (CsI 15×18×60, стакан со стенками Pb 0.1 мм, источник — вода), 59.541 кэВ, шкала
# ЭНЕРГОВЫДЕЛЕНИЯ (--no-light: перенос светом сдвигал бы бины 10…15 кэВ), бин 1 кэВ,
# 4 млн историй × 2 зерна. Полосы: 10-11 (Lα 10.55), 12-13 (Lβ 12.3/12.6), 14-15 (Lγ 14.8),
# 10-15 (вся L-серия Pb), 1-9 (контроль ниже серии), 55-59 (L иода/цезия).
# ⚠ Плечо RowDump (pbcup_*.csv) на 300 тыс. историй шумит в бинах L-серии на ±28 % —
# записано как отвергнутое; мерка — эта.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$tree = "$root\tools\effmaker\probes\build_p23"
$ck = "$root\tools\effmaker\probes\build_p23ck"
$out = "$root\handover\p23-physics17-keys\m9"
$pbcup = "$root\handover\p23-physics17-keys\scenes\ASN16_pbcup.in"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
foreach ($arm in @(@('off', $tree, '--lys=0'), @('lys1', $tree, '--lys=1'), @('lys2', $ck, '--lys=2'))) {
    $name, $bin, $key = $arm
    Push-Location $bin
    foreach ($seed in 20260901, 20260903) {
        & "$bin\G4RawProbe.exe" "--geometry=$pbcup" --energy=59.541 --n=4000000 --bin=1 --no-light "--seed=$seed" $key --bands=10-11,12-13,14-15,10-15,1-9,55-59 "--out=$out\pb_${name}_$seed.csv" > "$out\pb_${name}_$seed.txt" 2>&1
        "pb $name $seed code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    }
    Pop-Location
}
"done pb $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
