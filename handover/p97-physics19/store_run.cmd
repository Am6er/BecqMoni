@echo off
rem P97 17-18.09.2026 (physics 19, key eltr ON by default): store count, recipe --threads=10 --target=0 --n=3000000 (S140).
rem full: --force recounts the two control scenes too (determinism, A104); resume: --only=<missing>, no --force.
cd /d D:\BqMoni_Claude\p97\wt\tools\effmaker\probes\build_p97
echo start %date% %time% > D:\BqMoni_Claude\p97\count_start.txt
D:\BqMoni_Claude\p97\wt\tools\effmaker\probes\build_p97\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p97\store --threads=10 --target=0 --n=3000000 --force --dump=D:\BqMoni_Claude\p97\art\dumps\store.csv > D:\BqMoni_Claude\p97\count.log 2> D:\BqMoni_Claude\p97\count.err
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p97\count_done.txt
