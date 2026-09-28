@echo off
rem P162 25.09.2026 -- TAIL of the P161 night chain (physics 24, rev34), from the curves step.
rem P161 (night_rev34.cmd) finished the store (49/49 phys=24, code 0, 04:58) and stopped at curves (code 1):
rem RC103_lu_front median node noise 5.47 % > 5 % (AMBER95 grid below 40 keV, 86 nodes). 48 curves written.
rem Decision (coordinator, 25.09.2026, "prodolzhay"): ALL curves recomputed at 400 000 histories per node
rem (was 200 000); one recipe for all scenes; noise guard 5 % untouched; matrix store NOT recomputed.
rem Branch p147-physics24, worktree D:\BqMoni_Claude\p147\wt, HEAD 0db32dc8 (code = d3bd492b), probes build_rel_p161.
rem Steps 3 (curves) gates everything. Step 4 (showcase store) does NOT gate 5-7. Steps 5-7 gate each other.
rem Each step: own log + own *_done.txt. Codes: tools\CORPUS\scripts\detached_run.ps1 -Decode <code>.
rem PATH is set EXPLICITLY, %PATH% is NOT appended (stray double quote in the user PATH, P140 finding (a)).
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set WT=D:\BqMoni_Claude\p147\wt
set PROBES=D:\BqMoni_Claude\p147\wt\tools\effmaker\probes\build_rel_p161
set STORE=D:\BqMoni_Claude\p161\store
set LOGS=D:\BqMoni_Claude\p162\logs
set ARM=D:\BqMoni_Claude\p162\corpus_arm.ps1
set PARTIAL=0
cd /d %PROBES%
echo start %date% %time% > %LOGS%\_start.txt

rem ===== step 3: efficiency curves of all 49 scenes at 400 000 histories per node (--force) =====
%PROBES%\CorpusEffProbe.exe --dir=%STORE% --spectra=%WT%\tools\CORPUS\corpus\spectra --n=400000 --force >> %LOGS%\curves.log 2>> %LOGS%\curves.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\3_curves_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 4: FSA showcase store (rebuild_store.ps1 -Force); does NOT gate =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage show; exit $LASTEXITCODE" >> %LOGS%\show.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\4_show_done.txt
if not "%RC%"=="0" set PARTIAL=1

rem ===== step 5: run workdir wd_p161 (mk_appwd + check_appwd) =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage wd; exit $LASTEXITCODE" >> %LOGS%\wd.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\5_wd_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 6: corpus run, SMALL base (mini.csv) -> out_rev34_mini =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage mini; exit $LASTEXITCODE" >> %LOGS%\mini.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\6_mini_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 7: corpus run, FULL corpus -> out_rev34_full =====
%PWSH% -NoProfile -Command "& '%ARM%' -Stage full; exit $LASTEXITCODE" >> %LOGS%\full.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\7_full_done.txt
if not "%RC%"=="0" goto fail

if "%PARTIAL%"=="1" goto partial
echo ALL OK %date% %time% > %LOGS%\ALL_DONE.txt
goto end
:partial
echo CORPUS OK, SHOWCASE FAILED (see 4_show_done.txt, art\showcase_store.log) %date% %time% > %LOGS%\CHAIN_PARTIAL.txt
goto end
:fail
echo CHAIN STOPPED at RC=%RC% %date% %time% > %LOGS%\CHAIN_FAILED.txt
:end
echo finish %date% %time% > %LOGS%\_finish.txt