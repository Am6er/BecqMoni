# Оснастка корпусного прогона: ЕДИНЫЙ план «что откуда кладётся» — и сторож,
# который этот же план проверяет. Один файл на обе роли, и это нарочно.
#
# ⛔ Зачем он есть (`T63`, 25.08.2026). Каталог `wd_app` держит СВОЮ копию
#    приложения, проб, поставочного конфига, приборов корпуса и матриц отклика.
#    Прогон берёт ЭТУ копию, а не сборку. Измерено в день заведения строки:
#    в оснастке лежал `BecquerelMonitor.exe` от 25.08 12:56:08, а приложение
#    было пересобрано в 13:10:30 — разные sha256, ни ошибки, ни предупреждения.
#    Прогон бы состоялся и дал правдоподобные, но ЧУЖИЕ числа. Это класс
#    `B20`/`B21`, каждый из которых стоил корпусу недель.
#
# ⛔ Урок `T61`: сторож обязан спрашивать ТОТ ЖЕ код, что кладёт файлы. Поэтому
#    список пар «источник → место в оснастке» здесь ОДИН — `Get-AppWdPlan`.
#    `mk_appwd.ps1` копирует по нему (`Invoke-AppWdPlan`), сторож по нему же
#    сверяет (`Test-AppWdPlan`). Второго списка «что кому положено» не бывает:
#    именно такой список устарел молча в самом `mk_appwd.ps1` 19.08.2026
#    (`ProbeDeviceConfig.cs` завели, вписать забыли).
#
# ⛔ ПОЧЕМУ SHA-256, А НЕ ВРЕМЯ ПРАВКИ — измерено 25.08.2026, а не выведено:
#    * `Copy-Item` СОХРАНЯЕТ `LastWriteTime`: у источника и у копии 01.08.2026
#      01:02:03 — до секунды. Значит время файла в оснастке это время ИСХОДНОГО
#      файла, а не время копирования, и «когда оснастку собирали» по нему не
#      узнать вовсе.
#    * Подмена содержимого при равном времени временем НЕ ВИДНА: тот же опыт —
#      время равно, sha256 разный. Так выглядит откат сборки из бэкапа,
#      распаковка архива, `robocopy /COPY:DAT`, ручное копирование чужого exe.
#    * Обратный случай хуже: старая сборка, положенная в `bin` поверх новой,
#      имеет СТАРОЕ время — копия в оснастке оказывается НОВЕЕ источника, и по
#      времени всё «свежо», а содержимое чужое.
#    * Цена точного ответа: sha256 всей оснастки (247 файлов, 103.5 МБ) — 0.30 с.
#      Экономить на этом нечего.
#    Время правки применяется РОВНО там, где хэш бессмыслен: `.cs` против `.exe`
#    (сборка старше исходников, `T41`) — там сравнивать содержимое не с чем.
#
# ⛔ ЧЕТЫРЕ ЩЕЛИ, ЗАКРЫТЫЕ 26.08.2026 ПО ВСТРЕЧНОЙ ПРОВЕРКЕ (`T63`, `T66`).
#    Каждая была измерена опытом на стенде, каждая закрыта опытом же:
#    1. `CorpusFsaProbe.exe` СВЕРЯЕТСЯ ПО СОДЕРЖИМОМУ, как все прочие пробы.
#       Прежде она стояла в `KeepExe` с доводом «собирается на месте,
#       источника-двойника нет» — довод был ЛОЖНЫЙ: `build_all.ps1` собирает её
#       наравне с остальными, и `probes\build\CorpusFsaProbe.exe` существует.
#       Опыт: подмена содержимого при восстановленном `LastWriteTime` давала
#       «ОСНАСТКА СВЕЖАЯ», код 0 — у ЕДИНСТВЕННОГО двоичного файла, который и
#       считает корпус. Отдельной сборки `csc` в `mk_appwd.ps1` больше нет: две
#       сборки одной пробы — это две копии правила «чем её собирать».
#    2. ПРОМАХ ПО `-ProbeBuild` — ОТКАЗ, а не тишина. Прежде шаг 3 был обёрнут
#       в `if (Test-Path …)` без единого слова: план ужимался, и `mk_appwd.ps1`
#       сносил выносом сирот (ныне `Remove-AppWdExtra`) пробы из оснастки, после чего
#       сторож печатал «свежая», код 0. На стенде — 2 пробы из 3 удалены молча
#       с зелёным вердиктом; на настоящей оснастке это 74 пробы.
#    3. `Get-ChildItem` ВЕЗДЕ С `-Force`. Без него сторож не видит СКРЫТЫХ
#       файлов: опыт — скрытая лишняя `.rmx` в складе матриц давала НОЛЬ находок,
#       та же `.rmx` без атрибута — одну. То есть случай `B6` (два GUID =
#       модальное окно = зависание безоконного прогона) проходил насквозь.
#    4. ЧИСЛО ЗАПИСЕЙ БИБЛИОТЕКИ СПРАШИВАЕТ СТОРОЖ (`Test-AppWdLibrary`), а не
#       только сборщик. Прежде проверка жила в `mk_appwd.ps1`, а прогон идёт
#       через `run_appwd.ps1` → `Invoke-AppWdGuard`, который сверяет sha256 и
#       чисел не смотрит: выродись сам ИСТОЧНИК — копия совпала бы с ним
#       побайтно, и прогон пошёл бы по 4-записной заготовке. Опыт: так и было,
#       `run_appwd.ps1` запускал пробу с кодом 0.
#
# ⛔ Пятая щель того же захода — в `tools/check_registry.py` (храповик двух
#    копий `config/` был слеп к УДАЛЕНИЮ файла с диска). Она чинится там же.
#
# ⛔ ТРИ ЩЕЛИ, ЗАКРЫТЫЕ 27.08.2026 ПО ВСТРЕЧНОЙ ПРОВЕРКЕ (`T80`). Каждая была
#    измерена на синтетическом стенде в `%TEMP%` (своя сборка, свои пробы, своя
#    оснастка; настоящая `wd_app` только читалась), опыт ДО и опыт ПОСЛЕ:
#    1. ОТМЕТКА `.appwd.json` СТАЛА ОБЯЗАТЕЛЬНОЙ (`Test-AppWdStamp`). До этого
#       `mk_appwd.ps1` обещал в шапке, что без отметки оснастку не примут, —
#       и обещание было ЛОЖНЫМ: опыт «собрать оснастку (код 0) -> удалить
#       `.appwd.json`» давал `check_appwd.ps1` код 0 со словами «ОСНАСТКА
#       СВЕЖАЯ», а `run_appwd.ps1` ЗАПУСКАЛ ПРОБУ. Второй половиной той же щели
#       была отметка от УДАЧНОЙ прошлой сборки, переживавшая неудачную
#       пересборку: теперь `mk_appwd.ps1` снимает её ДО первого копирования.
#       Сторож при сборке зовётся с `-Building` — там отметки ещё нет по замыслу.
#    2. ЛИШНЕЕ ИЩЕТСЯ ПО ВСЕЙ ОСНАСТКЕ, а не в двух каталогах. Прежде посторонний
#       файл ловился только среди `config\device\*.xml`, `…\response\*.rmx` и
#       `*.exe` в корне. Опыт, каждый раз с чистой оснастки (код 0): чужая
#       `Ghost.dll` в корне — код 0, находок 0; чужая `ghostdb.sqlite` — 0/0;
#       `ru\Ghost.resources.dll` — 0/0; `config\NuclideDefinition.OLD.xml` —
#       0/0. Теперь обход один (`Get-AppWdExtra`) и разметка одна на двоих:
#       сторож по ней отказывает, `mk_appwd.ps1` по ней же выносит.
#    3. СИРОТА-exe — ОТКАЗ, А НЕ ЖЁЛТАЯ СТРОЧКА. `Test-AppWdPlan` клал их в
#       отдельный список, `Invoke-AppWdGuard` печатал и в находки НЕ ДОБАВЛЯЛ:
#       опыт — `PeakFinderProbe.exe` в корне оснастки, вердикт «свежая», код 0.
#       Это ровно «признак отказа без читателя»; в настоящей оснастке такой
#       `PeakFinderProbe.exe` от 17.08 пролежал восемь дней.
#    ⚠ Отказ вызывает не всякий файл вне плана, а только ЗАГРУЖАЕМЫЙ
#      (`$script:AppWdLoadableExt`). Продукты прогонов — `.png`, `.csv` и прочее,
#      что проба пишет себе под ноги, — перечисляются строкой и счёт не портят:
#      в настоящей оснастке такие лежат (`fsa_app.png`, `stack_s9ui.png`), и
#      отказ на них означал бы сторожа, который всегда отказывает.
#
# Пользуются этим файлом: `mk_appwd.ps1` (собирает), `check_appwd.ps1` (сторож
# отдельной командой), `run_appwd.ps1` (сторож + запуск пробы — ЧИТАТЕЛЬ отказа),
# `tools\effmaker\probes\build_all.ps1` (перебор исходников проб, план с ключом
# `-ProbeCatalog` и те же сверки — для каталога проб, а не оснастки корпуса).

$script:AppWdFlatMasks = @('BecquerelMonitor.exe', 'BecquerelMonitor.exe.config',
                           'BecquerelMonitor.pdb', '*.dll', '*.sqlite')
$script:AppWdDirs      = @('runtimes', 'ru')

# Расширения, которые в оснастке ЗАГРУЖАЮТСЯ — приложением, пробой или их
# зависимостями. Посторонний файл такого рода меняет то, что считает прогон:
# лишняя `.dll` рядом с exe перебивает поставочную сборку, лишняя `.sqlite` —
# справочные данные, лишний `.xml` в `config\` — библиотеку и приборы, лишний
# `.rmx` — матрицу отклика. Потому ОТКАЗ, а не заметка.
# Всё остальное — то, что проба пишет себе под ноги (`.png`, `.csv`, …):
# на счёт не влияет и перечисляется строкой.
$script:AppWdLoadableExt = @('.exe', '.dll', '.config', '.sqlite', '.xml', '.rmx')

