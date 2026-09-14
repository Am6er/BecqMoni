# П67 (AMBER22): снимки FSA по трём геометриям × плотностям — все плечи ОДНИМ скриптом.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p67\run_shots.ps1 [-Only <подстрока ключа>] [-Arms eq,kf2]
# Из D:\BqMoni_Claude\p67\wd (mk_wd.ps1) зовётся FsaStackShot.exe: --chain=Th-232,Ra-226 (галка «Равновесие» ВКЛ — умолчание),
# плечо kf2 — узлы сплайна 2·ПШПВ (--knots=2), плечо noeq — без связки (контроль вырожденности). Спектры — копии под сцены
# D:\BqMoni_Claude\p67\spectra_scenes\<спектр>__<сцена>.xml с узлом <Efficiency> от CorpusEffProbe.
# Выход — D:\BqMoni_Claude\p67\out\<ключ>\: rates.csv, dump.csv, stack.png, probe.txt.
param([string]$Only = '', [string[]]$Arms = @('eq'), [switch]$Force)
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$p = 'D:\BqMoni_Claude\p67'
$wd = "$p\wd"; $sp = "$p\spectra_scenes"; $out = "$p\out"
New-Item -ItemType Directory -Force $out | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$argsOf = @{ eq = @(); kf2 = @('--knots=2'); noeq = @('--no-equilibrium'); th = @(); asis = @() }
$rows = Import-Csv "$p\store\index.csv"
Push-Location $wd
$codes = @(); $sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($r in $rows) {
  if ($Only -and ($r.spectrum -notlike "*$Only*")) { continue }
  $spec = Join-Path $sp ($r.spectrum + '.xml')
  if (-not (Select-String -Path $spec -Pattern ("<Name>" + $r.geometry + "</Name>") -Quiet)) { "$($r.spectrum): нет узла <Efficiency> сцены — пропуск"; continue }
  foreach ($arm in $Arms) {
    $k = "$($r.spectrum)__$arm"
    $d = Join-Path $out $k
    if ((Test-Path "$d\rates.csv") -and -not $Force) { continue }
    New-Item -ItemType Directory -Force $d | Out-Null
    $chain = if ($arm -eq 'th') { '--chain=Th-232' } else { '--chain=Th-232,Ra-226' }
    $specArm = if ($arm -eq 'asis') { $spec } else { $spec -replace '\.xml$', '.escale.xml' }
    if (-not (Test-Path $specArm)) { "$k : нет $specArm — пропуск"; continue }
    $a = @("--spectrum=$specArm", $chain) + $argsOf[$arm] + @("--rates=$d\rates.csv", '--screen', "--out=$d\stack.png", "--dump=$d\dump.csv", '--scale=pow', '--from=15', '--to=2800', '--width=1400')
    & .\FsaStackShot.exe @a > "$d\probe.txt" 2>&1
    $code = $LASTEXITCODE
    $codes += "$k=$code"
    $chi = (Select-String -Path "$d\probe.txt" -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-52} код {1}  {2,6:F0} с  {3}" -f $k, $code, $sw.Elapsed.TotalSeconds, $chi
  }
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
