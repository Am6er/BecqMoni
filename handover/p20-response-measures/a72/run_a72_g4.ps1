# П20 12.09.2026, `A72`/~~`F15`~~ — арбитр Geant4 (вторая половина run_a72.ps1).
# ⛔ Кодировку консоли НЕ трогать: с `[Console]::OutputEncoding = UTF8` cmd читает
#    run_g4cf.bat в cp65001 и спотыкается о русские `rem`-строки («'�е' is not
#    recognized…», код 255) — поймано первым прогоном 12.09.2026 18:20.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$g4 = "$root\tools\g4cf\run_g4cf.bat"
$out = "$root\handover\p20-response-measures\a72"
$runs = @(
    @{ g = 'AS80_bare_gap5';  e = 661.657; g4n = 2000000 },
    @{ g = 'RC103_bare_gap5'; e = 1461;    g4n = 8000000 },
    @{ g = 'OBS_bare_gap5';   e = 1461;    g4n = 8000000 },
    @{ g = 'AS80_bare_gap5';  e = 1461;    g4n = 2000000 }
)
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & $g4 vacuum scene "$out\$($r.g).scene" hist $r.e $r.g4n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    "g4 $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
"done g4 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
