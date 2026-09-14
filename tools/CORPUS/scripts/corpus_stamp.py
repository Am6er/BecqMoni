# -*- coding: utf-8 -*-
u"""Клеймо ГЕНЕРАТОРА корпуса (`T244`): чем собран `corpus/`, и сходится ли это
с деревом.

Зачем. Корпус в git — порождённые данные: `build_corpus.py` и всё, что он
импортирует и читает, превращают исходники сопровождающего в `corpus/spectra`,
`manifest.csv`, `devices/`, `detectors.csv`. Генератор правят часто, корпус
пересобирают редко, и между ними никто не стоял: 05.09.2026 общий фит семьи
(`fam`) заведён в `build_corpus.py`, а корпус лежал собранным 02.09.2026 —
три дня все числа снимались с корпуса, который этой правки НЕ СОДЕРЖАЛ;
12.09.2026 (`f4b518f2`) из шаблона прибора снят `<DoseRateConfig>`, а все 24
`corpus/devices/*.xml` в git его несут до сих пор. `check_corpus.py` судит
корпус САМ ПО СЕБЕ и на устаревшем говорит СОШЛОСЬ; расхождение видно, только
запустив пересборку — то есть ровно тем действием, которого избегают.

⛔ Почему клеймо, а не одна история git. «Последний коммит генератора — предок
последнего коммита данных» НЕ судит: (а) правка генератора, не меняющая выхода,
не даёт коммита данных вовсе, и сторож краснел бы вечно; (б) ЧАСТИЧНАЯ правка
данных (узел `<Efficiency>` одному спектру, строка `manifest.csv` руками) даёт
коммит данных ПОЗЖЕ генератора, и сторож зеленел бы ложно — ровно так на
12.09.2026 `d0aeafcb` (диск, 4 строки манифеста) стоит ПОСЛЕ `f4b518f2`, а
устройства корпуса — старые. Измерено 12.09.2026 полосой П33. Поэтому истина
пишется В МОМЕНТ СБОРКИ: отпечаток генератора кладётся в `corpus/generator.json`
полной пересборкой, а сторож сверяет его с деревом.

Что входит в набор генератора — устанавливается САМИМ КОДОМ, а не списком:
  * `build_corpus.py` и замыкание его ЛОКАЛЬНЫХ импортов (файлы этого
    каталога, которые он импортирует прямо или через других: `calibrate`,
    `chains`, `corpus_calib`, `corpus_def`, `corpus_paths`, `gaussfit`,
    `spectrum` на 12.09.2026) — разбирается по тексту, включая импорты внутри
    функций; новый импорт входит в набор сам, без правки здесь;
  * то, что генератор ЧИТАЕТ помимо входов корпуса: правило родителя
    `BecquerelMonitor/FullSpectrumAnalysis/DecayParentRule.cs` (`chains.py`
    читает `LevelClause` из исходника, `T74`) и база линий `nucdb.sqlite`
    (`chains.DB`).
  Входы корпуса — исходники спектров и `data/calibration.json` — сюда НЕ
  входят: их отпечаток ведёт `inputs.csv` (`B10`).

Отпечаток файла — sha256 содержимого с CRLF, приведённым к LF: рабочее дерево
здесь выгружено под `core.autocrlf=true` вперемешку (часть файлов LF, часть
CRLF), и сырой байтовый хеш расходился бы между двумя выгрузками одного и того
же коммита. Свёртка набора — sha256 строк «путь<TAB>sha», отсортированных
ordinal, путь от корня дерева прямыми косыми.

Стадии 2–3 пересборки (`restore_eff_nodes.py`, `res_apply.py`) и сводка
(`corpus_summary.py`) в клеймо НЕ входят: клеймо пишет `build_corpus.py`, и
о них он не знает. Сводку сторож `tools/check_corpus_generator.py` судит
иначе — пересборкой в памяти и побайтным сравнением.

Использование:
  corpus_stamp.write(corpus_dir)         — из build_corpus.py при ПОЛНОЙ пересборке
  corpus_stamp.check(corpus_dir)         — из check_corpus.py и сторожа; печатает
                                           и возвращает True/False
"""
import hashlib
import io
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(LAB))
CORPUS = os.path.join(LAB, 'corpus')

STAMP_NAME = 'generator.json'
ALGO = 'sha256(путь+содержимое, CRLF->LF)/v1'

#: Корень замыкания импортов.
ROOT_MODULE = 'build_corpus'

#: Сам этот модуль из набора ИСКЛЮЧЁН: `build_corpus.py` импортирует его ради
#: записи клейма, а данных корпуса он не порождает — правка здесь красила бы
#: сторожа без единого байта разницы в корпусе.
SELF_MODULE = os.path.splitext(os.path.basename(__file__))[0]

