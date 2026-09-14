# -*- coding: utf-8 -*-
u"""Сторож разряда «заголовок строки `DONE.md` противоречит её собственному телу».

Задача `T102`. Человек ищет по архиву `grep`-ом и читает ЗАГОЛОВОК ячейки —
первую жирную фразу. Если заголовок утверждает НЕЗНАНИЕ («причина неизвестна»,
«не воспроизводится», «не установлено»), а тело той же ячейки это незнание
снимает (блок ✅ называет причину, либо строка отозвана словами «СНЯТА»,
«НАХОДКИ НЕТ», либо тело прямо говорит «воспроизводятся»), поиск выдаёт
противоположное действительности, и цена этому — чужой заход, начатый с
неверной посылки (так и случилось 27.08.2026 со строкой `S82`).

Что сторож ловит и чего НЕ ловит — сказать честно. Ловится узкий разряд:
слова незнания в заголовке против ответа в теле. Второй случай `T102`
(`S86`: заголовок называет пробу, у которой печать стоит за ключом, а не
всегда) — фактическая ошибка против КОДА, а не против собственного тела; её
машинно не поймать без разбора кода, и она остаётся текстовой заменой для
Amber. Обе замены собраны в журнале `handover/handover-2026-09-05-f6-t100-t102.md`.

Разбор СПЛОШНОЙ и CR-стойкий: файл читается нетронутым (`newline=""`) и
режется ТОЛЬКО по `\\n`, поэтому одиночный CR внутри описания остаётся в своей
строке, а номера строк совпадают с `grep -n`. Из черт режутся только первые
три (номер и состояние), дальше текст склеивается обратно — внутри описаний
есть свои `|`, и разбор по разделителю в лоб портит их молча.

⛔ `DONE.md` правит ТОЛЬКО Amber. Сторож ничего не пишет — он только называет
строки с номерами; замены текстом собирает тот, кто его позвал.

Запуск:
    python tools/check_done_headers.py                 # проверить DONE.md
    python tools/check_done_headers.py --file X.md     # проверить копию
    python tools/check_done_headers.py --scan          # все заголовки со
                                                       # словами незнания
    python tools/check_done_headers.py --dump-heads    # все заголовки подряд
    python tools/check_done_headers.py --selftest      # положительный контроль

Код возврата: 0 — противоречий нет (для `--selftest`: контроль сошёлся);
1 — есть (контроль провален); 2 — файл не разобрался.

Положительный контроль (`--selftest`), потому что сторож без доказанного
ОТКАЗА неотличим от ненаписанного (`T69`): во ВРЕМЕННОЙ копии `DONE.md` в
чистую строку подсаживается заголовок «причина неизвестна» с блоком ✅ в
теле, в другую чистую строку — одиночный CR и лишняя `|` в описании; контроль
требует ровно «честные находки + одна», названную своей строкой, строк
столько же, сколько было, и НИ ОДНОЙ находки в строке с CR.
"""

import argparse
import hashlib
import io
import os
import re
import shutil
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_FILE = os.path.join(REPO, "DONE.md")

# ---------------------------------------------------------------- разбор строк

SEPARATOR_RE = re.compile(r"^\|[\s:|-]*\|\s*$")
TABLE_HEAD_RE = re.compile(r"^\|\s*#\s*\|")


def read_lines(path):
    u"""Строки, резанные ТОЛЬКО по `\\n`; одиночный CR остаётся внутри строки."""
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        return f.read().split("\n")


def read_rows(path):
    u"""Список (номер строки, id, состояние, ячейка) — сплошным разбором.

    ⛔ Режем ТОЛЬКО первые три черты. Всё, что дальше, склеиваем обратно:
    в описаниях есть внутренние `|`, и наивный `split('|')` рвёт их молча.
    """
    rows = []
    for num, line in enumerate(read_lines(path), 1):
        line = line.rstrip("\r")
        if not line.startswith("|"):
            continue
        if SEPARATOR_RE.match(line) or TABLE_HEAD_RE.match(line):
            continue
        parts = line.split("|")
        if len(parts) < 4:
            continue
        ident = parts[1].strip()
        state = parts[2].strip()
        cell = "|".join(parts[3:]).rstrip()
        rows.append((num, ident, state, cell))
    return rows


