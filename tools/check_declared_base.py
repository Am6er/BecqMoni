#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож объявленной базы корпуса (`T248`, `T249`): каталог, НАЗВАННЫЙ в
объявлении, обязан давать ОБЪЯВЛЕННЫЕ числа, а само объявление обязано нести
ОТПЕЧАТОК корпуса и сборки, которыми снят прогон, — и они обязаны сходиться
с деревом и с каталогом прогона.

Зачем — первая половина (`T248`). 06.09.2026 объявление говорило «малая
`out_mini`», а каталог `tools/pie/out_mini` содержал прогон 04.09.2026 —
предыдущую, СНЯТУЮ базу. Пересчёт по нему давал 556.0 / 5.91 / 100 % и
205.3 / 4.95 / 90 % против объявленных 556.1 / 5.93 / 98 % и 205.9 / 4.95 /
93 %. Отказа не бывало никакого: числа правдоподобны, разница в десятых,
подмену поколения видно только по дате файлов. По форме это ровно `B20` —
признак был, потребителя у него не было.

Зачем — вторая половина (`T249`). Числа судят объявление с ОДНОЙ стороны:
«каталог даёт объявленное». 07.09.2026 полоса П29 перекалибровала девять
спектров корпуса — сторож остался ЗЕЛЁНЫМ: каталог прогона правкой корпуса не
трогается. И обратное: объявленные `out_p23_*` были посчитаны СНЯТЫМ
поколением двоичного файла (у `AS80_Charoite` 2 строки компонентов вместо 9),
заглавные числа совпадали. Поэтому с 12.09.2026 объявление несёт СТРОКУ
ОТПЕЧАТКА, и она судится с трёх сторон:

  corpus=<sha256>   отпечаток КОРПУСА, которым снят прогон, — против корпуса
                    в дереве (`--corpus=<каталог>` подменяет судимый корпус:
                    положительный контроль);
  sources=<sha256>  отпечаток НАБОРА РАЗБОРА — исходники приложения по
                    `.csproj`, `CorpusFsaProbe.cs` с довесками и три базы
                    `*.sqlite`, — против того же набора в коммите объявления
                    (`head=<коммит>`, если назван; иначе — коммит, которым
                    строка отпечатка попала в README; строка ещё не
                    закоммичена — рабочее дерево);
  клеймо прогона    `<каталог>/.run.json`, которое пишет `run_appwd.ps1` при
                    каждом прогоне: его `corpus` и `sources` обязаны равняться
                    объявленным. Каталога без клейма объявлять нельзя; для
                    прогонов, снятых ДО клейма (`out_rev19_*`, П24), стоит
                    явное `run=none`, и оно принимается только при `head`
                    не позже `RUN_STAMP_EPOCH`.

Отпечаток файла — sha256 содержимого с CRLF → LF (дерево выгружено под
`core.autocrlf=true` вперемешку; сырой байтовый хеш расходился бы между двумя
выгрузками одного коммита — измерено 12.09.2026: 107 файлов приложения из
549 лежат в основном дереве с LF, в свежем worktree — с CRLF). Свёртка —
sha256 строк «путь<TAB>sha» в ordinal-порядке. Тот же образец, что у клейма
генератора корпуса (`corpus_stamp.py`, `T244`).

Чем судит числа. ДВА НЕЗАВИСИМЫХ источника, и в этом весь смысл:

  объявленные числа  <-  таблица «ДЕЙСТВУЮЩАЯ БАЗА» в tools/CORPUS/README.md
  измеренные числа   <-  tools/pie/score.py по CSV-файлам самого каталога

Своей копии чисел сторож НЕ ХРАНИТ. Третье место с числами — то же протухание,
что и всякий перечень, живущий отдельно от источника (`T127`: 14 номеров из 24
разошлись за считанные дни). Таблица в шапке `TODO.md` — дословная копия той же
таблицы, и объявление само говорит «числа живут в одном месте»: README.

Как читается объявление. В графе «база» имя каталога стоит в обратных кавычках
(«малая `out_mini`»); каталог ищется как tools/pie/<имя>. Строка, у которой
каталог НЕ НАЗВАН, проверке не подлежит — она печатается предупреждением, и это
само по себе находка: такое объявление нечем сверить.

Род базы задаёт список спектров, и берётся он тоже из объявления:
  «малая»  -> score.py --only=tools/CORPUS/corpus/mini.csv (тот же список, что
              задаёт прогон в run_mini.ps1: двум спискам разойтись нечем);
  «полная» -> без --only, судится весь манифест своей части.
Часть корпуса: «понятная» -> --part=known, «непонятная» -> --part=unknown.
Режим (`spline`/`snip`) определяется по именам файлов В САМОМ каталоге, а не
задаётся здесь: иначе сторож судил бы не тот прогон, что лежит.

