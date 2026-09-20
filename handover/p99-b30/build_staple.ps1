# П99 — штатные каталоги главного дерева после переноса: MSBuild /t:Rebuild Debug -> bin\Debug_Codex (иначе Copy-Item
# оставляет mtime и инкрементальная сборка отдаёт СТАРЫЙ exe при коде 0), затем build_all.ps1 -Out probes\build.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = 'D:\BqMoni_Claude\p99\art'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
Set-Location $root
"staple start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes_staple.txt"
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_Codex\' /nologo /v:m *> "$art\msbuild_debug_codex.log"
$code = $LASTEXITCODE
"debug_codex rebuild code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_staple.txt"
if ($code -ne 0) { Get-Content "$art\msbuild_debug_codex.log" | Select-String 'error' | Select-Object -First 10; exit $code }
pwsh -File "$root\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Debug_Codex' -Out 'tools\effmaker\probes\build' *> "$art\build_all_staple.log"
$code = $LASTEXITCODE
"probes\build code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes_staple.txt"
Get-Content "$art\build_all_staple.log" | Select-String 'заверен|ОТКАЗ|error' | Select-Object -Last 3 | ForEach-Object { $_.Line } | Out-File -Append "$art\codes_staple.txt"
# символ в exe: RadiaCode-101 с зазором — ищем строку ключа в GeometryPresets через отражение проще пробой; здесь — sha
"debug exe sha256 $((Get-FileHash "$root\BecquerelMonitor\bin\Debug_Codex\BecquerelMonitor.exe").Hash.ToLower())" | Out-File -Append "$art\codes_staple.txt"
Get-Content "$art\codes_staple.txt" | Select-Object -Last 5
exit $code
