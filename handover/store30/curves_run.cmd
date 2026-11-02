@echo off
rem store30: corpus efficiency curves (physics 30) on CPU, below-normal priority, in parallel with the GPU store count.
rem The scene dir has no matrices on purpose: the probe prints MATRIX MISSING and exits 1 for that reason; store response is filled by mx_swap.py --into.
set S=D:\BqMoni_Claude\store30
set PR=D:\BqMoni_Claude\store30\wt\tools\effmaker\probes\build_s30
cd /d %S%
start "" /b /wait /belownormal "%PR%\CorpusEffProbe.exe" --dir=%S%\curvesdir --spectra=D:\BqMoni_Claude\store30\wt\tools\CORPUS\corpus\spectra --n=400000 > %S%\logs\curves_par.log 2> %S%\logs\curves_par.err
echo curves_par exit %ERRORLEVEL% %DATE% %TIME% > %S%\logs\curves_par_done.txt