# ⛔ А ОТКАЗ вызывает только ИСПОЛНЯЕМОЕ — решение Amber 27.08.2026 (`T88`);
# довод при `Deny` в `Get-AppWdExtra`. Список нарочно УЖЕ загружаемого: `.exe` и
# `.dll` подменяют КОД, остальное — данные, и на них сторож лишь предупреждает.
$script:AppWdExecutableExt = @('.exe', '.dll')

$script:AppWdStampName   = '.appwd.json'

# Ниже этого числа записей `config\NuclideDefinition.xml` — не библиотека.
# 4 записи пишет САМО приложение, когда файла нет
# (`NuclideDefinitionManager.InitializeNuclideDefinitionFile`), поставочная
# библиотека на 25.08.2026 несёт 152. Порог стоит между ними с большим запасом
# в обе стороны: состав библиотеки задаёт и поиск пиков, и разбор FSA.
$script:AppWdNuclideMin = 100

# ⛔ ИСХОДНИКИ ПРОБ — ОДИН ПЕРЕБОР НА ДВОИХ (`T89`, остаток `T83`; 05.09.2026).
#    Тот же набор `.cs` нужен `build_all.ps1` (чем компилировать) и плану (чем
#    отсеять exe без исходника и чем судить о свежести сборки, `Test-AppWdBuild`).
#    До 05.09.2026 перебор стоял в ОБОИХ файлах, и расхождение ловилось только
#    сверкой множеств — то есть ПОСЛЕ того, как один из двух перестанет видеть
#    пробу. Теперь перебор один и живёт здесь; сборщик зовёт его ДО компиляции
#    (план тогда ещё не строится — ему нужны собранные пробы), план — при
#    построении. Сверка множеств у сборщика осталась, но ловит она теперь
#    другое: пробу, ПОЯВИВШУЮСЯ в дереве между компиляцией и планом (соседняя
#    полоса завела `.cs` во время сборки), и возврат второго перебора.
#    Ключи `-File -Force` — часть правила: без `-Force` скрытый `.cs` попадал бы
#    в план и не попадал в сборку.
function Get-AppWdProbeSources {
    param([Parameter(Mandatory)][string]$Repo)
    @(Get-ChildItem (Join-Path $Repo 'tools\effmaker\*.cs') -File -Force -ErrorAction SilentlyContinue) +
    @(Get-ChildItem (Join-Path $Repo 'tools\effmaker\probes\*.cs') -File -Force -ErrorAction SilentlyContinue)
}

# ═══════════════════════════════════════════════════════════════════════════
# ⛔ ОТПЕЧАТОК НАБОРА ИСХОДНИКОВ (`T226` и слитые в неё `T129`, `T165`, `T182`,
#    `T194`; обратная сторона — `T233`). РЕШЕНИЕ Amber 05.09.2026.
#
# ЧТО БЫЛО. Свежесть судилась ВРЕМЕНЕМ ПРАВКИ и по КРАЙНИМ файлам: самый новый
# `.cs` всего каталога `BecquerelMonitor\` против `BecquerelMonitor.exe`, самый
# новый `.cs` проб против САМОЙ СТАРОЙ пробы. Отсюда весь куст из пяти строк:
#   * сосед кладёт НОВУЮ пробу — самый новый `.cs` дерева новее самой старой
#     `.exe`, и сторож красен у ВСЕХ полос сразу (`T165`, `T182`);
#   * все пробы до одной сходятся со своими исходниками, но времена сборок
#     перемежаются — сторож всё равно красен (`T182`: «самый новый против
#     самого старого»);
#   * файл ТРОНУТ без изменения содержимого (`git checkout`, пересохранение,
#     `Copy-Item`, генератор, переписавший тот же текст) — сторож красен, хотя
#     в сборке ровно те же байты (`T194`: «красен через минуты после любой
#     сборки», `T226`: четыре отказа подряд);
#   * и ЗЕРКАЛЬНО (`T233`): содержимое подменено с сохранением `LastWriteTime`
#     (`Copy-Item` его переносит) — сторож ЗЕЛЁН, а сборка чужая.
#
# ЧТО СТАЛО. Отметка `.appwd.json` несёт ОТПЕЧАТОК НАБОРА ИСХОДНИКОВ: карту
# «относительный путь -> sha256 содержимого» и её свёртку. Судится СОДЕРЖИМОЕ
# против записанного, а набор берётся ТОТ, ИЗ КОТОРОГО СОБРАН ДАННЫЙ ДВОИЧНЫЙ
# ФАЙЛ, а не «всё дерево»:
#   * `BecquerelMonitor.exe` — список `<Compile Include>` из `.csproj` плюс сам
#     `.csproj`. Не обход каталога: `.cs`, не вписанный в проект, в exe не
#     входит вовсе, и краснеть на нём — ложь;
#   * `<проба>.exe` — её собственный `.cs` ПЛЮС ДОВЕСКИ (файлы без `Main`,
#     которые `build_all.ps1` кладёт КАЖДОЙ пробе). Правка чужой пробы этот
#     набор не трогает, и новая чужая проба — тем более.
#
# ⛔ СТОРОЖ СТАЛ ТОЧНЕЕ, А НЕ ТЕРПИМЕЕ — это доказывается включениями, а не
#    надеждой. Прежнее «самый новый `.cs` дерева новее exe» влечёт новое
#    «изменившийся файл СВОЕГО набора новее своего exe» ВСЕГДА, кроме трёх
#    случаев, и в каждом из трёх двоичный файл содержит ровно те байты, что и
#    дерево: (1) файл не компилируется в этот двоичный файл вовсе; (2) файл
#    тронут без правки содержимого; (3) файл правился и был возвращён обратно.
#    Ни один справедливый отказ (`B20`/`B21`, `T41`) не ушёл, а два новых
#    прибавились: подмена содержимого при сохранённом времени (`T233`) и
#    расхождение набора при неизменном exe.
#
# Время правки осталось ровно там, где сравнивать содержимое не с чем: запись
# отсутствует (каталог проб ещё не заверен) — работает прежнее правило `T41`,
# но ПОФАЙЛОВО и по своему набору, а не по крайним датам всего дерева.
# ⛔ Отсутствие записи делает сторожа СТРОЖЕ, а не мягче: удалить отметку,
#    чтобы «пройти», нельзя — запасное правило отказывает чаще.
$script:AppWdSrcAlgo = 'sha256(путь+содержимое)/v1'

# Правило «что такое довесок» — ОДНО НА ДВОИХ (`T57`, `T61`): им пользуется и
# компилятор проб (`build_all.ps1` кладёт довески каждой пробе), и отпечаток
# набора здесь. Второй копии этого образца в дереве быть не должно.
$script:AppWdMainPattern = 'static\s+(int|void)\s+Main\s*\('

# sha256 набора файлов: карта «относительный путь (нижний регистр) -> объект
# с Sha/Time/Full». Хеш считается своим `SHA256`, а не `Get-FileHash`: на 547
# файлах приложения это 0.05 с против 0.56 с, и сторож зовётся каждый прогон.
function Get-AppWdShaMap {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][array]$Files
    )
    $root = $Root.TrimEnd('\') + '\'
    $map  = @{}
    $sha  = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($f in $Files) {
            $full = $f.FullName
            $rel  = if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                        $full.Substring($root.Length)
                    } else { $full }
            $h = [System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($full))).Replace('-', '').ToLowerInvariant()
            $map[$rel.Replace('/', '\').ToLowerInvariant()] = [pscustomobject]@{
                Sha = $h; Time = $f.LastWriteTime; Full = $full; Name = $f.Name
            }
        }
    } finally { $sha.Dispose() }
    $map
}

