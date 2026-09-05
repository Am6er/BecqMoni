# Полоса F11 (A230). msbuild в Debug_F11 / obj\F11 -> build_all.ps1 в build_f11 -> ReasonProbe.
# Полный вывод: handover\f11-a230\<Tag>_full.txt; плечо «предел по дереву»: <Tag>.txt.
param([string]$Tag)
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Debug_F11\' /p:IntermediateOutputPath='obj\F11\' /v:m /nologo 2>&1 | Select-Object -Last 1
"MSBUILD EXIT=$LASTEXITCODE"; if ($LASTEXITCODE -ne 0) { exit 10 }
pwsh -File tools\effmaker\probes\build_all.ps1 -Bin 'BecquerelMonitor\bin\Debug_F11' -Out 'tools\effmaker\probes\build_f11' 2>&1 | Select-Object -Last 2
"BUILD_ALL EXIT=$LASTEXITCODE"; if ($LASTEXITCODE -ne 0) { exit 11 }
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$art = 'handover\f11-a230'
$out = @(& tools\effmaker\probes\build_f11\ReasonProbe.exe 2>&1); $code = $LASTEXITCODE
$out | Set-Content -Encoding utf8 "$art\$Tag`_full.txt"
$heads = @(0..($out.Count-1) | ? { $out[$_] -like 'ПЛЕЧО *' })
$i = @($heads | ? { $out[$_] -like 'ПЛЕЧО предел по дереву*' })[0]
$j = @($heads | ? { $_ -gt $i })[0]; if ($null -eq $j) { $j = $out.Count }
$arm = @($out[$i..($j-1)]); $arm | Set-Content -Encoding utf8 "$art\$Tag.txt"; $arm
""; "плеч: " + $heads.Count; "ДА: " + ($out | ? { $_ -match '\sДА$' }).Count + ", НЕТ: " + ($out | ? { $_ -match '\sНЕТ$' }).Count
$said = @($arm | ? { $_ -like '  сказано:*' }) -join "`n"
"в строках сказано: (1/3)=" + ([regex]::Matches($said,'\(1/3\)').Count) + " (2/3)=" + ([regex]::Matches($said,'\(2/3\)').Count) + " (3/3)=" + ([regex]::Matches($said,'\(3/3\)').Count) + " ' <- …'=" + ([regex]::Matches($said,' <- …').Count)
$out | select -Last 1; "PROBE EXIT=$code"
"sha рядом с пробой = Debug_F11: " + ((Get-FileHash tools\effmaker\probes\build_f11\BecquerelMonitor.exe).Hash -eq (Get-FileHash BecquerelMonitor\bin\Debug_F11\BecquerelMonitor.exe).Hash)
