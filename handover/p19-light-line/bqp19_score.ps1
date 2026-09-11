# П19: recall/фантомы/подавлен по плечам ПОЛНОГО корпуса — score.py --part=known --members (без --only: гоняли все).
#   & C:\Users\moroz\bqp19_score.ps1 lin bin line peak anchor lin_skip bin_skip line_skip peak_skip
param([Parameter(ValueFromRemainingArguments)][string[]]$Arms)
$root = 'C:\Users\moroz\bqp19'
Set-Location $root
foreach ($a in $Arms) {
    $out = "C:\Users\moroz\bqp19_out\$a"
    $txt = & python 'tools\pie\score.py' --mode=spline "--out-dir=$out" --part=known --members 2>&1
    $txt | Out-File -Encoding utf8 "C:\Users\moroz\bqp19_out\$a`_score.txt"
    $line = $txt | Select-String -Pattern '^итого' | Select-Object -Last 1
    Write-Output ("{0,-10} {1}" -f $a, $line)
}
