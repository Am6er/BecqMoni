# Сборка ВСЕХ проб и харнесс-файлов effmaker — проверка, что ничего не
# сломано молча (TODO T3). Проекта у проб нет нарочно (см. README); цена
# этого — компилятор молчит про файл, который перестал собираться, и проба
# выглядит как «сегодня не гоняли». Этот скрипт — тот самый читатель
# признака отказа: гонять после ЛЮБОГО удаления или переименования в
# приложении, а не только перед запуском конкретной пробы.
#
#   pwsh tools\effmaker\probes\build_all.ps1 [-Bin <каталог сборки приложения>]
#                                            [-Out <куда класть exe>]
#
# Умолчания: -Bin BecquerelMonitor\bin\Debug_Codex (агентская сборка),
# -Out tools\effmaker\probes\build (в .gitignore). Выход 1 — есть сломанные,
# с перечнем поимённо. Пробам, читающим nucdb.sqlite, для ЗАПУСКА нужен ещё
# <имя>.exe.config — копия BecquerelMonitor.exe.config (см. README, шапка);
# сборке это не мешает, поэтому здесь конфиги не создаются.
param(
    [string]$Bin = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot 'build' }
if (-not (Test-Path (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    Write-Host "нет $Bin\BecquerelMonitor.exe — сначала соберите приложение"
    exit 2
}
New-Item -ItemType Directory -Force $Out | Out-Null

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

# Полный набор ссылок сразу всем: лишняя ссылка бесплатна, а раздача особых
# случаев по пробам уже дважды устаревала молча.
$refs = @(
    "/r:$Bin\BecquerelMonitor.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    "/r:$Bin\Microsoft.Data.Sqlite.dll",
    "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll",
    "/r:$facades\netstandard.dll"
)

$fail = @()
$sources = @(Get-ChildItem (Join-Path $repo 'tools\effmaker\*.cs')) +
           @(Get-ChildItem (Join-Path $PSScriptRoot '*.cs'))
foreach ($f in $sources) {
    if ($f.Name -eq 'GadrasDetector.cs') { continue }   # без Main, идёт довеском
    $extra = @()
    if ($f.Name -in @('GadrasProbe.cs', 'ResponseProbe.cs')) {
        $extra = @(Join-Path $PSScriptRoot 'GadrasDetector.cs')
    }
    $exe = Join-Path $Out ($f.BaseName + '.exe')
    $log = & $csc /nologo /target:exe /langversion:7.3 "/out:$exe" @refs $f.FullName @extra 2>&1
    if ($LASTEXITCODE -ne 0) {
        $fail += $f.Name
        Write-Host "FAIL $($f.Name)"
        $log | Select-Object -First 6 | ForEach-Object { Write-Host "    $_" }
    } else {
        Write-Host "ok   $($f.Name)"
    }
}
Write-Host "----"
if ($fail.Count) { Write-Host "СЛОМАНО: $($fail -join ', ')"; exit 1 }
Write-Host "все собрались: $($sources.Count - 1) файлов"
