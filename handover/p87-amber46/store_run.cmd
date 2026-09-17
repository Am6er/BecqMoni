@echo off
rem P87 16.09.2026 (AMBER46): full store count, 46 scenes, format 9 (Q_k inside the matrix), store recipe
rem --threads=10 --target=0 --n=3000000 (S140); --force recounts the two control scenes too (determinism, A104).
cd /d D:\BqMoni_Claude\p87\wt\tools\effmaker\probes\build_p87
echo start %date% %time% > D:\BqMoni_Claude\p87\count_start.txt
D:\BqMoni_Claude\p87\wt\tools\effmaker\probes\build_p87\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p87\store --threads=10 --target=0 --n=3000000 --force --dump=D:\BqMoni_Claude\p87\art\dumps\store.csv > D:\BqMoni_Claude\p87\count.log 2> D:\BqMoni_Claude\p87\count.err
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p87\count_done.txt
