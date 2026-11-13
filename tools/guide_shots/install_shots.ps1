# Установка снятого конвейером (`shoot.ps1`) в docs\img\<язык>\ (`AMBER319`).
#
#   & 'tools\guide_shots\install_shots.ps1' -Shots main,panel-control [-Lang ru,en] [-Work <каталог>]
#   & 'tools\guide_shots\install_shots.ps1' -Shots all -DiffOnly        — только сверка, без установки
#
# До замены каждого файла печатается попиксельная сверка с прежним (`GuideShot.exe --diff`):
# размер, число разных точек, прямоугольники изменившихся областей — это и есть ответ на
# «что изменилось» для журнала полосы. Снимок другого размера НЕ ставится (точки `data-hot`
# страниц гида заданы долями кадра, но кадр другого размера — другая раскладка окна), если
# его нет в `-Resized`: туда — снимки, чьи точки `data-hot` пересчитаны по <снимок>.hot.txt
# съёмки (`AMBER328`, 13.11.2026: `spectrum-bar`, `view-modes` впервые сняты конвейером).
# Снимка, которого в docs\img нет (новый), сверять не с чем — он ставится.
# `-Shots all` — все снимки, что лежат в <Work>\shots\<язык>\ под именами снимков гида.
# `-DiffOnly` — сверка без установки; в конце — сводка «снимок: разных точек / размер».
# После установки — `python tools/docs_stamp.py` (версия снимков `IMGV`), иначе браузер
# возьмёт прежний снимок из кэша.
# Коды: 0 — установлено (сверено); 1 — нет снятого файла; 2 — размер не тот.
param(
    [string[]]$Shots = @('main', 'panel-control', 'main-fsa', 'main-fsa-nomatrix'),
    [string[]]$Lang = @('ru', 'en'),
    [string]$Work = 'D:\BqMoni_Claude\guideshot',
    [switch]$DiffOnly,
    [string[]]$Resized = @()
)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$diff = Join-Path $Work 'tools\GuideShot.exe'
$Shots = @($Shots | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Lang = @($Lang | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Resized = @($Resized | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$code = 0
$summary = @()
foreach ($l in $Lang) {
    $names = $Shots
    if ($Shots -contains 'all') {
        $names = @(Get-ChildItem -LiteralPath (Join-Path $Work "shots\$l") -Filter *.png |
                   Where-Object { Test-Path -LiteralPath (Join-Path $repo "docs\img\$l\$($_.Name)") } |
                   ForEach-Object { $_.BaseName })
    }
    foreach ($s in $names) {
        $src = Join-Path $Work "shots\$l\$s.png"
        $dst = Join-Path $repo "docs\img\$l\$s.png"
        if (-not (Test-Path -LiteralPath $src)) { Write-Host "нет снятого: $src" -ForegroundColor Red; $code = 1; continue }
        Write-Host "== $l/$s"
        if (Test-Path -LiteralPath $dst) {
            $out = & $diff "--diff=$dst|$src"
            $rc = $LASTEXITCODE
            $out | ForEach-Object { Write-Host $_ }
            $line = ($out | Where-Object { $_ -match 'разных точек|РАЗМЕР' } | Select-Object -First 1)
            if ($rc -eq 2) {
                $sizes = ($out | Where-Object { $_ -match '^(было|стало)' } | ForEach-Object { ($_ -split ':')[0] -replace '\s+', ' ' }) -join ' → '
                $line = "размер другой: $sizes"
            }
            $summary += ("{0}/{1}: {2}" -f $l, $s, $line)
            if ($rc -eq 2 -and $Resized -notcontains $s) { if (-not $DiffOnly) { Write-Host "размер не тот — $l/$s не установлен" -ForegroundColor Red; $code = 2 }; continue }
            if ($rc -eq 2) { Write-Host "размер другой, разрешено -Resized: точки data-hot — по $Work\shots\$l\$s.hot.txt" -ForegroundColor Yellow }
        }
        if (-not $DiffOnly) {
            Copy-Item -LiteralPath $src -Destination $dst -Force
            Write-Host "установлен: docs\img\$l\$s.png"
        }
    }
}
Write-Host '== сводка'
$summary | ForEach-Object { Write-Host $_ }
exit $code
