# -*- coding: utf-8 -*-
r"""П114 — пишет .cmd отсоединённого счёта склада: ASCII, CRLF, полный путь к exe (грабли П87:
голое имя exe после cd /d не находится, LF-файл с не-ASCII cmd режет на куски). Образец — П107 make_cmd.py.
  python make_cmd.py full            -> store_run.cmd: (1) ТРИ дальние сцены --n=6000000 (решение Amber 18.09.2026
                                        «Добить историями ночью: дальним точкам ×2»; реестр DENSE_SCENES), (2) остальные
                                        46 рецептом --n=3000000; без --force — дальние во втором вызове пропускаются как
                                        «ГУЩЕ штатной» (T36), сцена контроля RC103_point0 в store НЕ лежит (контроль —
                                        в store_ctrl), так что все 49 считаются этим счётом
  python make_cmd.py only k1,k2,...  -> store_run2.cmd (досчёт недостающих рецептом 3 000 000, без --force;
                                        дальнюю досчитывать отдельно: make_cmd.py far k1,...)
  python make_cmd.py far k1,...      -> store_run3.cmd (досчёт дальних --n=6000000)
"""
import sys

LANE = r'D:\BqMoni_Claude\p114'
EXE = LANE + r'\wt\tools\effmaker\probes\build_p114\CorpusMatrixProbe.exe'
STORE = LANE + r'\store'
FAR = 'RC103_point50,ASN16_point10_house,G1S_point25'
RECIPE = ' --threads=10 --target=0'


def call(only, n, dump, tag):
    return (EXE + ' --dir=' + STORE + RECIPE + ' --n=%d' % n + (' --only=' + only if only else '')
            + ' --dump=' + LANE + r'\art\dumps\%s.csv >> ' % dump + LANE + '\\' + tag + '.log 2>> ' + LANE + '\\' + tag + '.err')


def main(argv):
    mode = argv[0]
    head = ['@echo off',
            'rem P114 19.09.2026 (physics 22, key lbang ON by default): store count, recipe --threads=10 --target=0 --n=3000000,',
            'rem far scenes (RC103_point50, ASN16_point10_house, G1S_point25) --n=6000000 (Amber 18.09.2026: far points x2).',
            'cd /d ' + LANE + r'\wt\tools\effmaker\probes\build_p114']
    if mode == 'full':
        name, tag = 'store_run.cmd', 'count'
        body = [call(FAR, 6000000, 'store_far', tag),
                'echo far exit %errorlevel% %date% %time% >> ' + LANE + '\\' + tag + '_far_done.txt',
                call(None, 3000000, 'store', tag)]
    elif mode == 'only':
        name, tag = 'store_run2.cmd', 'count2'
        body = [call(argv[1], 3000000, 'store2', tag)]
    else:
        name, tag = 'store_run3.cmd', 'count3'
        body = [call(argv[1], 6000000, 'store3', tag)]
    lines = head + ['echo start %date% %time% > ' + LANE + '\\' + tag + '_start.txt'] + body + \
            ['echo exit %errorlevel% %date% %time% > ' + LANE + '\\' + tag + '_done.txt']
    data = ('\r\n'.join(lines) + '\r\n').encode('ascii')
    open(LANE + '\\' + name, 'wb').write(data)
    print(name, len(data), 'bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
