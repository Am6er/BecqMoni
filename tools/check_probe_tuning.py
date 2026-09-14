# -*- coding: utf-8 -*-
u"""Сторож ОТЧЁТА О НАСТРОЙКАХ у проб, которые считают FSA (`T101`, `T243`).

## Что судится и почему

Проба, собирающая анализатор руками, обязана СКАЗАТЬ, чем её разбор отличается
от поставочного. Иначе прогон с ключами выглядит в выводе в точности как
умолчательный — это механизм ~~`S82`~~ («стенд настроен иначе, чем экран, и
молчит»), и до 06.09.2026 им болели СЕМЬ проб из девяти, включая `FsaStackShot`:
ровно тот прибор, которым это расхождение и ловят.

06.09.2026 (`T243`) правило вынесено в ОДНО место — довесок
`tools/effmaker/probes/FsaTuningReport.cs` (файл без точки входа; такие
`build_all.ps1` кладёт каждой пробе), — и позвано из ВСЕХ проб, считающих FSA.
До этого правило жило двумя копиями, и копии уже разошлись: доля пола
печаталась `F5` в одной и `P3` в другой. Поэтому сторож судит ДВЕ вещи:

  * ДОВЕСОК — что само правило цело (эталон отражением, полоса поимённо,
    и что довесок остался довеском, а не стал пробой);
  * КАЖДУЮ пробу, считающую FSA, — что она правило ЗОВЁТ, в верном порядке,
    и что второй копии правила у неё нет.

Правил у пробы четыре, и все четыре — про то, чем отчёт можно испортить, НЕ
ломая сборку (компилятор ни одного из них не видит):

  1. СНИМОК ЗВАН. `FsaTuningReport.Snapshot()` — иначе эталон сличается с
     нулями структуры, и отчёт врёт в обе стороны.
  2. ПЕЧАТЬ ЗВАНА. `FsaTuningReport.Print(...)` — определить печать и не
     позвать её самый дешёвый способ вернуть молчание.
  3. СНИМОК СНЯТ ДО РАЗБОРА КЛЮЧЕЙ. Полосу отражение не видит — это статика,
     которую оба конца читают в момент обращения; эталон, снятый ПОСЛЕ ключей,
     родится с уже уведённой полосой, и `--band=` в выводе не будет виден
     вовсе (`S101`).
  4. ОТЧЁТ ПЕЧАТАЕТСЯ ДО РАЗБОРА. После `Analyze` у анализатора появляется
     СОСТОЯНИЕ прогона (`RefitZState`, `T240`), и сличение с чистым эталоном
     начнёт называть исходом то, что обязано называть настройками.

Плюс пятое, про единственность: у пробы не должно быть СВОЕГО эталона
(`stock = new FsaAnalyzer()`) — это вторая копия правила, ровно то, от чего
лечила `T243`.

## Порог: молчащих проб НОЛЬ

⛔ До `T243` молчание остальных проб печаталось числом, но отказом НЕ считалось:
правило «печатать всем» сделало бы сторожа красным с рождения, а такого
перестают звать (`T220`). Теперь зовут все — и порог поднят: ЛЮБАЯ молчащая
проба это ОТКАЗ, с именем.

⚠ Перепись проб ведётся по образцу `new FsaAnalyzer` БЕЗ скобок нарочно:
узкий образец `new FsaAnalyzer()` не видит записи через список полей
(`new FsaAnalyzer { ... }`), и `FsaRobustnessProbe` пряталась от переписи
именно так — молчащая проба, невидимая сторожу молчания.

  python tools/check_probe_tuning.py [--selftest]

Коды возврата:
  0 — довесок цел, все пробы отчитываются, порядок соблюдён;
  1 — правило нарушено (нарушители названы поимённо);
  2 — самопроверка не прошла: сторож слеп к подставленной порче;
  3 — судить нечего (каталог проб, довесок или судимая проба не найдены).

⛔ Печать без знаков вне cp1251: консоль здесь cp1251, и `⛔`/`⚠`/`→` в ней
превращаются в `?`. Кода возврата это не меняет, а читателя лишает (`T219`).
"""

import io
import os
import re
import sys

PROBES = os.path.join(u'tools', u'effmaker', u'probes')

