# Развёртка порога значимости S57 по всему корпусу.
#
# ⛔⛔ СЦЕНАРИЙ СНЯТ 13.09.2026 (полоса П39) — ОСТАВЛЕН КАК ЗАПИСЬ ТОГО, КАК
#    МЕРИЛАСЬ `S57` (18.08.2026). Оба его плеча мертвы кодом 12 с 01.09.2026:
#    режимы `--lib=peaks` и `--lib=infer` `CorpusFsaProbe` отвергает (глобальное
#    правило Amber — поставочная библиотека на корпусе не используется), и
#    `Run-One` на первом же плече выйдет кодом 12. Ключ `-NoAnchor`
#    (`--no-infer-anchor`) снят вместе с самим ключом пробы: якорь снят из
#    приложения решением Amber 12.09.2026 (`S66`). Запуск ниже отказывает СРАЗУ,
#    не трогая оснастки, — чтобы «мёртвый сценарий» не выглядел как «отказала
#    оснастка».
#
# ⛔ Величина порога «весомого количества» обязана быть ВЫВЕДЕНА замером, а не
# назначена (строка S57). Скрипт гоняет `--lib=infer` с сеткой `--infer-theta=`
# и снимает мерку обеими частями корпуса; крайние точки сетки — `--lib=peaks`
# (нижняя граница: состав как есть) и `--lib=sample` (верхняя: истина из
# манифеста). Читать сводку надо ТРЕМЯ колонками сразу — recall, фантомы,
# невязка, — потому что порог двигает их в разные стороны.
#
# ⚠ Звать через `pwsh -Command`, а НЕ через `pwsh -File`: у `-File` все доводы
# приходят строками, `@(...)` не вычисляется, и список порогов расползается по
# соседним параметрам (первый прогон уехал искать каталог `…\0.2\0.25`).
#
# ⛔ И ХВОСТ `; exit $LASTEXITCODE` ОБЯЗАТЕЛЕН (`T188`, замер 06.09.2026): у
# формы `-Command` свой код возврата — 0/1 по `$?`, а не разряд скрипта. Без
# хвоста сторож видит 1 и слепнет к тому, ЧТО отказало: `run_appwd.ps1`
# различает 64 (лишние аргументы), 65 (склеенный ключ), 2 (протухшая оснастка)
# и собственный код пробы. ⚠ Одного хвоста МАЛО, нужны обе половины: пока
# `Run-One` отдавал разряд через `throw`, хвост не выполнялся вовсе и код
# оставался 1 при любом отказе — измерено обоими плечами. Вторая половина
# стоит на `Run-One` ниже.
#
#   pwsh -Command "& 'tools\CORPUS\scripts\sweep_s57.ps1' -Theta 0.25,0.30,0.35; exit $LASTEXITCODE"
#
param(
    [string] $Repo   = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8',
    [string] $Wd     = 'tools\CORPUS\scripts\wd_s57',
    [string] $Tag    = 's57',
    [double[]] $Theta = @(0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.60)
)

$ErrorActionPreference = 'Stop'
# ⛔ Снят 13.09.2026 (см. шапку): оба плеча развёртки отвергаются пробой кодом 12.
Write-Host '⛔ sweep_s57.ps1 СНЯТ 13.09.2026: режимы --lib=peaks / --lib=infer отвергаются CorpusFsaProbe кодом 12 с 01.09.2026 (правило Amber). Развёртка S57 — история, см. шапку.' -ForegroundColor Red
exit 12
$env:PYTHONIOENCODING = 'utf-8'
$corpus = Join-Path $Repo 'tools\CORPUS\corpus'
$runner = Join-Path $Repo 'tools\CORPUS\scripts\run_appwd.ps1'
$score  = Join-Path $Repo 'tools\pie\score.py'

