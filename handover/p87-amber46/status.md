# П87 — ход полосы (AMBER46, Q_k в матрице, формат 9)

- 00:40 worktree D:\BqMoni_Claude\p87\wt на 8b164a98; правки кода; сборка Release_p87/build_p87 код 0 (00:44)
- 00:45 снимок живого склада D:\BqMoni_Claude\p87\store_backup (184 файла, 58 364 599 байт)
- 00:45–00:55 контроль на первых двух сценах (AS80_point0, G1S_point5): тела побитово = формат 8, Q_k в 3σ сайдкаров, Geant4 до 0.2 %, читатель 140/140 до бита, --plant код 1
- 01:15:52 старт единого счёта 46 сцен: D:\BqMoni_Claude\p87\store_run.cmd (PID 29844), лог count.log; конец ≈ 05:30…06:30
- дальше: сверка 46 тел (cmp_store.py), перенос (store_swap.py swap/verify_after/rm_qk), корпус rev26 (run_arm.ps1), витрина, README, перенос кода (copy_back.py --apply), check_all
- 03:21 счёт снят системой (0x40010004) на 18-й сцене; 19 файлов в store целы (все побитово = живой склад)
- 08:29 досчёт 27 сцен: store_run2.cmd (PID 21328), лог count2.log; конец ≈ 11:30
