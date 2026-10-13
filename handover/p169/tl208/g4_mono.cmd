@echo off
set R="C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat"
set S=D:\BqMoni_Claude\p169\g4\G1S_contact.scene
call %R% vacuum seed 20260928 scene %S% hist 2614.511 2000000 0.5 > D:\BqMoni_Claude\p169\g4\mono_2614.log 2> D:\BqMoni_Claude\p169\g4\mono_2614.err
call %R% vacuum seed 20260928 scene %S% hist 583.187 2000000 0.5 > D:\BqMoni_Claude\p169\g4\mono_583.log 2> D:\BqMoni_Claude\p169\g4\mono_583.err
echo mono rc=%ERRORLEVEL% > D:\BqMoni_Claude\p169\g4\status_mono.txt
