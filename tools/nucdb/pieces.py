# -*- coding: utf-8 -*-
"""Соседние куски базы для импортёров.

С 08.08.2026 база разрезана на три файла (`split_db.py`). Читатели в самой
программе разведены так, что ни одному не нужно двух файлов сразу, — а вот
импортёрам иногда нужно: `import_xcom_star` строит `xray_fluorescence`
(вещество) по линиям рентгена из `decay_radiations` (нуклиды), а
`import_photon_evaporation` разбирает схемы (схемы) по символам элементов из
`nuclides` (нуклиды).

Присоединение решает это без правки самих запросов: SQLite ищет таблицу по
всем присоединённым файлам и находит её, пока имя не двоится, — а имена у нас
не двоятся, каждая таблица лежит ровно в одном куске.
"""

import os
import sqlite3


PIECE_FILES = ("matdb.sqlite", "schemedb.sqlite", "nucdb.sqlite")


def attach(connection, db_path, needed):
    """Присоединить перечисленные куски из каталога рядом с `db_path`.

    `needed` — имена файлов. Отсутствие файла — ошибка, а не повод молча
    продолжить: импортёр, не нашедший соседа, соберёт таблицу с дырой, и
    выглядеть это будет как «данных в поставке нет».
    """
    folder = os.path.dirname(os.path.abspath(db_path))
    for i, name in enumerate(needed):
        path = os.path.join(folder, name)
        if not os.path.exists(path):
            raise SystemExit(
                "рядом с %s нет куска %s.\n"
                "База разрезана на три файла (tools/nucdb/split_db.py);\n"
                "импортёру нужен ещё и этот — положите их в один каталог."
                % (os.path.basename(db_path), name))
        connection.execute("attach database ? as piece%d" % i, (path,))
    return connection


def open_with(db_path, needed):
    """Открыть свой кусок на запись и присоединить соседние на чтение."""
    connection = sqlite3.connect(db_path)
    return attach(connection, db_path, needed)
