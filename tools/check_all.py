#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Читатель сторожей (`T220`): зовёт каждый сторож `tools/check_*.py`, печатает
ОБРАЩЕНИЕ к нему («→ зову: …»), его вывод, код возврата, и в конце — сводку
словами. Любой отказ — код возврата 1 и строка «ОСТАНОВ: …» с именами.

Зачем. Сторож без читателя — признак отказа без потребителя: 05.09.2026 у
`check_registry_refs.py` и `check_build_recipe.py` не было ни одного вызова,
и красный сторож никого не останавливал. Это повторяющаяся ошибка дерева
(«признак заведён, потребитель не написан»), и читатель — её лекарство ровно
до тех пор, пока его самого зовут: приёмка полосы (SKILL `todo-work` §6)
и приём коммита гонят `python tools/check_all.py` и показывают код 0.

Состав — ЗЕЛЁНЫЕ на штатной области сторожа (замерено 06.09.2026, полоса G2,
`handover/g2-guard-readers/01-guards-census.txt`). Красный сторож в читатель
НЕ ставится (`T220`): он завалит каждую приёмку, и читателя перестанут звать.
Такие перечислены в `KNOWN_RED` с номером строки, которая их держит, и идут
только по `--all`; когда строка закрыта — перенести в `GUARDS`.

  python tools/check_all.py [--all] [--only имя,имя] [--fail-fast] [--quiet]

  --all        звать и заведомо красные (`KNOWN_RED`), для полной картины;
  --only       звать только названные (имена — без `check_` и `.py`);
  --fail-fast  остановиться на первом отказе; иначе идут все, чтобы отказы
               были видны разом;
  --quiet      не печатать вывод зелёных сторожей, только обращение и код.

Детям выставляется `PYTHONIOENCODING=utf-8`/`PYTHONUTF8=1`: консоль здесь cp1251,
и сторож без `reconfigure` (`check_corpus_library.py`) иначе падает на печати
`⛔` ещё до приговора — падение читалось бы как отказ по существу.

Коды возврата:
  0 — все позванные сторожа дали 0;
  1 — хотя бы один дал не 0 (в «ОСТАНОВ» — имена и коды);
  2 — сторож не найден на диске или `--only` назвал неизвестное имя.
