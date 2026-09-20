# П103 — штатные каталоги ОСНОВНОГО дерева после переноса кода: MSBuild Debug -> bin\Debug_Codex (рецепт CLAUDE.md),
# build_all.ps1 -Bin BecquerelMonitor\bin\Debug_Codex -Out tools\effmaker\probes\build. Коды — в codes_staple.txt.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'D:\BqMoni_Claude\p103\art'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$codes = "$art\codes_staple.txt"
"staple start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $codes
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_Codex\' /nologo /v:m *> "$art\msbuild_debug_codex.log"
$c1 = $LASTEXITCODE
"msbuild Debug_Codex code=$c1 $(Get-Date -Format 'HH:mm:ss') exe $((Get-Item "$root\BecquerelMonitor\bin\Debug_Codex\BecquerelMonitor.exe").LastWriteTime.ToString('HH:mm:ss'))" | Out-File -Append $codes
Push-Location $root
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Debug_Codex' -Out 'tools\effmaker\probes\build' *> "$art\build_all_staple.log"
$c2 = $LASTEXITCODE
Pop-Location
"build_all staple code=$c2 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Get-Content "$art\build_all_staple.log" | Select-String 'заверен|ОТКАЗ|error' | Select-Object -Last 3 | ForEach-Object { $_.Line } | Out-File -Append $codes
Get-Content $codes | Select-Object -Last 5
exit ($c1 + $c2)
