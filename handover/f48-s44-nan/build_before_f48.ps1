# Полоса F48: ОБРАТНЫЙ КОНТРОЛЬ. Собрать приложение БЕЗ правки и пробы рядом
# с ним, чтобы проверить, что приёмка умеет отказать.
#
# ⛔ Окно, в котором исходники приложения лежат «снятыми», — только сборка:
#    возврат стоит в `finally` и сверяется ПОБАЙТОВО. Пробы при этом не
#    трогаются вовсе: они те же, что и у зелёного плеча, — иначе плечи
#    отличались бы двумя вещами сразу.
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
[Environment]::CurrentDirectory = $repo

$AppDir = 'bin\Debug_F48_before'
$ObjDir = 'obj\F48_before\'

try {
  python 'handover\f48-s44-nan\strip_f48.py' --strip
  if ($LASTEXITCODE -ne 0) { "STRIP EXIT=$LASTEXITCODE"; exit 20 }

  & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath="$AppDir\" `
    /p:IntermediateOutputPath=$ObjDir /v:m /nologo 2>&1 | Select-Object -Last 6
  $build = $LASTEXITCODE
}
finally {
  python 'handover\f48-s44-nan\strip_f48.py' --restore
  if ($LASTEXITCODE -ne 0) { "⛔ ВОЗВРАТ НЕ СОШЁЛСЯ — исходники в снятом виде!"; exit 21 }
}

if ($build -ne 0) { "MSBUILD(before) EXIT=$build"; exit 22 }
"MSBUILD(before) EXIT=0"

pwsh -NoProfile -File 'handover\f48-s44-nan\build_f48.ps1' -SkipApp `
     -AppDir $AppDir -ProbeDir 'tools\effmaker\probes\build_f48_before'
if ($LASTEXITCODE -ne 0) { "PROBES(before) EXIT=$LASTEXITCODE"; exit 23 }
"BEFORE READY"