# ЕДИНСТВЕННОЕ место, где живёт правило отчёта (`T243`).
COMPANION = u'FsaTuningReport.cs'

# Приборы, которыми сличают стенд с экраном. Пропасть они не имеют права:
# исчезли из переписи — значит переименованы или перестали считать FSA, и
# это остановка, а не тихо уменьшившийся список.
ANCHORS = [
    (u'CorpusFsaProbe.cs', u'корпусная мерка: её числа объявляют базу'),
    (u'FsaStackShot.cs', u'снимок стека настоящим кодом отрисовки'),
]

ENTRY = re.compile(r'\bstatic\s+(?:int|void)\s+Main\s*\(')
SNAPSHOT = re.compile(r'^\s*FsaTuningReport\.Snapshot\s*\(\s*\)\s*;')
PRINT = re.compile(r'^\s*FsaTuningReport\.Print\s*\(')
ARGS = re.compile(r'\bin\s+args\b|\bargs\s*\[|\bargs\.Length\b|\bstring\s+a\s+in\s+args\b')
ANALYZE = re.compile(r'\.Analyze\s*\(')
OWN_STOCK = re.compile(r'\bstock\s*=\s*new\s+FsaAnalyzer\s*\(\s*\)')
# ⚠ БЕЗ скобок: запись через список полей (`new FsaAnalyzer { ... }`) — тоже
# счёт FSA, и узкий образец её не видел.
RUNNER = re.compile(r'\bnew\s+FsaAnalyzer\b')

# Правила довеска: что в нём обязано быть, и довод, почему.
COMPANION_RULES = [
    (re.compile(r'\bstock\s*=\s*new\s+FsaAnalyzer\s*\(\s*\)'),
     u'эталона нет: «stock = new FsaAnalyzer()» не найдено — значит'
     u' поставочные значения выписаны рядом второй копией'),
    (re.compile(r'FsaBand\.DefaultMode\s*!=\s*stockMode'),
     u'полоса не сличается поимённо: «FsaBand.DefaultMode != stockMode» не'
     u' найдено, а отражение полосу НЕ ВИДИТ — ключ --band= пропадёт из отчёта'),
    (re.compile(r'stockMode\s*=\s*FsaBand\.DefaultMode'),
     u'снимок поставочной полосы не берётся вовсе:'
     u' «stockMode = FsaBand.DefaultMode» не найдено'),
    (re.compile(r'public\s+static\s+void\s+Snapshot\s*\('),
     u'у довеска нет Snapshot(): снимать эталон нечем'),
    (re.compile(r'public\s+static\s+void\s+Print\s*\('),
     u'у довеска нет Print(): печатать отчёт нечем'),
]


def first(lines, rx):
    u"""Номер первой строки (с единицы), где сработало правило; 0 — не нашлось."""
    for i, line in enumerate(lines):
        if rx.search(line):
            return i + 1
    return 0


def judge_probe(text):
    u"""Нарушения одной пробы списком строк. Пусто — правила соблюдены."""
    lines = text.split(u'\n')
    bad = []

    snapshot = first(lines, SNAPSHOT)
    printed = first(lines, PRINT)
    keys = first(lines, ARGS)
    analyze = first(lines, ANALYZE)

    if snapshot == 0:
        bad.append(u'снимок эталона НЕ ЗВАН: строки «FsaTuningReport.Snapshot();»'
                   u' нет — сличать настройки не с чем')
    if printed == 0:
        bad.append(u'печать настроек НЕ ЗВАНА: строки «FsaTuningReport.Print(...)»'
                   u' нет, и проба о своих настройках молчит')

    if snapshot != 0 and keys != 0 and snapshot > keys:
        bad.append(u'снимок эталона взят ПОСЛЕ разбора ключей'
                   u' (строка %d против %d): эталон родится с уведённой полосой,'
                   u' и ключ полосы в выводе не виден' % (snapshot, keys))

    if printed != 0 and analyze != 0 and printed > analyze:
        bad.append(u'отчёт печатается ПОСЛЕ разбора (строка %d против %d):'
                   u' у анализатора к этому мигу есть состояние прогона,'
                   u' и оно попадёт в отчёт вместо настроек' % (printed, analyze))

    own = first(lines, OWN_STOCK)
    if own != 0:
        bad.append(u'ВТОРАЯ КОПИЯ правила (строка %d): свой эталон'
                   u' «stock = new FsaAnalyzer()» у пробы быть не должен —'
                   u' правило живёт в довеске %s' % (own, COMPANION))

    return bad