function Run-One([string] $out, [string[]] $extra) {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    # ⛔ ТОЛЬКО через сторожа оснастки (`T68`, 05.09.2026). До этого дня здесь
    #    стояло `& $probe …` — прямой запуск `<Wd>\CorpusFsaProbe.exe`, и вся
    #    развёртка порогов шла МИМО `T63`: сторож ловил всё, кроме этого
    #    автоматического потребителя. `run_appwd.ps1` сам отказывает на
    #    протухшей или несобранной оснастке (код 2, ни файла в `-Out`), а ключи
    #    пробы принимает массивом `-Extra`; оператор вызова `&` — правило `T84`.
    & $runner -Out $out -Wd (Join-Path $Repo $Wd) -Corpus $corpus -Extra (@('--quiet') + $extra) | Out-Null
    # ⛔ РАЗРЯД ОТКАЗА ОТДАЁТСЯ `exit`, А НЕ `throw` (`T188`, 06.09.2026). При
    #    `throw` разряд `run_appwd.ps1` (64/65/2/код пробы) умирал ЗДЕСЬ, ещё до
    #    обёртки: `-Command` отдаёт наружу 0/1 по `$?`, и хвост
    #    `; exit $LASTEXITCODE` из шапки не выполнялся вовсе. `exit` внутри
    #    функции выходит из ВСЕГО скрипта — проверено вторым плечом.
    if ($LASTEXITCODE -ne 0) {
        Write-Host "⛔ прогон вернул $LASTEXITCODE для $out" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $row = @{}
    foreach ($part in @('known', 'unknown')) {
        $text = & python $score --mode=spline --members "--part=$part" "--out-dir=$out" 2>&1 |
                Out-String
        # «итого  81  99%  0  (+55 комнатных)  часть: known»
        $m = [regex]::Match($text, '(?m)^итого\s+(\d+)\s+(\d+)%\s+(\d+)\s+\(\+(\d+)')
        $c = [regex]::Match($text, 'sum chi2/ndf\s+([\d.]+)\s+.*?\s([\d.]+)\s*$')
        $e = [regex]::Match($text, 'model residual .*?([\d.]+)\s*%')
        $row[$part] = [pscustomobject]@{
            Spectra  = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
            Recall   = if ($m.Success) { [int]$m.Groups[2].Value } else { 0 }
            Phantoms = if ($m.Success) { [int]$m.Groups[3].Value } else { 0 }
            Room     = if ($m.Success) { [int]$m.Groups[4].Value } else { 0 }
            Chi2     = if ($c.Success) { [double]$c.Groups[1].Value } else { [double]::NaN }
            Resid    = if ($e.Success) { [double]$e.Groups[1].Value } else { [double]::NaN }
        }
    }
    return $row
}

$results = @()

Write-Host '=== A-сторона: подписи поиска пиков как есть (--lib=peaks) ==='
$results += [pscustomobject]@{ Mode = 'peaks'; Theta = [double]::NaN
                               R = (Run-One (Join-Path $Repo "tools\pie\out_${Tag}_peaks") @('--lib=peaks')) }

foreach ($t in $Theta) {
    # ⚠ Форматировать ЧЕРЕЗ InvariantCulture, а не `-f`: у русской локали `-f`
    # ставит запятую, и каталоги выходили `out_s57_i0,30`.
    $name = $t.ToString('F2', [cultureinfo]::InvariantCulture).Replace('.', '')
    Write-Host "=== вывод состава, порог доли $($t.ToString('P0')) ==="
    $results += [pscustomobject]@{ Mode = 'infer'; Theta = $t
                                   R = (Run-One (Join-Path $Repo "tools\pie\out_${Tag}_i${name}") `
                                                (@('--lib=infer', "--infer-theta=$($t.ToString([cultureinfo]::InvariantCulture))"))) }
}

Write-Host '=== верхняя граница: объявленная проба (--lib=sample) ==='
$results += [pscustomobject]@{ Mode = 'sample'; Theta = [double]::NaN
                               R = (Run-One (Join-Path $Repo "tools\pie\out_${Tag}_sample") @('--lib=sample')) }

Write-Host ''
Write-Host ('{0,-10} {1,-6} | {2,-28} | {3,-28}' -f 'режим', 'порог', 'ПОНЯТНАЯ (81)', 'НЕПОНЯТНАЯ (40)')
Write-Host ('{0,-10} {1,-6} | {2,6} {3,7} {4,6} {5,6} | {6,6} {7,7} {8,6} {9,6}' -f `
            '', '', 'recall', 'фантом', 'комн', 'невяз', 'recall', 'фантом', 'комн', 'невяз')
foreach ($r in $results) {
    $k = $r.R['known']; $u = $r.R['unknown']
    Write-Host ('{0,-10} {1,-6} | {2,5}% {3,7} {4,6} {5,5}% | {6,5}% {7,7} {8,6} {9,5}%' -f `
                $r.Mode, $(if ([double]::IsNaN($r.Theta)) { '-' } else { $r.Theta.ToString('P0') }),
                $k.Recall, $k.Phantoms, $k.Room, $k.Resid,
                $u.Recall, $u.Phantoms, $u.Room, $u.Resid)
}