#: Что генератор читает помимо входов корпуса (пути от корня дерева).
READS = (
    'BecquerelMonitor/FullSpectrumAnalysis/DecayParentRule.cs',
    'BecquerelMonitor/nucdb.sqlite',
)

_IMPORT = re.compile(
    r'^\s*(?:import\s+([A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)'
    r'|from\s+([A-Za-z_]\w*)\s+import\b)', re.M)


def _local_imports(path):
    u"""Имена модулей ЭТОГО каталога, которые импортирует файл (и в функциях)."""
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        text = fh.read()
    names = set()
    for m in _IMPORT.finditer(text):
        if m.group(1):
            for piece in m.group(1).split(','):
                names.add(piece.strip().split(' as ')[0].strip())
        else:
            names.add(m.group(2))
    return sorted(n for n in names if os.path.isfile(os.path.join(HERE, n + '.py')))


def generator_files(scripts_dir=None):
    u"""Пути набора генератора от корня дерева, прямыми косыми, отсортированные."""
    scripts_dir = scripts_dir or HERE
    seen, todo = [], [ROOT_MODULE]
    while todo:
        name = todo.pop(0)
        if name in seen:
            continue
        seen.append(name)
        for dep in _local_imports(os.path.join(scripts_dir, name + '.py')):
            if dep not in seen and dep != SELF_MODULE:
                todo.append(dep)
    rel_scripts = os.path.relpath(scripts_dir, REPO).replace('\\', '/')
    files = ['%s/%s.py' % (rel_scripts, n) for n in seen] + list(READS)
    return sorted(files)


def file_sha(path):
    u"""sha256 содержимого с CRLF -> LF; None, если файла нет."""
    if not os.path.isfile(path):
        return None
    with open(path, 'rb') as fh:
        data = fh.read()
    if b'\0' not in data[:8000]:
        data = data.replace(b'\r\n', b'\n')
    return hashlib.sha256(data).hexdigest()


def fold(shas):
    u"""Свёртка карты «путь -> sha» в один отпечаток (ordinal-порядок путей)."""
    lines = [u'%s\t%s' % (k, shas[k] or '') for k in sorted(shas)]
    return hashlib.sha256(u'\n'.join(lines).encode('utf-8')).hexdigest()


def _git(*args):
    try:
        proc = subprocess.run(['git'] + list(args), cwd=REPO, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE)
    except OSError:
        return None
    if proc.returncode != 0:
        return None
    return proc.stdout.decode('utf-8', 'replace').rstrip('\r\n')


def tree_record(files=None):
    u"""Набор генератора в РАБОЧЕМ ДЕРЕВЕ: карта путей, отпечаток, HEAD, грязные."""
    files = files or generator_files()
    shas = dict((f, file_sha(os.path.join(REPO, *f.split('/')))) for f in files)
    head = _git('rev-parse', 'HEAD') or ''
    dirty = []
    status = _git('status', '--porcelain', '--', *files)
    if status:
        dirty = sorted(line[3:].strip().replace('\\', '/') for line in status.splitlines() if line.strip())
    return dict(files=shas, fp=fold(shas), head=head, dirty=dirty)


def write(corpus_dir=None):
    u"""Положить клеймо в корпус. Зовётся ПОЛНОЙ пересборкой (`build_corpus.py`)."""
    corpus_dir = corpus_dir or CORPUS
    rec = tree_record()
    stamp = dict(algo=ALGO, root=ROOT_MODULE, reads=list(READS),
                 head=rec['head'], dirty=rec['dirty'], fp=rec['fp'], files=rec['files'])
    path = os.path.join(corpus_dir, STAMP_NAME)
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(json.dumps(stamp, ensure_ascii=False, indent=1, sort_keys=True))
        fh.write(u'\n')
    return path, stamp


def read(corpus_dir=None):
    path = os.path.join(corpus_dir or CORPUS, STAMP_NAME)
    if not os.path.isfile(path):
        return None
    with io.open(path, encoding='utf-8') as fh:
        try:
            return json.load(fh)
        except ValueError:
            return {'broken': True}


def missed_commits(since, files):
    u"""Коммиты генератора после `since`, которых в данных нет — по имени и дате."""
    if not since:
        return None
    out = _git('log', '--format=%h %ad %s', '--date=short', '%s..HEAD' % since, '--', *files)
    if out is None:
        return None
    return [line for line in out.splitlines() if line.strip()]


