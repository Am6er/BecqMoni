# -*- coding: utf-8 -*-
u"""Пересборка корпуса — ЧЕТЫРЬМЯ шагами, но ОДНОЙ командой (`T61`).

⛔ Зачем это отдельный скрипт. Пересборка никогда не была одним шагом, а в
перечне конвейера стояла одним, и 24.08.2026 это стоило полного цикла впустую.
`build_corpus.py --from-library` пишет узел ПШПВ КОРНЕВОЙ формы
(`SqrtFwhmCalibration`) всем 129 спектрам, а степенной узел `V2`
(`PowerFwhmCalibration`, 100 спектров семи групп) кладёт отдельная команда.
Пропустив её, получаешь правдоподобный, но чужой корпус: Σχ² понятной части
**477.8 → 628.9 (+32 %)**, медиана χ²/ndf 2.90 → 3.61, recall 99 → 92 %.

Порядок шагов и почему он такой:

1. `build_corpus.py --from-library` — сами копии спектров, устройства, манифест,
   калибровки. Требует разрешения на библиотеку (`B8`), оно и передаётся сюда.
2. `restore_eff_nodes.py --apply` — вернуть узлы `<Efficiency>` привязки кривой
   и матрицы: сборка о них не знает и стирает (`T30`). Возвращает ТЕ ЖЕ узлы из
   git, не пересчитывая Монте-Карло (35 минут впустую).
3. `res_apply.py --mode=power-node --apply` — степенной узел ПШПВ (`V2`).
4. `check_corpus.py` — приёмка. С 24.08.2026 она же и сторож третьего шага:
   `check_fwhm_node` отказывает, если форма узла не та, что положена планом.

⚠ Скрипт не заменяет двух шагов, которые тоже обязательны после ПОЛНОЙ
пересборки и делаются НЕ питоном: `bg_from_spe.py --apply` (полный фон G1S,
`S44`) и склад матриц `CorpusEffProbe` (`B20`). Они названы в конце прогона.

    python tools/CORPUS/scripts/rebuild_corpus.py --from-library
    python tools/CORPUS/scripts/rebuild_corpus.py --from-library --only=G1S16_Cd109_P25
    python tools/CORPUS/scripts/rebuild_corpus.py --dry          # только показать шаги
"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))

#: Шаг: (заголовок, файл, аргументы, «отказ шага валит пересборку»).
#: Валит ВСЁ каждый из четырёх: половина пересобранного корпуса хуже
#: непересобранного, потому что выглядит целой.
STEPS = (
    (u'1/4 копии спектров, устройства, манифест, калибровки',
     'build_corpus.py', ['--from-library']),
    (u'2/4 узлы <Efficiency> обратно (T30)',
     'restore_eff_nodes.py', ['--apply']),
    (u'3/4 степенной узел ПШПВ (V2); без него Σχ² хуже на 32 %',
     'res_apply.py', ['--mode=power-node', '--apply']),
    (u'4/4 приёмка, она же сторож третьего шага (T61)',
     'check_corpus.py', []),
)


def run(script, argv):
    cmd = [sys.executable, os.path.join(HERE, script)] + argv
    print(u'\n$ python tools/CORPUS/scripts/%s %s' % (script, ' '.join(argv)))
    sys.stdout.flush()
    started = time.time()
    code = subprocess.call(cmd)
    return code, time.time() - started


def main():
    argv = sys.argv[1:]
    dry = '--dry' in argv
    only = [a for a in argv if a.startswith('--only=')]

    if not dry and '--from-library' not in argv:
        # Тот же запрет, что и у `build_corpus.py`, и слово в слово по той же
        # причине (`B8`): пересборка лезет в рабочую папку сопровождающего.
        # Повторён здесь, а не оставлен первому шагу, чтобы отказ пришёл ДО
        # того, как что-нибудь начнёт писаться.
        print(u'⛔ ПЕРЕСБОРКА ИЗ БИБЛИОТЕКИ НЕ РАЗРЕШЕНА')
        print(u'')
        print(u'Пересборка берёт исходники из библиотеки сопровождающего, и это')
        print(u'делается ТОЛЬКО с разрешения (правило Amber 16.08.2026):')
        print(u'')
        print(u'    python tools/CORPUS/scripts/rebuild_corpus.py --from-library')
        print(u'')
        print(u'Показать шаги, ничего не запуская: --dry')
        return 2

    if only:
        print(u'⚠ %s передаётся только первому шагу: остальные три работают по'
              % only[0])
        print(u'  всему корпусу по построению (узлы и приёмка не делятся на части).')

    if dry:
        print(u'шаги пересборки (--dry, ничего не запущено):')
        for title, script, args in STEPS:
            print(u'  %s' % title)
            print(u'      python tools/CORPUS/scripts/%s %s' % (script, ' '.join(args)))
        return 0

    done = []
    for title, script, args in STEPS:
        print(u'\n' + u'=' * 72)
        print(u'== %s' % title)
        print(u'=' * 72)
        code, spent = run(script, args + (only if script == 'build_corpus.py' else []))
        done.append((title, code, spent))
        if code != 0:
            print(u'\n⛔ ШАГ ОТКАЗАЛ (код %d) — пересборка ОСТАНОВЛЕНА.' % code)
            print(u'   Корпус сейчас НЕДОСОБРАН: следующие шаги не выполнялись,')
            print(u'   и числа с него снимать нельзя. Разберитесь с шагом и')
            print(u'   запустите пересборку заново с начала.')
            break

    print(u'\n' + u'=' * 72)
    for title, code, spent in done:
        print(u'  %-6s %5.0f с  %s' % (u'ок' if code == 0 else u'ОТКАЗ %d' % code,
                                       spent, title))
    ok = all(code == 0 for _t, code, _s in done) and len(done) == len(STEPS)
    if not ok:
        return 1

    print(u'\nПересборка прошла. Осталось то, что делается НЕ этим скриптом:')
    print(u'  python tools/CORPUS/scripts/bg_from_spe.py --apply   # полный фон G1S (S44)')
    print(u'  CorpusEffProbe.exe                                   # склад матриц (B20)')
    print(u'  python tools/CORPUS/scripts/corpus_summary.py        # сводка')
    print(u'  python tools/CORPUS/scripts/mkconfig.py              # рабочие каталоги wd_*')
    return 0


if __name__ == '__main__':
    sys.exit(main())
