@echo off
cd /d D:\BqMoni_Claude\p99\wt\tools\effmaker\probes\build_p99
D:\BqMoni_Claude\p99\wt\tools\effmaker\probes\build_p99\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p99\store --only=G1S_point5,G1S_point25,RC103_point50,ASN16_point0_house,ASN16_point10_house --threads=10 --target=0 --n=3000000 --dump=D:\BqMoni_Claude\p99\art\dumps\store.csv 1>D:\BqMoni_Claude\p99\art\count.log 2>D:\BqMoni_Claude\p99\art\count.err
echo exit %ERRORLEVEL% >D:\BqMoni_Claude\p99\art\count_done.txt