# Свёртка карты в один отпечаток. ⛔ Сортировка ORDINAL нарочно: культурная
# зависит от локали машины, и отпечаток ОДНОГО набора разошёлся бы на двух
# машинах — сторож начал бы отказывать за перенос дерева.
function Get-AppWdFingerprint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable]$Map,
        # ⚠ Проверка именно на `$null`, а не на истинность: ПУСТОЙ набор — это
        #   набор (довесков может не быть вовсе), и `if ($Only)` на пустом
        #   массиве ложно — отпечаток пустого набора вышел бы отпечатком ВСЕГО.
        [AllowNull()][AllowEmptyCollection()][string[]]$Only = $null
    )
    # ⚠ Присваивание ПРЯМОЕ, а не через `if (…) { … } else { … }`: пустой
    #   массив, вышедший из блока оператора, разворачивается в `$null`, и
    #   `[Array]::Sort` падает «Value cannot be null» (поймано самопроверкой).
    [string[]]$keys = @()
    if ($null -ne $Only) { $keys = [string[]]@($Only) } else { $keys = [string[]]@($Map.Keys) }
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    $lines = foreach ($k in $keys) {
        $v = $Map[$k]
        "{0}`t{1}" -f $k, $(if ($v -is [string]) { $v } else { $v.Sha })
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

# НАБОР ИСХОДНИКОВ ПРИЛОЖЕНИЯ — по `.csproj`, а не обходом каталога.
# ⛔ Разница не умозрительная: `.cs`, заведённый полосой и ещё не вписанный в
#    проект, в `BecquerelMonitor.exe` НЕ ВХОДИТ, а прежний сторож краснел и на
#    нём — ровно «отказ по файлу, от которого двоичный файл не зависит»
#    (`T226`). Сам `.csproj` — часть набора: он и есть правило «что собирается».
# ⚠ Не разобрался проект — это НАХОДКА, а не тишина: набор берётся обходом
#    каталога (прежнее, более грубое правило), и об этом говорится вслух.
function Get-AppWdAppSources {
    param([Parameter(Mandatory)][string]$Repo)

    $proj  = Join-Path $Repo 'BecquerelMonitor\BecquerelMonitor.csproj'
    $appDir = Join-Path $Repo 'BecquerelMonitor'
    $bad   = [System.Collections.Generic.List[string]]::new()
    $loose = [System.Collections.Generic.List[string]]::new()
    $walk  = @(Get-ChildItem -LiteralPath $appDir -Recurse -File -Force -Filter '*.cs' -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

    if (-not (Test-Path -LiteralPath $proj)) {
        $bad.Add("НЕТ ПРОЕКТА $proj — набор исходников приложения взят обходом каталога, сторож судит грубее (T226)")
        return [pscustomobject]@{ Files = @($walk); Loose = @(); Bad = @($bad); From = 'обход каталога' }
    }
    $text = Get-Content -LiteralPath $proj -Raw
    $inc  = @{}
    foreach ($m in [regex]::Matches($text, '<Compile\s+Include="([^"]+)"')) {
        $rel = $m.Groups[1].Value.Replace('/', '\')
        if ($rel.ToLowerInvariant().EndsWith('.cs')) { $inc[$rel.ToLowerInvariant()] = $rel }
    }
    if ($inc.Count -eq 0) {
        $bad.Add("В $proj нет ни одной записи <Compile Include=...cs> — формат проекта сменился; набор взят обходом каталога, сторож судит грубее (T226)")
        return [pscustomobject]@{ Files = @($walk); Loose = @(); Bad = @($bad); From = 'обход каталога' }
    }

    $prefix = $appDir.TrimEnd('\') + '\'
    $onDisk = @{}
    foreach ($w in $walk) {
        if ($w.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $onDisk[$w.FullName.Substring($prefix.Length).ToLowerInvariant()] = $w
        }
    }
    $files = [System.Collections.Generic.List[object]]::new()
    $files.Add((Get-Item -LiteralPath $proj -Force))
    foreach ($k in $inc.Keys) {
        if ($onDisk.ContainsKey($k)) { $files.Add($onDisk[$k]) }
        else { $bad.Add("ПРОЕКТ ССЫЛАЕТСЯ НА ФАЙЛ, КОТОРОГО НЕТ НА ДИСКЕ: BecquerelMonitor\$($inc[$k]) — таким проектом приложение не собирается") }
    }
    foreach ($k in $onDisk.Keys) { if (-not $inc.ContainsKey($k)) { $loose.Add($k) } }

    [pscustomobject]@{ Files = @($files); Loose = @($loose); Bad = @($bad); From = 'BecquerelMonitor.csproj' }
}

# ЗАПИСЬ НАБОРОВ: приложение, пробы, довески и — на каждую пробу — её
# собственный набор (`<проба>.cs` + все довески). Отсюда берут и `Write-AppWdStamp`
# (что записать в отметку), и `Test-AppWdBuild` (с чем сверить дерево).
function Get-AppWdSourceRecord {
    param([Parameter(Mandatory)][string]$Repo)

    $app    = Get-AppWdAppSources -Repo $Repo
    $appMap = Get-AppWdShaMap -Root $Repo -Files $app.Files

    $probeSrc = @(Get-AppWdProbeSources -Repo $Repo)
    $probeMap = Get-AppWdShaMap -Root $Repo -Files $probeSrc

    $compRel = [System.Collections.Generic.List[string]]::new()
    $mainRel = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $probeMap.Keys) {
        if (Select-String -Path $probeMap[$k].Full -Pattern $script:AppWdMainPattern -Quiet) { $mainRel.Add($k) }
        else { $compRel.Add($k) }
    }
    $each = @{}
    foreach ($k in $mainRel) {
        $set = @($k) + @($compRel)
        $each[[System.IO.Path]::GetFileNameWithoutExtension($probeMap[$k].Name).ToLowerInvariant()] = [pscustomobject]@{
            Fp   = (Get-AppWdFingerprint -Map $probeMap -Only $set)
            Rel  = @($set)
            Name = $probeMap[$k].Name
        }
    }

    [pscustomobject]@{
        Algo = $script:AppWdSrcAlgo
        App  = [pscustomobject]@{ Map = $appMap;   Fp = (Get-AppWdFingerprint -Map $appMap);   N = $appMap.Count
                                  Loose = @($app.Loose); From = $app.From }
        Probes = [pscustomobject]@{ Map = $probeMap; Fp = (Get-AppWdFingerprint -Map $probeMap); N = $probeMap.Count }
        Comp = [pscustomobject]@{ Rel = @($compRel); N = $compRel.Count
                                  Fp = (Get-AppWdFingerprint -Map $probeMap -Only @($compRel)) }
        Each = $each
        Bad  = @($app.Bad)
    }
}

# Сверка карты «путь -> sha» с записанной в отметке. Записанная приходит из
# JSON как `PSCustomObject`, поэтому разбирается свойствами, а не ключами.
function Compare-AppWdSourceMap {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][hashtable]$Now,
        [Parameter(Mandatory)][AllowNull()]$Was
    )
    $changed = [System.Collections.Generic.List[string]]::new()
    $added   = [System.Collections.Generic.List[string]]::new()
    $removed = [System.Collections.Generic.List[string]]::new()
    $wasMap  = @{}
    if ($Was) {
        foreach ($p in $Was.PSObject.Properties) { $wasMap[$p.Name] = [string]$p.Value }
    }
    foreach ($k in $Now.Keys) {
        if (-not $wasMap.ContainsKey($k)) { $added.Add($k) }
        elseif ($wasMap[$k] -ne $Now[$k].Sha) { $changed.Add($k) }
    }
    foreach ($k in $wasMap.Keys) { if (-not $Now.ContainsKey($k)) { $removed.Add($k) } }
    [pscustomobject]@{ Changed = @($changed); Added = @($added); Removed = @($removed) }
}

function Get-AppWdSha256 {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Записанные наборы из отметки каталога — либо `$null`, если отметки нет, она
# не разбирается или писана ДРУГИМ образцом отпечатка. ⛔ Смена образца делает
# запись негодной, а не «совместимой»: сравнивать отпечатки разных правил —
# это молча сравнивать разное.
function Get-AppWdStampSources {
    param([Parameter(Mandatory)][string]$Wd)
    $st = Read-AppWdStamp -Wd $Wd
    if (-not $st) { return $null }
    if (-not $st.PSObject.Properties['sources']) { return $null }
    $s = $st.sources
    if (-not $s -or -not $s.PSObject.Properties['algo'] -or $s.algo -ne $script:AppWdSrcAlgo) { return $null }
    $s
}

function Get-AppWdPlan {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Bin,
        [Parameter(Mandatory)][string]$Wd,
        # Откуда брать пробы. По умолчанию `probes\build` — каталог отладочной
        # сборки. Оптимизированный рецепт (CLAUDE.md, «Computing») кладёт пробы
        # в `probes\build_rel`, и оснастку из `bin\Release_Codex` надо собирать
        # оттуда же: иначе приложение будет из одной сборки, а пробы рядом —
        # из другой, и сторож законно откажет.
        [string]$ProbeBuild = '',
        # `S138`: склад матриц ПЛЕЧА. Пустой — штатный `corpus\geometries`.
        # Задаётся каталогом со сценами (`<ключ>.rmx`); оснастка берёт
        # матрицы из его `response`, и сторож сверяет клейма с НИМ ЖЕ.
        # ⛔ Без этого ключа плечо со своим складом можно было прогнать
        # только `-Force`, а он объявляет числа негодными для журнала —
        # то есть плечо было неизмеримо в принципе.
        [string]$Store = '',
        # `T149`: план для КАТАЛОГА ПРОБ (`build_all.ps1`), а не для оснастки
        # корпуса. Отличие одно — поставочные `config\device\*.xml` и
        # `config\ROI\*.xml` кладутся ТОЖЕ (шаг 4). Оснастке корпуса их класть
        # НЕЛЬЗЯ: у неё `config\device` строится из приборов КОРПУСА целиком, а
        # поставочный `AtomSpectraVCP.xml` несёт ТОТ ЖЕ GUID, что корпусный
        # `1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml` (сверено 05.09.2026)
        # — два GUID = модальное окно = зависание безоконного прогона (`B6`).
        # Поэтому ключ, а не общее правило.
        [switch]$ProbeCatalog
    )

    $corpus     = Join-Path $Repo 'tools\CORPUS\corpus'
    $storeDir   = if ($Store) { $Store } else { Join-Path $corpus 'geometries' }
    $response   = Join-Path $storeDir 'response'
    if (-not $ProbeBuild) { $ProbeBuild = Join-Path $Repo 'tools\effmaker\probes\build' }
    $probeBuild = $ProbeBuild
    $appCfgSrc  = Join-Path $Bin  'BecquerelMonitor.exe.config'
    $pairs      = [System.Collections.Generic.List[object]]::new()

    # 1. Сборка приложения: exe, конфиг, pdb, библиотеки, три базы.
    foreach ($mask in $script:AppWdFlatMasks) {
        Get-ChildItem (Join-Path $Bin $mask) -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $Wd $_.Name); Why = 'сборка' })
        }
    }

    # 2. Нативные провайдеры и русский сателлит — рекурсивно, пофайлово.
    #    Пофайлово нарочно: `Copy-Item <кат> <куда> -Recurse` ведёт себя
    #    по-разному в зависимости от того, есть ли уже такой каталог, и сверять
    #    результат такого копирования нечем.
    foreach ($dir in $script:AppWdDirs) {
        $src = Join-Path $Bin $dir
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $prefix = (Resolve-Path -LiteralPath $src).Path
        Get-ChildItem -LiteralPath $src -Recurse -File -Force | ForEach-Object {
            $rel = $_.FullName.Substring($prefix.Length).TrimStart('\')
            $pairs.Add([pscustomobject]@{
                Src = $_.FullName
                Dst = (Join-Path (Join-Path $Wd $dir) $rel)
                Why = "сборка\$dir"
            })
        }
    }

    # 3. Пробы — СВЕЖИМИ из каталога проб, ВСЕ, включая саму `CorpusFsaProbe`.
    #    Каждой нужен свой exe.config, иначе binding redirect SQLitePCLRaw не
    #    применяется и первое же чтение базы падает.
    #
    # ⛔ Промах по каталогу — ОТКАЗ, а не тишина (щель 2 выше). План строится
    #    ДО единого копирования и ДО `Remove-AppWdExtra`, поэтому отказ здесь
    #    оставляет оснастку нетронутой.
    if (-not (Test-Path -LiteralPath $probeBuild)) {
        throw ("НЕТ КАТАЛОГА ПРОБ: $probeBuild`n" +
               "   Отладочный рецепт: pwsh tools\effmaker\probes\build_all.ps1`n" +
               "   Оптимизированный (CLAUDE.md): build_all.ps1 -Out tools\effmaker\probes\build_rel")
    }
    if (-not (Test-Path -LiteralPath $appCfgSrc)) {
        throw ("НЕТ $appCfgSrc — без него пробам не с чего класть exe.config (T32)")
    }
    # Исходники проб: `tools\effmaker\*.cs` (харнесс) плюс `tools\effmaker\probes\*.cs`.
    # Ровно этот набор собирает `build_all.ps1`, и здесь он нужен дважды —
    # отсеять из каталога exe БЕЗ ИСХОДНИКА и назвать самый свежий `.cs`
    # (`Test-AppWdBuild`). Поэтому он считается ОДИН раз и живёт в плане.
    # ⛔ Сам перебор — в `Get-AppWdProbeSources`, и оттуда же его берёт
    #    сборщик проб (`T89`): двух переборов одного набора не бывает.
    $probeSrc = @(Get-AppWdProbeSources -Repo $Repo)
    $srcNames = @{}
    foreach ($f in $probeSrc) { $srcNames[$f.BaseName.ToLowerInvariant()] = $true }

    $allExe = @(Get-ChildItem (Join-Path $probeBuild '*.exe') -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne 'BecquerelMonitor.exe' })
    # exe, у которого в дереве нет `.cs`, — не проба, а мусор прошлых заходов
    # (в `probes\build` такие лежат: `<guid>_CorpusMatrixProbe.exe` от 09–17.08).
    # В оснастку он не едет по той же причине, по какой `Remove-AppWdExtra`
    # выносит его ИЗ оснастки: запустить его можно, и он покажет разбор,
    # которого в коде нет.
    $probeExe = @($allExe | Where-Object { $srcNames.ContainsKey($_.BaseName.ToLowerInvariant()) })
    $strayExe = @($allExe | Where-Object { -not $srcNames.ContainsKey($_.BaseName.ToLowerInvariant()) })
    if ($probeExe.Count -eq 0) {
        throw ("В $probeBuild НЕТ НИ ОДНОЙ СОБРАННОЙ ПРОБЫ — соберите: build_all.ps1 -Out '$probeBuild'")
    }
    if (@($probeExe | ForEach-Object { $_.Name }) -notcontains 'CorpusFsaProbe.exe') {
        throw ("В $probeBuild НЕТ CorpusFsaProbe.exe — а корпус считает именно она.`n" +
               "   Соберите пробы: pwsh tools\effmaker\probes\build_all.ps1 -Out '$probeBuild'")
    }
    foreach ($e in $probeExe) {
        $pairs.Add([pscustomobject]@{ Src = $e.FullName; Dst = (Join-Path $Wd $e.Name); Why = 'проба' })
        $pairs.Add([pscustomobject]@{ Src = $appCfgSrc;  Dst = (Join-Path $Wd ($e.Name + '.config')); Why = 'exe.config пробы' })
    }

    # 4. Конфиг — ПОСТАВОЧНЫЙ, а не сгенерированный `mkconfig.py`: в `wd_<группа>`
    #    лежат сеты-обманки `[decoy]` под изучение гейта, и разбор по ним мерит
    #    не тот состав библиотеки.
    foreach ($n in @('NuclideDefinition.xml', 'BecquerelMonitor.xml')) {
        $pairs.Add([pscustomobject]@{
            Src = (Join-Path $Repo "BecquerelMonitor\config\$n")
            Dst = (Join-Path $Wd   "config\$n")
            Why = 'поставочный конфиг'
        })
    }
    # 4а. ТОЛЬКО каталогу проб (`T149`, 05.09.2026): поставочные конфигурации
    #     приборов и ROI. Без каталога `config\device` `DeviceConfigManager`
    #     в безоконном прогоне бросает `InvalidOperationException` (проба
    #     падает необработанным исключением, код −532462766), без `config\ROI`
    #     `ROIConfigManager` пишет отказ и грузит НОЛЬ конфигураций — измерено
    #     05.09.2026 на свежем каталоге: `FsaStampProbe` упала, `RoiLoadProbe`
    #     и `RoiSupplyProbe` вернули 2 «нет каталога». Каталоги нарочно
    #     заводятся С СОДЕРЖИМЫМ, а не пустыми: пустой `config\ROI` — та самая
    #     заготовка, из-за которой завели `S100`. Источник — `BecquerelMonitor\config\`,
    #     тот же, что у шага 4 (в `$Bin\config\device` csproj кладёт ОДИН прибор
    #     из девяти). Дублей GUID среди девяти поставочных приборов нет
    #     (сверено 05.09.2026).
    if ($ProbeCatalog) {
        foreach ($sub in @('device', 'ROI')) {
            Get-ChildItem (Join-Path $Repo "BecquerelMonitor\config\$sub\*.xml") -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
                $pairs.Add([pscustomobject]@{
                    Src = $_.FullName
                    Dst = (Join-Path (Join-Path $Wd "config\$sub") $_.Name)
                    Why = "поставочный конфиг\$sub"
                })
            }
        }
    }

    # 5. Конфигурации приборов корпуса и матрицы отклика. `ResponseMatrixStore`
    #    ищет матрицу в `config\device\response` рабочего каталога, а кладёт их
    #    в `corpus\geometries\response` проба `CorpusEffProbe`.
    $devDir = Join-Path $Wd 'config\device'
    $rspDir = Join-Path $Wd 'config\device\response'
    Get-ChildItem (Join-Path $corpus 'devices\*.xml') -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $devDir $_.Name); Why = 'прибор корпуса' })
    }
    Get-ChildItem (Join-Path $response '*.rmx') -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $rspDir $_.Name); Why = 'матрица отклика' })
    }

    [pscustomobject]@{
        Repo = $Repo; Bin = $Bin; Wd = $Wd
        Corpus = $corpus; Response = $response; ProbeBuild = $probeBuild
        # `S138`: с чем сверять клейма. У штатного прогона — склад корпуса.
        Store = $storeDir
        Pairs = @($pairs)
        ProbeSources = @($probeSrc)
        Strays = @($strayExe)
        # Каталоги, которые строятся из корпуса ЦЕЛИКОМ: лишний файл в них — не
        # безобидный мусор. После переименования конфигурации (`B6`) старая и
        # новая несут ОДИН GUID, и приложение встаёт на модальном окне
        # «Одинаковые GUID» — в безоконном прогоне это выглядит как зависание.
        Exclusive = @(
            [pscustomobject]@{ Dir = $devDir; Mask = '*.xml'; What = 'конфигурации приборов' }
            [pscustomobject]@{ Dir = $rspDir; Mask = '*.rmx'; What = 'матрицы отклика' }
        )
    }
}

