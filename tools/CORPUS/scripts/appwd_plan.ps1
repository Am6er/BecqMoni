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
# Пользуются этим файлом: `mk_appwd.ps1` (собирает), `check_appwd.ps1` (сторож
# отдельной командой), `run_appwd.ps1` (сторож + запуск пробы — ЧИТАТЕЛЬ отказа).

$script:AppWdFlatMasks = @('BecquerelMonitor.exe', 'BecquerelMonitor.exe.config',
                           'BecquerelMonitor.pdb', '*.dll', '*.sqlite')
$script:AppWdDirs      = @('runtimes', 'ru')

function Get-AppWdPlan {
    param(
        [Parameter(Mandatory)][string]$Repo,
        [Parameter(Mandatory)][string]$Bin,
        [Parameter(Mandatory)][string]$Wd,
        # Откуда брать ПРОЧИЕ пробы. По умолчанию `probes\build` — каталог
        # отладочной сборки. Оптимизированный рецепт (CLAUDE.md, «Computing»)
        # кладёт пробы в `probes\build_rel`, и оснастку из `bin\Release_Codex`
        # надо собирать оттуда же: иначе приложение будет из одной сборки,
        # а пробы рядом — из другой, и сторож законно откажет.
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
        Get-ChildItem (Join-Path $Bin $mask) -File -ErrorAction SilentlyContinue | ForEach-Object {
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
        Get-ChildItem -LiteralPath $src -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($prefix.Length).TrimStart('\')
            $pairs.Add([pscustomobject]@{
                Src = $_.FullName
                Dst = (Join-Path (Join-Path $Wd $dir) $rel)
                Why = "сборка\$dir"
            })
        }
    }

    # 3. Прочие пробы — свежими из `tools\effmaker\probes\build`. Каждой нужен
    #    свой exe.config, иначе binding redirect SQLitePCLRaw не применяется.
    if (Test-Path -LiteralPath $probeBuild) {
        Get-ChildItem (Join-Path $probeBuild '*.exe') -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne 'BecquerelMonitor.exe' -and $_.Name -ne 'CorpusFsaProbe.exe' } |
            ForEach-Object {
                $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $Wd $_.Name); Why = 'проба' })
                $pairs.Add([pscustomobject]@{ Src = $appCfgSrc;  Dst = (Join-Path $Wd ($_.Name + '.config')); Why = 'exe.config пробы' })
            }
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
    Get-ChildItem (Join-Path $corpus 'devices\*.xml') -File -ErrorAction SilentlyContinue | ForEach-Object {
        $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $devDir $_.Name); Why = 'прибор корпуса' })
    }
    Get-ChildItem (Join-Path $response '*.rmx') -File -ErrorAction SilentlyContinue | ForEach-Object {
        $pairs.Add([pscustomobject]@{ Src = $_.FullName; Dst = (Join-Path $rspDir $_.Name); Why = 'матрица отклика' })
    }

    [pscustomobject]@{
        Repo = $Repo; Bin = $Bin; Wd = $Wd
        Corpus = $corpus; Response = $response; ProbeBuild = $probeBuild
        Pairs = @($pairs)
        # Каталоги, которые строятся из корпуса ЦЕЛИКОМ: лишний файл в них — не
        # безобидный мусор. После переименования конфигурации (`B6`) старая и
        # новая несут ОДИН GUID, и приложение встаёт на модальном окне
        # «Одинаковые GUID» — в безоконном прогоне это выглядит как зависание.
        Exclusive = @(
            [pscustomobject]@{ Dir = $devDir; Mask = '*.xml'; What = 'конфигурации приборов' }
            [pscustomobject]@{ Dir = $rspDir; Mask = '*.rmx'; What = 'матрицы отклика' }
        )
        # Собирается на месте компилятором, источника-двойника у него нет.
        KeepExe = @('CorpusFsaProbe.exe')
    }
}

function Invoke-AppWdPlan {
    param([Parameter(Mandatory)]$Plan)

    foreach ($ex in $Plan.Exclusive) {
        New-Item -ItemType Directory -Force $ex.Dir | Out-Null
        Remove-Item (Join-Path $ex.Dir $ex.Mask) -Force -ErrorAction SilentlyContinue
    }
    $dirs = $Plan.Pairs | ForEach-Object { Split-Path -Parent $_.Dst } | Sort-Object -Unique
    foreach ($d in $dirs) { New-Item -ItemType Directory -Force $d | Out-Null }
    foreach ($p in $Plan.Pairs) { Copy-Item -LiteralPath $p.Src -Destination $p.Dst -Force }
    $Plan.Pairs.Count
}

