# П32 12.09.2026, `A64` — плечо БЕЗ обвязки с шумом, как у плеча с обвязкой: голый AS80 (точка в 5 мм,
# сцена П27 AS80_bare_gap5) при 661.657 и 1332.5 кэВ — наша 8 млн (--etr=0 / --etr=1), арбитр 8 млн
# (`vacuum`, голый кристалл — T133). Первый прогон (П27 CSV 4 млн против П20 2 млн) дал край…пик
# +2.97 / +2.15 % при σ 1.3 — то же, что с обвязкой (+2.57 / +1.60 при σ 0.7): посылка строки
# «возврат из обвязки перебирает» под вопросом, нужен шум ≤ 0.7 % и на голом.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p32"
$scenes = "$root\handover\p32-g4\scenes"
$art = "$root\handover\p32-g4"
$g4 = "$art\g4\g4run.cmd"
$mode = $args[0]
if ($mode -eq 'g4') {
    foreach ($e in 661.657, 1332.5) {
        $tag = "AS80_bare_gap5_${e}_vacuum"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & $g4 vacuum scene "$root\handover\p27-electron-transport\g4\AS80_bare_gap5.scene" hist $e 8000000 1 2>&1 | Out-File -Encoding utf8 "$art\g4\g4_$tag.log"
        "g4 $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\g4\codes.txt"
    }
    "done g4 extra $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\g4\codes.txt"
} else {
    Push-Location $bin
    foreach ($e in 661.657, 1332.5) {
        foreach ($etr in 0, 1) {
            $tag = "AS80_bare_gap5_${e}_etr$etr"
            $bands = if ($e -eq 661.657) { '13-477,478-659' } else { '13-1118,1119-1330' }
            $sw = [Diagnostics.Stopwatch]::StartNew()
            & "$bin\G4RawProbe.exe" "--geometry=$scenes\AS80_bare_gap5.in" --no-light --bin=1 "--energy=$e" --n=8000000 `
                "--etr=$etr" "--bands=$bands" "--out=$art\bare\ours_$tag.csv" *> "$art\bare\ours_$tag.txt"
            "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ours.txt"
        }
    }
    Pop-Location
    "done ours extra $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_ours.txt"
}
