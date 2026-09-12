# П32 12.09.2026, `F16` + `A64` — арбитр Geant4 (tools\g4cf, 11.4.2, option4), режим hist, бин 1 кэВ.
# ⛔ Кодировку консоли НЕ трогать (П20: с UTF8 cmd спотыкается о русские rem в run_g4cf.bat, код 255)
#    — обёртка g4\g4run.cmd ставит chcp 1251.
# Мир: сцены С ОБВЯЗКОЙ/ПРОБОЙ — ВОЗДУХ (правило T133 узкое нарочно: vacuum только на голом кристалле);
# плечо `vacuum` на тех же узлах — вилка (замер 02.09 A55 шёл в vacuum, П26 на диске: вилка ≤ 0.04 % по пику).
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$g4 = "$root\handover\p32-g4\g4\g4run.cmd"
$scenes = "$root\handover\p32-g4\scenes"
$out = "$root\handover\p32-g4\g4"
$runs = @(
    @{ g = 'ASN16_lu_side'; e = 200;     n = 8000000; world = 'air' },
    @{ g = 'AS80_point0';   e = 661.657; n = 8000000; world = 'air' },
    @{ g = 'AS80_point0';   e = 1332.5;  n = 8000000; world = 'air' },
    @{ g = 'ASN16_lu_side'; e = 200;     n = 8000000; world = 'vacuum' },
    @{ g = 'AS80_point0';   e = 661.657; n = 8000000; world = 'vacuum' },
    @{ g = 'AS80_point0';   e = 1332.5;  n = 8000000; world = 'vacuum' }
)
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)_$($r.world)"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if ($r.world -eq 'vacuum') {
        & $g4 vacuum scene "$scenes\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    } else {
        & $g4 scene "$scenes\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    }
    "g4 $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
"done g4 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
