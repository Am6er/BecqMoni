# П88 (AMBER22): снимки FSA по сценам × спектрам — все плечи ОДНИМ скриптом (по образцу run_shots.ps1 П67).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p88\run_shots_p88.ps1 [-Only <подстрока ключа>] [-Arms asis,eq,noeqasis,noeq] [-Force]
# Из D:\BqMoni_Claude\p88\wd (mk_wd_p88.ps1) зовётся FsaStackShot.exe: --chain=Th-232,Ra-226 (равновесие ВКЛ — умолчание).
# Плечи: asis — файл как есть (шкала пробы P, шкала фона файловая: перекалиброванная у edge93/edge1709/contact_cal0809,
#        скопированная у *_bg0); eq — шкала пробы по её пикам (.escale.xml, mk_escale_p88.py), фон файловый;
#        noeqasis / noeq — то же без связки ряда (--no-equilibrium): амплитуды членов порознь, Pb-212 против Tl-208.
# Выход — D:\BqMoni_Claude\p88\out\<спектр>__<сцена>__<плечо>\: rates.csv, dump.csv, stack.png, probe.txt.
param([string]$Only = '', [string[]]$Arms = @('asis'), [switch]$Force)
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$p = 'D:\BqMoni_Claude\p88'
$wd = "$p\wd"; $sp = "$p\spectra_scenes"; $out = "$p\out"
New-Item -ItemType Directory -Force $out | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$argsOf = @{ asis = @(); eq = @(); noeqasis = @('--no-equilibrium'); noeq = @('--no-equilibrium'); kf2 = @('--knots=2') }
$rows = Import-Csv "$p\store\index.csv"
Push-Location $wd
$codes = @(); $sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($r in $rows) {
  if ($Only -and ("$($r.spectrum)" -notlike "*$Only*")) { continue }
  $spec = Join-Path $sp ($r.spectrum + '.xml')
  if (-not (Select-String -Path $spec -Pattern ("<Name>" + $r.geometry + "</Name>") -Quiet)) { "$($r.spectrum): нет узла <Efficiency> сцены — пропуск"; continue }
  foreach ($arm in $Arms) {
    $k = "$($r.spectrum)__$arm"
    $d = Join-Path $out $k
    if ((Test-Path "$d\rates.csv") -and -not $Force) { continue }
    New-Item -ItemType Directory -Force $d | Out-Null
    $specArm = if ($arm -eq 'asis' -or $arm -eq 'noeqasis') { $spec } else { $spec -replace '\.xml$', '.escale.xml' }
    if (-not (Test-Path $specArm)) { "$k : нет $specArm — пропуск"; continue }
    $a = @("--spectrum=$specArm", '--chain=Th-232,Ra-226') + $argsOf[$arm] + @("--rates=$d\rates.csv", '--screen', "--out=$d\stack.png", "--dump=$d\dump.csv", '--scale=pow', '--from=15', '--to=2800', '--width=1400')
    & .\FsaStackShot.exe @a > "$d\probe.txt" 2>&1
    $code = $LASTEXITCODE
    $codes += "$k=$code"
    $chi = (Select-String -Path "$d\probe.txt" -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-56} код {1}  {2,6:F0} с  {3}" -f $k, $code, $sw.Elapsed.TotalSeconds, $chi
  }
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
