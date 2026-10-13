@echo off
rem P184 chain G3: extra g4cf stream at 60 keV, coherent ON, P164 scene, after chain G
cd /d D:\BqMoni_Claude\p184\g4\
:w
findstr /c:ALLDONE D:\BqMoni_Claude\p184\g4\chainG_status.txt >nul 2>&1 || (ping -n 31 127.0.0.1 >nul & goto w)
echo start g4_on60b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG3_status.txt
call D:\BqMoni_Claude\p184\run_g4cf.bat vacuum seed 26460 scene D:\BqMoni_Claude\p184\g4\scene_P164.txt hist 60 200000000 0.1 > D:\BqMoni_Claude\p184\g4\g4_on60b.log 2> D:\BqMoni_Claude\p184\g4\g4_on60b.log.err
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\g4_on60b.log.err
echo done g4_on60b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG3_status.txt
echo ALLDONE %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG3_status.txt
