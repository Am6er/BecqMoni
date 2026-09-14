# -*- coding: utf-8 -*-
u"""Отсев по значимости НЕ ОСТАВЛЯЕТ РАЗБОР БЕЗ ЕДИНОГО НУКЛИДА (`A275`).

## Откуда взялся

`AS80_Charoite` (малая база, непонятная часть) возвращал разбор из ДВУХ
компонентов, и оба — приборные образы: `Xray-Pb` 20.6 % и `Xray-Ba` 1.1 %,
при объявленных K-40 и Ra-226. Вердикт мерки стоял «состав пересилен
приборным образом».

Замером 07.09.2026 названо, что образ состав НЕ ПЕРЕБИВАЛ. Отсев по
значимости (`FsaAnalyzer.RefitZ` = 3) судил нуклиды и мешающие образы ОДНИМ
списком: `Xray-Pb` z = 15.14 порог проходил, объявленный `Ra-226` z = 2.26 —
нет. Старое правило самоотключения («уцелевших нет — отсева не было») не
срабатывало ровно потому, что уцелел ОБРАЗ, и отсев выносил весь состав.
Континуум разносился потом по двум уцелевшим — отсюда и 20.6 % при
СОБСТВЕННЫХ 4433 отсчётах образа, то есть 1.0 % спектра.

⚠ Читатель здесь СТАТИЧЕСКИЙ, как у ~~`A271`~~, и по той же причине:
свободный компонент без верхней границы всегда уменьшает Σχ², приговор по
невязке подтвердил бы любую правку. Судится ФОРМА правила.

## Что проверяется

Область суждения — кусок `FsaAnalyzer.cs` от простановки порога
(`this.RefitZUsed = threshold;`) до простановки числа уцелевших
(`this.RefitZKept = keep.Count;`): именно там живёт приговор отсева.

1. класс компонента в этой области вообще спрашивается —
   `FsaComponentKind.Nuisance` упомянут;
2. считается ОТДЕЛЬНЫЙ знаменатель по нуклидам (`RefitZNuclidesJudged`) и
   отдельный счётчик возвращённых (`RefitZNuclidesRescued`); оба объявлены
   свойствами и оба сбрасываются в начале разбора — уцелевшее с прошлого
   спектра число было бы ложью того же рода, что и молчание ~~`T240`~~;
3. условие возврата спрашивает НУКЛИДНЫЙ счётчик прошедших (== 0), а не
   общий `keep.Count`;
4. условие возврата требует, чтобы порог прошёл ХОТЬ КТО-ТО (`passed > 0`):
   когда не проходит никто, приговор выносит старая ветка `AllBelow`, и
   подмена её на `NothingBelow` переписала бы смысл состояния ~~`T240`~~,
   ничего не изменив по существу;
5. возвращаются ТОЛЬКО нуклидные колонки: в ветке возврата стоит
   `Kind != FsaComponentKind.Nuisance`. Обратной симметрии нет нарочно —
   разбор без единого мешающего образа законен;
6. замер живёт пробой: `FsaRefitZClassProbe.cs` существует и читает
   `RefitZNuclidesRescued`. Правило без замера — не правило.

## Самопроверка

⛔ Все шесть проверок прошли бы и на пустом чтении, поэтому на каждом прогоне
сторож судит ТРИ порченые копии и обязан назвать каждую поимённо:

  * возврат распространён на мешающие образы (снято `Kind !=`);
  * условие возврата переписано на общий `keep.Count`;
  * снят сброс счётчика возвращённых в начале разбора.

Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — нечего читать.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ANALYZER = os.path.join(REPO, 'BecquerelMonitor', 'FullSpectrumAnalysis', 'FsaAnalyzer.cs')
PROBE = os.path.join(REPO, 'tools', 'effmaker', 'probes', 'FsaRefitZClassProbe.cs')

JUDGED = u'RefitZNuclidesJudged'
RESCUED = u'RefitZNuclidesRescued'
NUISANCE = u'FsaComponentKind.Nuisance'

HEAD = u'this.RefitZUsed = threshold;'
TAIL = u'this.RefitZKept = keep.Count;'

# Возврат разрешён только нуклидной колонке.
RE_ONLY_NUCLIDE = re.compile(
    u'rescueNuclides\\s*&&\\s*[A-Za-z_.]*\\.?Kind\\s*!=\\s*%s' % re.escape(NUISANCE))
# Условие возврата: нуклидных прошедших НЕТ, а хоть кто-то прошёл.
RE_RESCUE_COND = re.compile(
    u'bool\\s+rescueNuclides\\s*=\\s*([^;]+);', re.S)


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def read(path):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        return fh.read()


def strip_comments(text):
    u"""Тело без описаний и заметок: правило судится по КОДУ, а не по словам о нём."""
    text = re.sub(u'/\\*.*?\\*/', u' ', text, flags=re.S)
    return re.sub(u'^[ \\t]*//.*$', u' ', text, flags=re.M)


def verdict_block(code):
    u"""Кусок приговора отсева; None — границ нет, судить нечем."""
    head = code.find(HEAD)
    if head < 0:
        return None
    tail = code.find(TAIL, head)
    if tail < 0:
        return None
    return code[head:tail + len(TAIL)]


def judge(analyzer_text, probe_text):
    u"""Список отказов словами; пустой — сошлось."""
    bad = []
    code = strip_comments(analyzer_text)
    block = verdict_block(code)
    if block is None:
        bad.append(u'в FsaAnalyzer.cs не найден кусок приговора отсева '
                   u'(«%s» … «%s») — судить нечем' % (HEAD, TAIL))
        return bad

    # 1. класс компонента вообще спрашивается
    if NUISANCE not in block:
        bad.append(u'приговор отсева не спрашивает класс компонента (%s): '
                   u'нуклиды и приборные образы судятся одним списком' % NUISANCE)

    # 2. счётчики объявлены, читаются и сбрасываются
    for name in (JUDGED, RESCUED):
        if not re.search(u'public\\s+int\\s+%s\\s*\\{' % name, code):
            bad.append(u'нет свойства %s — счётчика класса' % name)
        if not re.search(u'this\\.%s\\s*=\\s*0\\s*;' % name, code):
            bad.append(u'счётчик %s не сбрасывается в начале разбора: число '
                       u'с прошлого спектра переживёт следующий' % name)
        if name not in block:
            bad.append(u'счётчик %s не проставляется в приговоре отсева' % name)

    # 3–4. условие возврата
    cond = RE_RESCUE_COND.search(block)
    if cond is None:
        bad.append(u'в приговоре отсева нет условия возврата '
                   u'(bool rescueNuclides = …): правило класса отсутствует')
    else:
        text = u' '.join(cond.group(1).split())
        if u'nuclidesPassed == 0' not in text:
            bad.append(u'условие возврата не спрашивает НУКЛИДНЫЙ счётчик '
                       u'прошедших (nuclidesPassed == 0): «%s»' % text)
        if u'passed > 0' not in text:
            bad.append(u'условие возврата не требует passed > 0: при пустом '
                       u'`keep` оно подменит приговор AllBelow на NothingBelow '
                       u'(смысл состояния `T240`): «%s»' % text)

    # 5. возвращаются только нуклидные колонки
    if RE_ONLY_NUCLIDE.search(block) is None:
        bad.append(u'ветка возврата не ограничена нуклидным классом '
                   u'(rescueNuclides && …Kind != %s): правило вернёт и '
                   u'приборный образ' % NUISANCE)

    # 6. замер живёт пробой
    if probe_text is None:
        bad.append(u'нет пробы FsaRefitZClassProbe.cs — правило ничем не замерено')
    elif RESCUED not in probe_text:
        bad.append(u'FsaRefitZClassProbe.cs не читает %s — замер не про правило'
                   % RESCUED)

    return bad


def spoil_any_kind(text):
    u"""Порча первая: возврат распространён и на мешающие образы."""
    spoiled = text.replace(
        u'if (rescueNuclides && component.Kind != FsaComponentKind.Nuisance)',
        u'if (rescueNuclides)')
    return spoiled if spoiled != text else None


def spoil_general_count(text):
    u"""Порча вторая: условие возврата переписано на общий счётчик."""
    spoiled = text.replace(
        u'bool rescueNuclides = passed > 0 && nuclidesJudged > 0 && nuclidesPassed == 0;',
        u'bool rescueNuclides = nuclidesJudged > 0 && keep.Count == 0;')
    return spoiled if spoiled != text else None


def spoil_no_reset(text):
    u"""Порча третья: снят сброс счётчика возвращённых."""
    spoiled = re.sub(u'\\n[ \\t]*this\\.%s\\s*=\\s*0\\s*;' % RESCUED, u'', text, count=1)
    return spoiled if spoiled != text else None


def main():
    if not os.path.isfile(ANALYZER):
        print(u'⛔ нет файла: %s' % ANALYZER)
        return 2

    analyzer_text = read(ANALYZER)
    probe_text = read(PROBE) if os.path.isfile(PROBE) else None

    print(u'=== ОТСЕВ ПО ЗНАЧИМОСТИ И КЛАСС КОМПОНЕНТА (`A275`) ===')
    block = verdict_block(strip_comments(analyzer_text))
    print(u'  кусок приговора отсева: %s'
          % (u'%d знаков' % len(block) if block else u'НЕ НАЙДЕН'))

    bad = judge(analyzer_text, probe_text)
    for line in bad:
        print(u'  ⛔ %s' % line)
    if not bad:
        print(u'  все шесть правил сошлись')

    print()
    print(u'=== САМОПРОВЕРКА: три порченые копии обязаны быть названы ===')
    checks = (
        (u'возврат распространён на мешающие образы', spoil_any_kind,
         lambda line: u'нуклидным классом' in line),
        (u'условие возврата переписано на общий счётчик', spoil_general_count,
         lambda line: u'nuclidesPassed' in line or u'passed > 0' in line),
        (u'снят сброс счётчика возвращённых', spoil_no_reset,
         lambda line: RESCUED in line and u'сбрасывается' in line),
    )
    for what, make, hits in checks:
        spoiled = make(analyzer_text)
        if spoiled is None:
            print(u'  ⛔ %s: порчу подставить не удалось' % what)
            bad.append(u'самопроверка')
            continue
        caught = judge(spoiled, probe_text)
        if any(hits(line) for line in caught):
            print(u'  подставлено: %s — названо поимённо' % what)
        else:
            print(u'  ⛔ САМОПРОВЕРКА ПРОВАЛЕНА (%s): названо %s'
                  % (what, u'; '.join(caught) or u'ничего'))
            bad.append(u'самопроверка')

    print()
    if bad:
        print(u'НЕ СОШЛОСЬ: %d' % len(bad))
        return 1
    print(u'СОШЛОСЬ: отсев не может оставить разбор без единого объявленного нуклида')
    return 0


if __name__ == '__main__':
    sys.exit(main())
