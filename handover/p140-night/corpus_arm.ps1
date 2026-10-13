# П140 (23.09.2026) — шаги корпусного прогона ночного счёта физики 23 (rev33).
# Плечо ОДНО: worktree `D:\BqMoni_Claude\p122\wt` (ветка `p122-physics23`), склад полосы
# `D:\BqMoni_Claude\p140\store`, выходы — `D:\BqMoni_Claude\p140\out_p140_{mini,full}`.
# Живой склад корпуса и витрина НЕ трогаются; перенос и объявление базы — дело приёмки.
#
# Зовётся из `night_rev33.cmd` так (оператором `&`, не `pwsh -File` — `T84`/`T91`):
#   pwsh -NoProfile -Command "& 'D:\BqMoni_Claude\p140\corpus_arm.ps1' -Stage wd"
#
# Коды возврата: 0 — шаг прошёл; 10/11 — mk_appwd / check_appwd отказали;
# 12 — прогон отказал; 13 — score.py отказал. Код печатается и в `arm_codes.txt`.
param([Parameter(Mandatory)][string]$Stage)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }

$wt      = 'D:\BqMoni_Claude\p122\wt'
$bin     = "$wt\BecquerelMonitor\bin\Release_p140"
$pb      = "$wt\tools\effmaker\probes\build_p140"
$wd      = "$wt\tools\CORPUS\scripts\wd_p140"
$store   = 'D:\BqMoni_Claude\p140\store'
$full    = 'D:\BqMoni_Claude\p140\out_p140_full'
$mini    = 'D:\BqMoni_Claude\p140\out_p140_mini'
$art     = 'D:\BqMoni_Claude\p140\art'
$log     = "$art\arm_codes.txt"
$score   = "$wt\tools\pie\score.py"
$minicsv = "$wt\tools\CORPUS\corpus\mini.csv"

Set-Location $wt
"[$Stage] start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') store=$store" | Out-File -Append -Encoding UTF8 $log
$rc = 0

switch ($Stage) {
    'wd' {
        & "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd -Store $store *> "$art\mk_appwd.log"
        $c1 = $LASTEXITCODE
        "[wd] mk_appwd code=$c1" | Out-File -Append -Encoding UTF8 $log
        & "$wt\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store *> "$art\check_appwd.log"
        $c2 = $LASTEXITCODE
        "[wd] check_appwd code=$c2" | Out-File -Append -Encoding UTF8 $log
        $rmx = @(Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count
        "[wd] матриц в оснастке: $rmx (ждём 49)" | Out-File -Append -Encoding UTF8 $log
        if ($c1 -ne 0) { $rc = 10 } elseif ($c2 -ne 0) { $rc = 11 } elseif ($rmx -ne 49) { $rc = 11 }
    }
    'mini' {
        $t0 = Get-Date
        & "$wt\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store *> "$art\run_mini.log"
        $c1 = $LASTEXITCODE
        "[mini] run_mini code=$c1 ($([int]((Get-Date)-$t0).TotalSeconds) с) out=$mini" | Out-File -Append -Encoding UTF8 $log
        if ($c1 -ne 0) { $rc = 12 }
        foreach ($part in @('known', 'unknown')) {
            & python $score --mode=spline "--out-dir=$mini" "--part=$part" --members "--only=$minicsv" *> "$art\score_mini_$part.txt"
            $cs = $LASTEXITCODE
            "[mini] score $part code=$cs" | Out-File -Append -Encoding UTF8 $log
            if ($cs -ne 0 -and $rc -eq 0) { $rc = 13 }
        }
        Copy-Item "$mini\.run.json" "$art\run_out_mini.json" -ErrorAction SilentlyContinue
    }
    'full' {
        $t0 = Get-Date
        & "$wt\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store *> "$art\run_full.log"
        $c1 = $LASTEXITCODE
        "[full] run_appwd code=$c1 ($([int]((Get-Date)-$t0).TotalSeconds) с) out=$full" | Out-File -Append -Encoding UTF8 $log
        if ($c1 -ne 0) { $rc = 12 }
        foreach ($part in @('known', 'unknown')) {
            & python $score --mode=spline "--out-dir=$full" "--part=$part" --members *> "$art\score_full_$part.txt"
            $cs = $LASTEXITCODE
            "[full] score $part code=$cs" | Out-File -Append -Encoding UTF8 $log
            if ($cs -ne 0 -and $rc -eq 0) { $rc = 13 }
        }
        Copy-Item "$full\.run.json" "$art\run_out_full.json" -ErrorAction SilentlyContinue
    }
    default {
        "[$Stage] НЕИЗВЕСТНЫЙ ШАГ" | Out-File -Append -Encoding UTF8 $log
        $rc = 2
    }
}

"[$Stage] end $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') rc=$rc" | Out-File -Append -Encoding UTF8 $log
exit $rc
