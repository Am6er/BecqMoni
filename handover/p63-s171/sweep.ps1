# П63 (S171, второе правило): все плечи одним скриптом (правило «гнать все плечи ОДНИМ скриптом»).
#
#   pwsh -File handover\p63-s171\sweep.ps1 [-Arm b] [-Only as80]
#
# Из D:\BqMoni_Claude\p63\wd_<плечо> зовётся FsaStackShot.exe: эталон AS80_Th232Medal (корпус,
# --chain=Th-232 --no-equilibrium, живая матрица AS80_th_disk), радоновый фильтр radon1/radon2 (сцена
# ASN16_rn_side, --chain=Ra-226,Th-232 --no-equilibrium).
# Плечи Б (правка):
#   base — привязка ВЫКЛ, предел ВЫКЛ (--tie=0 --no-limit) = прежний разбор до П60;
#   tie  — привязка ВКЛ, предел ВЫКЛ (--no-limit)          = HEAD (П60), плечо А побитово;
#   lim  — привязка ВЫКЛ, предел ВКЛ (--tie=0)             = «предел ДО привязки» (порядок правил, замер);
#   both — умолчания (привязка, затем предел)             = предлагаемый разбор;
#   high — умолчания + --limit-z=1000                     = положительный контроль: заведомо высокий порог
#                                                            ОБЯЗАН снять и членов с собственной линией;
#   eq   — связка равновесия ВКЛ (без --no-equilibrium)   = правило не судит, плечо А побитово.
# Плечи А (HEAD, ключей П63 не знает): head (умолчания HEAD = tie), eq.
# Выход — D:\BqMoni_Claude\p63\out_<плечо>\<спектр>_<плечо>.{log,png,csv}.
param([string]$Arm = 'b', [string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = "D:\BqMoni_Claude\p63\wd_$Arm"
$out = "D:\BqMoni_Claude\p63\out_$Arm"
$corpus = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
$sp = 'D:\BqMoni_Claude\p63\spectra'
New-Item -ItemType Directory -Force $out | Out-Null
# ⛔ A77: приложение в рабочем каталоге ОБЯЗАНО быть не старше исходника анализатора — иначе развёртка
#    меряет прежний код (грабля П60 14.09.2026: сборка не запустилась, прогон пошёл старым exe).
$exe = Get-Item (Join-Path $wd 'BecquerelMonitor.exe')
$src = Get-Item 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs'
if ($Arm -ne 'a' -and $exe.LastWriteTime -lt $src.LastWriteTime) {
    "⛔ exe в $wd ({0}) СТАРШЕ FsaAnalyzer.cs ({1}) — пересобрать и mk_wd" -f $exe.LastWriteTime, $src.LastWriteTime
    exit 3
}
"exe: {0}  sha256 {1}" -f $exe.LastWriteTime, (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.Substring(0, 16)
Push-Location $wd
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$noeq = @('--no-equilibrium')
$spectra = @(
    @{ key = 'as80';   path = (Join-Path $corpus 'AS80_Th232Medal.xml'); chain = '--chain=Th-232' },
    @{ key = 'radon1'; path = (Join-Path $sp 'radon1_side.xml');         chain = '--chain=Ra-226,Th-232' },
    @{ key = 'radon2'; path = (Join-Path $sp 'radon2_side.xml');         chain = '--chain=Ra-226,Th-232' }
)
if ($Arm -eq 'a') {
    $arms = @(
        @{ key = 'head'; args = $noeq },
        @{ key = 'eq';   args = @() }
    )
} else {
    $arms = @(
        @{ key = 'base'; args = $noeq + @('--tie=0', '--no-limit') },
        @{ key = 'tie';  args = $noeq + @('--no-limit') },
        @{ key = 'lim';  args = $noeq + @('--tie=0') },
        @{ key = 'both'; args = $noeq },
        @{ key = 'high'; args = $noeq + @('--limit-z=1000') },
        @{ key = 'eq';   args = @() }
    )
}

$codes = @()
foreach ($s in $spectra) {
    if ($Only -and ($s.key -notlike "*$Only*")) { continue }
    if (-not (Test-Path $s.path)) { "⛔ нет спектра $($s.path)"; $codes += "$($s.key)=nofile"; continue }
    foreach ($a in $arms) {
        $k = "$($s.key)_$($a.key)"
        $log = Join-Path $out "$k.log"
        $argv = @("--spectrum=$($s.path)", $s.chain) + $a.args + @(
            "--out=$out\$k.png", "--rates=$out\rates_$k.csv", '--screen', '--scale=pow', '--width=1400')
        & .\FsaStackShot.exe @argv > $log 2>&1
        $code = $LASTEXITCODE
        $codes += "$k=$code"
        $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
        $lim = (Select-String -Path $log -Pattern '^предел' | Select-Object -First 1).Line
        "{0,-14} код {1}  {2}  | {3}" -f $k, $code, $chi, ($lim -replace '^предел: ', '' -replace ':.*$', '')
    }
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
