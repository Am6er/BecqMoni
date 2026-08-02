param([string]$Lab = $PSScriptRoot)

# Deconvolution parameter sweep, no nuclide set: what the RJMCMC stage alone
# does on each detector.
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

New-Item -ItemType Directory -Force "$Lab\out_deconv" | Out-Null

$jobs = @()
foreach ($key in $map.Keys) {
  $det = $map[$key]
  $wd = "$Lab\wd_$det"
  $jobs += Start-Job -ScriptBlock {
    param($wd, $lab, $key)
    # finder-only baseline across the SNR range
    & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
      --no-set "--snr=3,4,5,6,8" --deconv=false `
      "--runs=$lab\out_deconv\${key}_base_runs.csv" "--peaks=$lab\out_deconv\${key}_base_peaks.csv" 2>&1
    # deconvolution grid
    & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
      --no-set "--snr=3,4,5,6,8" --deconv=true "--roi-radius=2,3,4,5" "--max-extra=1,2,3,4,5" `
      "--runs=$lab\out_deconv\${key}_runs.csv" "--peaks=$lab\out_deconv\${key}_peaks.csv" 2>&1
  } -ArgumentList $wd, $Lab, $key
}

Write-Output "started $($jobs.Count) deconvolution jobs"
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
Get-ChildItem "$Lab\out_deconv\*_runs.csv" | ForEach-Object {
  "{0}: {1} runs" -f $_.Name, ((Get-Content $_.FullName).Count - 1)
}