def check(corpus_dir=None, out=None, files=None):
    u"""Сверить клеймо корпуса с деревом. Печатает разбор, возвращает True/False."""
    corpus_dir = corpus_dir or CORPUS
    out = out or sys.stdout
    say = lambda s: out.write(s + u'\n')

    say(u'\n== клеймо генератора корпуса (T244) ==')
    now = tree_record(files)
    say(u'  набор генератора в дереве: %d файлов, отпечаток %s' % (len(now['files']), now['fp'][:16]))
    stamp = read(corpus_dir)
    rel = os.path.relpath(os.path.join(corpus_dir, STAMP_NAME), REPO)
    if stamp is None:
        say(u'  ⛔ ОТКАЗ: клейма %s НЕТ — корпус собран генератором неизвестного поколения' % rel)
        say(u'     (клеймо пишет ПОЛНАЯ пересборка build_corpus.py с 12.09.2026).')
        last_inputs = _git('log', '-1', '--format=%h %ad', '--date=short', '--',
                           'tools/CORPUS/corpus/inputs.csv') or u'?'
        say(u'     последний коммит, менявший inputs.csv (след полной пересборки): %s' % last_inputs)
        last_gen = _git('log', '-1', '--format=%h %ad %s', '--date=short', '--', *now['files']) or u'?'
        say(u'     последний коммит генератора: %s' % last_gen[:110])
        since = last_inputs.split()[0] if last_inputs != u'?' else None
        missed = missed_commits(since, now['files']) if since else None
        if missed:
            say(u'     коммиты генератора после следа пересборки — до данных не доехали (%d):' % len(missed))
            for line in missed:
                say(u'       %s' % line[:120])
        say(u'     Лечится пересборкой корпуса (rebuild_corpus.py --from-library — по разрешению Amber).')
        return False
    if stamp.get('broken') or stamp.get('algo') != ALGO or not isinstance(stamp.get('files'), dict):
        say(u'  ⛔ ОТКАЗ: клеймо %s не разбирается или писано другим образцом (%s, ждали %s)'
            % (rel, stamp.get('algo'), ALGO))
        return False

    was = stamp['files']
    changed = sorted(k for k in was if k in now['files'] and was[k] != now['files'][k])
    added = sorted(k for k in now['files'] if k not in was)
    removed = sorted(k for k in was if k not in now['files'])
    say(u'  клеймо: собрано на HEAD %s%s, отпечаток %s'
        % ((stamp.get('head') or '?')[:8],
           (u' (генератор был ГРЯЗНЫМ: %s)' % u', '.join(stamp.get('dirty') or [])) if stamp.get('dirty') else u'',
           (stamp.get('fp') or '?')[:16]))
    if not changed and not added and not removed and stamp.get('fp') == now['fp']:
        say(u'  СОШЛОСЬ: корпус собран ТЕМ генератором, что лежит в дереве (%d файлов)' % len(was))
        if now['dirty']:
            say(u'  ⚠ в дереве есть незакоммиченные правки генератора: %s — клеймо с ними СХОДИТСЯ,'
                % u', '.join(now['dirty']))
            say(u'    то есть корпус собран ими; коммитить корпус без них нельзя.')
        return True

    say(u'  ⛔ ОТКАЗ: корпус собран НЕ ТЕМ генератором, что в дереве — изменено %d, добавлено %d, удалено %d'
        % (len(changed), len(added), len(removed)))
    for k in changed:
        say(u'     изменён   %s  (в клейме %s, в дереве %s)' % (k, (was[k] or '')[:12], (now['files'][k] or '')[:12]))
    for k in added:
        say(u'     добавлен  %s  (генератор вырос — в клейме его нет)' % k)
    for k in removed:
        say(u'     удалён    %s  (в клейме есть, в дереве нет)' % k)
    missed = missed_commits(stamp.get('head'), sorted(set(now['files']) | set(was)))
    if missed is None:
        say(u'     коммит клейма %s git не знает — коммиты генератора не перечислить'
            % (stamp.get('head') or '?')[:8])
    elif missed:
        say(u'     коммиты генератора, не доехавшие до данных (%d, после %s):'
            % (len(missed), (stamp.get('head') or '?')[:8]))
        for line in missed:
            say(u'       %s' % line[:120])
    else:
        say(u'     закоммиченных правок генератора после клейма нет — расхождение в НЕЗАКОММИЧЕННОМ:')
        say(u'       %s' % (u', '.join(now['dirty']) if now['dirty'] else u'(git status чист — клеймо писано не этим деревом)'))
    say(u'     Лечится пересборкой корпуса (rebuild_corpus.py --from-library — по разрешению Amber).')
    return False


if __name__ == '__main__':
    for _s in (sys.stdout, sys.stderr):
        try:
            _s.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    sys.exit(0 if check() else 1)
