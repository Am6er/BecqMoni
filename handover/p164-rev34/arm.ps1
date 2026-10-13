# П164 — одно плечо корпуса: оснастка (mk_appwd + check_appwd) из сборки <Tag> дерева <Wt> со складом <Store>,
# прогон полной базы и малой базы, score.py обеими частями. Коды — art\arm_<Name>.txt.
param(
    [Parameter(Mandatory)][string]$Wt,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Store,
    [Parameter(Mandatory)][string]$Name,
    [string]$Corpus = '',
    [string[]]$Extra = @(),
    [switch]$NoMini
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$art   = 'D:\BqMoni_Claude\p164\art'
$bin   = "$Wt\BecquerelMonitor\bin\Release_$Tag"
$pb    = "$Wt\tools\effmaker\probes\build_$Tag"
$wd    = "$Wt\tools\CORPUS\scripts\wd_$Tag"
$outs  = 'D:\BqMoni_Claude\p164\out'
$full  = "$outs\${Name}_full"
$mini  = "$outs\${Name}_mini"
$log   = "$art\arm_$Name.txt"
$score = 'D:\BqMoni_Claude\p147\wt\tools\pie\score.py'   # один счётчик на все плечи
$minicsv = "$Wt\tools\CORPUS\corpus\mini.csv"
New-Item -ItemType Directory -Force $outs | Out-Null
Set-Location $Wt
"[$Name] start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') wt=$Wt tag=$Tag store=$Store corpus=$Corpus extra=$($Extra -join ' ')" | Out-File -Encoding UTF8 $log
& "$Wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $Store *> "$art\mk_appwd_$Name.log"
"mk_appwd code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
& "$Wt\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $Store *> "$art\check_appwd_$Name.log"
"check_appwd code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
$rmx = @(Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count
"rmx=$rmx" | Out-File -Append -Encoding UTF8 $log
$common = @{ Wd = $wd; Bin = $bin; ProbeBuild = $pb; Store = $Store }
if ($Corpus) { $common.Corpus = $Corpus }
if ($Extra.Count -gt 0) { $common.Extra = $Extra }
& "$Wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full @common *> "$art\run_full_$Name.log"
"run_full code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
& python $score --mode=spline "--out-dir=$full" --part=known --members *> "$art\score_full_known_$Name.txt"
"score_full code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
if (-not $NoMini) {
    if ($Corpus) {
        # run_mini не знает -Corpus: тот же вызов, что у него, — run_appwd с --only малой базы
        $only = (Get-Content 'D:\BqMoni_Claude\p164\mini_only.txt' -Raw).Trim()
        $cm = $common.Clone(); $cm.Extra = @($only) + $Extra
        & "$Wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $mini @cm *> "$art\run_mini_$Name.log"
    } else {
        $cm = $common.Clone()
        & "$Wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini @cm *> "$art\run_mini_$Name.log"
    }
    "run_mini code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
    & python $score --mode=spline "--out-dir=$mini" --part=known --members "--only=$minicsv" *> "$art\score_mini_known_$Name.txt"
    "score_mini code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
}
Get-Content $log
Select-String -Path "$art\score_full_known_$Name.txt" -Pattern 'sum chi2/ndf' | ForEach-Object { "FULL: " + $_.Line.Trim() }
if (-not $NoMini) { Select-String -Path "$art\score_mini_known_$Name.txt" -Pattern 'sum chi2/ndf' | ForEach-Object { "MINI: " + $_.Line.Trim() } }
