# П60 (S171): развёртка гейта привязки по порогу и по мере — одним скриптом (все плечи одним движением).
#
#   & handover\p60-s171\sweep.ps1 [-Arm b] [-Only as80]
#
# Из D:\BqMoni_Claude\p60\wd_<плечо> зовётся FsaStackShot.exe: эталон AS80_Th232Medal (корпус,
# --chain=Th-232 --no-equilibrium, живая матрица AS80_th_disk), радоновый фильтр radon1/radon2 (сцена
# ASN16_rn_side, --chain=Ra-226,Th-232 --no-equilibrium).
# Плечи: tie0 (гейт выключен, = плечо А), col<порог> (мера по колонкам), lin<порог> (мера по линиям);
# порог 0.1 — заведомо низкий, положительный контроль гейта (члены с собственной линией ОБЯЗАНЫ привязаться).
# Выход — D:\BqMoni_Claude\p60\out_<плечо>\<спектр>_<плечо>.{log,png,csv}.
param([string]$Arm = 'b', [string]$Only = '')
$ErrorActionPreference = 'Continue'
$wd  = "D:\BqMoni_Claude\p60\wd_$Arm"
$out = "D:\BqMoni_Claude\p60\out_$Arm"
$corpus = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra'
$sp = 'D:\BqMoni_Claude\p60\spectra'
New-Item -ItemType Directory -Force $out | Out-Null
# ⛔ A77: приложение в рабочем каталоге ОБЯЗАНО быть не старше исходника анализатора — иначе развёртка
#    меряет прежний код (поймано 14.09.2026 14:45: сборка не запустилась, прогон пошёл старым exe).
$exe = Get-Item (Join-Path $wd 'BecquerelMonitor.exe')
$src = Get-Item 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\FullSpectrumAnalysis\FsaAnalyzer.cs'
if ($Arm -ne 'a' -and $exe.LastWriteTime -lt $src.LastWriteTime) {
    "⛔ exe в $wd ({0}) СТАРШЕ FsaAnalyzer.cs ({1}) — пересобрать и mk_wd" -f $exe.LastWriteTime, $src.LastWriteTime
    exit 3
}
"exe: {0}  sha256 {1}" -f $exe.LastWriteTime, (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.Substring(0, 16)
Push-Location $wd
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$spectra = @(
    @{ key = 'as80';   path = (Join-Path $corpus 'AS80_Th232Medal.xml'); args = @('--chain=Th-232', '--no-equilibrium') },
    @{ key = 'radon1'; path = (Join-Path $sp 'radon1_side.xml');         args = @('--chain=Ra-226,Th-232', '--no-equilibrium') },
    @{ key = 'radon2'; path = (Join-Path $sp 'radon2_side.xml');         args = @('--chain=Ra-226,Th-232', '--no-equilibrium') }
    # ⛔ ASN16_UGlass снят из развёртки: у спектра НЕТ геометрии (непонятная часть корпуса), и разбор
    #    отказывает по A277 «нет геометрии — нет FSA» (код 1, «матрицы нет (NoFile)», журнал П60 §3.4).
)
$arms = @(@{ key = 'tie0'; args = @('--tie=0') })
foreach ($t in @('0.1', '0.3', '0.5', '0.7', '0.8', '0.9', '0.95')) {
    $arms += @{ key = "col$t"; args = @("--tie=$t") }
    $arms += @{ key = "lin$t"; args = @("--tie=$t", '--tie-lines') }
}

$codes = @()
foreach ($s in $spectra) {
    if ($Only -and ($s.key -notlike "*$Only*")) { continue }
    foreach ($a in $arms) {
        $k = "$($s.key)_$($a.key)"
        $log = Join-Path $out "$k.log"
        $argv = @("--spectrum=$($s.path)") + $s.args + $a.args + @(
            "--out=$out\$k.png", "--rates=$out\rates_$k.csv", '--screen', '--scale=pow', '--width=1400')
        & .\FsaStackShot.exe @argv > $log 2>&1
        $code = $LASTEXITCODE
        $codes += "$k=$code"
        $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
        $tie = (Select-String -Path $log -Pattern '^привязка' | Select-Object -First 1).Line
        "{0,-16} код {1}  {2}  | {3}" -f $k, $code, $chi, ($tie -replace '^привязка: ', '' -replace ':.*$', '')
    }
}
Pop-Location
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "⛔ НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
