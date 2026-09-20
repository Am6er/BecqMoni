# Склад витрины FSA (`T260`): матрицы отклика сцен витрины — `tools\fsa_showcase\store\`
# (в .gitignore), физика — УМОЛЧАНИЯ КЛАССА `ResponseMatrixOptions` (то, что получает
# человек кнопкой «посчитать»), счёт — `CorpusMatrixProbe --threads=10 --target=0`, как
# у живого склада корпуса (П50). Потом `CorpusEffProbe` вписывает кривую и guid в те
# спектры витрины, что названы в `scenes\index.csv` (сейчас — только радоновый фильтр:
# у него в файле Amber кривой нет вовсе; у `ASN16_Cs137_house` кривая и guid — её
# собственные, матрица кладётся под ЕЁ guid сторожем).
#
#   & 'tools\fsa_showcase\rebuild_store.ps1' [-Probes <каталог проб>] [-Force] [-SkipControl]
#
# ⛔ Оператором вызова `&`, не `pwsh <файл>` (`T84`/`T91`).
# ⛔ `-Probes` — каталог проб, СОБРАННЫЙ `build_all.ps1` из той сборки, которой будет
#    считаться склад (A77: перед счётом дольше получаса — пересобрать и посмотреть код).
#    Умолчание — штатный `tools\effmaker\probes\build`.
# ⛔ Контроль настройки на ДВУХ узлах (A77) идёт первым, в отдельный каталог
#    `store\_check`, и только после него — полный счёт; `-SkipControl` снимает его.
# `-Force` — пересчитать матрицы и кривые, даже если клеймо сошлось.
#
# Сцены (`scenes\*.in`, в git):
#   * `ASN16_point_house` — геометрия «Точка в защите» из файла Amber `Cs 137 в домике
#     24.11.2022.xml` (выписана `handover/p74-t260/ExportScene.cs`: круг Render побайтно,
#     клеймо геометрии при физике 18 `phys=18;3d28966c…`, с физики 19 — своё); матрица кладётся сторожем под guid кривой
#     файла `c482e3bc-…`;
#   * `ASN16_rn_side` — ватный диск Ø40×20 мм ρ 0.15 к широкой грани (П59, `AMBER27`),
#     копия `handover/p59-amber27/scenes/ASN16_rn_side.in`; кривую и guid в
#     `spectra\ASN16_Radon_filter2.xml` пишет `CorpusEffProbe` (шаг 4).
# Сцены корпуса (`AS80_th_disk`, `G1S_mar1l_coal_046_p24`) здесь НЕ считаются: их
# матрицы сторож берёт из живого склада `tools\CORPUS\corpus\geometries` (ссылка).
#
# Коды: 0 — склад собран (матрицы «ВСЕ СОШЛИСЬ», кривые записаны); 2 — нет проб;
#       3 — контроль на двух узлах не принял сцену; 4 — счёт матриц отказал;
#       5 — CorpusEffProbe отказал.
param(
    [string]$Probes = '',
    [switch]$Force,
    [switch]$SkipControl
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$show  = $PSScriptRoot
$repo  = (Resolve-Path (Join-Path $show '..\..')).Path
$store = Join-Path $show 'store'
$scenes = Join-Path $show 'scenes'
$spectra = Join-Path $show 'spectra'
if (-not $Probes) { $Probes = Join-Path $repo 'tools\effmaker\probes\build' }
$Probes = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Probes).TrimEnd('\')
foreach ($exe in 'CorpusMatrixProbe.exe', 'CorpusEffProbe.exe', 'MatrixAuditProbe.exe') {
    if (-not (Test-Path (Join-Path $Probes $exe))) {
        Write-Host "⛔ нет $Probes\$exe — сперва build_all.ps1 (A77: свежий каталог проб, код 0)" -ForegroundColor Red
        exit 2
    }
}
$stamp = Join-Path $Probes '.appwd.json'
if (-not (Test-Path $stamp)) {
    Write-Host "⛔ каталог проб $Probes не заверен (.appwd.json нет) — собрать build_all.ps1 заново" -ForegroundColor Red
    exit 2
}
# Поколение физики — ЧИСЛОМ ИЗ ИСХОДНИКА, не из памяти (как `check_curve_generation.py`):
# образец клейма контроля и `--phys=` аудита берутся отсюда, чтобы смена физики не
# оставляла в скрипте старое число (П97 18.09.2026: 18 → 19, `phys=18` стояло здесь трижды).
$phys = [int]([regex]::Match((Get-Content (Join-Path $repo 'BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs') -Raw), 'const int PhysicsVersion\s*=\s*(\d+)').Groups[1].Value)
if ($phys -le 0) {
    Write-Host '⛔ не прочитано `PhysicsVersion` из BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs' -ForegroundColor Red
    exit 2
}
New-Item -ItemType Directory -Force $store | Out-Null
Copy-Item (Join-Path $scenes '*.in') $store -Force
Copy-Item (Join-Path $scenes 'index.csv') $store -Force
$sceneNames = @(Get-ChildItem (Join-Path $scenes '*.in') | ForEach-Object { $_.BaseName })
Write-Host ("склад витрины: {0}; сцен {1}: {2}; пробы: {3}; физика {4}" -f $store, $sceneNames.Count, ($sceneNames -join ', '), $Probes, $phys)
$exeInfo = Get-Item (Join-Path $Probes 'BecquerelMonitor.exe')
Write-Host ("приложение у проб: {0} sha256 {1}" -f $exeInfo.LastWriteTime.ToString('dd.MM HH:mm:ss'), (Get-FileHash $exeInfo.FullName -Algorithm SHA256).Hash.Substring(0, 16))

$sw = [Diagnostics.Stopwatch]::StartNew()
Push-Location $Probes
try {
    # 1. Контроль настройки на первых двух узлах (A77) — отдельный каталог, чтобы
    #    двухузловая матрица не легла в склад.
    if (-not $SkipControl) {
        $check = Join-Path $store '_check'
        if (Test-Path $check) { Remove-Item $check -Recurse -Force }
        New-Item -ItemType Directory -Force $check | Out-Null
        Copy-Item (Join-Path $scenes '*.in') $check -Force
        $log = Join-Path $store 'control_2nodes.log'
        & .\CorpusMatrixProbe.exe "--dir=$check" --nodes=2 --emin=30 --emax=31 --n=400000 --threads=10 --target=0 --jnodes=0 *> $log
        $c = $LASTEXITCODE
        $accepted = @(Select-String -Path $log -Pattern "phys=$phys;").Count
        Write-Host ("контроль на 2 узлах (A77): код {0} (1 = «есть шумные», ожидаемо на 400 k), сцен с клеймом phys={4}: {1} из {2}, {3} с" -f $c, $accepted, $sceneNames.Count, [int]$sw.Elapsed.TotalSeconds, $phys)
        Get-Content $log | Select-String 'клейм|мкс|ОТКАЗ|исключ|Exception' | Select-Object -First 8 | ForEach-Object { Write-Host ('   ' + $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length))) }
        if ($c -notin 0, 1 -or $accepted -lt $sceneNames.Count) {
            Write-Host '⛔ контроль на двух узлах не принял сцены — полный счёт не запускается' -ForegroundColor Red
            exit 3
        }
        Remove-Item $check -Recurse -Force
    }

    # 2. Полный счёт матриц — умолчания класса.
    $log = Join-Path $store 'count.log'
    $argv = @("--dir=$store", '--threads=10', '--target=0')
    if ($Force) { $argv += '--force' }
    & .\CorpusMatrixProbe.exe @argv *> $log
    $c = $LASTEXITCODE
    Write-Host ("матрицы: код {0}, {1} с" -f $c, [int]$sw.Elapsed.TotalSeconds)
    Get-Content $log | Select-String 'клейм|СОШЛИСЬ|ШУМН|мкс|пропущ' | Select-Object -Last 6 | ForEach-Object { Write-Host ('   ' + $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length))) }
    if ($c -ne 0) { Write-Host "⛔ счёт матриц отказал (код $c) — см. $log" -ForegroundColor Red; exit 4 }

    # 3. Кривая и guid — в спектры витрины по index.csv (CorpusEffProbe: свой MC кривой,
    #    матрица копируется в store\response\<guid>.rmx).
    $log = Join-Path $store 'eff.log'
    $argv = @("--dir=$store", "--spectra=$spectra")
    if ($Force) { $argv += '--force' }
    & .\CorpusEffProbe.exe @argv *> $log
    $e = $LASTEXITCODE
    Write-Host ("кривые: код {0}, {1} с" -f $e, [int]$sw.Elapsed.TotalSeconds)
    Get-Content $log | Select-String 'guid|узл|медиан|спектр|СОШЛИСЬ|пропущена|НЕТ' | Select-Object -Last 8 | ForEach-Object { Write-Host ('   ' + $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length))) }
    if ($e -ne 0) { Write-Host "⛔ CorpusEffProbe отказал (код $e) — см. $log" -ForegroundColor Red; exit 5 }

    # 4. Приёмка склада — пять признаков каждой матрицы (клеймо, узлы, истории, шум, каналы).
    & .\MatrixAuditProbe.exe "--dir=$store" "--phys=$phys" 2>&1 | Select-Object -Last 12 | ForEach-Object { Write-Host ('   ' + $_) }
} finally {
    Pop-Location
}
Get-ChildItem (Join-Path $store '*.rmx') | ForEach-Object {
    Write-Host ("  {0}  {1} байт  sha256 {2}" -f $_.Name, $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16))
}
Write-Host ("готово за {0} с" -f [int]$sw.Elapsed.TotalSeconds)
exit 0
