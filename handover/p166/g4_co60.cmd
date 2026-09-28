@echo off
set R="C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat"
set S=D:\BqMoni_Claude\p166\g4\G1S_contact.scene
call %R% vacuum seed 20260928 ionhist 0.5 2700 scene %S% ion 27 60 4000000 1173.2 1332.5 2505.7 > D:\BqMoni_Claude\p166\g4\ion_co60.log 2> D:\BqMoni_Claude\p166\g4\ion_co60.err
echo ion rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
call %R% vacuum seed 20260928 scene %S% hist 1173.228 2000000 0.5 > D:\BqMoni_Claude\p166\g4\mono_1173.log 2> D:\BqMoni_Claude\p166\g4\mono_1173.err
echo m1173 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
call %R% vacuum seed 20260928 scene %S% hist 1332.492 2000000 0.5 > D:\BqMoni_Claude\p166\g4\mono_1332.log 2> D:\BqMoni_Claude\p166\g4\mono_1332.err
echo m1332 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
