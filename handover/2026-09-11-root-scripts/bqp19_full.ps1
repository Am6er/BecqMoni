# П19 12.09.2026: все плечи полного корпуса подряд (lin, bin, line, peak, anchor + выброс узла), копия bqp19.
$ErrorActionPreference = 'Continue'
$skip = '--anchor-skip=121.782,80.998,244.697,344.279,583.187,860.557'
$arms = @(
    @('lin', @()),
    @('bin', @('--anchor-light=bin')),
    @('line', @('--anchor-light=line')),
    @('peak', @('--anchor-light=peak')),
    @('anchor', @('--anchor-light=anchor')),
    @('lin_skip', @($skip)),
    @('bin_skip', @('--anchor-light=bin', $skip)),
    @('line_skip', @('--anchor-light=line', $skip)),
    @('peak_skip', @('--anchor-light=peak', $skip))
)
foreach ($a in $arms) {
    $name = $a[0]; $extra = $a[1]
    if ($extra.Count -gt 0) {
        & C:\Users\moroz\bqp19_run.ps1 -Arm $name -Extra $extra 2>&1 | Select-String "run |sum chi2|recall|итого"
    } else {
        & C:\Users\moroz\bqp19_run.ps1 -Arm $name 2>&1 | Select-String "run |sum chi2|recall|итого"
    }
}
Write-Output 'ВСЕ ПЛЕЧИ ПРОШЛИ'
