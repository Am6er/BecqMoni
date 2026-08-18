# Собрать ИЗОЛИРОВАННЫЙ рабочий каталог для корпусного прогона кодом
# приложения (TODO S1) и построить в нём `CorpusFsaProbe.exe`.
#
#   pwsh tools/CORPUS/scripts/mk_appwd.ps1 [-Bin <сборка>] [-Wd <каталог>] [-SkipBuild]
#
# Зачем отдельный каталог, а не `bin\Debug_Codex`, где уже лежит рецепт F25 «а»:
#
#   * приложение считает себя standalone всегда, кроме ClickOnce, и конфиг
#     берёт ОТ РАБОЧЕГО КАТАЛОГА — значит, каталог и есть конфигурация прогона;
#   * в `bin\Debug_Codex\config` лежит СТАРАЯ копия `NuclideDefinition.xml`
#     (04.08.2026, до правки S12 с K-сериями свинца и вольфрама), и прогон,
#     запущенный оттуда, молча меряет не тот состав библиотеки;
#   * `%AppData%\BecqMoni` — конфиг Amber, писать туда нельзя, а проба с
#     ключом `--rebuild` в родне (`FsaCascadeProbe`) пишет матрицы.
#
# Каталог `wd_app` попадает под `scripts/wd_*/` в .gitignore — как и остальные
# рабочие каталоги корпуса.
param(
    [string]$Bin = "",
    [string]$Wd = "",
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Wd)  { $Wd  = Join-Path $PSScriptRoot 'wd_app' }

if (-not (Test-Path (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    throw "нет $Bin\BecquerelMonitor.exe — сначала соберите приложение"
}

# T41: СБОРКА СТАРШЕ ИСХОДНИКОВ — и это молчит. 16.08.2026 в `wd_app` лежал
# exe от 17:25, а `PowerFwhmCalibration.cs` написан в 23:35 того же дня: типа
# класс не знал, XML-десериализатор пропустил неизвестный элемент МОЛЧА,
# `rd.FwhmCalibration` осталась null, проба законно откатилась на калибровку
# прибора — и прогон отработал без единой ошибки, дав правдоподобные числа
# (понятная 1766.1 при невязке 53 %), из которых был сделан вывод «дефект в
# самом узле». На свежей сборке узел работает: 692.3 при 17.9 %.
# Грабля не новая (T31, и строка «сборка → рабочие каталоги» в шапке README
# стоит с 14.08), но обе прежние полагались на то, что человек СРАВНИТ даты
# сам. Здесь их сравнивает скрипт и говорит вслух.
$exeTime = (Get-Item (Join-Path $Bin 'BecquerelMonitor.exe')).LastWriteTime
$newestSrc = Get-ChildItem (Join-Path $repo 'BecquerelMonitor') -Recurse -File -Filter '*.cs' |
             Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
             Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newestSrc -and $newestSrc.LastWriteTime -gt $exeTime) {
    Write-Host ""
    Write-Host "⚠⚠ СБОРКА СТАРШЕ ИСХОДНИКОВ (T41)" -ForegroundColor Yellow
    Write-Host ("   $Bin\BecquerelMonitor.exe    " + $exeTime.ToString('dd.MM HH:mm'))
    Write-Host ("   " + $newestSrc.FullName.Substring($repo.Length + 1) + "    " +
                $newestSrc.LastWriteTime.ToString('dd.MM HH:mm'))
    Write-Host "   Незнакомый узел XML пропускается МОЛЧА: прогон отработает без ошибок" -ForegroundColor Yellow
    Write-Host "   и даст правдоподобные числа не про то. Пересоберите приложение." -ForegroundColor Yellow
    Write-Host ""
}

$corpus = Join-Path $repo 'tools\CORPUS\corpus'
$response = Join-Path $corpus 'geometries\response'
if (-not (Test-Path $response)) {
    # Матрицы не лежат в репозитории (см. .gitignore) — они считаются на месте.
    # Молча собрать каталог без них значит намерить «понятную» часть БЕЗ
    # матрицы и не заметить: разложение просто тихо станет хуже.
    throw "нет $response — сначала посчитайте матрицы (corpuseffprobe / CorpusMatrixProbe)"
}

New-Item -ItemType Directory -Force $Wd | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Wd 'config\device\response') | Out-Null

# 1. Сборка приложения: exe, библиотеки, три базы, нативные провайдеры, ресурсы.
$flat = @('BecquerelMonitor.exe', 'BecquerelMonitor.exe.config', 'BecquerelMonitor.pdb',
          '*.dll', '*.sqlite')
foreach ($mask in $flat) {
    Get-ChildItem (Join-Path $Bin $mask) -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $Wd -Force
}
foreach ($dir in @('runtimes', 'ru')) {
    $src = Join-Path $Bin $dir
    if (Test-Path $src) { Copy-Item $src $Wd -Recurse -Force }
}

