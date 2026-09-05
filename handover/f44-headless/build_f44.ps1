# Полоса F44, `A245`: снятие свежей регрессии безоконного пути.
# ЭТО ТОТ САМЫЙ ПОРЯДОК, КАКИМ СОБРАНО ИЗМЕРЕННОЕ, а не пересказ.
#
# ⛔ ПОЧЕМУ СБОРКА В ОТДЕЛЬНОЙ КОПИИ, А НЕ В ДЕРЕВЕ. 05.09.2026 дерево НЕ
#    СОБИРАЛОСЬ вовсе: живая полоса F43 держала свои `EfficiencyMaker/*.cs`
#    на полпути — код звал `Resources.EfficiencyMakerGridWidened`,
#    `…GridFallback`, `…SampleIsAir`, которых в `Resources.resx` ещё нет
#    (`CS0117` ×3). Ждать чужую полосу нельзя, а трогать её файлы — тем
#    более. Поэтому дерево КОПИРУЕТСЯ, и в КОПИИ (только в ней) два файла
#    F43 возвращаются к `HEAD`. Рабочее дерево при этом не трогается ничем:
#    ни `checkout`, ни `stash`, ни правкой чужого файла.
#
# ⛔ `build_all.ps1` НЕ ЗАПУСКАТЬ — его правит полоса F40. Проба собирается
#    вручную тем же `csc`, каким её зовёт `build_all.ps1`.
# ⛔ Приложение и проба собираются ОДНИМ движением (`T233`): проба кладётся
#    ПРЯМО в выходной каталог приложения, поэтому «свежий exe при старых
#    пробах» тут невозможен по построению.
param(
  [string]$Scratch = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\0dfa526c-1c92-45cd-b97b-70d6b9e6b585\scratchpad\f44build',
  [switch]$Before   # собрать плечо «ДО правки»: `DeviceConfigForm.cs` из HEAD
)
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

# ── 1. копия дерева ────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force $Scratch | Out-Null
# ⚠ ЧЕРЕЗ ОПЕРАТОР ВЫЗОВА, А НЕ `Start-Process -ArgumentList`: последний
#   режет путь этого репозитория по пробелам («BQ Eng res .NET 4.8»), и
#   опыт мерит пустоту — грабля уже стоила четырёх прогонов.
& robocopy "$repo\BecquerelMonitor" "$Scratch\BecquerelMonitor" /E /XD bin obj .vs /NFL /NDL /NJH /NJS /NP > $null
& robocopy "$repo\packages"         "$Scratch\packages"         /E /NFL /NDL /NJH /NJS /NP > $null
Copy-Item "$repo\nuget.config" $Scratch -Force
Copy-Item "$repo\BecquerelMonitor.sln" $Scratch -Force

# ── 2. в КОПИИ: файлы чужой полосы — к HEAD ────────────────────────────────
Set-Location $repo
foreach ($f in 'BecquerelMonitor/EfficiencyMaker/EfficiencyCalculation.cs',
               'BecquerelMonitor/EfficiencyMaker/GeometryModel.cs') {
  & git cat-file -p "HEAD:$f" > (Join-Path $Scratch ($f -replace '/','\'))
}
if ($Before) {
  & git cat-file -p 'HEAD:BecquerelMonitor/DeviceConfigForm.cs' > "$Scratch\BecquerelMonitor\DeviceConfigForm.cs"
}
$OutDir = if ($Before) { 'bin\Debug_F44_before\' } else { 'bin\Debug_F44\' }
$ObjDir = if ($Before) { 'obj\F44b\' }            else { 'obj\F44\' }

# ── 3. сборка приложения ───────────────────────────────────────────────────
Set-Location $Scratch; [Environment]::CurrentDirectory = $Scratch
# ⚠ `PackageReference` без `obj` требует восстановления; источник пакетов —
#   свой `packages` (см. `nuget.config`), сеть не нужна.
& $msb 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Restore /v:m /nologo 2>&1 | Select-Object -Last 2
if ($LASTEXITCODE -ne 0) { "RESTORE EXIT=$LASTEXITCODE"; exit 9 }
& $msb 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
       /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath=$OutDir `
       /p:IntermediateOutputPath=$ObjDir /v:m /nologo 2>&1 | Select-Object -Last 6
if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
"MSBUILD EXIT=0"

# ── 4. проба — В ТОТ ЖЕ каталог ────────────────────────────────────────────
$Bin = [IO.Path]::GetFullPath("$Scratch\BecquerelMonitor\$OutDir")
$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll',
          '/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll",
          "/r:$Bin\MathNet.Numerics.dll")
$p = 'HeadlessButton1ProbeF44'
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Bin\$p.exe" @refs "$repo\handover\f44-headless\$p.cs" 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
# ⛔ `<проба>.exe.config` ОБЯЗАТЕЛЕН (`T32`).
Copy-Item "$Bin\BecquerelMonitor.exe.config" "$Bin\$p.exe.config" -Force
"CSC EXIT=0"
"КАТАЛОГ=$Bin"
"APP SHA=" + (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash.Substring(0,16)
"BUILD OK"

# ── как гонялось ───────────────────────────────────────────────────────────
# замер:               pwsh watch_run.ps1 -Exe "<Bin>\HeadlessButton1ProbeF44.exe" -Log run-after.txt
# контроль счётчика:   ... -ExeArgs '--window-control' -LimitSec 30      (ждём 1 окно, код 7)
# плечо «ДО правки»:   build_f44.ps1 -Before, затем watch_run.ps1 -LimitSec 45 (ждём 1 окно и УБИТ)
# вид сообщения:       pwsh face_dump.ps1 -Exe "<Bin>\HeadlessButton1ProbeF44.exe" -Log face.txt