def clean_id(ident):
    return ident.replace("~~", "").replace("**", "").strip()


LEAD_MARKS_RE = re.compile(u"^(?:[\\s✅⛔⚠️❗\U0001f528]|~~БЫЛО:?~~)+")


def split_head_body(cell):
    u"""Заголовок = первая жирная фраза ячейки; тело = всё остальное.

    Если ячейка начинается не с жирного, заголовком считается первое
    предложение (то, что видно в выдаче `grep`).
    """
    s = LEAD_MARKS_RE.sub("", cell).strip()
    if s.startswith("**"):
        end = s.find("**", 2)
        if end > 0:
            return s[2:end].strip(), s[end + 2:]
    m = re.search(u"[.!?»]\\s", s)
    if m:
        return s[: m.end()].strip(), s[m.end():]
    return s[:200].strip(), s[200:]


# --------------------------------------------------------------- сами признаки

# Слова НЕЗНАНИЯ в заголовке: заголовок утверждает, что ответа нет.
# Именно они и врут, когда тело ответ называет.
UNKNOWN_PATTERNS = [
    (u"причин[аыуе]\\s+(?:\\w+\\s+){0,3}?(?:неизвестн|не\\s+(?:установлен|найден|назван|ясн|понят|выясн))",
     u"«причина неизвестна»"),
    (u"\\bне\\s+воспроизвод", u"«не воспроизводится»"),
    (u"\\bневоспроизвод", u"«невоспроизводимо»"),
    (u"\\bнеизвестно,?\\s+(?:почему|отчего|что|где|как|какой|какая|чем|кто)",
     u"«неизвестно, почему/что/где»"),
    (u"\\bне\\s+установлен[оаы]?\\b", u"«не установлено»"),
    (u"\\bне\\s+удалось\\s+(?:найти|установить|воспроизвести|понять|объяснить)",
     u"«не удалось найти/понять»"),
    (u"\\b(?:непонятно|не\\s+понятно),?\\s+(?:почему|что|откуда|зачем|где|как)",
     u"«непонятно, почему»"),
    (u"\\bпочему\\s+—\\s+неизвестн", u"«почему — неизвестно»"),
    (u"\\bпричина\\s+не\\s+разобран", u"«причина не разобрана»"),
    (u"\\bни\\s+один\\s+не\\s+проверен", u"«ни один не проверен»"),
    (u"\\bне\\s+проверен[оаы]?\\b", u"«не проверено»"),
    (u"\\bне\\s+измерен[оаы]?\\b", u"«не измерено»"),
    (u"\\bне\\s+объяснен[оаы]?\\b", u"«не объяснено»"),
    (u"\\bзагадк", u"«загадка»"),
    (u"\\bне\\s+знаем\\b", u"«не знаем»"),
    (u"\\bне\\s+ясно\\b|\\bнеясно\\b", u"«не ясно»"),
]
UNKNOWN_RE = [(re.compile(p, re.I | re.U), name) for p, name in UNKNOWN_PATTERNS]