# Единственная точка входа для трёх скриптов: построить план либо ОТКАЗАТЬ
# громко, кодом 6. `Get-AppWdPlan` бросает, когда строить план не из чего
# (нет каталога проб, нет `CorpusFsaProbe.exe`, нет `BecquerelMonitor.exe.config`),
# и это НАРОЧНО отказ, а не «план поменьше»: именно ужавшийся план дал
# `Remove-AppWdExtra`-у снести пробы из оснастки при зелёном вердикте сверху.
# `exit` внутри функции завершает вызвавший скрипт — здесь это и требуется.
function New-AppWdPlanOrDie {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Bin,
        [Parameter(Mandatory)][string]$Wd,
        [string]$ProbeBuild = '',
        [string]$Store = '',
        [switch]$ProbeCatalog
    )
    try {
        return Get-AppWdPlan -Repo $Repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild -Store $Store -ProbeCatalog:$ProbeCatalog
    } catch {
        Write-Host ""
        Write-Host "⛔⛔ ОТКАЗ: ПЛАН ОСНАСТКИ НЕ СТРОИТСЯ — НИЧЕГО НЕ ТРОНУТО" -ForegroundColor Red
        foreach ($line in ($_.Exception.Message -split "`n")) { Write-Host ("   " + $line) -ForegroundColor Red }
        Write-Host ""
        exit 6
    }
}

function Invoke-AppWdPlan {
    param([Parameter(Mandatory)]$Plan)

    foreach ($ex in $Plan.Exclusive) {
        New-Item -ItemType Directory -Force $ex.Dir | Out-Null
        # `-Force` у `Remove-Item` снимает и СКРЫТЫЕ файлы — иначе чужая скрытая
        # матрица пережила бы очистку склада (щель 3).
        Remove-Item (Join-Path $ex.Dir $ex.Mask) -Force -ErrorAction SilentlyContinue
    }
    $dirs = $Plan.Pairs | ForEach-Object { Split-Path -Parent $_.Dst } | Sort-Object -Unique
    foreach ($d in $dirs) { New-Item -ItemType Directory -Force $d | Out-Null }
    foreach ($p in $Plan.Pairs) { Copy-Item -LiteralPath $p.Src -Destination $p.Dst -Force }
    $Plan.Pairs.Count
}

