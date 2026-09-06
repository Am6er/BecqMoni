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
    (u'numeric_updown', [], u'поля со стрелками мимо общего InvariantNumericUpDown; группировка разрядов'),
    (u'fsa_docs', [], u'XML-описания настроек разбора против конструктора'),
    (u'scheme_gaps', [], u'раздел «чего не хватает» scheme.md против реестра'),
    (u'corpus_library', [], u'корпус не читает поставочную библиотеку'),
    (u'corpus_truth', [], u'поля истины корпуса (chains, nuclides): corpus_def против manifest.csv'),
    (u'done_headers', [], u'заголовок строки DONE.md против её тела'),
    # `T76`: у кода возврата 3 корпусных мерок охвата не было ни одного
    # вызывающего. Здесь он в умолчании и потому быстрый: зовётся только
    # `gaussfit_check.py --coverage-only` (~1 с) плюс сверка, не завелась ли
    # мерка на сырье БЕЗ охвата. Две долгие мерки — ключом `--deep` у самого
    # сторожа, в приёмку они не ставятся (~2.5 мин на приёмку — тот же случай,
    # что и `registry` ниже: сторожа, который держит приёмку, звать перестают).
    (u'corpus_coverage', [], u'охват корпусных мерок: коды возврата и мерки на сырье без Coverage'),
    # `T127`: раздел «Отложенные прогоны» заполнен 06.09.2026, разряд ожидания назван
    # у каждой из четырёх строк — сторож перенесён из KNOWN_RED в умолчание (идёт 0.1 с).
    (u'pending_runs', [], u'признак «ЖДЁТ СЧЁТНОГО ЗАХОДА» против раздела «Отложенные прогоны»'),
    # `T101`: до 06.09.2026 `FsaStackShot` — прибор, которым ловят расхождение
    # стенда с экраном, — сам не говорил, чем настроен. Здесь читатель того,
    # что отчёт о настройках не снимут обратно молча (идёт 0.1 с).
    (u'probe_tuning', [], u'отчёт проб FSA о настройках: вызов, эталон, снимок полосы, порядок'),
    # `T242`: приёмка сводной таблицы ключей обоих путей расчёта по геометрии.
    # Таблица, которую не сверяют машинно, протухает за неделю; здесь она сверена
    # с исходниками восемью правилами (идёт 0.2 с).
    (u'matrix_keys', [], u'состав ключей обоих путей: матрица отклика против расчёта в UI'),
    # `A269`: имя вещества входит в отпечаток геометрии, и расхождение в одной
    # букве отнимает у спектра матрицу МОЛЧА — с 23.08.2026 так жили 37 спектров
    # из 129. Сторож сверяет имена в узле `<Efficiency>` с `.in`, из которых
    # считан склад (идёт 0.6 с).
    (u'geometry_names', [], u'имена веществ в узле <Efficiency> против corpus/geometries/*.in'),
    # `A265`: отрисовочная галка «Невязка модели» стояла шестой в ряду пяти
    # РАСЧЁТНЫХ, и различала их одна подсказка при наведении — из этого
    # смешения родился вопрос `A264`. Решением Amber 06.09.2026 отрисовочные
    # вынесены в свою группу; сторож судит род ПО ФАКТУ (идёт ли обработчик
    # через `ApplyCalculationChange`), а не по списку имён (идёт 0.1 с).
    (u'fsa_view_groups', [], u'переключатели окна отчёта FSA: расчётные и отрисовочные по своим группам'),
    # `A273`: высоту панели выделения считают ДВА места — отрисовка и мерка
    # `SelectionPanelProbeG10`, — и копии разошлись: у мерки не было слагаемого
    # «спор подписи», и на сцене со спором она отвергала ВЕРНУЮ отрисовку
    # («8 НЕ СОШЛОСЬ», код 1, полдня без читателя). Сторож судит договор клейм
    # `// ПАНЕЛЬ: <имя>` в обоих файлах (идёт 0.1 с).
    (u'selection_panel_height', [], u'слагаемые высоты панели выделения: отрисовка против мерки пробы'),
    # `A271`: отсев родителей образов вылета SE/DE выключался ЦЕЛИКОМ при
    # неизвестном веществе кристалла, и один образ забирал до 76 % отсчётов
    # спектра. Заглавным числом дефект выглядел ВЫИГРЫШЕМ (Σχ² улучшался),
    # поэтому читатель здесь СТАТИЧЕСКИЙ: он судит форму правила, а не числа
    # (идёт 0.1 с).
    (u'escape_parents', [], u'отсев родителей образов вылета SE/DE не выключается сам при NaN'),
]

# Не в умолчании: держит строка реестра либо слишком долго идёт; зовутся только по --all.
KNOWN_RED = [
    (u'registry', [], u'реестр: номера, ссылки на файлы, имена, копии config/',
     u'ЗЕЛЕН с 06.09.2026 («РЕЕСТР ЧИСТ»), но идёт 100 с и потому не в умолчании: восемь ссылок '
     u'на файлы, лежавшие только на диске (S61, S134, T134, T223), переписаны на копии в '
     u'handover/registry-artefacts/; противоречия DONE.md по T102 сняты по решению Amber'),
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
