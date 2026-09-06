# G7 (06.09.2026): три сторожа корпуса переведены на utf-8 тем же блоком — довесок к `T137`

Ветка `pie`, вершина `dc50f43f`. Строка реестра — `T137` (закрыта; `TODO.md`/`DONE.md` не
правлены — дописка отдана распорядителю в отчёте). Артефакты — `handover/g7-utf8-three/`.
Предшественница — G5 (`handover-2026-09-06-g5-cp1251.md`, замер читателей
`handover/g5-cp1251/04-readers.md`).

## Что было

Три скрипта `tools/CORPUS/scripts/` держали вывод в cp1251 и меняли только политику ошибок:
`check_corpus.py` — `reconfigure(errors='replace')` на stdout и stderr с письменным доводом
в шапке (`A71`, 02.09.2026: «в cp1251-консоли utf-8 превращает в кашу ВЕСЬ русский текст, а
с `replace` кириллица остаётся читаемой»); `gate_blind_check.py` и `gaussfit_check.py` —
`sys.stdout.reconfigure(errors='backslashreplace')`, только stdout.

Довод верен лишь для читателя, декодирующего cp1251 (живое окно cmd). Замер G5
(`04-readers.md`): оба канала агента и `check_all.py` декодируют utf-8, и при cp1251 весь
русский приходит как `������`. Решение Amber 06.09.2026 дословно: «Перевести на utf-8 тем же
блоком».

## Что сделано (правильщик `fix_utf8.py`, файлом, байтами)

Блок G5 — дословно тот же:

```python
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass
```

| файл | где стоит блок | почему там | что снято |
|---|---|---|---|
| `check_corpus.py` | тело `if __name__ == '__main__'` | модуль импортирует `gate_blind_check.py` — по правилу G5 в модулях двойного назначения поток импортёра не трогается | 23 строки: довод `A71` про replace-без-смены-кодировки и блок `errors='replace'` у импортов |
| `gaussfit_check.py` | тело `__main__` | импортируют `gate_blind_check.py` и `ecal_accept_check.py` | 8 строк: довод и `backslashreplace` у импортов |
| `gate_blind_check.py` | на месте прежнего (после импортов и путей, до импортов соседей) | импортёров нет | 5 строк: довод и `backslashreplace` |

Доводов снято — **3**; на их месте у каждого короткая ссылка на замер G5 и решение Amber
(`A71` у `check_corpus.py` упомянут — причина, по которой блок вообще нужен, не изменилась).
`git diff --stat`: 34 вставки, 36 удалений. Байты: у всех трёх было LF без BOM — осталось LF
без BOM (правильщик проверяет счётом `\r\n` и BOM до и после). Предупреждение git «LF will be
replaced by CRLF» — от `core.autocrlf=true` на машине, `.gitattributes` про `*.py` молчит, в
индексе LF; к правке не относится.

Проверка правила G5 замером: `import check_corpus, gaussfit_check` из чужого процесса —
поток импортёра `('cp1251', 'surrogateescape')` до и после, не тронут. Импорт всех четырёх
(`ecal_accept_check`, `gate_blind_check`, `check_corpus`, `gaussfit_check`) — ок.

## Замер: то же плечо, что у G5 — `handover/g5-cp1251/run1251.cmd` (настоящий cmd, chcp 1251, `PYTHONIOENCODING`/`PYTHONUTF8` сняты)

Самые дешёвые пути без побочных эффектов (`measure.py`, логи в `before/`, `after/`,
сводки `00-codes.txt`):

* `check_corpus.py --key=ASN16_Th232 --verbose` — один спектр, 16 с; по `--key` разделы
  целостности (`check_parts`, `check_fwhm_node` ~18 с, …) не идут, код 0 после сводки;
  замок чтения берётся и снимается штатно;
* `gate_blind_check.py --help` — argparse печатает подсказку, в ней `пик-3√N` (`√` вне
  cp1251) и русский текст `V16`; код 0;
