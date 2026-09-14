# Замер читателей: что видит канал при трёх политиках вывода (06.09.2026, G5)

Скрипт `enc_test.py` печатает `enc=<кодировка> | привет ⛔ σ χ² → конец` и выходит кодом 3.
Запуск — `run1251.cmd` (chcp 1251, `PYTHONIOENCODING=` и `PYTHONUTF8=` сняты).

## Читатель — Bash-обёртка агента (stdout — труба)

```
== none      (без reconfigure)
Traceback … UnicodeEncodeError: 'charmap' codec can't encode character '⛔' …
код 1
== utf8      (reconfigure(encoding='utf-8', errors='replace'))
enc=utf-8 | привет ⛔ σ χ² → конец
код 3
== replace   (reconfigure(errors='replace'), кодировка как есть)
enc=cp1251 | ������ ? ? ?? ? �����
код 3
```

## Читатель — канал pwsh агента (`[Console]::OutputEncoding` = utf-8, chcp 65001)

```
OutputEncoding=utf-8 chcp=Active code page: 65001 PYTHONIOENCODING=[] PYTHONUTF8=[]
== none     → та же трассировка UnicodeEncodeError, код 1
== utf8     → enc=utf-8 | привет ⛔ σ χ² → конец, код 3
== replace  → enc=cp1251 | ������ ? ? ?? ? �����, код 3
```

⚠ В канале pwsh кодовая страница консоли 65001, а питон всё равно взял cp1251:
в трубе он берёт **ANSI-страницу системы** (`locale.getencoding()`), а не страницу
консоли. `chcp` болезнь не лечит и не вызывает; лечит только `PYTHONUTF8`/`PYTHONIOENCODING`
или `reconfigure` из самого скрипта. (Python 3.15 включит режим UTF-8 умолчанием — PEP 686 —
и болезнь кончится сама; на машине 3.14.6.)

## Байты в файле (`> файл` из cmd)

```
replace: 656e 633d 6370 3132 3531 207c 20ef f0e8 e2e5 f220 3f20 3f20 3f3f 203f …   (cp1251, знаки → '?')
utf8:    656e 633d 7574 662d 3820 7c20 d0bf d180 d0b8 d0b2 d0b5 d182 20e2 9b94 …   (utf-8, всё цело)
```

## Вывод

Довод `check_corpus.py` («с `replace` кириллица остаётся читаемой, а пять знаков становятся
вопросом») верен только для читателя, который декодирует cp1251 — реальное окно cmd/pwsh
на этой машине. Оба канала агента (Bash-обёртка, pwsh-канал) декодируют utf-8, и для них
`replace` без смены кодировки даёт `������` на ВСЁМ русском тексте. Читатель `check_all.py`
тоже декодирует детей как utf-8. Поэтому ставится `encoding='utf-8'`.
