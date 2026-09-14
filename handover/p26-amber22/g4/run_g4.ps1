# П26 12.09.2026, `AMBER22` п. 1 — арбитр Geant4 (tools\g4cf, 11.4.2, option4), режим hist, бин 1 кэВ.
# ⛔ Кодировку консоли НЕ трогать (П20: с UTF8 cmd спотыкается о русские rem в run_g4cf.bat, код 255).
# Мир: контроль на ГОЛОМ кристалле — `vacuum` (T133); сцена диска с обвязкой и пробой — ВОЗДУХ
# (правило T133 узкое нарочно), плюс плечо `vacuum` на 583/2614 для вилки.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$g4 = "$root\handover\p26-amber22\g4\g4run.cmd"   # обёртка: chcp 1251 перед run_g4cf.bat (из Bash консоль 65001 — код 255)
$out = "$root\handover\p26-amber22\g4"
$runs = @(
    @{ g = 'AS80_bare_gap5'; e = 661.657;  n = 2000000; world = 'vacuum' },
    @{ g = 'AS80_th_disk';   e = 238.632;  n = 4000000; world = 'air' },
    @{ g = 'AS80_th_disk';   e = 583.187;  n = 4000000; world = 'air' },
    @{ g = 'AS80_th_disk';   e = 911.204;  n = 8000000; world = 'air' },
    @{ g = 'AS80_th_disk';   e = 2614.511; n = 8000000; world = 'air' },
    @{ g = 'AS80_th_disk';   e = 583.187;  n = 4000000; world = 'vacuum' },
    @{ g = 'AS80_th_disk';   e = 2614.511; n = 8000000; world = 'vacuum' }
)
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)_$($r.world)"
    if ($r.world -eq 'vacuum') {
        & $g4 vacuum scene "$out\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    } else {
        & $g4 scene "$out\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    }
    "g4 $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
"done g4 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
