@echo off
set R="C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat"
set S=D:\BqMoni_Claude\p166\g4\G1S_contact.scene
call %R% vacuum seed 20260928 ionhist 0.5 3000 scene %S% ion 39 88 4000000 898.0 1836.1 2734.1 1325.1 > D:\BqMoni_Claude\p166\g4\ion_y88.log 2> D:\BqMoni_Claude\p166\g4\ion_y88.err
echo y88ion rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
call %R% vacuum seed 20260928 scene %S% hist 898.042 2000000 0.5 > D:\BqMoni_Claude\p166\g4\mono_898.log 2> D:\BqMoni_Claude\p166\g4\mono_898.err
echo m898 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
call %R% vacuum seed 20260928 scene %S% hist 1836.063 2000000 0.5 > D:\BqMoni_Claude\p166\g4\mono_1836.log 2> D:\BqMoni_Claude\p166\g4\mono_1836.err
echo m1836 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
