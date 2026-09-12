# П27 12.09.2026, приёмка `A72`: наш сырой отклик (G4RawProbe, build_p27, шкала ЭНЕРГОВЫДЕЛЕНИЯ,
# --kdip=1 умолчанием пробы с П27 — как у склада; плечо kdip=0 (как П20) — в kdip0/)
# --no-light, бин 1 кэВ, допуск пика ноль, умолчания физики 16) ДВУМЯ плечами — --etr=0
# («до», эффективная глубина; обязано воспроизвести П20 §3 до знака — положительный контроль)
# и --etr=1 («после», перенос) — на голых RC103 / OBS / AS80 при 59.541 / 661.657 / 1461 /
# 2614.511 кэВ. Историй: 10 млн (мелкие) / 4 млн (AS80), как П20. Плюс сходимость по шагу:
# RC103 1461 при --etr-step=0.05 и 0.2 (умолчание 0.1).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p27"
$scenes = "$root\handover\p27-electron-transport\scenes"
$out = "$root\handover\p27-electron-transport\accept"
$runs = @()
foreach ($e in 1461, 661.657, 59.541, 2614.511) {
    $runs += @{ g = 'RC103_bare_gap5'; e = $e; n = 10000000 }
    $runs += @{ g = 'OBS_bare_gap5';   e = $e; n = 10000000 }
    $runs += @{ g = 'AS80_bare_gap5';  e = $e; n = 4000000 }
}
Push-Location $bin
foreach ($etr in 0, 1) {
    foreach ($r in $runs) {
        $tag = "$($r.g)_$($r.e)_etr$etr"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.n)" `
            "--etr=$etr" "--out=$out\ours_$tag.csv" *> "$out\ours_$tag.txt"
        "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    }
}
foreach ($step in 0.05, 0.2) {
    $tag = "RC103_bare_gap5_1461_etr1_step$step"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & "$bin\G4RawProbe.exe" "--geometry=$scenes\RC103_bare_gap5.in" --no-light --bin=1 --energy=1461 --n=10000000 `
        --etr=1 "--etr-step=$step" "--out=$out\ours_$tag.csv" *> "$out\ours_$tag.txt"
    "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
