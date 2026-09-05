# Полоса G4 (06.09.2026, `T237`): обстановка каталога проб `tools\effmaker\probes\build_g4`
# от сборки приложения `bin\Debug_G4` и компиляция НАЗВАННЫХ проб тем же `csc`,
# что `build_all.ps1`, с довесками, выведенными тем же правилом (файл без `Main`).
# `build_all.ps1` не зовётся: он компилирует все пробы дерева разом, а в дереве
# идут чужие полосы с незакоммиченными пробами.
#
#   pwsh handover\g4-target-framework\build_g4.ps1 -Probes A,B [-Extra <путь.cs>,…]
#        [-SkipTf] [-Suffix _old] [-NoSetup]
#
#   -Probes  имена проб в `tools\effmaker\probes` (без `.cs`);
#   -Extra   полные пути к `.cs` вне дерева (черновые копии), собираются как пробы;
#   -SkipTf  НЕ класть довесок `_TargetFramework.cs` (плечо «по-старому»);
#   -Suffix  добавка к имени exe (`CultureProbeO14_old.exe`), чтобы два плеча
#            лежали рядом; конфиг `<имя>.exe.config` кладётся под тем же именем;
#   -NoSetup не обставлять каталог заново (уже обставлен этим же скриптом).
#
# Даты скопированных файлов ставятся текущим временем (Copy-Item переносит дату
# источника). Каждая проба сверяется: exe свежее исходника.
param(
    [string[]]$Probes = @(),
    [string[]]$Extra = @(),
    [switch]$SkipTf,
    [string]$Suffix = '',
    [switch]$NoSetup
)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_G4')
$Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_g4')
New-Item -ItemType Directory -Force $Out | Out-Null

if (-not $NoSetup) {
    Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
    Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
    if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) { "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11 }
    Get-ChildItem -LiteralPath $Bin -File -Filter '*.dll'    | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
    Get-ChildItem -LiteralPath $Bin -File -Filter '*.sqlite' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Out -Force }
    foreach ($d in @('runtimes', 'ru', 'config')) {
        if (-not (Test-Path -LiteralPath "$Bin\$d")) { "В $Bin НЕТ КАТАЛОГА $d"; exit 11 }
        robocopy "$Bin\$d" "$Out\$d" /E /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { "ROBOCOPY $d EXIT=$LASTEXITCODE"; exit 11 }
    }
    $now = Get-Date
    Get-ChildItem -LiteralPath $Out -Recurse -File | ForEach-Object { $_.LastWriteTime = $now }
    foreach ($must in @('BecquerelMonitor.exe', 'Microsoft.Data.Sqlite.dll', 'SQLitePCLRaw.core.dll', 'SpecUtilsNet.dll',
                        'matdb.sqlite', 'nucdb.sqlite', 'schemedb.sqlite',
                        'runtimes\win-x64\native\e_sqlite3.dll', 'ru\BecquerelMonitor.resources.dll',
                        'config\BecquerelMonitor.xml', 'config\NuclideDefinition.xml', 'config\device', 'config\ROI')) {
        if (-not (Test-Path -LiteralPath "$Out\$must")) { "В КАТАЛОГЕ ПРОБ НЕТ $must"; exit 11 }
    }
    "КАТАЛОГ ПРОБ ОБСТАВЛЕН: $Out"
}

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
          '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
          "/r:$Bin\Microsoft.Data.Sqlite.dll", "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
. tools\CORPUS\scripts\appwd_plan.ps1
$sources = @(Get-AppWdProbeSources -Repo (Get-Location).Path)
$companions = @($sources | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
if ($SkipTf) { $companions = @($companions | Where-Object { [IO.Path]::GetFileName($_) -ne '_TargetFramework.cs' }) }
"довески: " + (($companions | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', ') + ($(if ($SkipTf) { '  [БЕЗ _TargetFramework.cs]' } else { '' }))

$jobs = @()
$Probes = @(($Probes -join ',') -split ',' | Where-Object { $_ })
$Extra  = @(($Extra  -join ',') -split ',' | Where-Object { $_ })
foreach ($p in $Probes) { $jobs += (Resolve-Path "tools\effmaker\probes\$p.cs").Path }
foreach ($e in $Extra)  { $jobs += (Resolve-Path $e).Path }
$bad = 0
foreach ($src in $jobs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($src) + $Suffix
    $exe = "$Out\$name.exe"
    Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe.config" -Destination "$Out\$name.exe.config" -Force
    (Get-Item -LiteralPath "$Out\$name.exe.config").LastWriteTime = Get-Date
    $log = & $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs $src @companions 2>&1
    if ($LASTEXITCODE -ne 0) { "CSC $name EXIT=$LASTEXITCODE"; $log | Select-Object -First 8 | ForEach-Object { "    $_" }; $bad++; continue }
    $srcTime = (Get-Item -LiteralPath $src).LastWriteTime
    $exeTime = (Get-Item -LiteralPath $exe).LastWriteTime
    if ($exeTime -le $srcTime) { "$name.exe ($exeTime) НЕ СВЕЖЕЕ исходника ($srcTime)"; $bad++; continue }
    "CSC $name EXIT=0  exe $($exeTime.ToString('HH:mm:ss'))  >  cs $($srcTime.ToString('dd.MM HH:mm:ss'))"
}
if ($bad) { "СБОРКА G4: НЕ СОБРАЛИСЬ $bad"; exit 12 }
"СБОРКА G4 ПРОШЛА: $($jobs.Count) проб"
