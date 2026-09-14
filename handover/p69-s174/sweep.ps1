# П69 (S174): все плечи одним скриптом (правило «гнать все плечи ОДНИМ скриптом»). По образцу П68.
#
#   pwsh -File D:\BqMoni_Claude\p69\sweep.ps1 [-Arm b] [-Only as80]
#
# Из D:\BqMoni_Claude\p69\wd_<плечо> зовётся FsaStackShot.exe: эталон AS80_Th232Medal (корпус из чистого
# worktree, --chain=Th-232, живая матрица AS80_th_disk), радоновый фильтр radon1/radon2 (сцена ASN16_rn_side,
# --chain=Ra-226,Th-232). Плечи одни и те же у А (HEAD 6c2833cf) и Б (правка):
#   free    — без связки равновесия (--no-equilibrium), матрица есть   = режим П63/П65/П68;
#   eq      — связка равновесия ВКЛ, матрица есть                       = режим корпуса (малой базы) и экрана приложения;
#   plant   — free + --plant-spread: ПОДСАДКА «разнести подложку как прежде, невязка по всей полосе»
#             (только у Б; у А ключа нет — прогон падает кодом 2, ожидаемо: плечо plant у А не снимается);
#   zoom    — free + --from=0 --to=300: крупный план низа шкалы (⚠ --from/--to — окно КАРТИНКИ, не фита);
#   from100 — free + --from=100: окно картинки от порога; в числах = free (контроль ключа окна).
# Каждый прогон — лог, PNG, rates csv (--rates=), дамп кривых (--dump=), строки экрана (--screen).
param([string]$Arm = 'b', [string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = "D:\BqMoni_Claude\p69\wd_$Arm"
$out = "D:\BqMoni_Claude\p69\out_$Arm"
$corpus = 'D:\BqMoni_Claude\p69\wt\tools\CORPUS\corpus\spectra'
$sp = 'D:\BqMoni_Claude\p69\spectra'
New-Item -ItemType Directory -Force $out | Out-Null
# ⛔ A77: приложение в рабочем каталоге ОБЯЗАНО быть не старше исходника анализатора — иначе развёртка
#    меряет прежний код.
$exe = Get-Item (Join-Path $wd 'BecquerelMonitor.exe')
$srcDir = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\FullSpectrumAnalysis'
$src = Get-ChildItem (Join-Path $srcDir '*.cs') | Sort-Object LastWriteTime | Select-Object -Last 1
if ($Arm -ne 'a' -and $exe.LastWriteTime -lt $src.LastWriteTime) {
    "⛔ exe в $wd ({0}) СТАРШЕ {1} ({2}) — пересобрать и mk_wd" -f $exe.LastWriteTime, $src.Name, $src.LastWriteTime
    exit 3
}
if ($Arm -ne 'a') {
    $pe = Get-Item (Join-Path $wd 'FsaStackShot.exe')
    $ps = Get-Item 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\FsaStackShot.cs'
    if ($pe.LastWriteTime -lt $ps.LastWriteTime) { "⛔ FsaStackShot.exe старше FsaStackShot.cs — пересобрать"; exit 3 }
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
    @{ key = 'free';    args = $noeq },
    @{ key = 'eq';      args = @() },
    @{ key = 'plant';   args = $noeq + @('--plant-spread') },
    @{ key = 'zoom';    args = $noeq + @('--from=0', '--to=300') },
    @{ key = 'from100'; args = $noeq + @('--from=100') }
)

$codes = @()
foreach ($s in $spectra) {
    if ($Only -and ($s.key -notlike "*$Only*")) { continue }
    if (-not (Test-Path $s.path)) { "⛔ нет спектра $($s.path)"; $codes += "$($s.key)=nofile"; continue }
    foreach ($a in $arms) {
        if ($Arm -eq 'a' -and $a.key -eq 'plant') { continue }
        $k = "$($s.key)_$($a.key)"
        $log = Join-Path $out "$k.log"
        $argv = @("--spectrum=$($s.path)", $s.chain) + $a.args + @(
            "--out=$out\$k.png", "--rates=$out\rates_$k.csv", "--dump=$out\curves_$k.csv", '--screen', '--scale=pow', '--width=1400')
        Push-Location $wd
        & .\FsaStackShot.exe @argv > $log 2>&1
        $code = $LASTEXITCODE
        Pop-Location
        $codes += "$k=$code"
        $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
        $grey = (Select-String -Path $log -Pattern '^серый слой' | Select-Object -First 1).Line
        $res = (Select-String -Path $log -Pattern '^SCREEN\t(Невязка|Residual|Model residual)' | Select-Object -First 1).Line
        "{0,-15} код {1}  {2}  | {3} | {4}" -f $k, $code, $chi, ($grey -replace '^серый слой \(S174\): ', 'серый: '), ($res -replace "`t", ' ')
    }
}
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
