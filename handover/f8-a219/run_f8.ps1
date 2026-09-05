param([string]$Tag)
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F8\' /p:IntermediateOutputPath='obj\F8\' /v:m /nologo 2>&1 | Select-Object -Last 1
"MSBUILD EXIT=$LASTEXITCODE"; if ($LASTEXITCODE -ne 0) { exit 10 }
pwsh -File tools\effmaker\probes\build_all.ps1 -Bin 'BecquerelMonitor\bin\Debug_F8' -Out 'tools\effmaker\probes\build_f8' 2>&1 | Select-Object -Last 2
"BUILD_ALL EXIT=$LASTEXITCODE"; if ($LASTEXITCODE -ne 0) { exit 11 }
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$art = 'handover\f8-a219'
$out = & tools\effmaker\probes\build_f8\ReasonProbe.exe 2>&1; $code = $LASTEXITCODE
$out | Set-Content -Encoding utf8 "$art\$Tag`_full.txt"
$i = [array]::IndexOf(@($out), ($out | ? { $_ -like 'ПЛЕЧО петля через ветвь*' } | select -First 1)); $j = [array]::IndexOf(@($out), ($out | ? { $_ -like 'ПЛЕЧО предел по дереву*' } | select -First 1))
$arm = @($out[$i..($j-1)]); $arm | Set-Content -Encoding utf8 "$art\$Tag.txt"; $arm
""; "плеч: " + ($out | ? { $_ -like 'ПЛЕЧО *' }).Count; "ДА: " + ($out | ? { $_ -match '\sДА$' }).Count + ", НЕТ: " + ($out | ? { $_ -match '\sНЕТ$' }).Count
$said = ($arm | ? { $_ -like '  сказано:*' })
"в строке сказано: (1/3)=" + ([regex]::Matches($said,'\(1/3\)').Count) + " (2/3)=" + ([regex]::Matches($said,'\(2/3\)').Count) + " (3/3)=" + ([regex]::Matches($said,'\(3/3\)').Count)
$out | select -Last 1; "PROBE EXIT=$code"
"sha рядом с пробой = Debug_F8: " + ((Get-FileHash tools\effmaker\probes\build_f8\BecquerelMonitor.exe).Hash -eq (Get-FileHash BecquerelMonitor\bin\Debug_F8\BecquerelMonitor.exe).Hash)