# Убрать из оснастки exe, у которого больше нет источника в `probes\build`.
# Так там восемь дней лежал `PeakFinderProbe.exe` от 17.08, чей `.cs` уже удалён:
# запустить его можно, и он покажет разбор, которого в коде нет.
function Remove-AppWdOrphans {
    param([Parameter(Mandatory)]$Plan)
    $dst = @{}
    foreach ($p in $Plan.Pairs) { $dst[$p.Dst.ToLowerInvariant()] = $true }
    $killed = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem (Join-Path $Plan.Wd '*.exe') -File -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $dst.ContainsKey($_.FullName.ToLowerInvariant()) -and $Plan.KeepExe -notcontains $_.Name) {
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
        if (-not (Test-Path -LiteralPath $p.Dst)) {
            $bad.Add(("НЕТ В ОСНАСТКЕ [{0}]: {1}" -f $p.Why, (Split-Path -Leaf $p.Dst)))
            continue
        }
        $hs = (Get-FileHash -LiteralPath $p.Src -Algorithm SHA256).Hash
        $hd = (Get-FileHash -LiteralPath $p.Dst -Algorithm SHA256).Hash
        if ($hs -ne $hd) {
            $ts = (Get-Item -LiteralPath $p.Src).LastWriteTime.ToString('dd.MM HH:mm:ss')
            $td = (Get-Item -LiteralPath $p.Dst).LastWriteTime.ToString('dd.MM HH:mm:ss')
            $bad.Add(("ПРОТУХЛО [{0}]: {1}" -f $p.Why, (Split-Path -Leaf $p.Dst)) +
                     ("`n           источник {0}  sha {1}" -f $ts, $hs.Substring(0, 12)) +
                     ("`n           оснастка {0}  sha {1}" -f $td, $hd.Substring(0, 12)))
        } else { $ok++ }
    }

    $dstSet = @{}
    foreach ($p in $Plan.Pairs) { $dstSet[$p.Dst.ToLowerInvariant()] = $true }

    foreach ($ex in $Plan.Exclusive) {
        if (-not (Test-Path -LiteralPath $ex.Dir)) { continue }
        Get-ChildItem (Join-Path $ex.Dir $ex.Mask) -File -ErrorAction SilentlyContinue | ForEach-Object {
            if (-not $dstSet.ContainsKey($_.FullName.ToLowerInvariant())) {
                $bad.Add(("ЛИШНЕЕ [{0}]: {1} — в корпусе такого файла нет (B6: два GUID = модальное окно)" -f $ex.What, $_.Name))
            }
        }
    }

    Get-ChildItem (Join-Path $Plan.Wd '*.exe') -File -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $dstSet.ContainsKey($_.FullName.ToLowerInvariant()) -and $Plan.KeepExe -notcontains $_.Name) {
            $orph.Add(("{0}  {1} — источника в probes\build нет" -f $_.Name, $_.LastWriteTime.ToString('dd.MM HH:mm')))
        }
    }

    [pscustomobject]@{ Bad = @($bad); Orphans = @($orph); Ok = $ok }
}

