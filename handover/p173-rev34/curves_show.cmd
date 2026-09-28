@echo off
rem p173 28.09.2026 -- physics 24 branch p147-physics24 (merged master cbd722fc + floor 0.10), worktree D:\BqMoni_Claude\p147\wt.
rem Step 1: corpus efficiency curves of all 49 scenes, CorpusEffProbe --n=400000 --force, store D:\BqMoni_Claude\p161\store
rem (same recipe as P162/P164 rev34/rev34b: positive control -- curves must equal rev34b bitwise, EfficiencyMaker untouched).
rem Step 2: FSA showcase store rebuild_store.ps1 -Force (physics 24, like P162). Does not gate anything.
rem PATH is set EXPLICITLY, %PATH% is NOT appended (P140 finding (a)).
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set WT=D:\BqMoni_Claude\p147\wt
set PROBES=D:\BqMoni_Claude\p147\wt\tools\effmaker\probes\build_rel_p173
set STORE=D:\BqMoni_Claude\p161\store
set LOGS=D:\BqMoni_Claude\p173\logs
cd /d %PROBES%
echo start %date% %time% > %LOGS%\_start.txt

rem ===== step 1: curves =====
%PROBES%\CorpusEffProbe.exe --dir=%STORE% --spectra=%WT%\tools\CORPUS\corpus\spectra --n=400000 --force >> %LOGS%\curves.log 2>> %LOGS%\curves.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\1_curves_done.txt

rem ===== step 2: showcase store =====
cd /d %WT%
%PWSH% -NoProfile -Command "& '%WT%\tools\fsa_showcase\rebuild_store.ps1' -Probes '%PROBES%' -Force; exit $LASTEXITCODE" >> %LOGS%\show.log 2>&1
set RC2=%errorlevel%
echo exit %RC2% %date% %time% > %LOGS%\2_show_done.txt
echo curves %RC% show %RC2% %date% %time% > %LOGS%\ALL_DONE.txt
endlocal
