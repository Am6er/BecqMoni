# П141 (23.09.2026) — шаги корпусного прогона на `master`: ЦЕНА ВЧЕРАШНИХ ПРАВОК FSA
# на действующей базе rev32 (физика 22, живой склад, формат 9).
#
# Решение Amber 23.09.2026, вопросником, дословно: «На `master` — цена вчерашних правок FSA
# на базе rev32»; пуск — её же слово того же дня, консоль: «Запускай расчёт корпуса сейчас,
# но не следи за ним».
#
# ⛔ РЕПЕТИЦИЯ ЦЕНЫ, А НЕ ПЕРЕОБЪЯВЛЕНИЕ БАЗЫ: `tools/pie/out_rev32_*` только читаются
#    (лестницей), живой склад `tools/CORPUS/corpus/geometries` только читается, витрина
#    `tools/fsa_showcase/**` не трогается, объявление в трёх местах не правится.
#
# Зовётся из `corpus_p141.cmd` так (оператором `&`, не `pwsh -File` — `T84`/`T91`;
# с `; exit $LASTEXITCODE`, иначе код возврата теряется — находка П140 §8 (б)):
#   pwsh -NoProfile -Command "& 'D:\BqMoni_Claude\p141\corpus_arm.ps1' -Stage wd; exit $LASTEXITCODE"
#
# Коды возврата: 0 — шаг прошёл; 10/11 — mk_appwd / check_appwd отказали (или матриц в
# оснастке не 49); 12 — прогон отказал; 13 — score.py отказал; 14 — сводка/лестница
# отказали; 2 — неизвестный шаг. Код печатается и в `art\arm_codes.txt`.
param([Parameter(Mandatory)][string]$Stage)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }   # без него `build_all`/пробы не видят runtimes\

$repo    = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin     = "$repo\BecquerelMonitor\bin\Release_p141"
$pb      = "$repo\tools\effmaker\probes\build_p141"
$wd      = "$repo\tools\CORPUS\scripts\wd_p141"
$full    = 'D:\BqMoni_Claude\p141\out_p141_full'
$mini    = 'D:\BqMoni_Claude\p141\out_p141_mini'
$art     = 'D:\BqMoni_Claude\p141\art'
$log     = "$art\arm_codes.txt"
$score   = "$repo\tools\pie\score.py"
$minicsv = "$repo\tools\CORPUS\corpus\mini.csv"
$ladder  = "$repo\handover\p114-physics22\ladder.py"
$lsum    = "$repo\handover\p114-physics22\ladder_summary.py"
$rev32f  = "$repo\tools\pie\out_rev32_full"
$rev32m  = "$repo\tools\pie\out_rev32_mini"

Set-Location $repo
"[$Stage] start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') склад=ЖИВОЙ корпусный (умолчание)" | Out-File -Append -Encoding UTF8 $log
$rc = 0

switch ($Stage) {
    'wd' {
        # -Store НЕ задаётся: пустой = штатный живой склад корпуса (49 матриц физики 22).
        & "$repo\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -ProbeBuild $pb -Wd $wd *> "$art\mk_appwd.log"
        $c1 = $LASTEXITCODE
        "[wd] mk_appwd code=$c1" | Out-File -Append -Encoding UTF8 $log
        & "$repo\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\check_appwd.log"
        $c2 = $LASTEXITCODE
        "[wd] check_appwd code=$c2" | Out-File -Append -Encoding UTF8 $log
        $rmx = @(Get-ChildItem "$wd\config\device\response\*.rmx" -ErrorAction SilentlyContinue).Count
        "[wd] матриц в оснастке: $rmx (ждём 49)" | Out-File -Append -Encoding UTF8 $log
        if ($c1 -ne 0) { $rc = 10 } elseif ($c2 -ne 0) { $rc = 11 } elseif ($rmx -ne 49) { $rc = 11 }
    }
    'mini' {
        $t0 = Get-Date
        & "$repo\tools\CORPUS\scripts\run_mini.ps1" -Out $mini -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_mini.log"
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
        & "$repo\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Wd $wd -Bin $bin -ProbeBuild $pb *> "$art\run_full.log"
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
    'cmp' {
        # Сводка против ОБЪЯВЛЕННЫХ чисел базы rev32.
        & python 'D:\BqMoni_Claude\p141\cmp_declared.py' "--art=$art" *> "$art\cmp_declared.txt"
        $c1 = $LASTEXITCODE
        "[cmp] cmp_declared code=$c1" | Out-File -Append -Encoding UTF8 $log
        if ($c1 -ne 0) { $rc = 14 }
        # Лестница rev32 -> p141: плечо ОДНО — разбор (склад и корпус те же файлы).
        foreach ($pair in @(@('full', $rev32f, $full), @('mini', $rev32m, $mini))) {
            foreach ($part in @('known', 'unknown')) {
                $out = "$art\ladder_$($pair[0])_$part.txt"
                & python $ladder $pair[1] $pair[2] "--part=$part" *> $out
                $cl = $LASTEXITCODE
                "[cmp] ladder $($pair[0]) $part code=$cl" | Out-File -Append -Encoding UTF8 $log
                if ($cl -ne 0 -and $rc -eq 0) { $rc = 14 }
                if ($part -eq 'known' -and $cl -eq 0) {
                    & python $lsum $out --top=10 *> "$art\ladder_summary_$($pair[0]).txt"
                    "[cmp] ladder_summary $($pair[0]) code=$LASTEXITCODE" | Out-File -Append -Encoding UTF8 $log
                }
            }
        }
    }
    default {
        "[$Stage] НЕИЗВЕСТНЫЙ ШАГ" | Out-File -Append -Encoding UTF8 $log
        $rc = 2
    }
}

"[$Stage] end $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') rc=$rc" | Out-File -Append -Encoding UTF8 $log
exit $rc