# Тело называет ответ / закрывает вопрос / отзывает строку. Строчные
# «сошлось», «виноват» сюда НАРОЧНО не входят: они встречаются и в ПОСТАНОВКЕ
# («клеймо сошлось, а спектры её отвергают»), и на `T112` ловили правильную
# строку по НЕПРАВИЛЬНОЙ причине. Здесь только то, чем реестр закрывает.
ANSWERED_PATTERNS = [
    (u"✅", u"блок ✅ в той же ячейке"),
    (u"ПРИЧИНА\\s+(?:НАЗВАНА|НАЙДЕНА|В\\s+ТОМ|ОКАЗАЛАСЬ)", u"«ПРИЧИНА НАЙДЕНА»"),
    (u"\\bСДЕЛАНО\\b", u"«СДЕЛАНО»"),
    (u"\\bЗАКРЫТ[АО]\\b", u"«ЗАКРЫТА»"),
    (u"\\bСНЯТ[АО]\\b", u"«СНЯТА»"),
    (u"\\bНАХОДКИ\\s+НЕТ\\b", u"«НАХОДКИ НЕТ»"),
    (u"\\bРАЗОБРАН[АО]?\\b", u"«РАЗОБРАНО»"),
    (u"(?<![Нн][Ее] )\\bвоспроизвод(?:ится|ятся)\\b", u"«воспроизводится» без «не»"),
]
ANSWERED_RE = [(re.compile(p, re.U), name) for p, name in ANSWERED_PATTERNS]

# Тело прямо ОТЗЫВАЕТ слова заголовка — цитируется первым.
RETRACTION_RE = re.compile(
    u"(?:⛔+\\s*)?(?:\\*\\*)?(?:Прежн\\w+|Прежде|Раньше|Сперва|Сначала|Первоначальн\\w+|"
    u"Старое|БЫЛО:|СНЯТ[АО]\\b|НАХОДКИ\\s+НЕТ)"
    u"[^.!?]{0,220}?(?:был[аио]?\\s+не\\b|оказал\\w+\\s+не\\b|не\\s+подтверди|снят|неверн|"
    u"ошибочн|ОШИБКА|НЕТ|а\\s+(?:ДРУГОЙ|другой|ДРУГАЯ|другая|НЕ\\b))",
    re.U,
)


def is_history(cell):
    u"""Заголовок сознательно оставлен как ИСТОРИЯ — вычеркнут или помечен.

    Вычеркнутый заголовок (`~~…~~ — ПРИЧИНА НАЙДЕНА …`) в выдаче `grep` виден
    вместе с маркерами, и читатель понимает, что это прошлое; ложью это не
    считается. Проверяется по ОБРЕЗАННОЙ ячейке: первая редакция смотрела
    `^~~` на сырой ячейке с ведущим пробелом и историю не узнавала ни разу.
    """
    s = cell.strip()
    return s.startswith("~~") or re.search(
        u"~~БЫЛО:?~~|Прежний\\s+текст:|\\bБЫЛО:\\s*⛔", s[:60]) is not None


def analyse(rows):
    findings = []
    stats = {"rows": len(rows), "with_head": 0, "unknown_head": 0}
    for num, ident, state, cell in rows:
        head, body = split_head_body(cell)
        if not head:
            continue
        stats["with_head"] += 1

        hits = [name for rx, name in UNKNOWN_RE if rx.search(head)]
        if not hits:
            continue
        stats["unknown_head"] += 1

        answers = [name for rx, name in ANSWERED_RE if rx.search(body)]
        retracted = bool(RETRACTION_RE.search(body))

        if answers and not is_history(cell):
            findings.append({
                "line": num,
                "id": clean_id(ident),
                "state": state,
                "head": head,
                "head_sha": hashlib.sha256(head.encode("utf-8")).hexdigest()[:12],
                "hits": hits,
                "answers": answers,
                "retracted": retracted,
                "body": body,
            })
    return findings, stats


def analyse_file(path):
    u"""(находки, счёт) для файла; так сторожа зовёт `check_registry.py`."""
    return analyse(read_rows(path))


def quote_of_contradiction(body, limit=460):
    u"""Кусок тела, который заголовку противоречит: сперва прямой отзыв,
    иначе — первое предложение блока ✅."""
    m = RETRACTION_RE.search(body)
    if m:
        start = max(0, m.start())
        end = body.find(".", m.end())
        end = end + 1 if end > 0 else min(len(body), start + limit)
        return body[start:end].strip()
    i = body.find(u"✅")
    if i >= 0:
        return body[i: i + limit].strip()
    return body[:limit].strip()


