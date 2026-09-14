# П59 (AMBER27): снимки и числа FSA по радоновым спектрам — все плечи одним скриптом
# (правило «гнать все плечи ОДНИМ скриптом», edit-during-measurement).
#
#   & handover\p59-amber27\run_shots.ps1 [-Only <подстрока ключа>]
#
# Из рабочего каталога `D:\BqMoni_Claude\p59\wd` (mk_wd.ps1) зовётся `FsaStackShot.exe`:
#   состав — ИЗ БАЗЫ: `--chain=Ra-226,Th-232` (ряды целиком; разбор сам решает, кто жив);
#   режимы: noeq = `--no-equilibrium` (галка «Равновесие» ВЫКЛ), eq = равновесие ВКЛ;
#   плечо singles = `--sample=214PB,214BI,212PB,212BI,208TL` (члены одиночками, без долей ветвления);
#   контроль метода — `AS80_Th232Medal` (корпус, `--chain=Th-232`), матрица живого склада.
# Выход — `D:\BqMoni_Claude\p59\out\`: <ключ>.png (стек + окно отчёта), rates_<ключ>.csv,
# curves_<ключ>.csv (кривые по каналам), <ключ>.log (stdout, в т.ч. строки SCREEN).
param([string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = 'D:\BqMoni_Claude\p59\wd'
$sp  = 'D:\BqMoni_Claude\p59\spectra'
$out = 'D:\BqMoni_Claude\p59\out'
$corpus = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location $wd
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$runs = @()
foreach ($scene in @('front', 'side', 'front_r010', 'front_r025', 'side_r010', 'side_r025')) {
    foreach ($n in @('1', '2')) {
        $spec = Join-Path $sp ("radon{0}_{1}.xml" -f $n, $scene)
        $runs += @{ key = "radon${n}_${scene}_noeq"; spectrum = $spec; args = @('--chain=Ra-226,Th-232', '--no-equilibrium') }
        if ($scene -in @('front', 'side')) {
            $runs += @{ key = "radon${n}_${scene}_eq";      spectrum = $spec; args = @('--chain=Ra-226,Th-232') }
            $runs += @{ key = "radon${n}_${scene}_singles"; spectrum = $spec; args = @('--sample=214PB,214BI,212PB,212BI,208TL', '--no-equilibrium') }
        }
    }
}
$runs += @{ key = 'as80_th_noeq'; spectrum = (Join-Path $corpus 'AS80_Th232Medal.xml'); args = @('--chain=Th-232', '--no-equilibrium') }
$runs += @{ key = 'as80_th_eq';   spectrum = (Join-Path $corpus 'AS80_Th232Medal.xml'); args = @('--chain=Th-232') }

$codes = @()
foreach ($r in $runs) {
    if ($Only -and ($r.key -notlike "*$Only*")) { continue }
    $k = $r.key
    $log = Join-Path $out "$k.log"
    $a = @("--spectrum=$($r.spectrum)") + $r.args + @(
        "--out=$out\$k.png", "--rates=$out\rates_$k.csv", "--dump=$out\curves_$k.csv",
        '--screen', '--scale=pow', '--width=1400')
    & .\FsaStackShot.exe @a > $log 2>&1
    $code = $LASTEXITCODE
    $codes += "$k=$code"
    $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-28} код {1}  {2}" -f $k, $code, $chi
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
