# Развёртка порога значимости S57 по всему корпусу.
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
#   pwsh -Command "& 'tools\CORPUS\scripts\sweep_s57.ps1' -Theta 0.25,0.30,0.35"
#
param(
    [string] $Repo   = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8',
    [string] $Wd     = 'tools\CORPUS\scripts\wd_s57',
    [string] $Tag    = 's57',
    [double[]] $Theta = @(0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.60),
    [switch] $NoAnchor
)

$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$corpus = Join-Path $Repo 'tools\CORPUS\corpus'
$probe  = Join-Path $Repo "$Wd\CorpusFsaProbe.exe"
$score  = Join-Path $Repo 'tools\pie\score.py'

function Run-One([string] $out, [string[]] $extra) {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Push-Location (Join-Path $Repo $Wd)
    try {
        & $probe "--corpus=$corpus" "--out=$out" --quiet @extra | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "проба вернула $LASTEXITCODE для $out" }
    } finally { Pop-Location }

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
$anchorArgs = if ($NoAnchor) { @('--no-infer-anchor') } else { @() }
$suffix     = if ($NoAnchor) { '_noanchor' } else { '' }

Write-Host '=== A-сторона: подписи поиска пиков как есть (--lib=peaks) ==='
$results += [pscustomobject]@{ Mode = 'peaks'; Theta = [double]::NaN
                               R = (Run-One (Join-Path $Repo "tools\pie\out_${Tag}_peaks") @('--lib=peaks')) }

foreach ($t in $Theta) {
    # ⚠ Форматировать ЧЕРЕЗ InvariantCulture, а не `-f`: у русской локали `-f`
    # ставит запятую, и каталоги выходили `out_s57_i0,30`.
    $name = $t.ToString('F2', [cultureinfo]::InvariantCulture).Replace('.', '')
    Write-Host "=== вывод состава, порог доли $($t.ToString('P0')) ==="
    $results += [pscustomobject]@{ Mode = 'infer'; Theta = $t
                                   R = (Run-One (Join-Path $Repo "tools\pie\out_${Tag}_i${name}${suffix}") `
                                                (@('--lib=infer', "--infer-theta=$($t.ToString([cultureinfo]::InvariantCulture))") + $anchorArgs)) }
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