# ЧТО ЛЕЖИТ В ОСНАСТКЕ НЕ ПО ПЛАНУ — один обход всего дерева и одна разметка
# на двоих: сторож по ней ОТКАЗЫВАЕТ (`Test-AppWdPlan`), сборщик по ней же
# ВЫНОСИТ (`Remove-AppWdExtra`). Двух списков «что здесь чужое» не бывает —
# ровно на втором списке стояла щель 2: лишнее искалось в двух каталогах и
# среди `*.exe` в корне, а `Ghost.dll`, `ghostdb.sqlite`, `ru\*.resources.dll`
# и `config\*.OLD.xml` проходили насквозь с зелёным вердиктом.
#
# Отметка `.appwd.json` посторонним файлом не считается: её пишет сюда сам
# сборщик, и спрашивает её `Test-AppWdStamp`.
function Get-AppWdExtra {
    param([Parameter(Mandatory)]$Plan)

    $res = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Plan.Wd)) { return @($res) }
    $wd = (Resolve-Path -LiteralPath $Plan.Wd).Path.TrimEnd('\')

    $dstSet = @{}
    foreach ($p in $Plan.Pairs) { $dstSet[$p.Dst.ToLowerInvariant()] = $true }
    # Каталоги, которые строятся из корпуса ЦЕЛИКОМ, названы планом; здесь они
    # нужны только затем, чтобы отказ называл ЦЕНУ лишнего файла именно там.
    $excl = @{}
    foreach ($ex in $Plan.Exclusive) {
        $excl[$ex.Dir.Substring($Plan.Wd.Length).TrimStart('\').ToLowerInvariant()] = $ex.What
    }
    $stampRel = $script:AppWdStampName.ToLowerInvariant()

    Get-ChildItem -LiteralPath $wd -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if ($dstSet.ContainsKey($_.FullName.ToLowerInvariant())) { return }
        $rel = $_.FullName.Substring($wd.Length).TrimStart('\')
        if ($rel.ToLowerInvariant() -eq $stampRel) { return }
        $ext    = $_.Extension.ToLowerInvariant()
        $relDir = (Split-Path -Parent $rel).ToLowerInvariant()
        if ($ext -eq '.exe') {
            $why = 'exe вне плана: запустить его можно, и он покажет разбор, которого в коде нет (так `PeakFinderProbe.exe` от 17.08 пролежал в оснастке восемь дней)'
        } elseif ($excl.ContainsKey($relDir)) {
            $why = ("{0} строятся из корпуса ЦЕЛИКОМ, в корпусе такого файла нет (B6: два GUID = модальное окно = зависание безоконного прогона)" -f $excl[$relDir])
        } else {
            $why = 'файлы этого рода приложение и пробы ЗАГРУЖАЮТ — прогон возьмёт и этот'
        }
        $res.Add([pscustomobject]@{
            File = $_
            Rel  = $rel
            Load = ($script:AppWdLoadableExt -contains $ext)
            # ⛔ РЕШЕНИЕ Amber 27.08.2026 (`T88`), ОДНО НА ТРИ КАТАЛОГА
            # (`probes\build`, `probes\build_rel`, `wd_app`): называть всё
            # постороннее, а ОТКАЗЫВАТЬ только на ИСПОЛНЯЕМОМ. Именно оно молча
            # подменяет то, что мерят: чужая `.dll` рядом с exe перебивает
            # поставочную сборку, чужой `.exe` показывает разбор, которого в
            # коде нет (`PeakFinderProbe.exe` пролежал в оснастке восемь дней).
            # Конфиги, `.sqlite`, `.rmx` и прочее ЗАГРУЖАЕМОЕ — предупреждение и
            # код 0: каталог проб это одновременно выход сборки и рабочий
            # каталог, и отказ на положенном туда руками `config\ROI\*.xml`
            # означал бы сторожа, который отказывает ВСЕГДА.
            # ⛔ Ничего не удаляется: сносить чужое агенту не разрешено.
            Deny = ($script:AppWdExecutableExt -contains $ext)
            Why  = $why
        })
    }
    @($res)
}

# Вынести из оснастки посторонние ЗАГРУЖАЕМЫЕ файлы. Продукты прогонов
# (`.png`, `.csv`, …) не трогаются: их пишет проба, и на счёт они не влияют.
function Remove-AppWdExtra {
    param([Parameter(Mandatory)]$Plan)
    $killed = [System.Collections.Generic.List[string]]::new()
    foreach ($x in (Get-AppWdExtra -Plan $Plan)) {
        if (-not $x.Load) { continue }
        Remove-Item -LiteralPath $x.File.FullName -Force
        $killed.Add($x.Rel)
    }
    @($killed)
}

# Сверка ОСНАСТКИ с источниками — по содержимому.
function Test-AppWdPlan {
    param([Parameter(Mandatory)]$Plan)

    $bad   = [System.Collections.Generic.List[string]]::new()
    $other = [System.Collections.Generic.List[string]]::new()
    $ok    = 0

    if (-not (Test-Path -LiteralPath $Plan.Wd)) {
        $bad.Add("ОСНАСТКИ НЕТ ВОВСЕ: $($Plan.Wd) — сначала pwsh mk_appwd.ps1")
        return [pscustomobject]@{ Bad = @($bad); Other = @(); Ok = 0 }
    }

    foreach ($p in $Plan.Pairs) {
        # Пары шага 4 (поставочный конфиг) названы ПОИМЁННО, а не обходом
        # каталога, — значит источника может не быть вовсе. Без этой проверки
        # `Get-FileHash` валит сторожа исключением вместо внятного отказа.
        if (-not (Test-Path -LiteralPath $p.Src)) {
            $bad.Add(("ПРОПАЛ ИСТОЧНИК [{0}]: {1}" -f $p.Why, $p.Src))
            continue
        }
        if (-not (Test-Path -LiteralPath $p.Dst)) {
            $bad.Add(("НЕТ В ОСНАСТКЕ [{0}]: {1}" -f $p.Why, (Split-Path -Leaf $p.Dst)))
            continue
        }
        $hs = (Get-FileHash -LiteralPath $p.Src -Algorithm SHA256).Hash
        $hd = (Get-FileHash -LiteralPath $p.Dst -Algorithm SHA256).Hash
        if ($hs -ne $hd) {
            $ts = (Get-Item -LiteralPath $p.Src -Force).LastWriteTime.ToString('dd.MM HH:mm:ss')
            $td = (Get-Item -LiteralPath $p.Dst -Force).LastWriteTime.ToString('dd.MM HH:mm:ss')
            $bad.Add(("ПРОТУХЛО [{0}]: {1}" -f $p.Why, (Split-Path -Leaf $p.Dst)) +
                     ("`n           источник {0}  sha {1}" -f $ts, $hs.Substring(0, 12)) +
                     ("`n           оснастка {0}  sha {1}" -f $td, $hd.Substring(0, 12)))
        } else { $ok++ }
    }

    # Посторонние файлы — ОДНИМ обходом всей оснастки (щели 2 и 3, 27.08.2026).
    # Загружаемое — отказ; прочее — продукты прогонов, их только перечисляем.
    foreach ($x in (Get-AppWdExtra -Plan $Plan)) {
        if ($x.Load) {
            $bad.Add(("ЛИШНЕЕ В ОСНАСТКЕ: {0}  {1}`n           {2}" -f $x.Rel,
                      $x.File.LastWriteTime.ToString('dd.MM HH:mm'), $x.Why))
        } else {
            $other.Add($x.Rel)
        }
    }

    [pscustomobject]@{ Bad = @($bad); Other = @($other); Ok = $ok }
}

# ОТМЕТКА О СБОРКЕ (`T80`). Оснастка без отметки — это либо «никогда не
# собиралась», либо «пересборка не прошла самопроверку»: `mk_appwd.ps1` пишет
# отметку последним действием и снимает её ПЕРЕД первым копированием.
# Прежде эта обязательность была только ОБЕЩАНА в шапке `mk_appwd.ps1`; опыт
# 26.08.2026: удалили `.appwd.json` — `check_appwd.ps1` дал код 0 «ОСНАСТКА
# СВЕЖАЯ», `run_appwd.ps1` запустил пробу.
function Test-AppWdStamp {
    param([Parameter(Mandatory)]$Plan)

    $bad = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $Plan.Wd)) {
        # Про отсутствие оснастки целиком кричит `Test-AppWdPlan`.
        return [pscustomobject]@{ Bad = @() }
    }
    $f = Join-Path $Plan.Wd $script:AppWdStampName
    if (-not (Test-Path -LiteralPath $f)) {
        $bad.Add("НЕТ ОТМЕТКИ $($script:AppWdStampName) — оснастку либо не собирали, либо её сборка не прошла самопроверку" +
                 "`n           Соберите: pwsh mk_appwd.ps1")
    } elseif (-not (Read-AppWdStamp -Wd $Plan.Wd)) {
        $bad.Add("ОТМЕТКА $($script:AppWdStampName) НЕ РАЗБИРАЕТСЯ КАК JSON — чем и из чего собрана оснастка, проверить нечем")
    }
    [pscustomobject]@{ Bad = @($bad) }
}

# Библиотека нуклидов ОСНАСТКИ: сколько записей и какая именно (`T66`).
#
# ⛔ Проверка стоит ЗДЕСЬ, а не в `mk_appwd.ps1`, потому что прогон идёт мимо
#    сборщика: `run_appwd.ps1` спрашивает только сторожа. Сверка по sha256 её
#    не заменяет — она сравнивает копию с ИСТОЧНИКОМ, а вырожденный источник
#    даёт вырожденную копию, совпадающую с ним побайтно.
#
# ⛔ Отпечаток печатается КАЖДЫЙ прогон и нарочно: в дереве лежат четыре рода
#    копий `NuclideDefinition.xml` (поставочная 152 записи, корневая `config\`
#    143 без полей `Sets`/`Chain`, `wd_<группа>` от 114 до 278 от `mkconfig.py`,
#    `probes\build` — 4-записная заготовка), и на этом споткнулась `S63`:
#    потолок опознания мерен по КОРНЕВОЙ копии, а корпус считался по поставочной.
function Test-AppWdLibrary {
    param([Parameter(Mandatory)]$Plan)

    $bad = [System.Collections.Generic.List[string]]::new()
    $f = Join-Path $Plan.Wd 'config\NuclideDefinition.xml'
    if (-not (Test-Path -LiteralPath $Plan.Wd)) {
        # Про отсутствие оснастки целиком кричит `Test-AppWdPlan`, второй раз незачем.
        return [pscustomobject]@{ Bad = @(); Count = 0; Sha = '' }
    }
    if (-not (Test-Path -LiteralPath $f)) {
        $bad.Add("В ОСНАСТКЕ НЕТ config\NuclideDefinition.xml — прогон возьмёт ЗАГОТОВКУ, которую напишет сам")
        return [pscustomobject]@{ Bad = @($bad); Count = 0; Sha = '' }
    }
    $sha = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.Substring(0, 12).ToLower()
    try {
        $doc = [xml](Get-Content -LiteralPath $f -Raw)
    } catch {
        $bad.Add("config\NuclideDefinition.xml НЕ РАЗБИРАЕТСЯ КАК XML (sha $sha): $($_.Exception.Message)")
        return [pscustomobject]@{ Bad = @($bad); Count = 0; Sha = $sha }
    }
    $n = @($doc.NuclideDefinitionFile.NuclideDefinitions.Nuclide).Count
    if ($n -lt $script:AppWdNuclideMin) {
        $bad.Add(("БИБЛИОТЕКА НУКЛИДОВ ВЫРОЖДЕНА: {0} записей при пороге {1} (sha {2})" -f $n, $script:AppWdNuclideMin, $sha) +
                 "`n           4 записи пишет само приложение, когда файла нет; в поставке их 152." +
                 "`n           Состав библиотеки задаёт и поиск пиков, и разбор FSA — прогонять НЕЛЬЗЯ.")
    }
    [pscustomobject]@{ Bad = @($bad); Count = $n; Sha = $sha }
}

