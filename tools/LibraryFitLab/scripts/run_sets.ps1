param([string]$Lab = $PSScriptRoot, [double[]]$Snr = @(4))

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

$manifest = Get-Content "$Lab\sets_manifest.json" -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force "$Lab\out_sets" | Out-Null

$jobs = @()
foreach ($key in $map.Keys) {
  $det = $map[$key]
  $sets = ($manifest | Where-Object { $_.det -eq $det } | ForEach-Object { $_.set_name }) -join ','
  $wd = "$Lab\wd_$det"
  $snrList = ($Snr -join ',')
  $jobs += Start-Job -ScriptBlock {
    param($wd, $lab, $key, $sets, $snrList)
    & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
      "--sets=$sets" --no-set "--snr=$snrList" --deconv=false `
      "--runs=$lab\out_sets\${key}_runs.csv" "--peaks=$lab\out_sets\${key}_peaks.csv" 2>&1
  } -ArgumentList $wd, $Lab, $key, $sets, $snrList
}

Write-Output "started $($jobs.Count) jobs"
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
Get-ChildItem "$Lab\out_sets\*_runs.csv" | ForEach-Object {
  "{0}: {1} runs" -f $_.Name, ((Get-Content $_.FullName).Count - 1)
}

