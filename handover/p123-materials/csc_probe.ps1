# П123 — быстрая компиляция ОДНОЙ пробы тем же csc и теми же ссылками, что build_all.ps1 (для итераций;
# итоговый каталог собирается build_all.ps1 целиком). Образец — D:\BqMoni_Claude\p122\csc_probe.ps1.
param([Parameter(Mandatory)][string]$Probe, [string]$Tag = 'p123')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Bin = "D:\BqMoni_Claude\p122\wt\BecquerelMonitor\bin\Release_$Tag"
$Out = "D:\BqMoni_Claude\p122\wt\tools\effmaker\probes\build_$Tag"
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$Bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
          '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$Bin\Microsoft.Data.Sqlite.dll",
          "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$Out\$Probe.exe" @refs "D:\BqMoni_Claude\p122\wt\tools\effmaker\probes\$Probe.cs"
$code = $LASTEXITCODE
if (-not (Test-Path "$Out\$Probe.exe.config")) { Copy-Item "$Out\ResponseRowDumpProbe.exe.config" "$Out\$Probe.exe.config" -Force }
"$Probe [$Tag] csc EXIT=$code"
exit $code