# Сверка СБОРОК с исходниками — ПО НАБОРУ, ИЗ КОТОРОГО ОНИ СОБРАНЫ (`T226`).
# Довод и доказательство «точнее, а не терпимее» — в блоке «ОТПЕЧАТОК НАБОРА
# ИСХОДНИКОВ» выше. Время правки осталось только там, где сравнивать содержимое
# не с чем: каталог проб ещё не заверен отметкой.
#
# `-Certifying` — зовёт САМ СБОРЩИК проб (`build_all.ps1`) сразу после
# компиляции: пробы в этот миг собраны ИМ ЖЕ из текущего дерева, и сверять их
# с ПРЕЖНЕЙ записью бессмысленно — она устарела на один шаг по построению.
# Приложение в этом режиме судится строже (время И содержимое): заверение
# ставится только на каталог, у которого exe не старше своих исходников.
function Test-AppWdBuild {
    param([Parameter(Mandatory)]$Plan, [switch]$Certifying)

    $bad  = [System.Collections.Generic.List[string]]::new()
    $note = [System.Collections.Generic.List[string]]::new()
    $appExe = Join-Path $Plan.Bin 'BecquerelMonitor.exe'
    if (-not (Test-Path -LiteralPath $appExe)) {
        $bad.Add("НЕТ СБОРКИ: $appExe — сначала соберите приложение")
        return [pscustomobject]@{ Bad = @($bad); Note = @($note) }
    }
    $exeTime = (Get-Item -LiteralPath $appExe -Force).LastWriteTime

    $rec  = Get-AppWdSourceRecord -Repo $Plan.Repo
    foreach ($b in $rec.Bad) { $bad.Add($b) }
    $prev = Get-AppWdStampSources -Wd $Plan.ProbeBuild
    if ($rec.App.Loose.Count -gt 0) {
        $note.Add(("в BecquerelMonitor\ лежат {0} .cs ВНЕ проекта — в exe не входят, свежести не судят: {1}" -f
                   $rec.App.Loose.Count, ((@($rec.App.Loose) | Sort-Object | Select-Object -First 4) -join ', ')))
    }

    # ── ПРИЛОЖЕНИЕ ───────────────────────────────────────────────────────────
    # T41: сборка старше исходников. Незнакомый узел XML десериализатор
    # пропускает МОЛЧА, разбор откатывается на калибровку прибора и даёт
    # правдоподобные числа не про то (16.08.2026: 1766.1 против 692.3).
    $appNewer = @($rec.App.Map.Keys | Where-Object { $rec.App.Map[$_].Time -gt $exeTime })
    if ($prev -and $prev.PSObject.Properties['app'] -and $prev.app.PSObject.Properties['files']) {
        $d = Compare-AppWdSourceMap -Now $rec.App.Map -Was $prev.app.files
        $moved = @($d.Changed) + @($d.Added) + @($d.Removed)
        if ($moved.Count -gt 0) {
            $msg = ("НАБОР ИСХОДНИКОВ ПРИЛОЖЕНИЯ РАЗОШЁЛСЯ С ЗАВЕРЕННЫМ (T226/T41): изменено {0}, добавлено {1}, удалено {2}" -f
                    @($d.Changed).Count, @($d.Added).Count, @($d.Removed).Count)
            foreach ($x in (@($moved) | Sort-Object | Select-Object -First 6)) { $msg += "`n           $x" }
            if ($moved.Count -gt 6) { $msg += ("`n           … и ещё {0}" -f ($moved.Count - 6)) }
            $msg += ("`n           отпечаток набора: заверено {0} -> в дереве {1}" -f
                     ([string]$prev.app.fp).Substring(0, 12), $rec.App.Fp.Substring(0, 12))
            if ($appNewer.Count -eq 0 -and $prev.PSObject.Properties['binaries'] -and
                $prev.binaries.app -eq (Get-AppWdSha256 -Path $appExe)) {
                $msg += "`n           ⛔ НИ ОДИН изменённый файл НЕ НОВЕЕ exe, а сам exe с заверения НЕ МЕНЯЛСЯ (T233):"
                $msg += "`n              содержимое подменено с сохранением LastWriteTime — по времени этого не видно."
            }
            $msg += "`n           Пересоберите приложение и перегоните build_all.ps1."
            $bad.Add($msg)
        }
    } else {
        $note.Add(("в {0} нет заверенной записи набора — свежесть судится ВРЕМЕНЕМ (правило T41)" -f $Plan.ProbeBuild))
    }
    # Время — при заверении ВСЕГДА (заверять протухшую сборку нельзя) и в
    # обычном режиме тогда, когда сверять содержимое не с чем.
    if (($Certifying -or -not $prev) -and $appNewer.Count -gt 0) {
        $msg = ("СБОРКА СТАРШЕ ИСХОДНИКОВ (T41): файлов новее exe — {0}" -f $appNewer.Count) +
               ("`n           BecquerelMonitor.exe {0}" -f $exeTime.ToString('dd.MM HH:mm:ss'))
        foreach ($x in (@($appNewer) | Sort-Object { $rec.App.Map[$_].Time } -Descending | Select-Object -First 4)) {
            $msg += ("`n           {0} {1}" -f $x, $rec.App.Map[$x].Time.ToString('dd.MM HH:mm:ss'))
        }
        $msg += "`n           Пересоберите приложение."
        $bad.Add($msg)
    }
    # ⛔ САМА СБОРКА СВЕРЯЕТСЯ С ЗАВЕРЕННОЙ (`T138`: «сторож сверяет оснастку со
    #    СБОРКОЙ, а сборку — НИ С ЧЕМ»). Подложенный поверх старый `.exe` имеет
    #    и старое время, и «свежее» окружение: по времени такое видно не всегда,
    #    по содержимому — всегда. При заверении не спрашивается: там сборку как
    #    раз и объявляют новой.
    if (-not $Certifying -and $prev -and $prev.PSObject.Properties['binaries'] -and $prev.binaries.app) {
        $nowSha = Get-AppWdSha256 -Path $appExe
        if ($prev.binaries.app -ne $nowSha) {
            $bad.Add("ПРИЛОЖЕНИЕ ПОДМЕНЕНО ИЛИ ПЕРЕСОБРАНО ПОСЛЕ ЗАВЕРЕНИЯ КАТАЛОГА ПРОБ (T138)" +
                     ("`n           заверено sha {0} -> в {1} лежит sha {2}" -f
                      ([string]$prev.binaries.app).Substring(0, 12), $Plan.Bin, $nowSha.Substring(0, 12)) +
                     "`n           Пробы собраны против ДРУГОГО приложения. Перегоните build_all.ps1.")
        }
    }

    # ── ПРОБЫ ────────────────────────────────────────────────────────────────
    # ⛔ ПОФАЙЛОВО, А НЕ ПО КРАЙНИМ ДАТАМ (`T182`). Прежде сравнивался самый
    #    новый `.cs` ВСЕХ проб с самой старой `.exe`: набор проб, сошедшийся до
    #    единой, объявлялся протухшим, стоило временам сборок перемежаться, а
    #    чужая новая проба валила сторожа у всех полос сразу (`T165`).
    $probeExeName = @{}
    foreach ($p in $Plan.Pairs) {
        if ($p.Why -eq 'проба') { $probeExeName[(Split-Path -Leaf $p.Src)] = $true }
    }
    $staleTime = [System.Collections.Generic.List[string]]::new()
    $staleSet  = [System.Collections.Generic.List[string]]::new()
    $swapped   = [System.Collections.Generic.List[string]]::new()
    $prevBin   = $null
    if ($prev -and $prev.PSObject.Properties['binaries'] -and $prev.binaries.PSObject.Properties['probes']) {
        $prevBin = $prev.binaries.probes
    }
    foreach ($n in $probeExeName.Keys) {
        $key  = [System.IO.Path]::GetFileNameWithoutExtension($n).ToLowerInvariant()
        $exeP = Join-Path $Plan.ProbeBuild $n
        if (-not (Test-Path -LiteralPath $exeP)) { continue }   # об этом кричит Test-AppWdPlan
        $set = $rec.Each[$key]
        if (-not $set) { continue }                              # exe без исходника отсеян планом
        $exeT  = (Get-Item -LiteralPath $exeP -Force).LastWriteTime
        # ⛔ У ПРОБ ВРЕМЯ СУДИТ ТОЛЬКО ТАМ, ГДЕ СОДЕРЖИМОЕ СВЕРИТЬ НЕ С ЧЕМ
        #    (`T226`, F46 06.09.2026) — ровно как у приложения строкой выше.
        #    05.09.2026 правка дошла до половины: набор приложения судился
        #    содержимым, а пробы по-прежнему временем ВСЕГДА, и `T194`/`T226`
        #    оставались открыты со стороны проб. Измерено на теневом дереве в
        #    124 файла: `Touch` ОДНОГО чужого `.cs` без правки содержимого давал
        #    отказ у 1 пробы, `Touch` довеска — у ВСЕХ 123 сразу, при том что в
        #    каждом exe лежали ровно те же байты. `git checkout`, повторное
        #    сохранение из редактора и `Copy-Item` соседа краснили сторожа всей
        #    волне — и лечилось это только `-Force`, то есть выключением.
        # ⚠ Ослаблением это не является: заверенная запись СТРОЖЕ времени —
        #   она ловит и подмену содержимого при сохранённом времени (`T233`),
        #   которую время не видит вовсе. Нет записи о ЭТОЙ пробе (каталог не
        #   заверен, либо проба появилась после заверения) — работает прежнее
        #   правило времени, и при заверении оно работает ВСЕГДА.
        $wasFp = $null
        if ($prev -and $prev.PSObject.Properties['each'] -and $prev.each.PSObject.Properties[$key]) {
            $wasFp = [string]$prev.each.$key
        }
        $byTime = $Certifying -or -not $wasFp
        if ($byTime) {
            $newer = @($set.Rel | Where-Object { $rec.Probes.Map[$_].Time -gt $exeT })
            if ($newer.Count -gt 0) {
                $staleTime.Add(("{0} {1} < {2} {3}" -f $n, $exeT.ToString('dd.MM HH:mm:ss'),
                                $rec.Probes.Map[$newer[0]].Name,
                                $rec.Probes.Map[$newer[0]].Time.ToString('dd.MM HH:mm:ss')))
            }
        } elseif ($wasFp -ne $set.Fp) {
            $staleSet.Add(("{0}: заверено {1} -> в дереве {2}" -f $n,
                           $wasFp.Substring(0, 12), $set.Fp.Substring(0, 12)))
        }
        # ⛔ САМА ПРОБА СВЕРЯЕТСЯ С ЗАВЕРЕННОЙ — вторая половина `T138`/`T233`
        #    (F46 06.09.2026). У ПРИЛОЖЕНИЯ такая сверка была с 05.09.2026
        #    (`binaries.app`), у проб — нет, и подложенный поверх старый
        #    `<проба>.exe` с сохранённым `LastWriteTime` проходил насквозь:
        #    исходники сходятся с заверенными, время не старше, а считает
        #    прежний код. Это ровно `A77` («положить файл мимо сторожа»), и
        #    ловилось оно до сих пор только пробой, спрашивающей отражением.
        # ⚠ При заверении не спрашивается: там пробы как раз и объявляют
        #   новыми, только что собранными этим же прогоном.
        if (-not $Certifying -and $prevBin -and $prevBin.PSObject.Properties[$key]) {
            $nowSha = Get-AppWdSha256 -Path $exeP
            if ([string]$prevBin.$key -ne $nowSha) {
                $swapped.Add(("{0}: заверено {1} -> лежит {2}" -f $n,
                              ([string]$prevBin.$key).Substring(0, 12), $nowSha.Substring(0, 12)))
            }
        }
    }
    if ($staleTime.Count -gt 0) {
        $bad.Add("ПРОБЫ СТАРШЕ СВОИХ ИСХОДНИКОВ — не гоняли build_all.ps1: " + $staleTime.Count + " шт." +
                 "`n           " + ((@($staleTime) | Select-Object -First 5) -join "`n           "))
    }
    if ($staleSet.Count -gt 0) {
        $bad.Add("ПРОБЫ СОБРАНЫ ИЗ ДРУГОГО НАБОРА ИСХОДНИКОВ (T226/T233): " + $staleSet.Count + " шт." +
                 "`n           " + ((@($staleSet) | Select-Object -First 5) -join "`n           ") +
                 "`n           Содержимое разошлось с заверенным — по времени такое не всегда видно.")
    }
    if ($swapped.Count -gt 0) {
        $bad.Add("ПРОБЫ ПОДМЕНЕНЫ ПОСЛЕ ЗАВЕРЕНИЯ КАТАЛОГА (T138/T233): " + $swapped.Count + " шт." +
                 "`n           " + ((@($swapped) | Select-Object -First 5) -join "`n           ") +
                 "`n           Двоичный файл в каталоге — не тот, что заверяли. Перегоните build_all.ps1.")
    }
    # ⚠ Проба, у которой в дереве есть `.cs`, а в каталоге НЕТ `.exe`, — не
    #   находка: этого двоичного файла в каталоге просто нет, и судить о его
    #   свежести нечего. Ровно на таких соседских пробах сторож краснел у всех
    #   (`T165`, `T182`). Называется строкой — и только.
    $noExe = @($rec.Each.Keys | Where-Object {
        $wantExe = [System.IO.Path]::GetFileNameWithoutExtension($rec.Each[$_].Name) + '.exe'
        -not $probeExeName.ContainsKey($wantExe)
    })
    if ($noExe.Count -gt 0) {
        $note.Add(("исходников проб без собранного exe в каталоге: {0} ({1}) — каталог их не содержит, свежести не судят" -f
                   $noExe.Count, ((@($noExe) | Sort-Object | Select-Object -First 4) -join ', ')))
    }

    # Пробы компилируются ПРОТИВ копии приложения в каталоге проб. Если она не
    # та, что в `bin`, — пробы и приложение из разных сборок.
    $pbApp = Join-Path $Plan.ProbeBuild 'BecquerelMonitor.exe'
    if (Test-Path -LiteralPath $pbApp) {
        $h1 = (Get-FileHash -LiteralPath $pbApp  -Algorithm SHA256).Hash
        $h2 = (Get-FileHash -LiteralPath $appExe -Algorithm SHA256).Hash
        if ($h1 -ne $h2) {
            $bad.Add("ПРОБЫ СОБРАНЫ ПРОТИВ ДРУГОГО ПРИЛОЖЕНИЯ" +
                     ("`n           {0}\BecquerelMonitor.exe {1} sha {2}" -f (Split-Path -Leaf $Plan.ProbeBuild), (Get-Item -LiteralPath $pbApp -Force).LastWriteTime.ToString('dd.MM HH:mm:ss'), $h1.Substring(0, 12)) +
                     ("`n           bin\BecquerelMonitor.exe {0} sha {1}" -f $exeTime.ToString('dd.MM HH:mm:ss'), $h2.Substring(0, 12)) +
                     "`n           Перегоните build_all.ps1.")
        }
    } else {
        $bad.Add("В $($Plan.ProbeBuild) НЕТ BecquerelMonitor.exe — против чего собраны пробы, проверить нечем")
    }

    # ⛔ Свежести самой `CorpusFsaProbe.exe` в оснастке здесь БОЛЬШЕ НЕ СПРАШИВАЮТ,
    #    и это не потеря: она едет в оснастку как обычная проба и сверяется по
    #    sha256 с `probes\build` (`Test-AppWdPlan`), а свежесть самого
    #    `probes\build` держат две проверки выше. Прежние сверки по времени были
    #    ровно второй копией того же правила — и вдобавок пропускали подмену.

    [pscustomobject]@{ Bad = @($bad); Note = @($note) }
}

