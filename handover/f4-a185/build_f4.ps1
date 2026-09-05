# Сборка приложения в Debug_F4 и ReasonProbe вручную тем же csc, что build_all.ps1,
# в build_f4 (каталог обставлен планом при первом прогоне build_all.ps1).
param([string]$Tag = 'run')
$ErrorActionPreference = 'Continue'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$s = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\aebc304d-5735-4e42-96c6-4bf53152b58d\scratchpad'
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F4\' /p:IntermediateOutputPath='obj\F4\' /v:m /nologo 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { "MSBUILD EXIT=$LASTEXITCODE"; exit 10 }
"MSBUILD EXIT=0"

$Bin = [IO.Path]::GetFullPath('BecquerelMonitor\bin\Debug_F4'); $Out = [IO.Path]::GetFullPath('tools\effmaker\probes\build_f4')
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe",'/r:System.dll','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/r:$Bin\Microsoft.Data.Sqlite.dll","/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll","/r:$facades\netstandard.dll")
. tools\CORPUS\scripts\appwd_plan.ps1
$sources = @(Get-AppWdProbeSources -Repo (Get-Location).Path)
$companions = @($sources | Where-Object { -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet) } | ForEach-Object { $_.FullName })
# Приложение рядом с пробой — свежее, по плану сборки (копия из $Bin).
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.exe" -Destination "$Out\BecquerelMonitor.exe" -Force
Copy-Item -LiteralPath "$Bin\BecquerelMonitor.pdb" -Destination "$Out\BecquerelMonitor.pdb" -Force -ErrorAction SilentlyContinue
if ((Get-FileHash "$Out\BecquerelMonitor.exe").Hash -ne (Get-FileHash "$Bin\BecquerelMonitor.exe").Hash) { "ПРИЛОЖЕНИЕ РЯДОМ С ПРОБОЙ НЕ СОШЛОСЬ"; exit 11 }
$exe = "$Out\ReasonProbe.exe"
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$exe" @refs (Resolve-Path 'tools\effmaker\probes\ReasonProbe.cs').Path @companions 2>&1
if ($LASTEXITCODE -ne 0) { "CSC EXIT=$LASTEXITCODE"; exit 12 }
"CSC EXIT=0"

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$out = & $exe 2>&1
$code = $LASTEXITCODE
$out | Set-Content -Encoding utf8 "$s\$Tag`_full.txt"
$i = [array]::IndexOf(@($out), ($out | Where-Object { $_ -like 'ПЛЕЧО петля через ветвь*' } | Select-Object -First 1))
$arm = $out[$i..($i+9)] | Where-Object { $_ -notlike 'ПЛЕЧО предел по дереву*' }
$arm = @($out[$i..($i+7)])
$arm | Set-Content -Encoding utf8 "$s\$Tag.txt"
$arm
""
"плеч (строк ПЛЕЧО): " + ($out | Where-Object { $_ -like 'ПЛЕЧО *' }).Count
"проверок ДА: " + ($out | Where-Object { $_ -match '\sДА$' }).Count + ", НЕТ: " + ($out | Where-Object { $_ -match '\sНЕТ$' }).Count
"двойных знаков в выводе: " + ($out | Select-String -SimpleMatch '<- … <- …' | Measure-Object).Count
$out | Select-Object -Last 1
"PROBE EXIT=$code"
