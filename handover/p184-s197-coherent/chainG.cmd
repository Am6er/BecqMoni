@echo off
rem P184 chain G: g4cf coherent on/off at 32/60 keV on the P164 scene
cd /d D:\BqMoni_Claude\p184\g4\
echo start g4_off32a.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
call D:\BqMoni_Claude\p184\run_g4cf.bat vacuum norayl seed 16432 scene D:\BqMoni_Claude\p184\g4\scene_P164.txt hist 32 200000000 0.1 > D:\BqMoni_Claude\p184\g4\g4_off32a.log 2> D:\BqMoni_Claude\p184\g4\g4_off32a.log.err
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\g4_off32a.log.err
echo done g4_off32a.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
echo start g4_off32b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
call D:\BqMoni_Claude\p184\run_g4cf.bat vacuum norayl seed 26432 scene D:\BqMoni_Claude\p184\g4\scene_P164.txt hist 32 200000000 0.1 > D:\BqMoni_Claude\p184\g4\g4_off32b.log 2> D:\BqMoni_Claude\p184\g4\g4_off32b.log.err
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\g4_off32b.log.err
echo done g4_off32b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
echo start g4_on32b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
call D:\BqMoni_Claude\p184\run_g4cf.bat vacuum seed 26432 scene D:\BqMoni_Claude\p184\g4\scene_P164.txt hist 32 200000000 0.1 > D:\BqMoni_Claude\p184\g4\g4_on32b.log 2> D:\BqMoni_Claude\p184\g4\g4_on32b.log.err
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\g4_on32b.log.err
echo done g4_on32b.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
echo start g4_off60a.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
call D:\BqMoni_Claude\p184\run_g4cf.bat vacuum norayl seed 16460 scene D:\BqMoni_Claude\p184\g4\scene_P164.txt hist 60 100000000 0.1 > D:\BqMoni_Claude\p184\g4\g4_off60a.log 2> D:\BqMoni_Claude\p184\g4\g4_off60a.log.err
echo code=%ERRORLEVEL% >> D:\BqMoni_Claude\p184\g4\g4_off60a.log.err
echo done g4_off60a.log %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
echo ALLDONE %DATE% %TIME% >> D:\BqMoni_Claude\p184\g4\chainG_status.txt
