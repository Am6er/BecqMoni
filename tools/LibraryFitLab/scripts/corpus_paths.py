# -*- coding: utf-8 -*-
"""Разрешение путей библиотеки спектров.

Часть имён на диске лежит в NFD (буква «й» как «и» + U+0306) — так их записал
клиент облачного диска. Литерал в исходнике питона нормализован в NFC, и
os.path.isfile на него отвечает False, хотя файл есть. Сравниваем покомпонентно
по NFC-нормализации.
"""
import os
import unicodedata


def nfc(text):
    return unicodedata.normalize('NFC', text)


def resolve(path):
    """Реальный путь на диске для NFC-написания."""
    if os.path.exists(path):
        return path
    drive, tail = os.path.splitdrive(os.path.abspath(path))
    parts = [x for x in tail.split(os.sep) if x]
    cur = drive + os.sep
    for part in parts:
        candidate = os.path.join(cur, part)
        if os.path.exists(candidate):
            cur = candidate
            continue
        target = nfc(part)
        for name in os.listdir(cur):
            if nfc(name) == target:
                cur = os.path.join(cur, name)
                break
        else:
            raise IOError('нет файла: %s (не найдено «%s» в %s)' % (path, part, cur))
    return cur
