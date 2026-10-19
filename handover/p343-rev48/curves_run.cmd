@echo off
rem P343 corpus efficiency curves (physics 27): all stale by phys=26 stamp, guard recomputes without --force.
set P=D:\BqMoni_Claude\p343
set W=D:\BqMoni_Claude\p343\wt
set PR=D:\BqMoni_Claude\p343\wt\tools\effmaker\probes\build_p343
cd /d %W%
"%PR%\CorpusEffProbe.exe" --dir=%W%\tools\CORPUS\corpus\geometries --spectra=%W%\tools\CORPUS\corpus\spectra --n=400000 > %P%\logs\curves.log 2> %P%\logs\curves.err
echo curves exit %ERRORLEVEL% %DATE% %TIME% > %P%\logs\curves_done.txt
