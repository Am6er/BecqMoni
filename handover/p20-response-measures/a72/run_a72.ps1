# П20 12.09.2026, замер `A72`/~~`F15`~~: пик 1461 кэВ на мелких голых кристаллах против Geant4,
# физика 16. Наша сторона — G4RawProbe (worktree C:\Users\moroz\bqp20, HEAD 98cb5532, Release_P20):
# сырой отклик, шкала ЭНЕРГОВЫДЕЛЕНИЯ (--no-light), бин 1 кэВ, допуск пика ноль; тем же заходом
# дамп сцены для арбитра. Арбитр — tools\g4cf (Geant4 11.4.2, option4), ключ `vacuum` (T133,
# голый кристалл), режим hist, бин 1 кэВ.
# Сцены (копии, handover\p20-response-measures\scenes): RC103_bare_gap5 (CsI 10×10×10 мм),
# OBS_bare_gap5 (CsI 7×7×30 мм), AS80_bare_gap5 (NaI Ø80×80) — обвязка обнулена, точка в 5 мм.
# Контроль: AS80_bare_gap5 на 661.657 кэВ обязан сойтись с 02.09.2026 (§9в: пик ±3 +0.01 %).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = 'C:\Users\moroz\bqp20\tools\effmaker\probes\build_p20'
$g4 = "$root\tools\g4cf\run_g4cf.bat"
$scenes = "$root\handover\p20-response-measures\scenes"
$out = "$root\handover\p20-response-measures\a72"
$runs = @(
    @{ g = 'AS80_bare_gap5';  e = 661.657; ours = 4000000;  g4n = 2000000 },
    @{ g = 'RC103_bare_gap5'; e = 1461;    ours = 10000000; g4n = 8000000 },
    @{ g = 'OBS_bare_gap5';   e = 1461;    ours = 10000000; g4n = 8000000 },
    @{ g = 'AS80_bare_gap5';  e = 1461;    ours = 4000000;  g4n = 2000000 }
)
Push-Location $bin
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.ours)" `
        "--scene=$out\$($r.g).scene" "--out=$out\ours_$tag.csv" *> "$out\ours_$tag.txt"
    "ours $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
Pop-Location
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & $g4 vacuum scene "$out\$($r.g).scene" hist $r.e $r.g4n 1 *> "$out\g4_$tag.log"
    "g4 $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
