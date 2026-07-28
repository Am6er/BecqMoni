param([string]$Lab = $PSScriptRoot)

# Deconvolution and nuclide set together, around the optimum of each separate
# sweep. Real sets give recall, decoy sets give the false-positive count under
# exactly the same deconvolution settings.
$map = @{
  'ASN16_Th232'     = 'ASN16'
  'ASN16_Charoite'  = 'ASN16'
  'ASN16_UGlass'    = 'ASN16'
  'ASN16_Granite'   = 'ASN16'
  'AS80_Th232WT20'  = 'AS80x80'
  'AS80_Th232_v2'  = 'AS80x80'
  'AS80_UGlass'     = 'AS80x80'
  'AS80_Charoite'   = 'AS80x80'
  'RC103_Th232WT20' = 'RC103'
}

# filled from the deconvolution sweep result
$deconv = @{
  'ASN16'   = @{ snr = 4; roi = 4; extra = 3 }
  'AS80x80' = @{ snr = 4; roi = 4; extra = 3 }
  'RC103'   = @{ snr = 4; roi = 4; extra = 3 }
}
if (Test-Path "$Lab\deconv_best.json") {
  $best = Get-Content "$Lab\deconv_best.json" -Raw | ConvertFrom-Json
  foreach ($d in $best.PSObject.Properties) { $deconv[$d.Name] = $d.Value }
}

$kList = '0.50', '0.70', '0.85'
$iList = '0.50', '1.00', '2.00'
$chains = 'Th-232', 'Ra-226', 'U-238', 'U-235'

New-Item -ItemType Directory -Force "$Lab\out_comb" | Out-Null

$jobs = @()
foreach ($key in $map.Keys) {
  $det = $map[$key]
  $cfg = $deconv[$det]
  foreach ($kind in @('', '_decoy')) {
    $names = @()
    foreach ($c in $chains) {
      foreach ($k in $kList) {
        foreach ($i in $iList) {
          $suffix = if ($kind -eq '_decoy') { '~decoy' } else { '' }
          $names += "$c$suffix|k$k|i$i"
        }
      }
    }
    $sets = $names -join ','
    $wd = "$Lab\wd_$det$kind"
    $tag = if ($kind -eq '_decoy') { 'decoy' } else { 'real' }
    $jobs += Start-Job -ScriptBlock {
      param($wd, $lab, $key, $sets, $tag, $snr, $roi, $extra)
      # NB: the comma-separated values must be quoted. Unquoted,
      # --deconv=true,false is parsed by PowerShell as a two-element array and
      # reaches the exe as "--deconv=true" plus a stray "false".
      & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
        "--sets=$sets" --no-set "--snr=$snr" "--deconv=true,false" `
        "--roi-radius=$roi" "--max-extra=$extra" `
        "--runs=$lab\out_comb\${key}_${tag}_runs.csv" "--peaks=$lab\out_comb\${key}_${tag}_peaks.csv" 2>&1
    } -ArgumentList $wd, $Lab, $key, $sets, $tag, $cfg.snr, $cfg.roi, $cfg.extra
  }
}

Write-Output "started $($jobs.Count) combined jobs"
$jobs | Wait-Job | Out-Null
foreach ($j in $jobs) { Receive-Job $j | Where-Object { $_ } | Write-Output }
# Упавший джоб раньше проходил незамеченным: скрипт печатал
# сводку по лежалым CSV прошлого прогона и выходил с кодом 0.
$failed = 0
foreach ($j in $jobs) {
  if ($j.State -ne 'Completed') { $failed++ }
}
if ($failed -gt 0) {
  Write-Error "$failed job(s) failed"
  exit 1
}
$jobs | Remove-Job
Get-ChildItem "$Lab\out_comb\*_runs.csv" | ForEach-Object {
  "{0}: {1} runs" -f $_.Name, ((Get-Content $_.FullName).Count - 1)
}

