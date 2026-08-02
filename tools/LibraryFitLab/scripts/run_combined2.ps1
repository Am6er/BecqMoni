param([string]$Lab = $PSScriptRoot, [int]$Throttle = 5)

# Same job as run_combined.ps1 but throttled and resumable. Launching all 18
# jobs at once wedged five of them at zero CPU indefinitely - the machine cannot
# start that many .NET processes simultaneously - so this runs at most $Throttle
# at a time and skips any output file that is already complete.
$map = @{
  'ASN16_Th232'     = 'ASN16'
  'ASN16_Charoite'  = 'ASN16'
  'ASN16_UGlass'    = 'ASN16'
  'ASN16_Granite'   = 'ASN16'
  'AS80_Th232WT20'  = 'AS80x80'
  'AS80_Th232_v2'   = 'AS80x80'
  'AS80_UGlass'     = 'AS80x80'
  'AS80_Charoite'   = 'AS80x80'
  'RC103_Th232WT20' = 'RC103'
}
$deconv = Get-Content "$Lab\deconv_best.json" -Raw | ConvertFrom-Json
$kList = '0.50', '0.70', '0.85'
$iList = '0.50', '1.00', '2.00'
$chains = 'Th-232', 'Ra-226', 'U-238', 'U-235'
$expected = 74
New-Item -ItemType Directory -Force "$Lab\out_comb" | Out-Null

$work = @()
foreach ($key in $map.Keys) {
  foreach ($tag in @('real', 'decoy')) {
    $out = "$Lab\out_comb\${key}_${tag}_runs.csv"
    if ((Test-Path $out) -and ((Get-Content $out).Count - 1) -ge $expected) {
      Write-Output "skip $key/$tag (already complete)"
      continue
    }
    $work += [pscustomobject]@{ Key = $key; Tag = $tag; Det = $map[$key] }
  }
}
Write-Output "$($work.Count) jobs to run, throttle $Throttle"

$jobs = @()
foreach ($w in $work) {
  while (@($jobs | Where-Object { $_.State -eq 'Running' }).Count -ge $Throttle) {
    Start-Sleep -Seconds 5
  }
  $suffix = if ($w.Tag -eq 'decoy') { '~decoy' } else { '' }
  $names = @()
  foreach ($c in $chains) { foreach ($k in $kList) { foreach ($i in $iList) {
    $names += "$c$suffix|k$k|i$i" } } }
  $sets = $names -join ','
  $wd = if ($w.Tag -eq 'decoy') { "$Lab\wd_$($w.Det)_decoy" } else { "$Lab\wd_$($w.Det)" }
  $cfg = $deconv.$($w.Det)
  $jobs += Start-Job -ScriptBlock {
    param($wd, $lab, $key, $sets, $tag, $snr, $roi, $extra)
    & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
      "--sets=$sets" --no-set "--snr=$snr" "--deconv=true,false" `
      "--roi-radius=$roi" "--max-extra=$extra" `
      "--runs=$lab\out_comb\${key}_${tag}_runs.csv" `
      "--peaks=$lab\out_comb\${key}_${tag}_peaks.csv" 2>&1
  } -ArgumentList $wd, $Lab, $w.Key, $sets, $w.Tag, $cfg.snr, $cfg.roi, $cfg.extra
  Write-Output "started $($w.Key)/$($w.Tag)"
}

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
