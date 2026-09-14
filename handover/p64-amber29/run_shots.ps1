# П64 (AMBER29): числа и снимки FSA по 33 съёмкам угля — все плечи одним скриптом
# (правило «гнать все плечи ОДНИМ скриптом», edit-during-measurement).
#
#   & handover\p64-amber29\run_shots.ps1 [-Only <подстрока ключа>]
#
# Из рабочего каталога `D:\BqMoni_Claude\p64\wd` (mk_wd.ps1) зовётся `FsaStackShot.exe`:
#   состав — ИЗ БАЗЫ: `--chain=Ra-226,Th-232` (ряды целиком; разбор сам решает, кто жив);
#   плечи: `A_noeq` — сцена А (сосуд ОМАСН), галка «Равновесие» ВЫКЛ, все 33 съёмки;
#          `B_noeq` — сцена Б (сосуд корпусный), то же, все 33 (п. 6 — как меняются активности);
#          `A_eq`   — равновесие ВКЛ на трёх ранних и на e01 (п. 5);
#          `A_bg2`  — фон 05.04.2026 вместо 19.11.2025 на t20m / e01 / e30 (контроль фона).
# PNG (стек + окно отчёта) и кривые по каналам — для трёх ранних и e01 в обоих режимах;
# у остальных — только `rates_*.csv` и лог (строки SCREEN).
# Выход — `D:\BqMoni_Claude\p64\out\`: rates_<ключ>_<плечо>.csv, <ключ>_<плечо>.log, *.png, curves_*.csv.
param([string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = 'D:\BqMoni_Claude\p64\wd'
$sp  = 'D:\BqMoni_Claude\p64\spectra_ab'
$out = 'D:\BqMoni_Claude\p64\out'
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location $wd
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$keys = @('coal_t20m', 'coal_t2h', 'coal_t3h') + (1..30 | ForEach-Object { 'coal_e{0:d2}' -f $_ })
$png  = @('coal_t20m', 'coal_t2h', 'coal_t3h', 'coal_e01')
$runs = @()
foreach ($scene in @('A', 'B')) {
    foreach ($k in $keys) {
        $spec = Join-Path $sp ($k.Replace('coal_', "coal${scene}_") + '.xml')
        $runs += @{ key = "${k}_${scene}_noeq"; spectrum = $spec; args = @('--chain=Ra-226,Th-232', '--no-equilibrium'); png = ($scene -eq 'A' -and $png -contains $k) }
    }
}
foreach ($k in $png) {
    $spec = Join-Path $sp ($k.Replace('coal_', 'coalA_') + '.xml')
    $runs += @{ key = "${k}_A_eq"; spectrum = $spec; args = @('--chain=Ra-226,Th-232'); png = $true }
}
foreach ($k in @('coal_t20m', 'coal_e01', 'coal_e30')) {
    $spec = Join-Path $sp ($k.Replace('coal_', 'coalA_') + '_bg2.xml')
    $runs += @{ key = "${k}_A_bg2"; spectrum = $spec; args = @('--chain=Ra-226,Th-232', '--no-equilibrium'); png = $false }
}

$codes = @()
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($r in $runs) {
    if ($Only -and ($r.key -notlike "*$Only*")) { continue }
    $k = $r.key
    $log = Join-Path $out "$k.log"
    $a = @("--spectrum=$($r.spectrum)") + $r.args + @("--rates=$out\rates_$k.csv", '--screen')
    if ($r.png) { $a += @("--out=$out\$k.png", "--dump=$out\curves_$k.csv", '--scale=pow', '--width=1400') }
    & .\FsaStackShot.exe @a > $log 2>&1
    $code = $LASTEXITCODE
    $codes += "$k=$code"
    $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-22} код {1}  {2,6:F0} с  {3}" -f $k, $code, $sw.Elapsed.TotalSeconds, $chi
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