Запуск:

  python tools/check_declared_base.py                 приговор по объявлению
  python tools/check_declared_base.py --base=мал      только малая база
  python tools/check_declared_base.py --base=мал --dir=tools/pie/out_p23_mini1
                                                      подсунуть свой каталог
  python tools/check_declared_base.py --corpus=<дир>  судить другой корпус
  python tools/check_declared_base.py --decl=<файл>   другое объявление
  python tools/check_declared_base.py --fingerprints  напечатать отпечатки дерева
                                                      (corpus=, sources=, head=)
  python tools/check_declared_base.py --write-run-stamp=<каталог прогона>
                                      [--corpus=<дир>] [--field имя=значение …]
                                                      положить клеймо прогона
                                                      (зовёт run_appwd.ps1)

Ключи `--dir`, `--corpus`, `--decl` существуют ради ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ, и
они же показывают, что сверка не циркулярна: объявленное при подмене НЕ
МЕНЯЕТСЯ (оно из README), а измеренное меняется, и два плеча дают
ПРОТИВОПОЛОЖНЫЕ приговоры. Сравнивай сторож величину саму с собой — оба плеча
были бы зелены.

Коды возврата:
  0 — каждая проверяемая строка объявления сошлась со своим каталогом, и все
      отпечатки сошлись;
  1 — хоть одно расхождение (названо поимённо);
  2 — сторожу нечем судить (нет объявления, нет каталога, нет score.py,
      неизвестный род базы, score.py отказал).

