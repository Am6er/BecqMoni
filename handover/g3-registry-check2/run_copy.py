# -*- coding: utf-8 -*-
# Проверка 2 на КОПИИ реестров в scratch (снимок 01:42), корень дерева — репозиторий.
import io, os, sys, collections
ROOT = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
S = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import check_registry as cr
out = io.open(1, "w", encoding="utf-8", closefd=False)
files = collections.OrderedDict([
    ("TODO.md", cr.read_rows(os.path.join(S, "TODO.md.copy"))),
    ("DONE.md", cr.read_rows(os.path.join(S, "DONE.md.copy")))])
bad = cr.check_file_refs(ROOT, out, files)
out.write(u"\nНАХОДОК В СЧЁТ (проверка 2, копия): %d\n" % bad)
out.flush()