* `gaussfit_check.py --ref=no_such_ref.py` — отказ «нет прежнего gaussfit» ДО вычислений,
  код 2.

| скрипт | код до | код после | русский для читателя utf-8, до | после |
|---|---|---|---|---|
| `check_corpus.py --key=… --verbose` | 0 | 0 | `������` (128 знаков замены), `ок` → `��` | читаем целиком, `ок`, `РАЗБЕЖАЛСЯ` |
| `gate_blind_check.py --help` | 0 | 0 | `������` (93), `√` → `√` | читаем, `пик-3√N` |
| `gaussfit_check.py --ref=…` | 2 | 2 | `������` (18) | читаем |

`UnicodeEncodeError` и трассировок — 0 и до, и после (эти трое не падали — они и раньше
доходили до приговора, только нечитаемого). Коды приговоров не изменились.

**Bash-канал агента напрямую** (`*.bash.log`): до — те же `������` и `√`, коды 0/0/2;
после — русский читаем, `√` цел, коды 0/0/2. `check_all.py` этих троих не зовёт (он ходит
только по `tools/check_*.py`), поэтому «через check_all» для них меряется только тем, что он
сам остался зелёным (ниже).

## Положительный контроль (`control/`, `make_control.py`)

У каждого вылеченного файла — две копии с подброшенной первой строкой `main()`
`print(u"⛔ КОНТРОЛЬ G7 … ✅ ➜ σ 𝄞")`: `fixed` (блок на месте) и `broken` (блок снят). Копии
клались рядом с оригиналами (импортируют соседей через `HERE`), гонялись через `run1251.cmd`,
после — перенесены в `control/`, из `scripts/` удалены вместе с `__pycache__`; `git status`
чист от `_g7ctrl_*`.

| копия | код | ждали | UnicodeEncodeError | контрольная строка | строка приговора |
|---|---|---|---|---|---|
| `check_corpus_fixed` | 0 | 0 | нет | цела | есть (`плохих:`) |
| `check_corpus_broken` | 1 | 1 | да | нет | нет |
| `gate_blind_check_fixed` | 0 | 0 | нет | цела | есть (`--no-inject`) |
| `gate_blind_check_broken` | 1 | 1 | да | нет | нет |
| `gaussfit_check_fixed` | 2 | 2 | нет | цела | есть (`нет прежнего gaussfit`) |
| `gaussfit_check_broken` | 1 | 1 | да | нет | нет |

6 из 6 сошлись с ожиданием.

## `python tools/check_all.py` — код 0 (`05-check_all.log`), «ВСЕ ЗЕЛЕНЫ: 12 из 12».

## Находки по §9 — строк не заведено

1. `core.autocrlf=true` на машине даёт предупреждение «LF will be replaced by CRLF» на
   каждом `git diff` по этим трём (и любым LF-файлам). Факт, не дело: в индексе LF, в дереве
   LF, правка байтов не меняла. В журнал.
2. Питон 3.15 включит UTF-8 умолчанием (PEP 686) — блок станет холостым, не вредным. Уже
   записано у G5, повторять нечего.

## Файлы

Изменены (3): `tools/CORPUS/scripts/check_corpus.py`, `tools/CORPUS/scripts/gate_blind_check.py`,
`tools/CORPUS/scripts/gaussfit_check.py`.

Созданы: этот журнал; `handover/g7-utf8-three/` — `fix_utf8.py`, `measure.py`,
`before/` и `after/` (по три лога cmd-плеча, три лога Bash-канала, `00-codes.txt`),
`control/` (`make_control.py`, шесть копий, шесть логов, `00-codes.txt`), `05-check_all.log`.

Не трогались: `TODO.md`, `DONE.md`, `BecquerelMonitor/**`, `N42/**`, `corpus_calib.py` и
`calib_*_f56.py` (в дереве видны как чужие изменения F56), `handover/g5-cp1251/**` (только
читался и звался `run1251.cmd`). Коммитов, `git add`, `checkout`, `stash` — не было.
