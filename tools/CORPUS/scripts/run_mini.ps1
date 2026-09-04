# Прогон МАЛОЙ БАЗЫ корпуса — постоянного набора для частых замеров (`S136`).
#
#   & 'tools\CORPUS\scripts\run_mini.ps1' -Out tools\pie\out_mini
#   & 'tools\CORPUS\scripts\run_mini.ps1' -Out tools\pie\out_mini_arm -Extra '--pairth=1'
#
# ⛔ ЗВАТЬ ОПЕРАТОРОМ ВЫЗОВА `&` ИЗ ТЕКУЩЕЙ СЕССИИ, а не порождать `pwsh` на файл:
#    массив `-Extra` уходит в порождённый процесс через командную строку и
#    схлопывается в один элемент (`T84`). С ОДНИМ ключом разницы не видно —
#    ломается молча начиная со второго.
#
# ## Зачем эта база вообще
#
# Решение Amber 04.09.2026: «прогоны корпуса по много часов на каждой задаче —
# неправильный путь». Полный корпус гоняем, когда список задач устаканится и
# матрицы перестанут переделываться.
#
# ⛔ **Дорог не прогон, а СЦЕНЫ.** Разбор всех 129 спектров идёт 28 секунд;
# часы съедает пересчёт матриц отклика, и матрица считается НА СЦЕНУ. У полного
# корпуса сцен 44, и сорок из них несут ПО ОДНОМУ спектру каждая — они и
# составляют почти всю цену. Малая база сделана по этому признаку: 59 спектров
# на ПЯТИ сценах, то есть при смене физики пересчитывать надо 5 матриц вместо 44.
#
# Состав и обоснование каждой строки — `corpus/mini.csv`.
#
# ## Чего этот скрипт НЕ делает
#
# Он НЕ считает матрицы. Все пять сцен малой базы уже лежат в складе корпуса, и
# пока версия физики не менялась, прогон берёт их готовыми. Пересчёт — отдельное
# движение (`CorpusMatrixProbe` + `mx_swap.py --store`), и звать его надо явно.
param(
    [string]$Out = 'tools\pie\out_mini',
    [string[]]$Extra = @(),
    [switch]$SkipScore
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$lab = Split-Path -Parent $here
$root = Split-Path -Parent (Split-Path -Parent $lab)
$mini = Join-Path $lab 'corpus\mini.csv'

# ⛔ `-Out` ОБЯЗАН быть абсолютным. Проба запускается ИЗ `wd_app` и относительный
# путь трактует от СВОЕГО каталога: `-Out tools\pie\out_mini` кладёт результат в
# `wd_app\tools\pie\out_mini`, а счёт ищет его в корне дерева и падает
# «не удаётся найти путь». Поймано первым же прогоном 04.09.2026 — прогон при
# этом ПРОШЁЛ и вернул 0, то есть молчаливой эта ошибка не была только потому,
# что следом звался `score.py`.
if (-not [System.IO.Path]::IsPathRooted($Out)) {
    $Out = Join-Path $root $Out
}

if (-not (Test-Path $mini)) {
    Write-Error "нет определения малой базы: $mini"
    exit 2
}

# Строки-комментарии начинаются с `#`, заголовок — `spectrum,...`.
$rows = Get-Content $mini | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^spectrum,' -and $_.Trim() }
$keys = $rows | ForEach-Object { ($_ -split ',')[0] }
if ($keys.Count -eq 0) { Write-Error 'в mini.csv нет ни одной строки'; exit 2 }

$known = ($rows | Where-Object { ($_ -split ',')[2] -eq 'known' }).Count
$unknown = ($rows | Where-Object { ($_ -split ',')[2] -eq 'unknown' }).Count
$scenes = ($rows | ForEach-Object { ($_ -split ',')[3] } | Where-Object { $_ } | Sort-Object -Unique)

Write-Output '=== МАЛАЯ БАЗА ==='
Write-Output ("  спектров {0} (понятных {1}, непонятных {2}), сцен {3}: {4}" -f `
    $keys.Count, $known, $unknown, $scenes.Count, ($scenes -join ', '))
Write-Output '  ⛔ числа частей НЕ складывать; с полным корпусом НЕ сравнивать'
Write-Output ''

$sw = [Diagnostics.Stopwatch]::StartNew()
$argv = @("--only=$($keys -join ',')") + $Extra
& (Join-Path $here 'run_appwd.ps1') -Out $Out -Extra $argv
$code = $LASTEXITCODE
$spent = $sw.Elapsed.TotalSeconds

if ($code -ne 0) {
    Write-Output ("⛔ прогон отказал, код {0}; счёт не считается" -f $code)
    exit $code
}

Write-Output ''
Write-Output ("ПРОГОН МАЛОЙ БАЗЫ: {0:N1} с" -f $spent)

if (-not $SkipScore) {
    foreach ($part in @('known', 'unknown')) {
        Write-Output ''
        Write-Output ("=== score.py --part=$part ===")
        # ⚠ `--members` ОБЯЗАТЕЛЕН: приложение раскладывает спектр на ДОЧЕРНИЕ
        #    нуклиды, а манифест говорит цепочками; без разворота каждый спектр
        #    разом промах и фантом, и счёт врёт вдвое.
        # ⛔ `--only` ОБЯЗАТЕЛЕН ЗДЕСЬ: без него recall считается против всего
        #    манифеста, и спектр, который НЕ ГОНЯЛИ, идёт промахом наравне с
        #    упавшим. Первый прогон малой базы дал так «итого 79, recall 48 %»
        #    на 42 спектрах (`S136`). Список берётся из ТОГО ЖЕ `mini.csv`, что
        #    задаёт прогон, — двум спискам разойтись было бы нечем помешать.
        & python 'tools\pie\score.py' --mode=spline "--out-dir=$Out" "--part=$part" `
            --members "--only=$mini" | Select-Object -Last 10
    }
}

exit 0
