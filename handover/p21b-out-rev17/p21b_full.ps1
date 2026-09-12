# П21б: все полные плечи подряд, статус — в p21b_out\full.status.txt
$skip = '--anchor-skip=121.782,80.998,244.697,344.279,583.187,860.557'
$arms = @(
  @('lin',       @('--anchor-light=0'),    $true),
  @('line',      @('--anchor-light=line'), $true),
  @('peak',      @('--anchor-light=peak'), $true),
  @('lin_skip',  @('--anchor-light=0', $skip),    $false),
  @('line_skip', @('--anchor-light=line', $skip), $false),
  @('peak_skip', @('--anchor-light=peak', $skip), $false)
)
$st = 'C:\Users\moroz\p21b_out\full.status.txt'
Add-Content -Encoding utf8 $st ("{0} START" -f (Get-Date -Format 'HH:mm:ss'))
foreach ($a in $arms) {
    $name = $a[0]; $extra = $a[1]; $dump = $a[2]
    if ($dump) { $r = & C:\Users\moroz\p21b_run.ps1 -Arm $name -Extra $extra -Dump 2>&1 }
    else       { $r = & C:\Users\moroz\p21b_run.ps1 -Arm $name -Extra $extra 2>&1 }
    $r | Out-File -Encoding utf8 "C:\Users\moroz\p21b_out\$name.summary.txt"
    Add-Content -Encoding utf8 $st ("{0} {1}: {2}" -f (Get-Date -Format 'HH:mm:ss'), $name, (($r | Select-String -Pattern '^run ') -join ' '))
}
Add-Content -Encoding utf8 $st ("{0} DONE" -f (Get-Date -Format 'HH:mm:ss'))