def print_findings(out, findings, quiet=False):
    for f in findings:
        out.write(u"\n--- %s (строка %d), состояние: %s\n"
                  % (f["id"], f["line"], f["state"]))
        out.write(u"  признак: %s\n" % u", ".join(f["hits"]))
        out.write(u"  тело отвечает: %s%s\n"
                  % (u", ".join(f["answers"]),
                     u"; тело ПРЯМО отзывает заголовок" if f["retracted"] else u""))
        out.write(u"  ЗАГОЛОВОК: %s\n" % f["head"])
        if not quiet:
            out.write(u"  ТЕЛО:      %s\n" % quote_of_contradiction(f["body"]))


# ------------------------------------------------------- положительный контроль

FAKE_HEAD = (u"**Стенд подделки не воспроизводит экран, и причина неизвестна.** "
             u"Подсажено положительным контролем. ✅ **СДЕЛАНО: причина названа "
             u"здесь же.** ")


def selftest(path, out):
    u"""Положительный контроль. 0 — сошёлся, 1 — провален."""
    out.write(u"# Положительный контроль сторожа заголовков (T102)\n\n")
    if not os.path.exists(path):
        out.write(u"  ⛔ %s не найден — контроль не проведён\n" % path)
        return 1
    tmp = tempfile.mkdtemp(prefix=u"check_done_headers_selftest_")
    try:
        dst = os.path.join(tmp, u"DONE.md")
        shutil.copyfile(path, dst)
        clean_rows = read_rows(dst)
        clean, _ = analyse(clean_rows)
        clean_lines = {f["line"] for f in clean}
        out.write(u"  чистая копия: строк %d, противоречий %d — %s\n"
                  % (len(clean_rows), len(clean),
                     u", ".join(u"%s:%d" % (f["id"], f["line"]) for f in clean) or u"нет"))

        lines = read_lines(dst)
        fake_line = cr_line = None
        failures = []
        for num, ident, state, cell in clean_rows:
            if num in clean_lines:
                continue
            head, _ = split_head_body(cell)
            if any(rx.search(head) for rx, _ in UNKNOWN_RE) or is_history(cell):
                continue
            parts = lines[num - 1].split("|")
            if fake_line is None:
                parts[3] = u" " + FAKE_HEAD + parts[3].lstrip()
                fake_line = num
            else:
                parts[3] = parts[3] + u" хвост после CR\r а тут | черта "
                cr_line = num
                lines[num - 1] = u"|".join(parts)
                break
            lines[num - 1] = u"|".join(parts)
        with io.open(dst, "w", encoding="utf-8", newline="") as f:
            f.write(u"\n".join(lines))

        dirty_rows = read_rows(dst)
        dirty, _ = analyse(dirty_rows)
        dirty_lines = {f["line"] for f in dirty}
        out.write(u"  подсажено: заголовок незнания + ✅ в строку %s; CR и `|` в "
                  u"описание строки %s\n" % (fake_line, cr_line))
        out.write(u"  подделанная копия: строк %d (ожидалось %d), противоречий %d "
                  u"(ожидалось %d)\n"
                  % (len(dirty_rows), len(clean_rows), len(dirty), len(clean) + 1))
        if fake_line is None or cr_line is None:
            failures.append(u"не хватило чистых строк для подсадки")
        if len(dirty_rows) != len(clean_rows):
            failures.append(u"CR разрезал строку")
        if fake_line not in dirty_lines:
            failures.append(u"подсаженная строка %s не названа" % fake_line)
        else:
            out.write(u"  подсаженная строка %d названа: да\n" % fake_line)
        if cr_line in dirty_lines:
            failures.append(u"строка с CR названа находкой без порчи заголовка")
        if dirty_lines - clean_lines - {fake_line}:
            failures.append(u"лишние находки в строках %s"
                            % sorted(dirty_lines - clean_lines - {fake_line}))
        if len(dirty) != len(clean) + 1:
            failures.append(u"противоречий %d вместо %d" % (len(dirty), len(clean) + 1))
        cr_row = [r for r in dirty_rows if r[0] == cr_line]
        if not cr_row or u"| черта" not in cr_row[0][3]:
            failures.append(u"хвост описания за CR потерян")
        else:
            out.write(u"  хвост описания за CR в разборе цел: да\n")
        ok = not failures
        out.write(u"\n  %s\n" % (u"КОНТРОЛЬ СОШЁЛСЯ: честные находки те же, подсаженная "
                                u"названа, CR и `|` пережиты"
                                if ok else
                                u"⛔ КОНТРОЛЬ ПРОВАЛЕН — сторож меряет не то: "
                                + u"; ".join(failures)))
        return 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