"""

import argparse
import os
import subprocess
import sys
import time

# (имя, доводы, что судит). Все — без доводов зелены на штатной области.
GUARDS = [
    (u'registry_refs', [], u'ссылки `T<n>` в оснастке, приложении и журналах против TODO.md/DONE.md'),
    (u'build_recipe', [], u'копии рецепта MSBuild без /p:GenerateManifests=false'),
    (u'resx', [], u'пары Foo.resx / Foo.ru.resx: непереведённое'),
    (u'resx_designer', [], u'GetString("X") без X в парном resx'),
    (u'resx_letters', [], u'кириллица в английских resx и наоборот'),
    (u'resx_zorder', [], u'>>X.Parent / >>X.ZOrder против designer-кода'),
    (u'headless', [], u'окна на безоконном пути'),
    (u'menu_accelerators', [], u'столкновения ускорителей главного меню'),
    (u'fsa_docs', [], u'XML-описания настроек разбора против конструктора'),
    (u'scheme_gaps', [], u'раздел «чего не хватает» scheme.md против реестра'),
    (u'corpus_library', [], u'корпус не читает поставочную библиотеку'),
    (u'done_headers', [], u'заголовок строки DONE.md против её тела'),
]

# Красные по причине, которую держит строка реестра; зовутся только по --all.
KNOWN_RED = [
    (u'registry', [], u'реестр: номера, ссылки на файлы, имена, копии config/',
     u'красен (06.09.2026, 10 находок): 8 ссылок реестра на файлы, лежащие только на диске '
     u'(строки S61, S134, T134, T223 — tools/pie/out_*, tools/effmaker/out/a85/band.py: закоммитить '
     u'либо не ссылаться); противоречия DONE.md по T102 сняты 06.09.2026 по решению Amber'),
]


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def run_guard(repo, name, args, quiet):
    script = os.path.join(repo, 'tools', 'check_%s.py' % name)
    rel = 'tools/check_%s.py' % name
    if not os.path.isfile(script):
        print(u'⛔ сторож не найден на диске: %s' % rel)
        return None, 0.0, u''
    cmd = [sys.executable, script] + list(args)
    print((u'→ зову: python %s %s' % (rel, u' '.join(args))).rstrip())
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    env['PYTHONUTF8'] = '1'
    t0 = time.time()
    proc = subprocess.run(cmd, cwd=repo, env=env,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    dt = time.time() - t0
    out = proc.stdout.decode('utf-8', 'replace')
    if proc.returncode != 0 or not quiet:
        for line in out.splitlines():
            print(u'    ' + line)
    mark = u'✅' if proc.returncode == 0 else u'⛔'
    print(u'%s %s: код %d, %.1f с' % (mark, rel, proc.returncode, dt))
    print(u'')
    return proc.returncode, dt, out


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--all', action='store_true', help=u'звать и заведомо красные сторожа')
    ap.add_argument('--only', default=None, help=u'звать только названные, через запятую')
    ap.add_argument('--fail-fast', action='store_true', help=u'остановиться на первом отказе')
    ap.add_argument('--quiet', action='store_true', help=u'не печатать вывод зелёных')
    args = ap.parse_args(argv)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    plan = [(n, a, w, None) for n, a, w in GUARDS]
    if args.all:
        plan += [(n, a, w, why) for n, a, w, why in KNOWN_RED]
    if args.only:
        wanted = [s.strip() for s in args.only.split(',') if s.strip()]
        known = dict((n, (a, w, why)) for n, a, w, why in plan + [(n, a, w, why) for n, a, w, why in KNOWN_RED])
        unknown = [w for w in wanted if w not in known]
        if unknown:
            print(u'ОТКАЗ: неизвестные имена в --only: %s' % u', '.join(unknown))
            return 2
        plan = [(n,) + known[n] for n in wanted]

    print(u'читатель сторожей: %d к вызову%s' % (len(plan), u'' if args.all else
          u' (заведомо красных по строкам реестра пропущено %d — см. --all)' % len(KNOWN_RED)))
    for n, _, _, why in KNOWN_RED:
        if not args.all and not (args.only and n in [p[0] for p in plan]):
            print(u'  пропущен tools/check_%s.py — %s' % (n, why))
    print(u'')

    results = []
    missing = []
    for name, a, what, why in plan:
        rc, dt, _ = run_guard(repo, name, a, args.quiet)
        if rc is None:
            missing.append(name)
            continue
        results.append((name, rc, dt, what, why))
        if rc != 0 and args.fail_fast:
            print(u'--fail-fast: дальше не идём')
            break

    print(u'=== сводка ===')
    for name, rc, dt, what, why in results:
        mark = u'✅' if rc == 0 else u'⛔'
        tail = u'' if not why else u' [заведомо красен: %s]' % why
        print(u'  %s check_%s.py — код %d, %.1f с — %s%s' % (mark, name, rc, dt, what, tail))
    red = [(n, rc) for n, rc, _, _, _ in results if rc != 0]
    if missing:
        print(u'ОСТАНОВ: сторож не найден на диске — %s' % u', '.join(u'check_%s.py' % n for n in missing))
        return 2
    if red:
        print(u'ОСТАНОВ: отказали %d из %d — %s. Приёмка НЕ пройдена; читать вывод отказавшего выше.'
              % (len(red), len(results), u', '.join(u'check_%s.py (код %d)' % (n, rc) for n, rc in red)))
        return 1
    print(u'ВСЕ ЗЕЛЕНЫ: %d из %d сторожей дали 0.' % (len(results), len(results)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
