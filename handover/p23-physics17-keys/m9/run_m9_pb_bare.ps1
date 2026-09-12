# П23 12.09.2026, `M9` (б''): образ L-серии СВИНЦА на ГОЛОМ кристалле — G4RawProbe на сцене
# AS80_bare_pbcup (NaI Ø80×80 без обвязки, стакан со стенками Pb 0.1 мм в 5 мм от торца,
# источник — воздух), 59.541 кэВ, --no-light, бин 1 кэВ, 4 млн × 2 зерна. У сцены ASN16_pbcup
# L-серия свинца до кристалла не доходила (Al 1.8 мм пропускает 2 % на 12 кэВ) — здесь обвязки нет.
# Плечи: off / lys1 / lys2 и абляция nolx (--no-lxray): вклад Pb L = плечо − nolx.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$tree = "$root\tools\effmaker\probes\build_p23"
$ck = "$root\tools\effmaker\probes\build_p23ck"
$out = "$root\handover\p23-physics17-keys\m9"
$geo = "$root\handover\p23-physics17-keys\scenes\AS80_bare_pbcup.in"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
foreach ($arm in @(@('off', $tree, '--lys=0'), @('lys1', $tree, '--lys=1'), @('lys2', $ck, '--lys=2'), @('nolx', $tree, '--no-lxray'))) {
    $name, $bin, $key = $arm
    Push-Location $bin
    foreach ($seed in 20260901, 20260903) {
        & "$bin\G4RawProbe.exe" "--geometry=$geo" --energy=59.541 --n=4000000 --bin=1 --no-light "--seed=$seed" $key --bands=10-11,12-13,14-15,10-15,1-9,55-59 "--out=$out\pbbare_${name}_$seed.csv" > "$out\pbbare_${name}_$seed.txt" 2>&1
        "pbbare $name $seed code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
    }
    Pop-Location
}
"done pbbare $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
