param([string]$Lab = $PSScriptRoot)

# Controlled test of the background flag. The two 28.08.2025 spectra are the only
# ones carrying a measured background, and they show roughly double the phantom
# rate of the rest. Statistics and calibration differ too, so the flag has to be
# isolated: the SAME spectra, the same decoy sets, run once with
# BackgroundMode.Substract and once with Visible.
$keys = 'AS80_Th232_v2', 'AS80_UGlass'
$manifest = Get-Content "$Lab\sets_manifest_decoy.json" -Raw | ConvertFrom-Json
$sets = ($manifest | Where-Object { $_.det -eq 'AS80x80' } | ForEach-Object { $_.set_name }) -join ','
New-Item -ItemType Directory -Force "$Lab\out_bgtest" | Out-Null

$jobs = @()
foreach ($key in $keys) {
  foreach ($mode in @('substract', 'visible')) {
    $jobs += Start-Job -ScriptBlock {
      param($lab, $key, $sets, $mode)
      $wd = "$lab\wd_AS80x80_decoy"
      & "$wd\LibraryFitLab.exe" "--workdir=$wd" "--input=$lab\spectra\$key.xml" `
        "--sets=$sets" --no-set "--snr=4" --deconv=false "--bg=$mode" `
        "--runs=$lab\out_bgtest\${key}_${mode}_runs.csv" `
        "--peaks=$lab\out_bgtest\${key}_${mode}_peaks.csv" 2>&1
    } -ArgumentList $Lab, $key, $sets, $mode
  }
}
Write-Output "started $($jobs.Count) background-flag jobs"
$jobs | Wait-Job | Out-Null
foreach ($j in $jobs) { Receive-Job $j | Where-Object { $_ } | Write-Output }
$jobs | Remove-Job
Get-ChildItem "$Lab\out_bgtest\*_runs.csv" | ForEach-Object {
  "{0}: {1} runs" -f $_.Name, ((Get-Content $_.FullName).Count - 1)
}
