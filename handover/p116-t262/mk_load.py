# -*- coding: utf-8 -*-
r"""П116 (T262) — пишет .cmd безвредной нагрузки для опытов: ASCII, CRLF, полные пути (грабли П87).
  python mk_load.py <tag> [секунд=600]
Нагрузка: ping -n <сек+1> 127.0.0.1 (один пакет в секунду), потом строка `exit %errorlevel% дата время`
в <tag>_done.txt — как у store_run.cmd П114. Сердцебиение <tag>_hb.txt — старт с PID-ами cmd.
"""
import sys

LANE = r'D:\BqMoni_Claude\p116'


def main(argv):
    tag = argv[0]
    sec = int(argv[1]) if len(argv) > 1 else 600
    lines = [
        '@echo off',
        'rem P116 T262: harmless load for detachment experiments, tag ' + tag,
        'echo start %date% %time% > ' + LANE + '\\' + tag + '_start.txt',
        'set > ' + LANE + '\\' + tag + '_env.txt',
        'ping -n %d 127.0.0.1 > nul' % (sec + 1),
        'echo exit %errorlevel% %date% %time% > ' + LANE + '\\' + tag + '_done.txt',
    ]
    data = ('\r\n'.join(lines) + '\r\n').encode('ascii')
    name = LANE + '\\load_' + tag + '.cmd'
    open(name, 'wb').write(data)
    print(name, len(data), 'bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
