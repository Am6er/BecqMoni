# П343: Release-сборка нового кода и каталог проб (A77 — коды возврата пишутся в codes.txt).
$ErrorActionPreference = 'Continue'
$P = 'D:\BqMoni_Claude\p343'
$W = "$P\wt"
$log = "$P\logs"
$codes = "$log\codes_build_p343.txt"
Set-Location $W
"start $(Get-Date -Format 'HH:mm:ss')" | Set-Content $codes
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /restore /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false '/p:OutputPath=bin\Release_p343\' '/p:IntermediateOutputPath=obj\Release_p343\' /v:m /nologo *> "$log\msbuild_p343.log"
"msbuild $LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Add-Content $codes
if ($LASTEXITCODE -ne 0) { exit 1 }
& pwsh -NoProfile -File tools\effmaker\probes\build_all.ps1 -Bin 'BecquerelMonitor\bin\Release_p343' -Out 'tools\effmaker\probes\build_p343' *> "$log\build_all_p343.log"
"build_all $LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Add-Content $codes
$a = (Get-FileHash "$W\BecquerelMonitor\bin\Release_p343\BecquerelMonitor.exe").Hash.ToLower()
$b = (Get-FileHash "$W\tools\effmaker\probes\build_p343\BecquerelMonitor.exe").Hash.ToLower()
"exe app $a" | Add-Content $codes
"exe probes $b" | Add-Content $codes
$d1 = (Get-FileHash "$W\BecquerelMonitor\bin\Release_p343\rmgpu_f.dll").Hash.ToLower()
$d2 = (Get-FileHash "$W\tools\effmaker\gpu\bin\rmgpu_f.dll").Hash.ToLower()
$d3 = (Get-FileHash "$W\BecquerelMonitor\rmgpu_f.dll").Hash.ToLower()
"dll app-out $d1" | Add-Content $codes
"dll gpu-bin $d2" | Add-Content $codes
"dll shipped $d3" | Add-Content $codes
"done $(Get-Date -Format 'HH:mm:ss')" | Add-Content $codes
