# A339: прогон RC103_Lu176 на складах плеч (зёрна матрицы RC103_lu_front).
#   & D:\BqMoni_Claude\a339\arms.ps1 -Tag cur -Seeds 1,2,3,4,5,6 [-ProbeBuild build_a339t] [-Extra '--lib-dump']
param(
    [string]$Tag = 'cur',
    [int[]]$Seeds = @(1, 2, 3, 4, 5, 6),
    [string]$ProbeBuild = 'tools\effmaker\probes\build_a339t',
    [string]$List = 'D:\BqMoni_Claude\a339\list_lu.csv',
    [string[]]$Extra = @('--lib-dump')
)
$ErrorActionPreference = 'Stop'
$r = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $r
$g = "$r\tools\CORPUS\corpus\geometries"
$a = 'D:\BqMoni_Claude\a339'
foreach ($s in $Seeds) {
    $store = "$a\store_s$s"
    if (-not (Test-Path "$store\response")) {
        New-Item -ItemType Directory -Force "$store\response" | Out-Null
        Copy-Item "$g\*.rmx" $store
        Copy-Item "$g\response\*.rmx" "$store\response"
        Copy-Item "$a\mx_s$s\RC103_lu_front.rmx" "$store\RC103_lu_front.rmx" -Force
        & python 'tools\CORPUS\scripts\mx_swap.py' "--from=$a\mx_s$s" "--into=$store" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "mx_swap $s" }
    }
    & 'tools\CORPUS\scripts\mk_appwd.ps1' -Bin 'BecquerelMonitor\bin\Release_a339' -Wd 'tools\CORPUS\scripts\wd_a339' -ProbeBuild $ProbeBuild -Store $store *> "$a\mk_wd_$Tag`_s$s.log"
    if ($LASTEXITCODE -ne 0) { throw "mk_appwd $s код $LASTEXITCODE" }
    & 'tools\CORPUS\scripts\run_mini.ps1' -List $List -Out "$a\out_$Tag`_s$s" -Wd 'tools\CORPUS\scripts\wd_a339' -Bin 'BecquerelMonitor\bin\Release_a339' -ProbeBuild $ProbeBuild -Store $store -SkipScore -Extra $Extra *> "$a\run_$Tag`_s$s.log"
    "seed $s run $LASTEXITCODE"
}
# штатный склад — вернуть оснастку
& 'tools\CORPUS\scripts\mk_appwd.ps1' -Bin 'BecquerelMonitor\bin\Release_a339' -Wd 'tools\CORPUS\scripts\wd_a339' -ProbeBuild $ProbeBuild *> "$a\mk_wd_$Tag`_stock.log"
& 'tools\CORPUS\scripts\run_mini.ps1' -List $List -Out "$a\out_$Tag`_s0" -Wd 'tools\CORPUS\scripts\wd_a339' -Bin 'BecquerelMonitor\bin\Release_a339' -ProbeBuild $ProbeBuild -SkipScore -Extra $Extra *> "$a\run_$Tag`_s0.log"
"stock run $LASTEXITCODE"