# 1а. Прочие пробы — СВЕЖИМИ из `tools\effmaker\probes\build`, если они там
#     собраны. Сам этот каталог строит только `CorpusFsaProbe` (ниже), а
#     соседи (`FsaStackShot`, `RoiActivityProbe`, …) попадали сюда однажды и
#     потом лежали месяцами: 15.08.2026 снимок таблицы FSA рисовался пробой от
#     10:09 и показывал разбор, которого в коде уже не было. Копия обновляется
#     тем же движением, что и приложение (грабля класса T31).
$probeBuild = Join-Path $repo 'tools\effmaker\probes\build'
if (Test-Path $probeBuild) {
    Get-ChildItem (Join-Path $probeBuild '*.exe') -File |
        Where-Object { $_.Name -ne 'BecquerelMonitor.exe' -and $_.Name -ne 'CorpusFsaProbe.exe' } |
        ForEach-Object {
            Copy-Item $_.FullName $Wd -Force
            $cfg = Join-Path $Wd ($_.Name + '.config')
            Copy-Item (Join-Path $Wd 'BecquerelMonitor.exe.config') $cfg -Force
        }
}

# 2. Конфиг — ПОСТАВОЧНЫЙ, а не тот, что сгенерировал mkconfig.py: в рабочих
#    каталогах `wd_<группа>` лежит `NuclideDefinition.xml` с сетами-обманками
#    под изучение гейта (`[decoy]`), и разбор по нему мерил бы не то.
Copy-Item (Join-Path $repo 'BecquerelMonitor\config\NuclideDefinition.xml') `
          (Join-Path $Wd 'config\NuclideDefinition.xml') -Force
Copy-Item (Join-Path $repo 'BecquerelMonitor\config\BecquerelMonitor.xml') `
          (Join-Path $Wd 'config\BecquerelMonitor.xml') -Force

# 3. Конфигурации приборов корпуса и матрицы отклика. Именно этого копирования
#    не делали ни `mkconfig.py`, ни `run_corpus.ps1` (S1): `ResponseMatrixStore`
#    ищет матрицу в `config\device\response` рабочего каталога, а раскладывает
#    их `CorpusEffProbe` в `corpus\geometries\response`.
#    ⚠ Каталог СНАЧАЛА ОЧИЩАЕТСЯ (T33, 16.08.2026). Копирование поверх
#    оставляет файл, которого в корпусе больше нет, — а после переименования
#    конфигурации (B6, раздел G1S на эпохи) старая и новая несут ОДИН GUID, и
#    приложение встаёт на модальном окне «Одинаковые GUID в разных файлах
#    конфигурации устройств». В прогоне без консоли это выглядит как зависание:
#    ни строки вывода, ни кода возврата. Каталог целиком строится из корпуса,
#    так что чистить его безопасно по построению.
Remove-Item (Join-Path $Wd 'config\device\*.xml') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $Wd 'config\device\response\*.rmx') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $corpus 'devices\*.xml') (Join-Path $Wd 'config\device') -Force
$rmx = Get-ChildItem (Join-Path $response '*.rmx') -File
Copy-Item $rmx (Join-Path $Wd 'config\device\response') -Force

# 4. Сама проба. `ResidualScan.cs` — довесок без Main (как в build_all.ps1):
#    без него csc падает на карте невязок (`--residuals`/`--near`).
if (-not $SkipBuild) {
    $csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
    $facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
    $src = Join-Path $repo 'tools\effmaker\probes\CorpusFsaProbe.cs'
    $scan = Join-Path $repo 'tools\effmaker\probes\ResidualScan.cs'
    $exe = Join-Path $Wd 'CorpusFsaProbe.exe'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 "/out:$exe" `
        "/r:$Wd\BecquerelMonitor.exe" `
        /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        "/r:$Wd\Microsoft.Data.Sqlite.dll" "/r:$facades\netstandard.dll" `
        $src $scan
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    # Пробам, читающим базы, нужен свой exe.config — иначе binding redirect
    # SQLitePCLRaw не применяется и чтение падает уже на месте.
    Copy-Item (Join-Path $Wd 'BecquerelMonitor.exe.config') "$exe.config" -Force
}

Write-Host ""
Write-Host "рабочий каталог: $Wd"
Write-Host ("  конфигураций приборов: {0}, матриц: {1}" -f `
    (Get-ChildItem (Join-Path $Wd 'config\device\*.xml') -File).Count, $rmx.Count)
Write-Host "запуск (из каталога прогона):"
Write-Host "  cd `"$Wd`""
Write-Host "  .\CorpusFsaProbe.exe --corpus=`"$corpus`" --out=`"$repo\tools\pie\out_app`""
