# П65 (S172, вылет кристалла при матрице): все плечи одним скриптом (правило «гнать все плечи ОДНИМ скриптом»).
#
#   pwsh -File handover\p65-s172\sweep.ps1 [-Arm b] [-Only as80]
#
# Из D:\BqMoni_Claude\p65\wd_<плечо> зовётся FsaStackShot.exe: эталон AS80_Th232Medal (корпус,
# --chain=Th-232, живая матрица AS80_th_disk), радоновый фильтр radon1/radon2 (сцена ASN16_rn_side,
# --chain=Ra-226,Th-232). Плечи одни и те же у А (HEAD 847a8799) и Б (правка):
#   free — без связки равновесия (--no-equilibrium), матрица есть   = режим П63 (где сток попал в Esc-CsI);
#   eq   — связка равновесия ВКЛ, матрица есть                       = режим корпуса (малой базы);
#   nomx — без связки, БЕЗ матрицы: рабочий каталог wd_<плечо>_nomx без .rmx (геометрия у спектра есть,
#          кристалл назван) + --no-matrix (терпимость к отсутствию)   = положительный контроль: образ вылета
#                                                                      строится и берёт долю, А и Б побитово.
#          ⚠ Один `--no-matrix` при живом складе матрицу НЕ выключает (замерено: плечо = free побитово).
# Выход — D:\BqMoni_Claude\p65\out_<плечо>\<спектр>_<плечо>.{log,png,csv}.
param([string]$Arm = 'b', [string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = "D:\BqMoni_Claude\p65\wd_$Arm"
$wdn = "D:\BqMoni_Claude\p65\wd_${Arm}_nomx"
$out = "D:\BqMoni_Claude\p65\out_$Arm"
$corpus = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
$sp = 'D:\BqMoni_Claude\p65\spectra'
New-Item -ItemType Directory -Force $out | Out-Null
# ⛔ A77: приложение в рабочем каталоге ОБЯЗАНО быть не старше исходника анализатора — иначе развёртка
#    меряет прежний код (грабля П60 14.09.2026: сборка не запустилась, прогон пошёл старым exe).
$exe = Get-Item (Join-Path $wd 'BecquerelMonitor.exe')
$exen = Get-Item (Join-Path $wdn 'BecquerelMonitor.exe')
if ((Get-FileHash $exe.FullName -Algorithm SHA256).Hash -ne (Get-FileHash $exen.FullName -Algorithm SHA256).Hash) { "⛔ exe в $wd и $wdn различаются"; exit 3 }
if ((Get-ChildItem (Join-Path $wdn 'config\device\response\*.rmx') -ErrorAction SilentlyContinue).Count -ne 0) { "⛔ в $wdn есть матрицы"; exit 3 }
$src = Get-Item 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs'
if ($Arm -ne 'a' -and $exe.LastWriteTime -lt $src.LastWriteTime) {
    "⛔ exe в $wd ({0}) СТАРШЕ FsaAnalyzer.cs ({1}) — пересобрать и mk_wd" -f $exe.LastWriteTime, $src.LastWriteTime
    exit 3
}
"exe: {0}  sha256 {1}" -f $exe.LastWriteTime, (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.Substring(0, 16)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$noeq = @('--no-equilibrium')
$spectra = @(
    @{ key = 'as80';   path = (Join-Path $corpus 'AS80_Th232Medal.xml'); chain = '--chain=Th-232' },
    @{ key = 'radon1'; path = (Join-Path $sp 'radon1_side.xml');         chain = '--chain=Ra-226,Th-232' },
    @{ key = 'radon2'; path = (Join-Path $sp 'radon2_side.xml');         chain = '--chain=Ra-226,Th-232' }
)
$arms = @(
    @{ key = 'free'; args = $noeq;                     wd = $wd },
    @{ key = 'eq';   args = @();                       wd = $wd },
    @{ key = 'nomx'; args = $noeq + @('--no-matrix');  wd = $wdn }
)

$codes = @()
foreach ($s in $spectra) {
    if ($Only -and ($s.key -notlike "*$Only*")) { continue }
    if (-not (Test-Path $s.path)) { "⛔ нет спектра $($s.path)"; $codes += "$($s.key)=nofile"; continue }
    foreach ($a in $arms) {
        $k = "$($s.key)_$($a.key)"
        $log = Join-Path $out "$k.log"
        $argv = @("--spectrum=$($s.path)", $s.chain) + $a.args + @(
            "--out=$out\$k.png", "--rates=$out\rates_$k.csv", '--screen', '--scale=pow', '--width=1400')
        Push-Location $a.wd
        & .\FsaStackShot.exe @argv > $log 2>&1
        $code = $LASTEXITCODE
        Pop-Location
        $codes += "$k=$code"
        $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
        $esc = (Select-String -Path $log -Pattern '^(ROW|CUT)\tEsc-' | Select-Object -First 1).Line
        $gate = (Select-String -Path $log -Pattern '^гейты при матрице' | Select-Object -First 1).Line
        $mx = (Select-String -Path (Join-Path $out "rates_$k.csv") -Pattern '^meta,response_matrix' | Select-Object -First 1).Line
        "{0,-12} код {1}  {2}  | {3} | {4} | {5}" -f $k, $code, $chi, ($esc -replace "`t", ' '), ($gate -replace '^гейты при матрице: ', ''), $mx
    }
}
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
