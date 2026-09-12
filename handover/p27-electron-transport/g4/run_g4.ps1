# П27 12.09.2026, `A72` — арбитр Geant4 (tools\g4cf, 11.4.2, option4, ключ `vacuum` — T133)
# на узлах, которых у П20 нет: 59.541 / 661.657 / 2614.511 кэВ на голых RC103 / OBS / AS80.
# Логи 1461 (все три) и AS80 661.657 уже лежат в handover\p20-response-measures\a72 и
# НЕ пересчитываются (те же сцены — .scene скопированы оттуда же).
# Историй: ≥ 4 млн, шум пика ≤ 1 % — на 2614 у мелких кристаллов пик ~5e-4, поэтому 24 млн.
# ⛔ Кодировку консоли НЕ трогать (П20: с UTF8 cmd спотыкается о русские rem-строки .bat).
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$g4 = "$root\tools\g4cf\run_g4cf.bat"
$out = "$root\handover\p27-electron-transport\g4"
$runs = @(
    @{ g = 'RC103_bare_gap5'; e = 661.657;  n = 8000000 },
    @{ g = 'OBS_bare_gap5';   e = 661.657;  n = 8000000 },
    @{ g = 'RC103_bare_gap5'; e = 59.541;   n = 8000000 },
    @{ g = 'OBS_bare_gap5';   e = 59.541;   n = 8000000 },
    @{ g = 'AS80_bare_gap5';  e = 59.541;   n = 4000000 },
    @{ g = 'RC103_bare_gap5'; e = 2614.511; n = 24000000 },
    @{ g = 'OBS_bare_gap5';   e = 2614.511; n = 24000000 },
    @{ g = 'AS80_bare_gap5';  e = 2614.511; n = 4000000 }
)
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & $g4 vacuum scene "$out\$($r.g).scene" hist $r.e $r.n 1 2>&1 | Out-File -Encoding utf8 "$out\g4_$tag.log"
    "g4 $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
"done g4 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