# ОТМЕТКА НЕСЁТ ОТПЕЧАТОК НАБОРА ИСХОДНИКОВ (`T226`, решение Amber 05.09.2026).
# До этого дня она несла только `built`/`bin`/`probes`/`repo`/`files` — то есть
# НАБОРА НЕ РАЗЛИЧАЛА ВОВСЕ, и весь куст из семи строк реестра рос из этого.
# ⚠ Поля `bin` и `probes` остаются на прежних местах и с прежним смыслом: по
#   ним `check_appwd.ps1` и `run_appwd.ps1` находят сборку и каталог проб.
# ⚠ `-Depth 6` не украшение: без него `ConvertTo-Json` обрубает вложенные карты
#   на втором уровне и пишет в отметку слово `System.Collections.Hashtable`.
function Write-AppWdStamp {
    param([Parameter(Mandatory)]$Plan, [int]$Files)

    $rec = Get-AppWdSourceRecord -Repo $Plan.Repo
    $flat = {
        param($Map)
        $o = [ordered]@{}
        $keys = [string[]]@($Map.Keys)
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        foreach ($k in $keys) { $o[$k] = $Map[$k].Sha }
        $o
    }
    $eachFp = [ordered]@{}
    foreach ($k in (@($rec.Each.Keys) | Sort-Object)) { $eachFp[$k] = $rec.Each[$k].Fp }

    $appExe = Join-Path $Plan.Bin 'BecquerelMonitor.exe'
    $pbApp  = Join-Path $Plan.ProbeBuild 'BecquerelMonitor.exe'
    # Отпечатки САМИХ ПРОБ (F46 06.09.2026): без них заверение утверждало пару
    # «этот exe приложения — из этого набора», а про пробы — только «их
    # исходники были такими». Подложенный поверх старый `<проба>.exe` с
    # сохранённым временем не ловился ничем.
    $probeBins = [ordered]@{}
    foreach ($p in ($Plan.Pairs | Where-Object { $_.Why -eq 'проба' } | Sort-Object { $_.Src })) {
        $n = Split-Path -Leaf $p.Src
        $f = Join-Path $Plan.ProbeBuild $n
        if (Test-Path -LiteralPath $f) {
            $probeBins[[System.IO.Path]::GetFileNameWithoutExtension($n).ToLowerInvariant()] = (Get-AppWdSha256 -Path $f)
        }
    }
    $stamp = [ordered]@{
        built  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        bin    = $Plan.Bin
        probes = $Plan.ProbeBuild
        repo   = $Plan.Repo
        files  = $Files
        sources = [ordered]@{
            algo   = $script:AppWdSrcAlgo
            app    = [ordered]@{ n = $rec.App.N;    fp = $rec.App.Fp;    files = (& $flat $rec.App.Map) }
            probes = [ordered]@{ n = $rec.Probes.N; fp = $rec.Probes.Fp; files = (& $flat $rec.Probes.Map) }
            comp   = [ordered]@{ n = $rec.Comp.N;   fp = $rec.Comp.Fp }
            each   = $eachFp
            # Отпечатки самих ДВОИЧНЫХ файлов — рядом с набором и в той же
            # записи нарочно: пара «этот exe — из этого набора» и есть всё, что
            # отметка утверждает. Порознь ни та, ни другая половина не значит
            # ничего (`T138`, `T233`).
            binaries = [ordered]@{
                app      = $(if (Test-Path -LiteralPath $appExe) { Get-AppWdSha256 -Path $appExe } else { '' })
                probeApp = $(if (Test-Path -LiteralPath $pbApp)  { Get-AppWdSha256 -Path $pbApp }  else { '' })
                probes   = $probeBins
            }
        }
    }
    ($stamp | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath (Join-Path $Plan.Wd $script:AppWdStampName) -Encoding utf8
}

# Снять отметку. Зовётся ПЕРЕД первым копированием (`mk_appwd.ps1`): иначе
# неудачная пересборка оставляет отметку от удачной прошлой, и следующий
# `run_appwd.ps1` принимает оснастку за собранную.
function Remove-AppWdStamp {
    param([Parameter(Mandatory)][string]$Wd)
    $f = Join-Path $Wd $script:AppWdStampName
    if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
}

function Read-AppWdStamp {
    param([Parameter(Mandatory)][string]$Wd)
    $f = Join-Path $Wd $script:AppWdStampName
    if (Test-Path -LiteralPath $f) { try { Get-Content -LiteralPath $f -Raw | ConvertFrom-Json } catch { $null } }
}

# ⛔ ПЕРЕКЛАДКА СКЛАДА — ПО КЛЕЙМУ, А НЕ ПО ВРЕМЕНИ (`A79`).
#
# Склад пишет `corpus/geometries/<ключ>.rmx`, разбор читает
# `<оснастка>/config/device/response/<guid>.rmx`, между ними стоит
# `mx_swap.py --store`, которого никто не зовёт автоматически. Шаг ручной, и он
# уже ДВАЖДЫ не сделался: 18.08.2026 весь корпус посчитался без матрицы
# (`B14`, `B20`), 02.09.2026 прогон едва не взял матрицы прошлых суток — и
# отработал бы штатно, молча, на прежней физике.
#
# Сверку делает `store_vs_wd.py`: он зовёт ту же приёмку тем же
# `ResponseMatrix.Load` и сравнивает КЛЕЙМА. Время файла говорит «кто-то
# писал», клеймо — «лежит то самое».
#
# ⚠ Отсутствие питона, склада или пробы находкой НЕ считается: сторож обязан
# работать в дереве, где корпуса нет. Такое печатается как пропуск — иначе
# сторож начнёт отказывать там, где стеречь нечего.
function Test-AppWdStore {
    param([Parameter(Mandatory)]$Plan)

    $probe = Join-Path $Plan.ProbeBuild 'MatrixAuditProbe.exe'
    $script = Join-Path $PSScriptRoot 'store_vs_wd.py'
    # `S138`: у плеча склад свой, и сверять надо С НИМ, иначе сторож
    # объявит расхождением ровно то, ради чего плечо и считалось.
    $store = if ($Plan.PSObject.Properties['Store'] -and $Plan.Store) `
             { $Plan.Store } else { Join-Path $Plan.Corpus 'geometries' }
    $resp = Join-Path $Plan.Wd 'config\device\response'

    foreach ($need in @($probe, $script, $store, $resp)) {
        if (-not (Test-Path -LiteralPath $need)) {
            return [pscustomobject]@{ Bad = @(); Note = ("пропущено, нет: {0}" -f $need) }
        }
    }

    $prev = $env:PYTHONIOENCODING
    $env:PYTHONIOENCODING = 'utf-8'
    $out = & python $script "--probe=$probe" "--store=$store" "--wd=$resp" 2>&1
    $code = $LASTEXITCODE
    $env:PYTHONIOENCODING = $prev

    if ($code -eq 0) {
        return [pscustomobject]@{ Bad = @(); Note = 'клейма склада и оснастки сошлись' }
    }

    # Код 2 — сломался сам сторож; это тоже находка, но с иной причиной, и
    # смешивать их нельзя: «сверка не сошлась» и «сверку не удалось сделать»
    # требуют разных действий.
    # Берём ТОЛЬКО находки, а не всю печать: у отчёта есть шапка с путями и
    # числом матриц, и она в списке причин выглядит как две лишние находки.
    # Находки идут после строки «НАХОДОК: N» и до пустой строки.
    $all = @($out | ForEach-Object { $_.ToString() })
    $from = [Array]::FindIndex($all, [Predicate[string]] { param($x) $x -match 'НАХОДОК:' })
    $lines = @()
    if ($from -ge 0) {
        for ($i = $from + 1; $i -lt $all.Count; $i++) {
            if ($all[$i].Trim().Length -eq 0) { break }
            $lines += $all[$i]
        }
    }
    if ($lines.Count -eq 0) { $lines = @($all | Where-Object { $_ -match '⛔' }) }
    if ($code -ne 1) {
        return [pscustomobject]@{
            Bad = @("⛔ сверка перекладки НЕ ВЫПОЛНЕНА (код $code): " + ($lines -join ' / '))
            Note = 'сверка не выполнена'
        }
    }

    $bad = @("⛔ СКЛАД И ОСНАСТКА РАСХОДЯТСЯ ПО КЛЕЙМУ — перекладка не сделана или сделана частично")
    foreach ($l in ($lines | Select-Object -First 6)) { $bad += ("    " + $l.ToString().Trim()) }
    $bad += "    повторить: python tools/CORPUS/scripts/mx_swap.py --from=tools/CORPUS/corpus/geometries --store"
    return [pscustomobject]@{ Bad = $bad; Note = 'РАСХОЖДЕНИЕ' }
}

# Сторож целиком. Печатает вердикт, ВОЗВРАЩАЕТ число отказных находок.
# Ноль — оснастка свежая; всё остальное обязано останавливать прогон.
#
# `-Building` — сторожа зовёт САМ СБОРЩИК, самопроверкой сразу после
# копирования. Отметки о сборке в этот момент ещё нет по замыслу (её пишут
# последним действием и только после удачной самопроверки), поэтому и только
# поэтому её отсутствие в этом режиме не считается находкой.
function Invoke-AppWdGuard {
    param([Parameter(Mandatory)]$Plan, [switch]$Building)

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $b = Test-AppWdBuild   -Plan $Plan
    $p = Test-AppWdPlan    -Plan $Plan
    $l = Test-AppWdLibrary -Plan $Plan
    $s = if ($Building) { [pscustomobject]@{ Bad = @() } } else { Test-AppWdStamp -Plan $Plan }
    $m = Test-AppWdStore   -Plan $Plan
    $sw.Stop()
    # Порядок нарочный: печатаются только первые 20 находок, а расхождений по
    # файлам бывают десятки (опыт 27.08.2026: занятая соседом база — 69 находок).
    # Поэтому впереди то, что решает судьбу прогона целиком — отметка, сборка,
    # библиотека, — а пофайловая сверка последней.
    # Сверка перекладки идёт сразу за отметкой и сборкой: она решает судьбу
    # прогона целиком — на чужих матрицах он отработает штатно и молча.
    $bad = @($s.Bad) + @($b.Bad) + @($m.Bad) + @($l.Bad) + @($p.Bad)

    Write-Host ""
    Write-Host "=== СТОРОЖ ОСНАСТКИ (T63) ===" -ForegroundColor Cyan
    Write-Host ("  оснастка : {0}" -f $Plan.Wd)
    Write-Host ("  сборка   : {0}" -f $Plan.Bin)
    Write-Host ("  пробы    : {0}" -f $Plan.ProbeBuild)
    $st = Read-AppWdStamp -Wd $Plan.Wd
    if ($st) { Write-Host ("  собрана  : {0} из {1}" -f $st.built, $st.bin) }
    # ⛔ ОТПЕЧАТОК НАБОРА ПЕЧАТАЕТСЯ КАЖДЫЙ ПРОГОН (`T226`): именно он делает
    #    числа прогона ПРИВЯЗАННЫМИ к набору исходников, а не к движущемуся
    #    дереву. Без него «числа не годятся» приходилось доказывать отдельно.
    if ($st -and $st.PSObject.Properties['sources'] -and $st.sources.PSObject.Properties['app']) {
        Write-Host ("  наборы   : приложение {0} ({1} файлов), пробы {2} ({3})" -f
                    ([string]$st.sources.app.fp).Substring(0, 12), $st.sources.app.n,
                    ([string]$st.sources.probes.fp).Substring(0, 12), $st.sources.probes.n)
    }
    Write-Host ("  сверено  : {0} файлов по sha256 за {1} с" -f $Plan.Pairs.Count, $sw.Elapsed.TotalSeconds.ToString('F2'))
    if ($l.Sha) { Write-Host ("  библиотека: {0} записей, sha {1}" -f $l.Count, $l.Sha) }
    if ($m.Note) { Write-Host ("  перекладка: {0}" -f $m.Note) }

    foreach ($x in $Plan.Strays) {
        Write-Host ("  ⚠ в каталоге проб exe без исходника, в оснастку не едет: {0}  {1}" -f $x.Name, $x.LastWriteTime.ToString('dd.MM HH:mm')) -ForegroundColor Yellow
    }
    # Замечания сторожа сборки (`T226`): чем судилась свежесть и что в счёт НЕ
    # пошло. Печатаются всегда — иначе «сторож промолчал» и «сторожу нечем было
    # судить» выглядят одинаково.
    if ($b.PSObject.Properties['Note']) {
        foreach ($x in @($b.Note)) { Write-Host ("  ⚠ {0}" -f $x) -ForegroundColor DarkYellow }
    }
    # Не по плану, но и не загружается: продукты прогонов. Перечисляются, чтобы
    # видно было, ЧТО именно сторож пропустил, — но не отказ: иначе сторож
    # отказывал бы после каждого прогона, а такой сторож бесполезен.
    if ($p.Other.Count -gt 0) {
        $head = @($p.Other | Select-Object -First 6) -join ', '
        if ($p.Other.Count -gt 6) { $head += (", … и ещё {0}" -f ($p.Other.Count - 6)) }
        Write-Host ("  продукты прогонов (не по плану, не загружаются): {0} — {1}" -f $p.Other.Count, $head)
    }

    if ($bad.Count -eq 0) {
        Write-Host ("  ОСНАСТКА СВЕЖАЯ: {0} файлов сошлись" -f $p.Ok) -ForegroundColor Green
        Write-Host ""
        return 0
    }

    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: ОСНАСТКА НЕ СООТВЕТСТВУЕТ ИСХОДНИКАМ — ПРОГОН НЕ ЗАПУСКАЕТСЯ" -ForegroundColor Red
    $i = 0
    foreach ($x in $bad) {
        $i++
        if ($i -gt 20) { Write-Host ("  … и ещё {0}" -f ($bad.Count - 20)) -ForegroundColor Red; break }
        Write-Host ("  {0,2}. {1}" -f $i, $x) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Прогон на такой оснастке даёт правдоподобные, но ЧУЖИЕ числа (B20/B21)." -ForegroundColor Red
    Write-Host "  Порядок: собрать приложение -> build_all.ps1 -> mk_appwd.ps1." -ForegroundColor Red
    Write-Host ""
    return $bad.Count
}