Печать держится в пределах cp1251: консоль здесь cp1251, и знак вне неё
превращается в «?» — код возврата этого не ловит вовсе.
"""

import argparse
import glob
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DECL = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
SCORE = os.path.join(ROOT, 'tools', 'pie', 'score.py')
RUNS = os.path.join(ROOT, 'tools', 'pie')
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')
MINI = os.path.join(CORPUS, 'mini.csv')
APPWD_PLAN = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts', 'appwd_plan.ps1')

SECTION = u'ДЕЙСТВУЮЩАЯ БАЗА'

#: Имя клейма прогона в каталоге `--out`; пишет `run_appwd.ps1` через
#: `--write-run-stamp`, читает этот сторож.
RUN_STAMP = '.run.json'
ALGO = 'sha256(путь+содержимое, CRLF->LF)/v1'

#: Коммит, до которого (включительно) объявление вправе сказать `run=none`:
#: каталоги `out_rev19_*` сняты полосой П24 в `48987c0d`, ДО того как
#: `run_appwd.ps1` стал писать клеймо (12.09.2026, П33). Всё, что объявлено
#: с `head` позже этого коммита, обязано нести клеймо прогона.
RUN_STAMP_EPOCH = 'bb5df5ae'

#: Что входит в отпечаток КОРПУСА — всё из `corpus/`, что читает прогон и что
#: лежит в git. Сводки (`summary.csv`, `SUMMARY.md`), отпечаток входа
#: (`inputs.csv`) и закреплённые исходники (`pinned/`) прогону не нужны.
#: Матрицы `*.rmx` вне git и судятся клеймом склада (`store_vs_wd.py`).
CORPUS_GLOBS = (
    'manifest.csv', 'parts.csv', 'mini.csv', 'materials.csv', 'detectors.csv',
    'spectra/*.xml', 'devices/*.xml', 'geometries/*.in', 'geometries/index.csv',
)

#: Пробы, из которых собирается `CorpusFsaProbe.exe`: сама проба и довески.
PROBE_MAIN = 'tools/effmaker/probes/CorpusFsaProbe.cs'
PROBE_DIRS = ('tools/effmaker', 'tools/effmaker/probes')
SUPPLY = ('BecquerelMonitor/nucdb.sqlite', 'BecquerelMonitor/matdb.sqlite',
          'BecquerelMonitor/schemedb.sqlite')
CSPROJ = 'BecquerelMonitor/BecquerelMonitor.csproj'

#: графа «часть» объявления -> ключ `--part` у score.py.
PARTS = {u'понятная': 'known', u'непонятная': 'unknown'}

#: подстрока графы «база» -> нужен ли `--only` и какой.
#: Список малой базы берётся из ТОГО ЖЕ mini.csv, что задаёт её прогон.
KINDS = ((u'мал', MINI), (u'полн', None))

#: разбор итоговых строк score.py. Числа печатаются точкой и без группировки.
RE_TOTAL = re.compile(u'^итого\\s+(\\d+)\\s+(\\d+)%\\s+(\\d+)\\s+(\\d+)')
RE_CHI2 = re.compile(u'sum chi2/ndf\\s+([0-9.]+)\\s+медиана\\s+([0-9.]+)')

#: Строка отпечатка объявления: `corpus=<64>`, `sources=<64>`, `head=<7..40>`, `run=none`.
RE_FP = {
    'corpus': re.compile(r'\bcorpus=([0-9a-f]{64})\b'),
    'sources': re.compile(r'\bsources=([0-9a-f]{64})\b'),
    'head': re.compile(r'\bhead=([0-9a-f]{7,40})\b'),
    'run': re.compile(r'\brun=(none)\b'),
}


def _console():
    """Не глушить печать на консоли, которая не всё умеет: приговор важнее вида.

    Кодировку НЕ подменяем нарочно. Подмена на utf-8 сделала бы проверку
    читаемости под cp1251 бессмысленной: знаков «?» не появилось бы никогда,
    а в cp1251-консоли текст стал бы нечитаемым другим способом.
    """
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors='replace')
        except (AttributeError, ValueError):
            pass


def die(msg):
    print(u'ОТКАЗ СТОРОЖА: %s' % msg)
    sys.exit(2)


# ---------------------------------------------------------------------------
# отпечатки
# ---------------------------------------------------------------------------
def sha_norm(data):
    u"""sha256 содержимого с CRLF -> LF (двоичное — как есть)."""
    if b'\0' not in data[:8000]:
        data = data.replace(b'\r\n', b'\n')
    return hashlib.sha256(data).hexdigest()


def fold(shas):
    lines = [u'%s\t%s' % (k, shas[k] or '') for k in sorted(shas)]
    return hashlib.sha256(u'\n'.join(lines).encode('utf-8')).hexdigest()


def _read(path):
    with open(path, 'rb') as fh:
        return fh.read()


def corpus_files(corpus_dir):
    u"""Пути отпечатка корпуса относительно каталога, прямыми косыми."""
    out = []
    for pattern in CORPUS_GLOBS:
        for path in glob.glob(os.path.join(corpus_dir, *pattern.split('/'))):
            out.append(os.path.relpath(path, corpus_dir).replace('\\', '/'))
    return sorted(set(out))


def corpus_shas(corpus_dir):
    return dict((rel, sha_norm(_read(os.path.join(corpus_dir, *rel.split('/')))))
                for rel in corpus_files(corpus_dir))


def _git(*args, **kw):
    binary = kw.get('binary', False)
    try:
        proc = subprocess.run(['git'] + list(args), cwd=ROOT, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, input=kw.get('input'))
    except OSError:
        return None
    if proc.returncode != 0:
        return None
    return proc.stdout if binary else proc.stdout.decode('utf-8', 'replace').rstrip('\r\n')


def main_pattern():
    u"""Правило «что такое довесок» — ОДНО НА ВСЕХ: читается из appwd_plan.ps1
    (`$script:AppWdMainPattern`), а не переписывается здесь (`T57`, `T61`)."""
    try:
        text = io.open(APPWD_PLAN, encoding='utf-8').read()
    except (IOError, OSError):
        text = ''
    hit = re.search(r"\$script:AppWdMainPattern\s*=\s*'([^']+)'", text)
    return re.compile(hit.group(1) if hit else r'static\s+(int|void)\s+Main\s*\(')


def _csproj_files(text):
    files = [CSPROJ]
    for m in re.finditer(r'<Compile\s+Include="([^"]+)"', text):
        rel = m.group(1).replace('\\', '/')
        if rel.lower().endswith('.cs'):
            files.append('BecquerelMonitor/' + rel)
    return files


def sources_tree():
    u"""Набор разбора В РАБОЧЕМ ДЕРЕВЕ: карта «путь -> sha» и отпечаток."""
    proj = os.path.join(ROOT, *CSPROJ.split('/'))
    if not os.path.isfile(proj):
        die(u'нет проекта %s — набор разбора взять неоткуда' % CSPROJ)
    files = _csproj_files(_read(proj).decode('utf-8', 'replace'))
    pat = main_pattern()
    probe_cs = []
    for d in PROBE_DIRS:
        for path in glob.glob(os.path.join(ROOT, *(d.split('/') + ['*.cs']))):
            rel = os.path.relpath(path, ROOT).replace('\\', '/')
            if rel == PROBE_MAIN or not pat.search(_read(path).decode('utf-8', 'replace')):
                probe_cs.append(rel)
    files += probe_cs + list(SUPPLY)
    shas = {}
    for rel in files:
        path = os.path.join(ROOT, *rel.split('/'))
        shas[rel] = sha_norm(_read(path)) if os.path.isfile(path) else None
    return shas


def sources_at(commit):
    u"""Тот же набор В КОММИТЕ, по блобам git (без выгрузки на диск)."""
    names = _git('ls-tree', '-r', '--name-only', commit, '--', 'BecquerelMonitor', 'tools/effmaker')
    if names is None:
        return None
    actual = dict((n.lower(), n) for n in names.splitlines())
    proj_blob = _git('cat-file', '-p', '%s:%s' % (commit, CSPROJ), binary=True)
    if proj_blob is None:
        return None
    wanted = _csproj_files(proj_blob.decode('utf-8', 'replace'))
    probe_cs = [n for n in names.splitlines()
                if n.lower().endswith('.cs') and os.path.dirname(n).replace('\\', '/') in PROBE_DIRS]
    query = [actual.get(w.lower(), w) for w in wanted] + probe_cs + list(SUPPLY)
    inp = u'\n'.join('%s:%s' % (commit, q) for q in query).encode('utf-8') + b'\n'
    out = _git('cat-file', '--batch', binary=True, input=inp)
    if out is None:
        return None
    pat = main_pattern()
    shas = {}
    pos = 0
    for q, w in zip(query, wanted + probe_cs + list(SUPPLY)):
        nl = out.index(b'\n', pos)
        hdr = out[pos:nl].decode('utf-8', 'replace').split()
        pos = nl + 1
        if hdr[-1] == 'missing':
            shas[w] = None
            continue
        size = int(hdr[2])
        data = out[pos:pos + size]
        pos += size + 1
        if w in probe_cs and w != PROBE_MAIN and pat.search(data.decode('utf-8', 'replace')):
            continue                                   # проба со своим Main — не довесок
        shas[w] = sha_norm(data)
    return shas


def tree_head():
    return _git('rev-parse', 'HEAD') or ''


def tree_dirty(sources, corpus_dir):
    u"""Незакоммиченные файлы ИЗ ОТПЕЧАТКОВ: набор разбора и файлы корпуса дерева.

    Судится членство в наборе, а не каталог целиком: грязная проба со своим
    `Main` (не довесок) в `CorpusFsaProbe.exe` не входит и `head=` не мешает.
    """
    st = _git('status', '--porcelain', '--', 'BecquerelMonitor', 'tools/effmaker', 'tools/CORPUS/corpus')
    if not st:
        return []
    corpus_rel = 'tools/CORPUS/corpus/'
    wanted = set(sources)
    if os.path.normcase(corpus_dir) == os.path.normcase(CORPUS):
        wanted |= set(corpus_rel + f for f in corpus_files(corpus_dir))
    dirty = []
    for line in st.splitlines():
        if not line.strip():
            continue
        path = line[3:].strip().replace('\\', '/')
        if ' -> ' in path:
            path = path.split(' -> ')[-1]
        if path in wanted:
            dirty.append(path)
    return sorted(dirty)


def fingerprints(corpus_dir):
    c = corpus_shas(corpus_dir)
    s = sources_tree()
    return dict(corpus=fold(c), corpus_files=len(c), sources=fold(s), sources_files=len(s),
                head=tree_head(), dirty=tree_dirty(s, corpus_dir))


def write_run_stamp(out_dir, corpus_dir, fields):
    fp = fingerprints(corpus_dir)
    stamp = dict(algo=ALGO, written=time.strftime('%Y-%m-%d %H:%M:%S'),
                 corpus_dir=corpus_dir, corpus=fp['corpus'], corpus_files=fp['corpus_files'],
                 sources=fp['sources'], sources_files=fp['sources_files'],
                 head=fp['head'], dirty=fp['dirty'], fields=fields)
    path = os.path.join(out_dir, RUN_STAMP)
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(json.dumps(stamp, ensure_ascii=False, indent=1, sort_keys=True))
        fh.write(u'\n')
    return path, stamp


def read_run_stamp(out_dir):
    path = os.path.join(out_dir, RUN_STAMP)
    if not os.path.isfile(path):
        return None
    try:
        return json.load(io.open(path, encoding='utf-8'))
    except ValueError:
        return {'broken': True}


# ---------------------------------------------------------------------------
# объявление
# ---------------------------------------------------------------------------
def read_section(path):
    """Строки раздела объявления: от его заголовка до следующего `## `."""
    if not os.path.isfile(path):
        die(u'нет файла объявления: %s' % path)
    with open(path, encoding='utf-8') as fh:
        lines = fh.read().splitlines()
    start = None
    for i, line in enumerate(lines):
        if line.startswith('## ') and SECTION in line:
            start = i
            break
    if start is None:
        die(u'в %s нет раздела «%s»' % (path, SECTION))
    end = len(lines)
    for i in range(start + 1, len(lines)):
        if lines[i].startswith('## '):
            end = i
            break
    return lines[start:end]


def cell_number(text):
    """Число из графы таблицы: `**556.1**`, `98 %`, `—` (не объявлено)."""
    clean = text.replace('*', '').replace('%', '').replace(u'\u00a0', ' ').strip()
    if not clean or clean in (u'—', u'-', u'?'):
        return None
    try:
        return float(clean)
    except ValueError:
        return None


def parse_table(lines):
    """Строки таблицы объявления как словари. Своих чисел сторож не заводит."""
    rows = []
    for line in lines:
        if not line.startswith('|'):
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) < 8:
            continue
        if set(cells[0].replace('*', '').strip()) <= set('-: '):
            continue                              # разделитель шапки
        part = cells[1].replace('*', '').strip()
        if part not in PARTS:
            continue                              # шапка таблицы
        rows.append({
            'base': cells[0],
            'part': part,
            'spectra': cell_number(cells[2]),
            'chi2': cell_number(cells[3]),
            'median': cell_number(cells[4]),
            'recall': cell_number(cells[5]),
            'phantoms': cell_number(cells[6]),
            'suppressed': cell_number(cells[7]),
        })
    return rows


def parse_fingerprint(lines):
    u"""Строка отпечатка объявления -> словарь; None, если её нет.

    Берётся ОДНА строка раздела — та, где стоит `corpus=<64 hex>`, — и с неё
    только пары В ОБРАТНЫХ КАВЫЧКАХ: `corpus=…`, `sources=…`, `head=…`,
    `run=none`. Упоминание run=none в пояснительном тексте (без кавычек, в
    другой строке) парой не считается — иначе пометка о снятом до клейма
    прогоне срабатывала бы от одного слова в описании (поймано контролем 4,
    12.09.2026). Две строки с `corpus=` — отказ сторожа: объявление двусмысленно.
    """
    hits = [ln for ln in lines if RE_FP['corpus'].search(ln)]
    if not hits:
        return None
    if len(hits) > 1:
        die(u'в разделе «%s» %d строк с corpus=… — объявление двусмысленно' % (SECTION, len(hits)))
    found = {}
    for key, value in re.findall(r'`([a-z]+)=([^`]+)`', hits[0]):
        rx = RE_FP.get(key)
        if rx is None:
            continue
        hit = rx.match(key + '=' + value.strip())
        if hit and hit.end() == len(key + '=' + value.strip()):
            found[key] = hit.group(1)
    return found


def base_dir(base_cell):
    """Каталог, НАЗВАННЫЙ в графе «база» (обратные кавычки), либо None.

    Годится и голое имя (`out_mini` -> tools/pie/out_mini), и путь от корня
    дерева (`tools/pie/out_p23_mini1`): объявление вольно назвать каталог и
    так, и так, а расходиться с ним сторож не имеет права.
    """
    for found in re.findall('`([^`]+)`', base_cell):
        name = found.strip().strip('/\\')
        if not name:
            continue
        if '/' in name or '\\' in name:
            return os.path.join(ROOT, *re.split(r'[\\/]+', name))
        return os.path.join(RUNS, name)
    return None


def base_kind(base_cell):
    plain = base_cell.replace('*', '').lower()
    for mark, only in KINDS:
        if mark in plain:
            return mark, only
    return None, None


def detect_mode(path):
    """Режим разбора — по тому, ЧТО ЛЕЖИТ в каталоге, а не по умолчанию здесь."""
    modes = set()
    for name in os.listdir(path):
        hit = re.match('^.+_(spline|snip)_components\\.csv$', name)
        if hit:
            modes.add(hit.group(1))
    if not modes:
        return None, u'в каталоге нет ни одного `*_<режим>_components.csv`'
    if len(modes) > 1:
        return None, (u'в каталоге смешаны режимы: %s' % u', '.join(sorted(modes)))
    return sorted(modes)[0], None


def run_score(path, mode, part, only):
    argv = [sys.executable, SCORE, '--mode=' + mode, '--out-dir=' + path,
            '--part=' + part, '--members']
    if only:
        argv.append('--only=' + only)
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    env['PYTHONUTF8'] = '1'
    proc = subprocess.run(argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          env=env)
    text = proc.stdout.decode('utf-8', 'replace')
    if proc.returncode != 0:
        die(u'score.py отказал (код %d) на каталоге %s:\n%s'
            % (proc.returncode, path, text[-2000:]))
    got = {}
    for line in text.splitlines():
        hit = RE_TOTAL.match(line.strip())
        if hit:
            got['spectra'] = float(hit.group(1))
            got['recall'] = float(hit.group(2))
            got['phantoms'] = float(hit.group(3))
            got['suppressed'] = float(hit.group(4))
        hit = RE_CHI2.search(line)
        if hit:
            got['chi2'] = float(hit.group(1))
            got['median'] = float(hit.group(2))
    missing = [k for k in ('spectra', 'recall', 'chi2', 'median') if k not in got]
    if missing:
        die(u'в выводе score.py по %s нет величин: %s'
            % (path, u', '.join(missing)))
    return got, ' '.join(argv[1:])


#: величина -> (подпись, знаков после точки). Объявление печатает столько же.
FIELDS = (
    ('spectra', u'спектров', 0),
    ('chi2', u'sum chi2/ndf', 1),
    ('median', u'медиана', 2),
    ('recall', u'recall, %', 0),
    ('phantoms', u'фантомов', 0),
    ('suppressed', u'подавлен', 0),
)


def compare(row, got):
    """Расхождения строки. Сверяется с точностью, с какой объявлено."""
    bad = []
    for key, title, digits in FIELDS:
        want = row[key]
        if want is None:
            continue                              # клетка не заполнена
        have = got[key]
        if round(want, digits) != round(have, digits):
            bad.append((title, want, have, digits))
    return bad


def fmt(value, digits):
    return ('%.' + str(digits) + 'f') % value     # точка, без группировки


# ---------------------------------------------------------------------------
# сверка отпечатков (T249)
# ---------------------------------------------------------------------------
def _short(h):
    return (h or u'?')[:12]


def declaration_commit(decl_path, fp):
    u"""Коммит, которым строка отпечатка попала в объявление; None — не закоммичена."""
    rel = os.path.relpath(decl_path, ROOT).replace('\\', '/')
    out = _git('log', '--format=%H', '--reverse', '-S', 'sources=' + fp['sources'], '--', rel)
    if not out:
        return None
    return out.splitlines()[0]


def is_ancestor(a, b):
    proc = subprocess.run(['git', 'merge-base', '--is-ancestor', a, b], cwd=ROOT,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return proc.returncode == 0


def check_fingerprints(decl_path, fp, corpus_dir, catalogs):
    u"""Три сверки строки отпечатка. Возвращает список отказов (строк)."""
    bad = []
    print(u'ОТПЕЧАТОК ОБЪЯВЛЕНИЯ (T249):')
    if fp is None:
        print(u'  ⛔ в разделе «%s» НЕТ строки отпечатка (corpus=… sources=…).' % SECTION)
        print(u'     С 12.09.2026 объявление без отпечатка проверке не подлежит: заполнить по')
        print(u'     `.run.json` каталога прогона или `python tools/check_declared_base.py --fingerprints`.')
        return [u'объявление без строки отпечатка']
    for key in ('corpus', 'sources'):
        if key not in fp:
            bad.append(u'в строке отпечатка нет %s=' % key)
    if bad:
        for b in bad:
            print(u'  ⛔ %s' % b)
        return bad

    # 1. корпус
    c_now = corpus_shas(corpus_dir)
    c_fp = fold(c_now)
    src = u'дерево' if os.path.normcase(corpus_dir) == os.path.normcase(CORPUS) else u'подан --corpus'
    if c_fp == fp['corpus']:
        print(u'  [ ОК ] корпус: объявлено %s = %s (%d файлов, %s)' % (_short(fp['corpus']), _short(c_fp), len(c_now), src))
    else:
        bad.append(u'корпус уехал из-под объявления')
        print(u'  [ОТКАЗ] корпус: объявлено %s,' % fp['corpus'])
        print(u'          %s даёт %s (%d файлов)' % (src, c_fp, len(c_now)))
        # назвать файлы: против корпуса дерева, если судится копия, иначе против коммита объявления
        ref = None
        ref_name = u''
        if src != u'дерево':
            ref, ref_name = corpus_shas(CORPUS), u'корпус дерева'
        elif fp.get('head'):
            ref_name = u'коммит %s' % fp['head'][:8]
            names = _git('ls-tree', '-r', '--name-only', fp['head'], '--', 'tools/CORPUS/corpus')
            if names:
                want = [n for n in names.splitlines()
                        if n[len('tools/CORPUS/corpus/'):] in c_now or
                        any(glob.fnmatch.fnmatch(n[len('tools/CORPUS/corpus/'):], g) for g in CORPUS_GLOBS)]
                inp = u'\n'.join('%s:%s' % (fp['head'], n) for n in want).encode('utf-8') + b'\n'
                out = _git('cat-file', '--batch', binary=True, input=inp)
                if out is not None:
                    ref, pos = {}, 0
                    for n in want:
                        nl = out.index(b'\n', pos)
                        hdr = out[pos:nl].decode().split()
                        pos = nl + 1
                        if hdr[-1] == 'missing':
                            continue
                        size = int(hdr[2])
                        ref[n[len('tools/CORPUS/corpus/'):]] = sha_norm(out[pos:pos + size])
                        pos += size + 1
        if ref:
            diff = sorted(k for k in set(ref) | set(c_now) if ref.get(k) != c_now.get(k))
            print(u'      разошлось с %s: %d файлов%s' % (ref_name, len(diff),
                  (u' — ' + u', '.join(diff[:12]) + (u', …' if len(diff) > 12 else u'')) if diff else u''))
        print(u'      Корпус сменился — базу надо переснять и ПЕРЕОБЪЯВИТЬ (числа старого прогона')
        print(u'      сняты с другого корпуса, даже если каталог их по-прежнему даёт).')

    # 2. набор разбора против коммита объявления
    head = fp.get('head')
    where = u''
    if head:
        if _git('rev-parse', '--verify', head + '^{commit}') is None:
            bad.append(u'head=%s git не знает' % head)
            print(u'  [ОТКАЗ] сборка: head=%s — такого коммита нет' % head)
            ref_s = None
        else:
            ref_s, where = sources_at(head), u'коммит head=%s' % head[:8]
            if not is_ancestor(head, 'HEAD'):
                bad.append(u'head=%s не предок HEAD' % head[:8])
                print(u'  [ОТКАЗ] сборка: head=%s не предок нынешнего HEAD' % head[:8])
    else:
        commit = declaration_commit(decl_path, fp)
        if commit:
            ref_s, where = sources_at(commit), u'коммит объявления %s' % commit[:8]
        else:
            ref_s, where = sources_tree(), u'рабочее дерево (строка ещё не закоммичена)'
    if ref_s is not None:
        s_fp = fold(ref_s)
        if s_fp == fp['sources']:
            print(u'  [ ОК ] сборка: объявлено %s = %s (%d файлов, %s)' % (_short(fp['sources']), _short(s_fp), len(ref_s), where))
        else:
            bad.append(u'набор разбора объявления не тот, что в %s' % where)
            print(u'  [ОТКАЗ] сборка: объявлено %s,' % fp['sources'])
            print(u'          %s даёт %s (%d файлов)' % (where, s_fp, len(ref_s)))
            print(u'      Прогон снят поколением, которого нет в коммите объявления: либо head= назван')
            print(u'      не тот, либо стенд нёс незакоммиченные правки разбора. Переснять или назвать коммит.')
    if head and ref_s is not None:
        moved = _git('log', '--oneline', '%s..HEAD' % head, '--', 'BecquerelMonitor', PROBE_MAIN, 'tools/effmaker/probes')
        n = len(moved.splitlines()) if moved else 0
        if n:
            print(u'      к сведению: после head=%s набор разбора менялся %d коммит(ами); воспроизводит ли' % (head[:8], n))
            print(u'      нынешнее дерево объявленные числа, сторож не судит — это A/B на полном корпусе.')
            print(u'      Первые: %s' % u'; '.join(l[:70] for l in moved.splitlines()[:3]))

    # 3. клеймо прогона в каждом объявленном каталоге
    for title, path in catalogs:
        st = read_run_stamp(path)
        rel = os.path.relpath(path, ROOT)
        if st is None:
            if fp.get('run') == 'none' and head and is_ancestor(head, RUN_STAMP_EPOCH):
                print(u'  [ -- ] %s: клейма прогона нет, объявление говорит run=none (снят до клейма, head=%s)' % (title, head[:8]))
            elif fp.get('run') == 'none':
                bad.append(u'%s: run=none без head не позже %s' % (title, RUN_STAMP_EPOCH))
                print(u'  [ОТКАЗ] %s: run=none принимается только с head= не позже %s' % (title, RUN_STAMP_EPOCH))
            else:
                bad.append(u'%s: в каталоге нет клейма прогона %s' % (title, RUN_STAMP))
                print(u'  [ОТКАЗ] %s: в %s нет клейма прогона %s — прогон снят МИМО run_appwd.ps1' % (title, rel, RUN_STAMP))
            continue
        if st.get('broken') or st.get('algo') != ALGO:
            bad.append(u'%s: клеймо прогона не разбирается' % title)
            print(u'  [ОТКАЗ] %s: клеймо %s не разбирается или другого образца' % (title, rel))
            continue
        errs = []
        for key in ('corpus', 'sources'):
            if st.get(key) != fp[key]:
                errs.append(u'%s: в клейме %s, объявлено %s' % (key, st.get(key), fp[key]))
        if errs:
            bad.append(u'%s: клеймо прогона расходится с объявлением' % title)
            print(u'  [ОТКАЗ] %s: клеймо прогона в %s НЕ ТО, что объявлено — %s' % (title, rel, u'; '.join(errs)))
            print(u'      Каталог перезаписан другим прогоном или объявлен чужой каталог.')
        else:
            f = st.get('fields') or {}
            print(u'  [ ОК ] %s: клеймо прогона сошлось (снято %s, HEAD %s%s%s)'
                  % (title, st.get('written', '?'), (st.get('head') or '?')[:8],
                     (u', ключи: ' + f['keys']) if f.get('keys') else u'',
                     (u', ГРЯЗНОЕ дерево: ' + u', '.join(st.get('dirty'))) if st.get('dirty') else u''))
    print()
    return bad


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--decl', default=DECL,
                    help=u'файл объявления (по умолчанию tools/CORPUS/README.md)')
    ap.add_argument('--base', default=None,
                    help=u'судить только базу, чья графа содержит эту подстроку')
    ap.add_argument('--dir', default=None,
                    help=u'каталог вместо названного в объявлении '
                         u'(положительный контроль; нужен --base)')
    ap.add_argument('--corpus', default=None,
                    help=u'каталог корпуса вместо tools/CORPUS/corpus (положительный контроль)')
    ap.add_argument('--fingerprints', action='store_true',
                    help=u'напечатать отпечатки дерева и выйти')
    ap.add_argument('--write-run-stamp', default=None, metavar='DIR',
                    help=u'положить клеймо прогона в каталог (зовёт run_appwd.ps1)')
    ap.add_argument('--field', action='append', default=[], metavar='NAME=VALUE',
                    help=u'поле в клеймо прогона (можно несколько)')
    args = ap.parse_args()

    corpus_dir = args.corpus if args.corpus else CORPUS
    if not os.path.isabs(corpus_dir):
        corpus_dir = os.path.join(ROOT, corpus_dir)
    if not os.path.isdir(corpus_dir):
        die(u'нет каталога корпуса %s' % corpus_dir)

    if args.fingerprints:
        fp = fingerprints(corpus_dir)
        print(u'corpus=%s' % fp['corpus'])
        print(u'sources=%s' % fp['sources'])
        print(u'head=%s' % fp['head'])
        print(u'corpus_files=%d' % fp['corpus_files'])
        print(u'sources_files=%d' % fp['sources_files'])
        print(u'dirty=%s' % u','.join(fp['dirty']))
        return 0

    if args.write_run_stamp:
        out_dir = args.write_run_stamp
        if not os.path.isdir(out_dir):
            die(u'нет каталога прогона %s' % out_dir)
        fields = {}
        for f in args.field:
            k, _, v = f.partition('=')
            fields[k.strip()] = v
        path, stamp = write_run_stamp(out_dir, corpus_dir, fields)
        print(u'клеймо прогона: %s (corpus %s, sources %s, HEAD %s%s)'
              % (path, _short(stamp['corpus']), _short(stamp['sources']), stamp['head'][:8],
                 (u', ГРЯЗНОЕ дерево: ' + u', '.join(stamp['dirty'])) if stamp['dirty'] else u''))
        return 0

    if not os.path.isfile(SCORE):
        die(u'нет счётчика: %s' % SCORE)

    section = read_section(args.decl)
    rows = parse_table(section)
    if not rows:
        die(u'в разделе «%s» файла %s не разобрано ни одной строки таблицы'
            % (SECTION, args.decl))
    fp = parse_fingerprint(section)

    if args.base:
        rows = [r for r in rows if args.base.lower() in r['base'].replace('*', '').lower()]
        if not rows:
            die(u'в объявлении нет базы с подстрокой «%s»' % args.base)
    if args.dir:
        if not args.base:
            die(u'--dir без --base: непонятно, какой базе подменять каталог')
        kinds = set(base_kind(r['base'])[0] for r in rows)
        if len(kinds) != 1:
            die(u'--base=«%s» выбрал %d разных баз: подмена каталога двусмысленна'
                % (args.base, len(kinds)))

    print(u'ОБЪЯВЛЕНИЕ: %s, раздел «%s»'
          % (os.path.relpath(args.decl, ROOT), SECTION))
    print(u'ИЗМЕРЕНИЕ:  tools/pie/score.py по файлам самого каталога')
    print(u'строк объявления взято: %d' % len(rows))
    print()

    checked = failed = skipped = 0
    verdicts = []
    catalogs = []
    for row in rows:
        title = u'%s / %s' % (row['base'].replace('*', '').strip(), row['part'])
        mark, only = base_kind(row['base'])
        if mark is None:
            die(u'неизвестен род базы в графе «%s»: ждались «малая» или «полная»'
                % row['base'])
        named = base_dir(row['base'])
        if args.dir:
            path = args.dir if os.path.isabs(args.dir) else os.path.join(ROOT, args.dir)
            source = u'подан ключом --dir'
        elif named:
            path = named
            source = u'назван объявлением'
        else:
            skipped += 1
            print(u'[ -- ] %s' % title)
            print(u'      ВНИМАНИЕ: каталог в объявлении НЕ НАЗВАН - сверить нечем.')
            print(u'      Объявление, не называющее каталога, проверке не подлежит:')
            print(u'      имя каталога ставится в графу «база» обратными кавычками.')
            print()
            continue

        if not os.path.isdir(path):
            die(u'%s: каталог %s (%s) не найден'
                % (title, os.path.relpath(path, ROOT), source))
        mode, err = detect_mode(path)
        if mode is None:
            die(u'%s: %s (%s)' % (title, err, os.path.relpath(path, ROOT)))
        if path not in [p for _t, p in catalogs]:
            catalogs.append((row['base'].replace('*', '').strip(), path))

        got, cmd = run_score(path, mode, PARTS[row['part']], only)
        bad = compare(row, got)
        checked += 1
        if bad:
            failed += 1
            print(u'[ОТКАЗ] %s' % title)
        else:
            print(u'[ ОК ] %s' % title)
        print(u'      каталог: %s (%s)' % (os.path.relpath(path, ROOT), source))
        print(u'      счёт:    python %s' % cmd)
        if bad:
            print(u'      РАСХОЖДЕНИЯ (%d):' % len(bad))
            for title2, want, have, digits in bad:
                print(u'        %-14s объявлено %s, измерено %s'
                      % (title2, fmt(want, digits), fmt(have, digits)))
            verdicts.append((title, bad, os.path.relpath(path, ROOT)))
        else:
            shown = u', '.join(
                u'%s %s' % (t, fmt(got[k], d)) for k, t, d in FIELDS
                if row[k] is not None)
            print(u'      сошлось: %s' % shown)
        print()

    fp_bad = check_fingerprints(args.decl, fp, corpus_dir, catalogs)

    print(u'ИТОГО: строк сверено %d, расхождений %d, без каталога %d; отпечатки: отказов %d'
          % (checked, failed, skipped, len(fp_bad)))
    if failed:
        print()
        print(u'ОСТАНОВ: каталог из объявления даёт НЕ ОБЪЯВЛЕННЫЕ числа.')
        print(u'Это не описка в таблице, а подмена ПОКОЛЕНИЯ прогона: числа')
        print(u'правдоподобны, разница в десятых, и глазом её не видно.')
        print(u'Чинится одним из двух движений, и оба - за распорядителем:')
        print(u'  1) переснять базу В названный каталог')
        print(u'     (& tools\\CORPUS\\scripts\\run_mini.ps1 -Out <корень>\\tools\\pie\\out_mini);')
        print(u'  2) назвать в объявлении тот каталог, где объявленные числа лежат.')
        for title, bad, path in verdicts:
            print(u'  %s -> %s: %s' % (title, path,
                                       u'; '.join(t for t, _w, _h, _d in bad)))
    if fp_bad:
        print()
        print(u'ОСТАНОВ: отпечаток объявления НЕ СХОДИТСЯ — %s.' % u'; '.join(fp_bad))
        print(u'Объявление ссылается на прогон, который нынешним корпусом или нынешней')
        print(u'сборкой не воспроизводится. Переснять базу и переобъявить (строку отпечатка')
        print(u'дают `.run.json` каталога прогона и `--fingerprints`).')
    if failed or fp_bad:
        return 1
    if checked == 0:
        print()
        print(u'ВНИМАНИЕ: сверено НОЛЬ строк - объявление не называет ни одного')
        print(u'каталога. Сторож при этом молчит, и молчание тут ничего не значит.')
    return 0


if __name__ == '__main__':
    _console()
    sys.exit(main())
