# -*- coding: utf-8 -*-
r"""П97 — пишет .cmd отсоединённого счёта склада: ASCII, CRLF, полный путь к exe (грабли П87:
голое имя exe после cd /d не находится, LF-файл с не-ASCII cmd режет на куски).
  python make_cmd.py full            -> store_run.cmd  (46 сцен, --force: две сцены контроля пересчитываются — детерминизм A104)
  python make_cmd.py only k1,k2,...  -> store_run2.cmd (досчёт недостающих, без --force)
"""
import sys

LANE = r'D:\BqMoni_Claude\p97'
EXE = LANE + r'\wt\tools\effmaker\probes\build_p97\CorpusMatrixProbe.exe'
STORE = LANE + r'\store'


def main(argv):
    mode = argv[0]
    if mode == 'full':
        name, tag, extra = 'store_run.cmd', 'count', ' --force'
    else:
        name, tag, extra = 'store_run2.cmd', 'count2', ' --only=' + argv[1]
    lines = [
        '@echo off',
        'rem P97 17-18.09.2026 (physics 19, key eltr ON by default): store count, recipe --threads=10 --target=0 --n=3000000 (S140).',
        'rem full: --force recounts the two control scenes too (determinism, A104); resume: --only=<missing>, no --force.',
        'cd /d ' + LANE + r'\wt\tools\effmaker\probes\build_p97',
        'echo start %date% %time% > ' + LANE + '\\' + tag + '_start.txt',
        EXE + ' --dir=' + STORE + ' --threads=10 --target=0 --n=3000000' + extra
        + ' --dump=' + LANE + r'\art\dumps\store.csv > ' + LANE + '\\' + tag + '.log 2> ' + LANE + '\\' + tag + '.err',
        'echo exit %errorlevel% %date% %time% > ' + LANE + '\\' + tag + '_done.txt',
    ]
    data = ('\r\n'.join(lines) + '\r\n').encode('ascii')
    open(LANE + '\\' + name, 'wb').write(data)
    print(name, len(data), 'bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
