if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin  = "$root\BecquerelMonitor\bin\Debug_p21b"; $prb = "$root\tools\effmaker\probes\build_p21b"; $wd = "$root\tools\CORPUS\scripts\wd_p21b"; $store = "$root\tools\CORPUS\corpus\geometries"
Set-Location $root
$st = 'C:\Users\moroz\p21b_out\rev17.status.txt'
Add-Content -Encoding utf8 $st ("{0} START" -f (Get-Date -Format 'HH:mm:ss'))
& "$root\tools\CORPUS\scripts\run_mini.ps1" -Out "$root\tools\pie\out_rev17_mini" -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store *> C:\Users\moroz\p21b_out\rev17_mini.log
Add-Content -Encoding utf8 $st ("{0} mini exit={1}" -f (Get-Date -Format 'HH:mm:ss'), $LASTEXITCODE)
& "$root\tools\CORPUS\scripts\run_appwd.ps1" -Out "$root\tools\pie\out_rev17_full" -Wd $wd -Bin $bin -ProbeBuild $prb -Store $store *> C:\Users\moroz\p21b_out\rev17_full.log
Add-Content -Encoding utf8 $st ("{0} full exit={1}" -f (Get-Date -Format 'HH:mm:ss'), $LASTEXITCODE)
Add-Content -Encoding utf8 $st ("{0} DONE" -f (Get-Date -Format 'HH:mm:ss'))
