# Полоса F8 (A219). Сборка приложения в Debug_F8 / obj\F8 и ReasonProbe вручную тем же csc,
# что build_all.ps1, в build_f8 (каталог обставлен планом прогоном build_all.ps1 -Out build_f8).
# Вывод плеча и полный вывод пробы кладутся в handover\f8-a219\<Tag>.txt и <Tag>_full.txt.
param([string]$Tag = 'run')
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\f8-a219'
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F8\' /p:IntermediateOutputPath='obj\F8\' /v:m /nologo 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
"MSBUILD EXIT=0"

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_F8'); $Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_f8')
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll","/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")
. tools\CORPUS\scripts\appwd_plan.ps1
$sources = @(Get-AppWdProbeSources -Repo (Get-Location).Path)
$companions = @($sources | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
# Приложение рядом с пробой — свежее, по плану сборки (копия из $Bin), сверка sha256.
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) { "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11 }
"sha256 приложения рядом с пробой = sha256 Debug_F8: " + (Get-FileHash "$Out\BecquerelMonitor.exe").Hash.Substring(0,16) + "…"
$exe = "$Out\ReasonProbe.exe"
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs (Resolve-Path 'tools\effmaker\probes\ReasonProbe.cs').Path @companions 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
"CSC EXIT=0"

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$out = & $exe 2>&1
$code = $LASTEXITCODE
$out | Set-Content -Encoding utf8 "$art\$Tag`_full.txt"
$i = [array]::IndexOf(@($out), ($out | Where-Object { $_ -like 'ПЛЕЧО петля через ветвь*' } | Select-Object -First 1))
$j = [array]::IndexOf(@($out), ($out | Where-Object { $_ -like 'ПЛЕЧО предел по дереву*' } | Select-Object -First 1))
$arm = @($out[$i..($j-1)])
$arm | Set-Content -Encoding utf8 "$art\$Tag.txt"
$arm
""
"плеч (строк ПЛЕЧО): " + ($out | Where-Object { $_ -like 'ПЛЕЧО *' }).Count
"проверок ДА: " + ($out | Where-Object { $_ -match '\sДА$' }).Count + ", НЕТ: " + ($out | Where-Object { $_ -match '\sНЕТ$' }).Count
"пометок (1/3): " + ($arm | Select-String -SimpleMatch '(1/3)' | Measure-Object).Count + ", (2/3): " + ($arm | Select-String -SimpleMatch '(2/3)' | Measure-Object).Count + ", (3/3): " + ($arm | Select-String -SimpleMatch '(3/3)' | Measure-Object).Count
$out | Select-Object -Last 1
"PROBE EXIT=$code"