# ------------------------------------------------------------------- выкладка


def main(argv=None):
    ap = argparse.ArgumentParser(description=u"Сторож заголовков DONE.md (T102)")
    ap.add_argument("--file", default=DEFAULT_FILE, help=u"какой файл проверять")
    ap.add_argument("--scan", action="store_true",
                    help=u"показать ВСЕ заголовки со словами незнания, включая безобидные")
    ap.add_argument("--dump-heads", action="store_true", help=u"выложить все заголовки подряд")
    ap.add_argument("--quiet", action="store_true", help=u"только счёт")
    ap.add_argument("--selftest", action="store_true",
                    help=u"положительный контроль на ВРЕМЕННОЙ копии файла")
    args = ap.parse_args(argv)
    # ⛔ Консоль Windows — cp1251, и первая редакция падала на самом первом
    # ✅ в выдаче (`UnicodeEncodeError`), не дойдя до отказа. Пишем utf-8 сами.
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    if not os.path.exists(args.file):
        out.write(u"НЕТ ФАЙЛА: %s\n" % args.file)
        out.flush()
        return 2

    if args.selftest:
        rc = selftest(args.file, out)
        out.flush()
        return rc

    rows = read_rows(args.file)
    if not rows:
        out.write(u"ОТКАЗ: в %s не разобрано ни одной строки таблицы\n" % args.file)
        out.flush()
        return 2

    if args.dump_heads:
        for num, ident, state, cell in rows:
            head, _ = split_head_body(cell)
            out.write(u"%4d  %-8s  %s\n" % (num, clean_id(ident), head[:170]))
        out.flush()
        return 0

    findings, stats = analyse(rows)

    if args.scan:
        out.write(u"=== заголовки со словами незнания (все, и здоровые тоже) ===\n")
        for num, ident, state, cell in rows:
            head, body = split_head_body(cell)
            hits = [name for rx, name in UNKNOWN_RE if rx.search(head)]
            if not hits:
                continue
            answers = [name for rx, name in ANSWERED_RE if rx.search(body)]
            history = is_history(cell)
            mark = u"ПРОТИВОРЕЧИЕ" if (answers and not history) else u"чисто"
            out.write(u"%4d %-8s [%s] %s\n" % (num, clean_id(ident), mark, u", ".join(hits)))
            out.write(u"       заголовок: %s\n" % head[:200])
            out.write(u"       тело: %s | история=%s\n"
                      % (u", ".join(answers) or u"—", history))
        out.write(u"\n")

    out.write(u"=== счёт ===\n")
    out.write(u"строк таблицы в %s: %d\n" % (os.path.basename(args.file), stats["rows"]))
    out.write(u"разобрано (заголовок выделен): %d\n" % stats["with_head"])
    out.write(u"заголовков со словами незнания: %d\n" % stats["unknown_head"])
    out.write(u"ПРОТИВОРЕЧИЙ (незнание в заголовке + ответ в теле): %d\n" % len(findings))

    print_findings(out, findings, quiet=args.quiet)

    if findings:
        out.write(u"\nОТКАЗ: заголовок противоречит телу в %d строках — см. выше. "
                  u"⛔ DONE.md правит только Amber; замены текстом — в журнале.\n"
                  % len(findings))
        out.flush()
        return 1
    out.write(u"порядок: противоречий не найдено\n")
    out.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())