# Сверка СБОРОК с исходниками — единственное место, где законно время правки:
# `.cs` с `.exe` по содержимому не сверить.
function Test-AppWdBuild {
    # `-SkipWdChecks` — для `mk_appwd.ps1` ДО сборки: он сам и есть тот, кто
    # кладёт пробу в оснастку, спрашивать с него её наличие заранее незачем.
    param([Parameter(Mandatory)]$Plan, [switch]$SkipWdChecks)

    $bad = [System.Collections.Generic.List[string]]::new()
    $appExe = Join-Path $Plan.Bin 'BecquerelMonitor.exe'
    if (-not (Test-Path -LiteralPath $appExe)) {
        $bad.Add("НЕТ СБОРКИ: $appExe — сначала соберите приложение")
        return [pscustomobject]@{ Bad = @($bad) }
    }
    $exeTime = (Get-Item -LiteralPath $appExe).LastWriteTime

    # T41: сборка старше исходников. Незнакомый узел XML десериализатор
    # пропускает МОЛЧА, разбор откатывается на калибровку прибора и даёт
    # правдоподобные числа не про то (16.08.2026: 1766.1 против 692.3).
    $newestSrc = Get-ChildItem (Join-Path $Plan.Repo 'BecquerelMonitor') -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and $newestSrc.LastWriteTime -gt $exeTime) {
        $bad.Add("СБОРКА СТАРШЕ ИСХОДНИКОВ (T41)" +
                 ("`n           BecquerelMonitor.exe {0}" -f $exeTime.ToString('dd.MM HH:mm:ss')) +
                 ("`n           {0} {1}" -f $newestSrc.Name, $newestSrc.LastWriteTime.ToString('dd.MM HH:mm:ss')) +
                 "`n           Пересоберите приложение.")
    }

    $newestProbeCs = Get-ChildItem (Join-Path $Plan.Repo 'tools\effmaker\probes\*.cs') -File -ErrorAction SilentlyContinue |
                     Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $oldestProbeExe = Get-ChildItem (Join-Path $Plan.ProbeBuild '*.exe') -File -ErrorAction SilentlyContinue |
                      Where-Object { $_.Name -ne 'BecquerelMonitor.exe' } |
                      Sort-Object LastWriteTime | Select-Object -First 1
    if ($newestProbeCs -and $oldestProbeExe -and $newestProbeCs.LastWriteTime -gt $oldestProbeExe.LastWriteTime) {
        $bad.Add("ПРОБЫ СТАРШЕ СВОИХ ИСХОДНИКОВ — не гоняли build_all.ps1" +
                 ("`n           {0} {1}" -f $newestProbeCs.Name, $newestProbeCs.LastWriteTime.ToString('dd.MM HH:mm:ss')) +
                 ("`n           {0} {1}" -f $oldestProbeExe.Name, $oldestProbeExe.LastWriteTime.ToString('dd.MM HH:mm:ss')))
    }

    # Пробы компилируются ПРОТИВ копии приложения в `probes\build`. Если она не
    # та, что в `bin`, — пробы и приложение из разных сборок.
    $pbApp = Join-Path $Plan.ProbeBuild 'BecquerelMonitor.exe'
    if (Test-Path -LiteralPath $pbApp) {
        $h1 = (Get-FileHash -LiteralPath $pbApp  -Algorithm SHA256).Hash
        $h2 = (Get-FileHash -LiteralPath $appExe -Algorithm SHA256).Hash
        if ($h1 -ne $h2) {
            $bad.Add("ПРОБЫ СОБРАНЫ ПРОТИВ ДРУГОГО ПРИЛОЖЕНИЯ" +
                     ("`n           probes\build\BecquerelMonitor.exe {0} sha {1}" -f (Get-Item -LiteralPath $pbApp).LastWriteTime.ToString('dd.MM HH:mm:ss'), $h1.Substring(0, 12)) +
                     ("`n           bin\BecquerelMonitor.exe          {0} sha {1}" -f $exeTime.ToString('dd.MM HH:mm:ss'), $h2.Substring(0, 12)) +
                     "`n           Перегоните build_all.ps1.")
        }
    }

    # Сама проба корпуса собирается в оснастке и обязана быть МОЛОЖЕ и приложения,
    # которое лежит рядом с ней, и всех исходников проб.
    $probe = Join-Path $Plan.Wd 'CorpusFsaProbe.exe'
    $wdApp = Join-Path $Plan.Wd 'BecquerelMonitor.exe'
    if ($SkipWdChecks) {
        # ничего про оснастку не спрашиваем
    } elseif (-not (Test-Path -LiteralPath $probe)) {
        if (Test-Path -LiteralPath $Plan.Wd) { $bad.Add("НЕТ CorpusFsaProbe.exe в оснастке — собирали с -SkipBuild?") }
    } else {
        $pt = (Get-Item -LiteralPath $probe).LastWriteTime
        if (Test-Path -LiteralPath $wdApp) {
            $at = (Get-Item -LiteralPath $wdApp).LastWriteTime
            if ($pt -lt $at) {
                $bad.Add("ПРОБА КОРПУСА СТАРШЕ ПРИЛОЖЕНИЯ РЯДОМ С НЕЙ" +
                         ("`n           CorpusFsaProbe.exe   {0}" -f $pt.ToString('dd.MM HH:mm:ss')) +
                         ("`n           BecquerelMonitor.exe {0}" -f $at.ToString('dd.MM HH:mm:ss')))
            }
        }
        if ($newestProbeCs -and $pt -lt $newestProbeCs.LastWriteTime) {
            $bad.Add("ПРОБА КОРПУСА СТАРШЕ ИСХОДНИКОВ ПРОБ" +
                     ("`n           CorpusFsaProbe.exe {0}" -f $pt.ToString('dd.MM HH:mm:ss')) +
                     ("`n           {0} {1}" -f $newestProbeCs.Name, $newestProbeCs.LastWriteTime.ToString('dd.MM HH:mm:ss')))
        }
    }

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
    $b = Test-AppWdBuild -Plan $Plan
    $p = Test-AppWdPlan  -Plan $Plan
    $sw.Stop()
    $bad = @($b.Bad) + @($p.Bad)

    Write-Host ""
    Write-Host "=== СТОРОЖ ОСНАСТКИ (T63) ===" -ForegroundColor Cyan
    Write-Host ("  оснастка : {0}" -f $Plan.Wd)
    Write-Host ("  сборка   : {0}" -f $Plan.Bin)
    $st = Read-AppWdStamp -Wd $Plan.Wd
    if ($st) { Write-Host ("  собрана  : {0} из {1}" -f $st.built, $st.bin) }
    Write-Host ("  сверено  : {0} файлов по sha256 за {1} с" -f $Plan.Pairs.Count, $sw.Elapsed.TotalSeconds.ToString('F2'))

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
