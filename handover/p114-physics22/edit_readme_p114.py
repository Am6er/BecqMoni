# -*- coding: utf-8 -*-
r"""П114 (19.09.2026) — README корпуса в worktree, части БЕЗ чисел базы: §1.5 (склад — физика 22), §1.10 (матрица
«физика 22» у 92), §2.5 (исполнено … и П114 — физики 22), «Как воспроизвести» (плечо «как физика 21» --lbang=0).
Объявление базы rev32 — отдельным скриптом после прогона (edit_readme_declare.py). BOM и CRLF сохраняются."""
import sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
P = r'D:\BqMoni_Claude\p114\wt\tools\CORPUS\README.md'
raw = open(P, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
crlf = raw.count(b'\r\n'); lf = raw.count(b'\n') - crlf
print('BOM', bom, 'CRLF', crlf, 'bare LF', lf)
txt = raw.decode('utf-8-sig')
NL = '\r\n' if crlf >= lf else '\n'


def rep(old, new):
    global txt
    old = old.replace('\n', NL); new = new.replace('\n', NL)
    n = txt.count(old)
    if n != 1:
        print(u'⛔ ожидалось 1, найдено %d: %r' % (n, old[:100]))
        sys.exit(1)
    txt = txt.replace(old, new)


# §1.5
rep(u"""Склад сегодня — **физика 21, формат файла 9** (физика 21 — `M13`, П107 19.09.2026: тормозное электрона в
слоях обвязки ПО ХОДУ переноса `lbrem=1` ВКЛ единым счётом, решение Amber 19.09.2026 по приёмке П106 «ВКЛ
сейчас, единый счёт ночью»; физика 20 — `M13`, П103: смешанная схема упругого рассеяния электрона в слоях
обвязки `elmix=1`; физика 19 — `AMBER44`/`M12`, П97: перенос электрона в слоях обвязки `eltr=1`; формат 9 —
`AMBER46`, П87 16.09.2026: Q_k угловой корреляции внутри матрицы):""",
    u"""Склад сегодня — **физика 22, формат файла 9** (физика 22 — `M13`, П114 ночь 19→20.09.2026: направление
кванта тормозного в слоях обвязки по 2BS Коха—Моца `lbang=1` ВКЛ единым счётом, решение Amber 19.09.2026 по
приёмке П111 «ВКЛ единым счётом ночью»; физика 21 — `M13`, П107: тормозное электрона в слоях обвязки ПО ХОДУ
переноса `lbrem=1`; физика 20 — `M13`, П103: смешанная схема упругого рассеяния электрона в слоях обвязки
`elmix=1`; физика 19 — `AMBER44`/`M12`, П97: перенос электрона в слоях обвязки `eltr=1`; формат 9 —
`AMBER46`, П87 16.09.2026: Q_k угловой корреляции внутри матрицы):""")
rep(u"""клеймо каждой — `phys=21;<sha256 геометрии и настроек>` (формат в клеймо не входит: судится `Load`; число""",
    u"""клеймо каждой — `phys=22;<sha256 геометрии и настроек>` (формат в клеймо не входит: судится `Load`; число""")
rep(u"""`phys=21; hist=200000; grid=10-3000 keV/37 std; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1;
ecomp=1; bpath=2; eltr=1; elmix=1; lbrem=1` и guid, по которому разбор находит матрицу. Приёмка склада —
`MatrixAuditProbe --phys=21 --hist=3000000 --except=RC103_point50:6000000""",
    u"""`phys=22; hist=200000; grid=10-3000 keV/37 std; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1;
ecomp=1; bpath=2; eltr=1; elmix=1; lbrem=1; lbang=1` и guid, по которому разбор находит матрицу. Приёмка склада —
`MatrixAuditProbe --phys=22 --hist=3000000 --except=RC103_point50:6000000""")
rep(u"""Плечо «как физика 20» — `--lbrem=0` у `CorpusMatrixProbe` (тело побайтно = склад физики 20 rev30,
клеймо отличается только `phys=`); плечо «как физика 19» — `--elmix=0 --lbrem=0`; «как физика 18» —
`--eltr=0 --elmix=0 --lbrem=0`; снимок склада физики 20 (rev30) — `D:\\BqMoni_Claude\\p107\\store_backup\\`,
физики 19 (rev29) — `D:\\BqMoni_Claude\\p103\\store_backup\\`.""",
    u"""Плечо «как физика 21» — `--lbang=0` у `CorpusMatrixProbe` (тело побайтно = склад физики 21 rev31,
клеймо отличается только `phys=`); плечо «как физика 20» — `--lbrem=0 --lbang=0`; «как физика 19» —
`--elmix=0 --lbrem=0 --lbang=0`; «как физика 18» — `--eltr=0 --elmix=0 --lbrem=0 --lbang=0`; снимок склада
физики 21 (rev31) — `D:\\BqMoni_Claude\\p114\\store_backup\\`, физики 20 (rev30) — `D:\\BqMoni_Claude\\p107\\store_backup\\`,
физики 19 (rev29) — `D:\\BqMoni_Claude\\p103\\store_backup\\`.""")

# §1.10
rep(u"""сегодня матрица `физика 21` у 92, узел кривой у 94 (92 понятных + 2 непонятных с""",
    u"""сегодня матрица `физика 22` у 92, узел кривой у 94 (92 понятных + 2 непонятных с""")

# §2.5
rep(u"""  исполнено П103 единым счётом физики 20 и П107 — физики 21. Густые сцены — `RC103_point50`,""",
    u"""  исполнено П103 единым счётом физики 20, П107 — физики 21 и П114 — физики 22. Густые сцены — `RC103_point50`,""")

out = ('\ufeff' if bom else '') + txt
data = out.encode('utf-8')
open(P, 'wb').write(data)
crlf2 = data.count(b'\r\n'); lf2 = data.count(b'\n') - crlf2
print('после: BOM', data.startswith(b'\xef\xbb\xbf'), 'CRLF', crlf2, 'bare LF', lf2)
print('OK')
