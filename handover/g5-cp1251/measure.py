# -*- coding: utf-8 -*-
"""Замер G5: каждый скрипт напрямую из cmd с cp1251 и БЕЗ PYTHONIOENCODING/PYTHONUTF8
(через run1251.cmd). Лог каждого — в handover/g5-cp1251/<фаза>/<имя>.log, сводка —
00-codes.txt: имя, доводы, код возврата, был ли UnicodeEncodeError.

  python measure.py <repo> <фаза>
"""
import os, subprocess, sys, time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO, PHASE = sys.argv[1], sys.argv[2]
HERE = os.path.dirname(os.path.abspath(__file__))
RUN = os.path.join(HERE, 'run1251.cmd')
OUT = os.path.join(REPO, 'handover', 'g5-cp1251', PHASE)
os.makedirs(OUT, exist_ok=True)

# (скрипт, доводы, чем мерен). Только пути без побочных эффектов.
PLAN = [
    # сторожа — штатный прогон
    ('tools/check_corpus_library.py', [], 'штатный прогон'),
    ('tools/check_done_headers.py', [], 'штатный прогон (KNOWN_RED, код 1 — приговор)'),
    ('tools/check_fsa_docs.py', [], 'штатный прогон'),
    ('tools/check_headless.py', [], 'штатный прогон'),
    ('tools/check_registry.py', [], 'штатный прогон (KNOWN_RED, код 1 — приговор)'),
    ('tools/check_resx.py', [], 'штатный прогон'),
    ('tools/check_resx_designer.py', [], 'штатный прогон'),
    ('tools/check_resx_letters.py', [], 'штатный прогон'),
    ('tools/check_scheme_gaps.py', [], 'штатный прогон'),
    ('tools/check_all.py', ['--quiet'], 'читатель, штатный прогон'),
    # todo-work
    ('.claude/skills/todo-work/scripts/todo_check.py', ['--help'], '--help'),
    ('.claude/skills/todo-work/scripts/todo_edit.py', ['--help'], '--help'),
    # корпус — отказы и --help
    ('tools/CORPUS/scripts/split_corpus.py', ['--check'], 'отказ снятого ключа --check (T164), файл не пишется'),
    ('tools/CORPUS/scripts/rebuild_corpus.py', [], 'отказ без --from-library'),
    ('tools/CORPUS/scripts/ecal_compare.py', [], 'без доводов печатает __doc__'),
    ('tools/CORPUS/scripts/mx_noise.py', ['--help'], '--help'),
    ('tools/CORPUS/scripts/mx_swap.py', ['--help'], '--help'),
    ('tools/CORPUS/scripts/peakshape.py', ['--help'], '--help'),
    ('tools/CORPUS/scripts/res_apply.py', ['--help'], '--help'),
    ('tools/CORPUS/scripts/res_form.py', ['--help'], '--help'),
    ('tools/CORPUS/scripts/res_low.py', ['--help'], '--help'),
    # nucdb — отказы по числу доводов и --help; базу никто не открывает
    ('tools/nucdb/check_edges.py', ['--help'], '--help'),
    ('tools/nucdb/check_interpolation.py', ['--help'], '--help'),
    ('tools/nucdb/compare_copies.py', ['--help'], '--help'),
    ('tools/nucdb/compare_intensities.py', ['--help'], '--help'),
    ('tools/nucdb/compare_photo.py', ['--help'], '--help'),
    ('tools/nucdb/db_hygiene.py', ['--help'], '--help'),
    ('tools/nucdb/import_photon_evaporation.py', ['--help'], '--help'),
    ('tools/nucdb/link_isomer_parents.py', ['--help'], '--help'),
    ('tools/nucdb/fill_intensity.py', [], 'отказ без доводов (usage)'),
    ('tools/nucdb/import_geant4.py', [], 'отказ без доводов (usage)'),
    ('tools/nucdb/import_light_yield.py', [], 'отказ без доводов (usage)'),
    ('tools/nucdb/import_sandia_coincidence.py', [], 'отказ без доводов (usage)'),
    ('tools/nucdb/import_fluor_yield.py', [], 'отказ без доводов (usage)'),
    ('tools/nucdb/split_db.py', [], 'отказ без доводов (usage)'),
    # pie
    ('tools/pie/compare.py', ['--help'], '--help'),
    ('tools/pie/plot_decomp.py', ['--help'], '--help'),
    ('tools/pie/score.py', ['--help'], '--help'),
    ('tools/pie/sweep_sthr.py', ['--help'], '--help'),
]

rows = []
for script, args, how in PLAN:
    name = os.path.splitext(os.path.basename(script))[0]
    log = os.path.join(OUT, name + '.log')
    t0 = time.time()
    with open(log, 'wb') as f:
        p = subprocess.run(['cmd', '/c', RUN, script] + args, stdout=f, stderr=subprocess.STDOUT, cwd=REPO)
    dt = time.time() - t0
    data = open(log, 'rb').read()
    uee = b'UnicodeEncodeError' in data
    tb = b'Traceback (most recent call last)' in data
    rows.append((script, ' '.join(args), p.returncode, uee, tb, len(data.splitlines()), dt, how))

with open(os.path.join(OUT, '00-codes.txt'), 'w', encoding='utf-8') as f:
    f.write('| скрипт | доводы | код | UnicodeEncodeError | трассировка | строк | с | чем мерен |\n|---|---|---|---|---|---|---|---|\n')
    for r in rows:
        line = '| `%s` | `%s` | %d | %s | %s | %d | %.1f | %s |' % (
            r[0], r[1], r[2], 'ДА' if r[3] else 'нет', 'да' if r[4] else 'нет', r[5], r[6], r[7])
        f.write(line + '\n')
        print(line)
print('всего %d; с UnicodeEncodeError: %d' % (len(rows), sum(1 for r in rows if r[3])))
