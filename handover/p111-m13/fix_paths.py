# П111: пути p92 -> p111 в bat-файлах арбитра (байтами, кодировку файлов не трогаем).
import os
d = r"D:\BqMoni_Claude\p111\g4"
for name in ("g4_build.bat", "g4run.cmd", "run_g4cf.bat"):
    p = os.path.join(d, name)
    b = open(p, "rb").read()
    b = b.replace(rb"D:\BqMoni_Claude\p92\g4", rb"D:\BqMoni_Claude\p111\g4")
    open(p, "wb").write(b)
    print(name, b.count(b"p111"), b.count(b"p92"))
