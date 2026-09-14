# П71 (S170): положительный контроль — нейтральная мерка П49 на своём стенде (плечо A = HEAD 311c98b0).
#   pwsh -File D:\BqMoni_Claude\p71\run_cf.ps1 -Arm a|b [-Tag <метка>]
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm, [string]$Tag = '')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = if ($Arm -eq 'a') { 'D:\BqMoni_Claude\p71\wt' } else { 'D:\BqMoni_Claude\p71\wt_b' }
$wd = "$wt\tools\CORPUS\scripts\wd_p71$Arm"
$sp = "$wt\tools\CORPUS\corpus\spectra"
$out = "D:\BqMoni_Claude\p71\cf$Tag"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Location $wd
$codes = @()
foreach ($k in 0,1) {
  foreach ($s in 'G1S16_Co60_P5','G1S24_Co60_P5','G1S16_Co60_P25') {
    & '.\FsaCascadeProbe.exe' "--spectrum=$sp\$s.xml" --sample=Co-60 "--angcorr=$k" --lines=4 --scan=2300:2700 *> "$out\cf_${s}_ang$k.log"
    $codes += "${s}_ang$k=$LASTEXITCODE"
  }
}
"коды: " + ($codes -join ' ')
