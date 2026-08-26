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
#       сносил `Remove-AppWdOrphans`-ом пробы из оснастки как сирот, после чего
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
# Пользуются этим файлом: `mk_appwd.ps1` (собирает), `check_appwd.ps1` (сторож
# отдельной командой), `run_appwd.ps1` (сторож + запуск пробы — ЧИТАТЕЛЬ отказа).

$script:AppWdFlatMasks = @('BecquerelMonitor.exe', 'BecquerelMonitor.exe.config',
                           'BecquerelMonitor.pdb', '*.dll', '*.sqlite')
$script:AppWdDirs      = @('runtimes', 'ru')

# Ниже этого числа записей `config\NuclideDefinition.xml` — не библиотека.
# 4 записи пишет САМО приложение, когда файла нет
# (`NuclideDefinitionManager.InitializeNuclideDefinitionFile`), поставочная
# библиотека на 25.08.2026 несёт 152. Порог стоит между ними с большим запасом
# в обе стороны: состав библиотеки задаёт и поиск пиков, и разбор FSA.
$script:AppWdNuclideMin = 100

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
        [string]$ProbeBuild = ''
    )

    $corpus     = Join-Path $Repo 'tools\CORPUS\corpus'
    $response   = Join-Path $corpus 'geometries\response'
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
    #    ДО единого копирования и ДО `Remove-AppWdOrphans`, поэтому отказ здесь
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
    $probeSrc = @(Get-ChildItem (Join-Path $Repo 'tools\effmaker\*.cs') -File -Force -ErrorAction SilentlyContinue) +
                @(Get-ChildItem (Join-Path $Repo 'tools\effmaker\probes\*.cs') -File -Force -ErrorAction SilentlyContinue)
    $srcNames = @{}
    foreach ($f in $probeSrc) { $srcNames[$f.BaseName.ToLowerInvariant()] = $true }

    $allExe = @(Get-ChildItem (Join-Path $probeBuild '*.exe') -File -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne 'BecquerelMonitor.exe' })
    # exe, у которого в дереве нет `.cs`, — не проба, а мусор прошлых заходов
    # (в `probes\build` такие лежат: `<guid>_CorpusMatrixProbe.exe` от 09–17.08).
    # В оснастку он не едет по той же причине, по какой `Remove-AppWdOrphans`
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
# `Remove-AppWdOrphans`-у снести пробы из оснастки при зелёном вердикте сверху.
# `exit` внутри функции завершает вызвавший скрипт — здесь это и требуется.
function New-AppWdPlanOrDie {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Bin,
        [Parameter(Mandatory)][string]$Wd,
        [string]$ProbeBuild = ''
    )
    try {
        return Get-AppWdPlan -Repo $Repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild
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

# Убрать из оснастки exe, у которого больше нет источника в каталоге проб.
# Так там восемь дней лежал `PeakFinderProbe.exe` от 17.08, чей `.cs` уже удалён:
# запустить его можно, и он покажет разбор, которого в коде нет.
function Remove-AppWdOrphans {
    param([Parameter(Mandatory)]$Plan)
    $dst = @{}
    foreach ($p in $Plan.Pairs) { $dst[$p.Dst.ToLowerInvariant()] = $true }
    $killed = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem (Join-Path $Plan.Wd '*.exe') -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $dst.ContainsKey($_.FullName.ToLowerInvariant())) {
            Remove-Item -LiteralPath $_.FullName -Force
            Remove-Item -LiteralPath ($_.FullName + '.config') -Force -ErrorAction SilentlyContinue
            $killed.Add($_.Name)
        }
    }
    @($killed)
}

# Сверка ОСНАСТКИ с источниками — по содержимому.
function Test-AppWdPlan {
    param([Parameter(Mandatory)]$Plan)

    $bad  = [System.Collections.Generic.List[string]]::new()
    $orph = [System.Collections.Generic.List[string]]::new()
    $ok   = 0

    if (-not (Test-Path -LiteralPath $Plan.Wd)) {
        $bad.Add("ОСНАСТКИ НЕТ ВОВСЕ: $($Plan.Wd) — сначала pwsh mk_appwd.ps1")
        return [pscustomobject]@{ Bad = @($bad); Orphans = @(); Ok = 0 }
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

    $dstSet = @{}
    foreach ($p in $Plan.Pairs) { $dstSet[$p.Dst.ToLowerInvariant()] = $true }

    foreach ($ex in $Plan.Exclusive) {
        if (-not (Test-Path -LiteralPath $ex.Dir)) { continue }
        Get-ChildItem (Join-Path $ex.Dir $ex.Mask) -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
            if (-not $dstSet.ContainsKey($_.FullName.ToLowerInvariant())) {
                $bad.Add(("ЛИШНЕЕ [{0}]: {1} — в корпусе такого файла нет (B6: два GUID = модальное окно)" -f $ex.What, $_.Name))
            }
        }
    }

    Get-ChildItem (Join-Path $Plan.Wd '*.exe') -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $dstSet.ContainsKey($_.FullName.ToLowerInvariant())) {
            $orph.Add(("{0}  {1} — источника в каталоге проб нет" -f $_.Name, $_.LastWriteTime.ToString('dd.MM HH:mm')))
        }
    }

    [pscustomobject]@{ Bad = @($bad); Orphans = @($orph); Ok = $ok }
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