def judge_companion(text):
    u"""Нарушения довеска списком строк."""
    bad = []
    if ENTRY.search(text):
        bad.append(u'довесок перестал быть довеском: в нём есть объявление точки'
                   u' входа, и build_all.ps1 (он отличает довесок от пробы поиском'
                   u' по ТЕКСТУ) соберёт его отдельной пробой — правило пропадёт'
                   u' у всех разом')
    for rx, why in COMPANION_RULES:
        if not rx.search(text):
            bad.append(why)
    return bad


def read(path):
    fh = io.open(path, u'r', encoding=u'utf-8', newline=u'')
    try:
        return fh.read()
    finally:
        fh.close()


def census(root):
    u"""Пробы (файлы с точкой входа), которые СЧИТАЮТ FSA: (имя, текст)."""
    out = []
    for name in sorted(os.listdir(root)):
        if not name.endswith(u'.cs') or name == COMPANION:
            continue
        text = read(os.path.join(root, name))
        if not ENTRY.search(text):
            continue  # довесок, а не проба
        if not (RUNNER.search(text) and ANALYZE.search(text)):
            continue
        out.append((name, text))
    return out


def selftest(root):
    u"""ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подставленная порча обязана быть поймана.

    Правило судится не на выдуманном тексте, а на НАСТОЯЩИХ файлах,
    испорченных в памяти, — иначе проверялась бы выдумка, а не сторож.
    """
    missed = []

    comp = read(os.path.join(root, COMPANION))
    if judge_companion(comp):
        missed.append(u'целый довесок объявлен испорченным — сторож ложно тревожит')

    comp_cases = [
        (u'у довеска отобрали эталон',
         comp.replace(u'FsaAnalyzer stock = new FsaAnalyzer()',
                      u'FsaAnalyzer stock = HandMadeDefaults()')),
        (u'у довеска сняли сличение полосы',
         comp.replace(u'FsaBand.DefaultMode != stockMode', u'false')),
        (u'у довеска сняли снятие полосы',
         comp.replace(u'stockMode = FsaBand.DefaultMode', u'stockMode = default(FsaBandMode)')),
        (u'довеску приделали точку входа',
         comp + u'\nstatic class Podstava { static int Main() { return 0; } }\n'),
    ]
    for what, spoiled in comp_cases:
        if spoiled == comp:
            missed.append(u'порча «%s» не подставилась: текст не изменился' % what)
        elif not judge_companion(spoiled):
            missed.append(u'порча «%s» НЕ ПОЙМАНА' % what)

    # Проба берётся НАСТОЯЩАЯ и та, что настраивает анализатор из ключей.
    name = u'FsaCascadeProbe.cs'
    path = os.path.join(root, name)
    if not os.path.exists(path):
        return missed + [u'самопроверке не на чем работать: нет %s' % path]

    good = read(path)
    if judge_probe(good):
        missed.append(u'целая проба %s объявлена испорченной — сторож ложно тревожит'
                      % name)

    # ⚠ Перевод строки у части проб `\r\n`: снимать вызов надо ПРАВИЛОМ, а не
    # строковой заменой с `\n`, — иначе замена молча не срабатывает, вызов
    # остаётся на месте, и «порча не поймана» означает «порчи не было».
    no_print = re.sub(r'^[ \t]*FsaTuningReport\.Print\s*\([^)]*\);[ \t]*\r?\n',
                      u'', good, flags=re.M)
    no_snap = re.sub(r'^[ \t]*FsaTuningReport\.Snapshot\s*\(\s*\)\s*;[ \t]*\r?\n',
                     u'', good, flags=re.M)
    probe_cases = [
        (u'вызов печати снят', no_print),
        (u'вызов снимка снят', no_snap),
        (u'снимок уехал после разбора ключей',
         no_snap.replace(u'FsaAnalyzer analyzer = new FsaAnalyzer();',
                         u'FsaTuningReport.Snapshot();\r\n'
                         u'            FsaAnalyzer analyzer = new FsaAnalyzer();')),
        (u'отчёт печатается после разбора',
         no_print.replace(u'            double plainMs = clock.Elapsed.TotalMilliseconds;',
                          u'            FsaTuningReport.Print(analyzer);\r\n'
                          u'            double plainMs = clock.Elapsed.TotalMilliseconds;')),
        (u'у пробы завёлся свой эталон',
         good.replace(u'FsaAnalyzer analyzer = new FsaAnalyzer();',
                      u'FsaAnalyzer stock = new FsaAnalyzer();\r\n'
                      u'            FsaAnalyzer analyzer = new FsaAnalyzer();')),
    ]
    for what, spoiled in probe_cases:
        if spoiled == good:
            missed.append(u'порча «%s» не подставилась: текст не изменился' % what)
        elif not judge_probe(spoiled):
            missed.append(u'порча «%s» НЕ ПОЙМАНА' % what)

    return missed


