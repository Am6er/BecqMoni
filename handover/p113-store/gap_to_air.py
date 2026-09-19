# -*- coding: utf-8 -*-
r"""П113 (19.09.2026), `AMBER47`: в КОПИИ конфига Amber заменить блок <Gap> «Water, liquid»
на «Air, dry» — байтами, без разбора XML (перевод строки CRLF, без BOM — как пишет приложение).

    python gap_to_air.py <config\device\*.xml> [...]

Печатает по файлу: сколько блоков воды найдено и заменено. Код 1, если файл не тронут
(нечего менять) — читатель обязан видеть, где замена не случилась.
Блок воздуха — тот же, что стоит у «Th медальон» / «Цилиндр» AS 80x80
(`GeometryModel.DefaultGapMaterial()`: N 0.63648302312054683, O 0.36351697687945328).
"""
import sys

WATER = (b"        <Gap>\r\n"
         b"          <Name>Water, liquid</Name>\r\n"
         b"          <Density>1</Density>\r\n"
         b"          <Fractions>\r\n"
         b"            <Element Z=\"1\" Fraction=\"0.11189440028420446\" />\r\n"
         b"            <Element Z=\"8\" Fraction=\"0.8881055997157955\" />\r\n"
         b"          </Fractions>\r\n"
         b"        </Gap>\r\n")
AIR = (b"        <Gap>\r\n"
       b"          <Name>Air, dry</Name>\r\n"
       b"          <Density>0.001205</Density>\r\n"
       b"          <Fractions>\r\n"
       b"            <Element Z=\"7\" Fraction=\"0.63648302312054683\" />\r\n"
       b"            <Element Z=\"8\" Fraction=\"0.36351697687945328\" />\r\n"
       b"          </Fractions>\r\n"
       b"        </Gap>\r\n")


def main(paths):
    rc = 0
    for p in paths:
        with open(p, "rb") as f:
            data = f.read()
        n = data.count(WATER)
        if n == 0:
            print("%s: блоков воды в зазоре нет — не тронут" % p)
            rc = 1
            continue
        out = data.replace(WATER, AIR)
        with open(p, "wb") as f:
            f.write(out)
        # Считается ИМЕННО зазор: вода как ВЕЩЕСТВО ПРОБЫ (<Source>) законна и остаётся.
        left = out.count(b"<Gap>\r\n          <Name>Water, liquid</Name>")
        print("%s: заменено блоков Water -> Air: %d; байт %d -> %d; осталось <Gap> с водой: %d"
              % (p, n, len(data), len(out), left))
        if left:
            rc = 1
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
