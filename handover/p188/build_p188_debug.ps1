# П188 — Debug-набор worktree: приложение bin\Debug_p188 + штатный каталог проб tools\effmaker\probes\build (для сторожей check_all)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'D:\BqMoni_Claude\p183\wt'
$logs = 'D:\BqMoni_Claude\p188\art'
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$codes = "$logs\codes_p188.txt"
Set-Location $root
"=== сборка П188 [debug] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $root rev-parse --short HEAD) dirty=$((git -C $root status --short | Measure-Object).Count) ===" | Out-File -Append $codes
& $msb "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Debug_p188\" "/p:IntermediateOutputPath=obj\Debug_p188\" *> "$logs\build_app_debug.log"
"Debug app code=$LASTEXITCODE -> bin\Debug_p188" | Out-File -Append $codes
& pwsh -NoLogo -NoProfile -File "$root\tools\effmaker\probes\build_all.ps1" -Bin "$root\BecquerelMonitor\bin\Debug_p188" *> "$logs\build_probes_debug.log"
"probes build_all (штатный каталог) code=$LASTEXITCODE" | Out-File -Append $codes
$a = (Get-FileHash "$root\BecquerelMonitor\bin\Debug_p188\BecquerelMonitor.exe").Hash.ToLower()
$p = (Get-FileHash "$root\tools\effmaker\probes\build\BecquerelMonitor.exe").Hash.ToLower()
"sha256 debug app=$a probe=$p равны=$($a -eq $p) проб=$((Get-ChildItem "$root\tools\effmaker\probes\build" -Filter *.exe).Count)" | Out-File -Append $codes