def repo():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main(argv):
    try:
        sys.stdout.reconfigure(encoding=u'utf-8')
    except Exception:
        pass

    root = os.path.join(repo(), PROBES)
    if not os.path.isdir(root):
        print(u'ОСТАНОВ: каталога проб нет — %s' % root)
        return 3

    companion = os.path.join(root, COMPANION)
    if not os.path.exists(companion):
        print(u'ОСТАНОВ: довеска с правилом нет на диске — %s' % companion)
        return 3

    probes = census(root)
    if not probes:
        print(u'ОСТАНОВ: ни одной пробы, считающей FSA, не нашлось — так не бывает')
        return 3

    names = set(n for n, _ in probes)
    print(u'сторож отчёта о настройках (`T243`): правило живёт ОДНИМ местом —'
          u' %s; судятся довесок и %d проб, считающих FSA'
          % (COMPANION, len(probes)))

    # ⛔ ПРИГОВОР ПЕРВЫМ, САМОПРОВЕРКА ВТОРОЙ, и порядок этот измерен. Обратный
    # (проверено 06.09.2026 снятием вызова с диска) даёт код 2 «сторож слеп»
    # там, где на деле сломана ПРОБА: самопроверка портит НАСТОЯЩИЕ файлы, и её
    # опора — те же файлы, что судятся. Настоящая поломка обязана называться
    # поломкой пробы, а не отказом сторожа.
    bad = {}

    found = judge_companion(read(companion))
    print(u'  %-26s %s — единственное место, где живёт правило'
          % (COMPANION, u'ЦЕЛ' if not found else u'НАРУШЕНО'))
    if found:
        bad[COMPANION] = found

    for name, why in ANCHORS:
        if name not in names:
            print(u'ОСТАНОВ: судимой пробы нет в переписи — %s (%s).'
                  u' Переименована или перестала считать FSA' % (name, why))
            return 3

    silent = []
    for name, text in probes:
        found = judge_probe(text)
        if found:
            bad[name] = found
            if any(u'печать настроек НЕ ЗВАНА' in f for f in found):
                silent.append(name)

    print(u'  проб, считающих FSA: %d; о настройках молчат %d — %s'
          % (len(probes), len(silent),
             u', '.join(silent) if silent else u'таких нет'))

    if bad:
        print(u'ОСТАНОВ: отчёт о настройках сломан у %d из %d судимых:'
              % (len(bad), len(probes) + 1))
        for name in sorted(bad):
            for line in bad[name]:
                print(u'   %s: %s' % (name, line))
        print(u'  (самопроверка не гонялась: её опора — те же файлы, что сломаны)')
        return 1

    missed = selftest(root)
    if missed:
        print(u'ОСТАНОВ: САМОПРОВЕРКА НЕ ПРОШЛА — сторож слеп:')
        for m in missed:
            print(u'   ' + m)
        return 2
    print(u'  самопроверка: девять подставленных порч пойманы,'
          u' целые довесок и проба чисты')

    if u'--selftest' in argv:
        return 0

    print(u'ОТЧЁТ О НАСТРОЙКАХ ЦЕЛ: правило одно, зовут его все %d проб,'
          u' молчащих нет.' % len(probes))
    return 0


if __name__ == u'__main__':
    sys.exit(main(sys.argv[1:]))
