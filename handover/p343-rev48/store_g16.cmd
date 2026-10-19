@echo off
rem P343 G1S16 crystal depth: 13 vessel scenes recomputed (stamp mismatch, no --force), physics 27, GPU.
set P=D:\BqMoni_Claude\p343
set PR=D:\BqMoni_Claude\p343\wt\tools\effmaker\probes\build_p343
set DLL=D:\BqMoni_Claude\p343\wt\tools\effmaker\gpu\bin\rmgpu_f.dll
cd /d %P%
"%PR%\CorpusMatrixProbe.exe" --dir=%P%\store --gpu=%DLL% --target=0 --n=3000000 > %P%\logs\store_g16.log 2> %P%\logs\store_g16.err
echo g16 exit %ERRORLEVEL% %DATE% %TIME% > %P%\logs\store_g16_done.txt
