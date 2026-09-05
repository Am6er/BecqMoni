# -*- coding: utf-8 -*-
# Контроль контроля: проверка 2 подменена заглушкой (ничего не находит) —
# положительный контроль ОБЯЗАН провалиться. Второе плечо: исключения сняты —
# контроль обязан провалиться на «всплыло имя из OUTSIDE_ON_PURPOSE».
import io, os, sys
ROOT = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
sys.path.insert(0, os.path.join(ROOT, "tools"))
import check_registry as cr
out = io.open(1, "w", encoding="utf-8", closefd=False)

real = cr.check_file_refs
def stub(root, o, files, index=None, tracked=None):
    o.write(u"# Ссылки на файлы, которых нет в дереве\n\n  нет\n\n# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ (лежат только на диске)\n\n  нет\n")
    return 0
cr.check_file_refs = stub
f1 = cr.selftest_file_refs(ROOT, out)
cr.check_file_refs = real
out.write(u"плечо 1 (заглушка): провалов %d — %s\n\n" % (len(f1), u"; ".join(f1)))

saved = dict(cr.OUTSIDE_ON_PURPOSE)
cr.OUTSIDE_ON_PURPOSE.clear()
cr.OUTSIDE_ON_PURPOSE[".appwd.json"] = u"единственная запись, чтобы контроль выбрал её и подсадил"
# запись есть, но сверка по ней отключена: подменяем словарь на пустой ВНУТРИ проверки
class NoExcl(dict):
    def __contains__(self, k): return False
cr.OUTSIDE_ON_PURPOSE = NoExcl(cr.OUTSIDE_ON_PURPOSE)
f2 = cr.selftest_file_refs(ROOT, out)
cr.OUTSIDE_ON_PURPOSE = saved
out.write(u"плечо 2 (исключения не действуют): провалов %d — %s\n" % (len(f2), u"; ".join(f2)))
out.flush()
sys.exit(0 if (f1 and f2) else 1)
