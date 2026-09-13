# П42 13.09.2026, `AMBER22` — плечи на СЦЕНЕ AMBER (стенд mk_stand.ps1): FsaStackShot --infer (ключи П29/П31/П34:
# шкала pow 15…2800, дамп кривых amber_<Tag>\<плечо>\dump.csv) гонится отражением одной из двух обёрток:
#   FsaNnlsDumpProbe (--keep=1: последний вызов NNLS в calls.csv/calls.bin; ключ --fit-floor=)
#   FsaP42ArmProbe   (--cascade=off, --channels=peak — рычаги хука FsaAnalyzer.ProbeSetup, П42)
# Умолчания = правило пола (A309). -Tag p42r20 → плечо `def` = контроль П34 §2.
# Варианты СПЕКТРА (абляции данных): mk_variants.py (ПШПВ ×k, гаусс), mk_recal.py (шкала/ПШПВ по пикам данных).
param([string[]]$Arms = @(), [string]$Tag = 'p42', [string]$Sub = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sb = "C:\Users\moroz\bq${Tag}_amber"
$here = "$root\handover\p42-amber22\amber_$Tag$Sub"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
# плечо = @(обёртка, ключи обёртки, ключи FsaStackShot[, файл спектра])
$all = [ordered]@{
    def      = @('nnls', @(), @('--infer'))
    off      = @('nnls', @('--fit-floor=off'), @('--infer'))
    nomx2    = @('nnls', @(), @('--infer', '--no-matrix'))          # матрица на время плеча УБИРАЕТСЯ из response\ (П26 infer_nomx2)
    noeq     = @('nnls', @(), @('--infer', '--no-equilibrium'))
    kf2      = @('nnls', @(), @('--infer', '--knots=2'))            # у FsaStackShot --knots= это ContinuumKnotFwhm
    kf8      = @('nnls', @(), @('--infer', '--knots=8'))
    huber0   = @('nnls', @(), @('--infer', '--huber=0'))            # ключ соседней незакоммиченной копии FsaStackShot (П29)
    huber05  = @('nnls', @(), @('--infer', '--huber=0.5'))
    noanch   = @('nnls', @(), @('--infer', '--no-anchor'))
    noatom   = @('nnls', @(), @('--infer', '--no-atomic'))
    nobs     = @('nnls', @(), @('--infer', '--no-backscatter'))
    nomx2eq  = @('nnls', @(), @('--infer', '--no-matrix', '--no-equilibrium'))
    nocasc   = @('p42', @('--cascade=off'), @('--infer'))
    chpeak   = @('p42', @('--channels=peak'), @('--infer'))
    chpeakeq = @('p42', @('--channels=peak'), @('--infer', '--no-equilibrium'))
    # абляции ДАННЫХ (mk_variants.py): калибровка ПШПВ спектра a × k; тип пика 0 (гаусс без хвостов)
    fw110    = @('nnls', @(), @('--infer'), 'Th-232_amber_fw110.xml')
    fw115    = @('nnls', @(), @('--infer'), 'Th-232_amber_fw115.xml')
    fw120    = @('nnls', @(), @('--infer'), 'Th-232_amber_fw120.xml')
    fw130    = @('nnls', @(), @('--infer'), 'Th-232_amber_fw130.xml')
    gauss    = @('nnls', @(), @('--infer'), 'Th-232_amber_gauss.xml')
    # абляции ДАННЫХ (mk_recal.py): шкала и/или ПШПВ по пикам данных
    recal    = @('nnls', @(), @('--infer'), 'Th-232_amber_recal.xml')
    escale   = @('nnls', @(), @('--infer'), 'Th-232_amber_escale.xml')
    sqrtfw   = @('nnls', @(), @('--infer'), 'Th-232_amber_sqrtfw.xml')
    recalh0  = @('nnls', @(), @('--infer', '--huber=0'), 'Th-232_amber_recal.xml')
    recalkf2 = @('nnls', @(), @('--infer', '--knots=2'), 'Th-232_amber_recal.xml')
    recalnomx = @('nnls', @(), @('--infer', '--no-matrix'), 'Th-232_amber_recal.xml')
    escalekf2 = @('nnls', @(), @('--infer', '--knots=2'), 'Th-232_amber_escale.xml')
    escalenocasc = @('p42', @('--cascade=off'), @('--infer'), 'Th-232_amber_escale.xml')
    escalechpeak = @('p42', @('--channels=peak'), @('--infer'), 'Th-232_amber_escale.xml')
    escalenoeq = @('nnls', @(), @('--infer', '--no-equilibrium'), 'Th-232_amber_escale.xml')
    escalekf2chpeak = @('p42', @('--channels=peak'), @('--infer', '--knots=2'), 'Th-232_amber_escale.xml')
    escalekf2h0 = @('nnls', @(), @('--infer', '--knots=2', '--huber=0'), 'Th-232_amber_escale.xml')
    escalekf2nocasc = @('p42', @('--cascade=off'), @('--infer', '--knots=2'), 'Th-232_amber_escale.xml')
    escalekf2noeq = @('nnls', @(), @('--infer', '--knots=2', '--no-equilibrium'), 'Th-232_amber_escale.xml')
}
if ($Arms.Count -eq 0) { $Arms = @('def') }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $sb
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $wrap = $all[$name][0]
    $pkeys = $all[$name][1]
    $keys = $all[$name][2]
    $spec = if ($all[$name].Count -gt 3) { $all[$name][3] } else { 'Th-232_amber.xml' }
    $out = "$here\$name"
    New-Item -ItemType Directory -Force $out | Out-Null
    $rmx = "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx"
    if ($name -like 'nomx2*' -or $name -eq 'recalnomx') { Move-Item $rmx "$rmx.away" -Force }
    $shot = @("--spectrum=$sb\$spec", "--out=$out\stack.png", "--dump=$out\dump.csv", '--scale=pow', '--from=15', '--to=2800') + $keys
    if ($wrap -eq 'p42') {
        & "$sb\FsaP42ArmProbe.exe" @pkeys -- @shot *> "$here\probe_$name.txt"
    } else {
        & "$sb\FsaNnlsDumpProbe.exe" "--out=$out" --keep=1 @pkeys -- @shot *> "$here\probe_$name.txt"
    }
    $code = $LASTEXITCODE
    if ($name -like 'nomx2*' -or $name -eq 'recalnomx') { Move-Item "$rmx.away" $rmx -Force }
    "$name code=$code $(Get-Date -Format 'HH:mm:ss') wrap=$wrap probe=[$($pkeys -join ' ')] keys=[$($keys -join ' ')] spectrum=$spec" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
