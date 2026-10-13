@echo off
set R="C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat"
set S=D:\BqMoni_Claude\p166\g4\G1S_contact.scene
call %R% vacuum seed 20260928 ionhist 0.5 3800 scene %S% ion 81 208 4000000 2614.5 583.2 > D:\BqMoni_Claude\p166\g4\ion_tl208.log 2> D:\BqMoni_Claude\p166\g4\ion_tl208.err
echo tl208 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
call %R% vacuum seed 20260928 ionhist 0.5 2000 scene %S% ion 56 133 4000000 356.0 81.0 > D:\BqMoni_Claude\p166\g4\ion_ba133.log 2> D:\BqMoni_Claude\p166\g4\ion_ba133.err
echo ba133 rc=%ERRORLEVEL% >> D:\BqMoni_Claude\p166\g4\status.txt