# Сверка СБОРОК с исходниками — единственное место, где законно время правки:
# `.cs` с `.exe` по содержимому не сверить.
function Test-AppWdBuild {
    param([Parameter(Mandatory)]$Plan)

    $bad = [System.Collections.Generic.List[string]]::new()
    $appExe = Join-Path $Plan.Bin 'BecquerelMonitor.exe'
    if (-not (Test-Path -LiteralPath $appExe)) {
        $bad.Add("НЕТ СБОРКИ: $appExe — сначала соберите приложение")
        return [pscustomobject]@{ Bad = @($bad) }
    }
    $exeTime = (Get-Item -LiteralPath $appExe -Force).LastWriteTime

    # T41: сборка старше исходников. Незнакомый узел XML десериализатор
    # пропускает МОЛЧА, разбор откатывается на калибровку прибора и даёт
    # правдоподобные числа не про то (16.08.2026: 1766.1 против 692.3).
    $newestSrc = Get-ChildItem (Join-Path $Plan.Repo 'BecquerelMonitor') -Recurse -File -Force -Filter '*.cs' -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and $newestSrc.LastWriteTime -gt $exeTime) {
        $bad.Add("СБОРКА СТАРШЕ ИСХОДНИКОВ (T41)" +
                 ("`n           BecquerelMonitor.exe {0}" -f $exeTime.ToString('dd.MM HH:mm:ss')) +
                 ("`n           {0} {1}" -f $newestSrc.Name, $newestSrc.LastWriteTime.ToString('dd.MM HH:mm:ss')) +
                 "`n           Пересоберите приложение.")
    }

    # Исходники проб — оба каталога, которые собирает `build_all.ps1`; список
    # уже посчитан планом, второй раз его не строим.
    $newestProbeCs = $Plan.ProbeSources | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    # Самая СТАРАЯ проба — только из тех, у кого есть исходник: exe без `.cs`
    # в план не попадает и судить по нему о свежести сборки нечестно (пять
    # `<guid>_CorpusMatrixProbe.exe` от 09–17.08 отказывали бы вечно).
    $probeDst = @{}
    foreach ($p in $Plan.Pairs) { if ($p.Why -eq 'проба') { $probeDst[(Split-Path -Leaf $p.Src).ToLowerInvariant()] = $true } }
    $oldestProbeExe = Get-ChildItem (Join-Path $Plan.ProbeBuild '*.exe') -File -Force -ErrorAction SilentlyContinue |
                      Where-Object { $probeDst.ContainsKey($_.Name.ToLowerInvariant()) } |
                      Sort-Object LastWriteTime | Select-Object -First 1
    if ($newestProbeCs -and $oldestProbeExe -and $newestProbeCs.LastWriteTime -gt $oldestProbeExe.LastWriteTime) {
        $bad.Add("ПРОБЫ СТАРШЕ СВОИХ ИСХОДНИКОВ — не гоняли build_all.ps1" +
                 ("`n           {0} {1}" -f $newestProbeCs.Name, $newestProbeCs.LastWriteTime.ToString('dd.MM HH:mm:ss')) +
                 ("`n           {0} {1}" -f $oldestProbeExe.Name, $oldestProbeExe.LastWriteTime.ToString('dd.MM HH:mm:ss')))
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

    [pscustomobject]@{ Bad = @($bad) }
}

function Write-AppWdStamp {
    param([Parameter(Mandatory)]$Plan, [int]$Files)
    $stamp = [pscustomobject]@{
        built  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        bin    = $Plan.Bin
        probes = $Plan.ProbeBuild
        repo   = $Plan.Repo
        files  = $Files
    }
    $stamp | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Plan.Wd '.appwd.json') -Encoding utf8
}

function Read-AppWdStamp {
    param([Parameter(Mandatory)][string]$Wd)
    $f = Join-Path $Wd '.appwd.json'
    if (Test-Path -LiteralPath $f) { try { Get-Content -LiteralPath $f -Raw | ConvertFrom-Json } catch { $null } }
}

# Сторож целиком. Печатает вердикт, ВОЗВРАЩАЕТ число отказных находок.
# Ноль — оснастка свежая; всё остальное обязано останавливать прогон.
function Invoke-AppWdGuard {
    param([Parameter(Mandatory)]$Plan)

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $b = Test-AppWdBuild   -Plan $Plan
    $p = Test-AppWdPlan    -Plan $Plan
    $l = Test-AppWdLibrary -Plan $Plan
    $sw.Stop()
    $bad = @($b.Bad) + @($p.Bad) + @($l.Bad)

    Write-Host ""
    Write-Host "=== СТОРОЖ ОСНАСТКИ (T63) ===" -ForegroundColor Cyan
    Write-Host ("  оснастка : {0}" -f $Plan.Wd)
    Write-Host ("  сборка   : {0}" -f $Plan.Bin)
    Write-Host ("  пробы    : {0}" -f $Plan.ProbeBuild)
    $st = Read-AppWdStamp -Wd $Plan.Wd
    if ($st) { Write-Host ("  собрана  : {0} из {1}" -f $st.built, $st.bin) }
    Write-Host ("  сверено  : {0} файлов по sha256 за {1} с" -f $Plan.Pairs.Count, $sw.Elapsed.TotalSeconds.ToString('F2'))
    if ($l.Sha) { Write-Host ("  библиотека: {0} записей, sha {1}" -f $l.Count, $l.Sha) }

    foreach ($s in $Plan.Strays) {
        Write-Host ("  ⚠ в каталоге проб exe без исходника, в оснастку не едет: {0}  {1}" -f $s.Name, $s.LastWriteTime.ToString('dd.MM HH:mm')) -ForegroundColor Yellow
    }
    foreach ($o in $p.Orphans) {
        Write-Host ("  ⚠ лишний exe: {0}" -f $o) -ForegroundColor Yellow
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
