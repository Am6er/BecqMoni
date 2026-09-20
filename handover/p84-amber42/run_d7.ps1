# П84 (AMBER42 п.3): плечо Δ = 7 мм — свой склад D:\BqMoni_Claude\p84\store (матрица G1S_point5 при
# pdistance 5.7 см, посчитана CorpusMatrixProbe рецептом склада: умолчания класса физики 18 + --target=0),
# раскладка по guid (mx_swap.py), оснастка wd_p84d7 с этим складом, спектры-копии с PointDistance 57;
# SumPeakProbe (оба плеча ключа внутри одной пробы: плечо 3 = ВЫКЛ, плечо 4 = ВКЛ) и FsaCascadeProbe --angcorr=0|1.
#   pwsh -File D:\BqMoni_Claude\p84\scripts\run_d7.ps1 [-Tag d7] [-Store D:\BqMoni_Claude\p84\store] [-Spectra <каталог копий>]
param([string]$Tag = 'd7', [string]$Store = 'D:\BqMoni_Claude\p84\store', [string]$Spectra = 'D:\BqMoni_Claude\p84\spectra_d7',
      [string[]]$Names = @('G1S16_Co60_P5', 'G1S24_Co60_P5'))
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$probes = "$repo\tools\effmaker\probes\build_p84"
$wd = "$repo\tools\CORPUS\scripts\wd_p84$Tag"
$out = "D:\BqMoni_Claude\p84\logs"
$cf = "D:\BqMoni_Claude\p84\cf_$Tag"
New-Item -ItemType Directory -Force $cf | Out-Null

Set-Location $repo
python tools\CORPUS\scripts\mx_swap.py "--from=$Store" "--into=$Store" *> "$out\mx_swap_$Tag.log"
"mx_swap code $LASTEXITCODE"
Get-Content "$out\mx_swap_$Tag.log" | Select-String 'подменено|куда' | ForEach-Object { "  $_" }

& "$repo\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p84" -Wd $wd -ProbeBuild $probes -Store $Store *> "$out\mk_wd_$Tag.log"
"mk_appwd code $LASTEXITCODE"
Get-Content "$out\mk_wd_$Tag.log" | Select-String 'ОСНАСТКА|⛔|ПРОТУХ|положено|матриц' | Select-Object -Last 4 | ForEach-Object { "  $_" }
"response: rmx " + (Get-ChildItem "$wd\config\device\response\*.rmx").Count + ", qk " + (Get-ChildItem "$wd\config\device\response\*.qk").Count
Get-ChildItem "$wd\config\device\response" | ForEach-Object { "  {0,-50} {1,9}" -f $_.Name, $_.Length }

Set-Location $wd
$codes = @()
foreach ($s in $Names) {
  .\SumPeakProbe.exe "--spectrum=$Spectra\$s.xml" --sample=Co-60 *> "$out\sp_${s}_$Tag.log"
  $codes += "sp:$s=$LASTEXITCODE"
}
foreach ($k in 0, 1) {
  foreach ($s in $Names) {
    & '.\FsaCascadeProbe.exe' "--spectrum=$Spectra\$s.xml" --sample=Co-60 "--angcorr=$k" --lines=4 --scan=2300:2700 *> "$cf\cf_${s}_ang$k.log"
    $codes += "cf:${s}_ang$k=$LASTEXITCODE"
  }
}
"коды: " + ($codes -join ' ')
