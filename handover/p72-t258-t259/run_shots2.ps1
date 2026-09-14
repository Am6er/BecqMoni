# П72 (T259): второй заход плеч — спектр radon1 («Радон деревня.xml», первая съёмка после прокачки: дочерние
# радона ЖИВЫ; на «11 часов спустя» они распались, и ряд Ra-226/Rn-222 в обоих плечах уходит в предел, см. журнал §2.2).
#
#   & handover\p72-t258-t259\run_shots2.ps1
#
#   r1_head_ra226      wd_head  radon1  --chain=Ra-226           (HEAD)
#   r1_new_ra226       wd_new   radon1  --chain=Ra-226           (правка) — побитово = r1_head_ra226
#   r1_new_rn222       wd_new   radon1  --chain=Rn-222           (правка) — Ra-226/Pb-210 НЕТ, Pb-214/Bi-214 одной амплитудой
#   r1_head_ra226_th   wd_head  radon1  --chain=Ra-226,Th-232    (HEAD, постановка П59)
#   r1_new_ra226_th    wd_new   radon1  --chain=Ra-226,Th-232    (правка) — побитово = r1_head_ra226_th
#   r1_new_rn222_th    wd_new   radon1  --chain=Rn-222,Th-232    (правка)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = 'D:\BqMoni_Claude\p72\out'
$sp  = 'D:\BqMoni_Claude\p72\spectra\radon1_side.xml'
New-Item -ItemType Directory -Force $out | Out-Null
$runs = @(
    @{ key = 'r1_head_ra226';    wd = 'D:\BqMoni_Claude\p72\wd_head'; args = @('--chain=Ra-226') },
    @{ key = 'r1_new_ra226';     wd = 'D:\BqMoni_Claude\p72\wd_new';  args = @('--chain=Ra-226') },
    @{ key = 'r1_new_rn222';     wd = 'D:\BqMoni_Claude\p72\wd_new';  args = @('--chain=Rn-222') },
    @{ key = 'r1_head_ra226_th'; wd = 'D:\BqMoni_Claude\p72\wd_head'; args = @('--chain=Ra-226,Th-232') },
    @{ key = 'r1_new_ra226_th';  wd = 'D:\BqMoni_Claude\p72\wd_new';  args = @('--chain=Ra-226,Th-232') },
    @{ key = 'r1_new_rn222_th';  wd = 'D:\BqMoni_Claude\p72\wd_new';  args = @('--chain=Rn-222,Th-232') }
)
$codes = @()
foreach ($r in $runs) {
    $k = $r.key
    Push-Location $r.wd
    $log = Join-Path $out "$k.log"
    $a = @("--spectrum=$sp") + $r.args + @(
        "--out=$out\$k.png", "--rates=$out\rates_$k.csv", "--dump=$out\curves_$k.csv",
        '--screen', '--scale=pow', '--width=1400')
    & .\FsaStackShot.exe @a > $log 2>&1
    $code = $LASTEXITCODE
    Pop-Location
    $codes += "$k=$code"
    $chi = (Select-String -Path $log -Pattern '^chi2/ndf' | Select-Object -First 1).Line
    "{0,-18} код {1}  {2}" -f $k, $code, $chi
}
"коды: " + ($codes -join ' ')
$codes -join "`n" | Out-File -Encoding utf8 (Join-Path $out 'codes_shots2.txt')
