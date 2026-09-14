# П72 (T259): все плечи приёмки одним скриптом (правило «гнать все плечи ОДНИМ скриптом»).
#
#   & handover\p72-t258-t259\run_shots.ps1
#
# Плечи (равновесие ВКЛ — умолчание корпуса; --scale=pow --width=1400 --screen как у П59):
#   head_ra226   wd_head  фильтр radon2_side  --chain=Ra-226          (HEAD 311c98b0)
#   new_ra226    wd_new   фильтр radon2_side  --chain=Ra-226          (правка) — обязан быть побитово = head_ra226
#   new_rn222    wd_new   фильтр radon2_side  --chain=Rn-222          (правка) — Ra-226/Pb-210 в составе НЕТ
#   new_xx999    wd_new   фильтр radon2_side  --chain=Xx-999          (правка) — отказ словами, код 2
#   head_xx999   wd_head  фильтр radon2_side  --chain=Xx-999          (HEAD) — отказ и прежде (контроль контроля)
#   head_coal_ra wd_head  уголь eq01          --chain=Ra-226,Th-228   (HEAD, истина манифеста сегодня)
#   new_coal_ra  wd_new   уголь eq01          --chain=Ra-226,Th-228   (правка) — побитово = head_coal_ra
#   new_coal_rn  wd_new   уголь eq01          --chain=Rn-222,Th-228   (правка) — число на угле для следующего переобъявления
# Выход — D:\BqMoni_Claude\p72\out\: <ключ>.png, rates_<ключ>.csv, curves_<ключ>.csv, <ключ>.log; коды — в codes_shots.txt.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = 'D:\BqMoni_Claude\p72\out'
$sp  = 'D:\BqMoni_Claude\p72\spectra\radon2_side.xml'
$coal = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\spectra\G1S24_Rn222Coal_Mar_eq01.xml'
New-Item -ItemType Directory -Force $out | Out-Null
$runs = @(
    @{ key = 'head_ra226';   wd = 'D:\BqMoni_Claude\p72\wd_head'; spectrum = $sp;   args = @('--chain=Ra-226') },
    @{ key = 'new_ra226';    wd = 'D:\BqMoni_Claude\p72\wd_new';  spectrum = $sp;   args = @('--chain=Ra-226') },
    @{ key = 'new_rn222';    wd = 'D:\BqMoni_Claude\p72\wd_new';  spectrum = $sp;   args = @('--chain=Rn-222') },
    @{ key = 'new_xx999';    wd = 'D:\BqMoni_Claude\p72\wd_new';  spectrum = $sp;   args = @('--chain=Xx-999') },
    @{ key = 'head_xx999';   wd = 'D:\BqMoni_Claude\p72\wd_head'; spectrum = $sp;   args = @('--chain=Xx-999') },
    @{ key = 'head_coal_ra'; wd = 'D:\BqMoni_Claude\p72\wd_head'; spectrum = $coal; args = @('--chain=Ra-226,Th-228') },
    @{ key = 'new_coal_ra';  wd = 'D:\BqMoni_Claude\p72\wd_new';  spectrum = $coal; args = @('--chain=Ra-226,Th-228') },
    @{ key = 'new_coal_rn';  wd = 'D:\BqMoni_Claude\p72\wd_new';  spectrum = $coal; args = @('--chain=Rn-222,Th-228') }
)
$codes = @()
foreach ($r in $runs) {
    $k = $r.key
    Push-Location $r.wd
    $log = Join-Path $out "$k.log"
    $a = @("--spectrum=$($r.spectrum)") + $r.args + @(
        "--out=$out\$k.png", "--rates=$out\rates_$k.csv", "--dump=$out\curves_$k.csv",
        '--screen', '--scale=pow', '--width=1400')
    & .\FsaStackShot.exe @a > $log 2>&1
    $code = $LASTEXITCODE
    Pop-Location
    $codes += "$k=$code"
    $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-14} код {1}  {2}" -f $k, $code, $chi
}
"коды: " + ($codes -join ' ')
$codes -join "`n" | Out-File -Encoding utf8 (Join-Path $out 'codes_shots.txt')
