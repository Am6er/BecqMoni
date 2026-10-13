# П111 (M13): геометрии-пластины для LayerReturnProbe --brem из RC103_point0_p55.in (П92):
# толщины передних/боковых слоёв правятся строками, всё остальное — как у RC103 П55. Файл cp1251 — правим байтами.
import os, re

src = r"D:\BqMoni_Claude\p111\geo\RC103_point0_p55.in"
dst = r"D:\BqMoni_Claude\p111\geo"
base = open(src, "rb").read()

def make(name, refl_front, refl_side, clad_front, clad_side, mounting):
    b = base
    def put(key, value):
        nonlocal b
        pat = re.compile(rb"(?m)^" + key.encode() + rb" = [0-9.eE+-]+ cm")
        assert pat.search(b), key
        b = pat.sub((key + " = " + repr(value) + " cm").encode(), b, count=1)
    put("DS_CrystalFrontReflectorThickness", refl_front)
    put("DS_CrystalSideReflectorThickness", refl_side)
    put("DS_CrystalFrontCladdingThickness", clad_front)
    put("DS_CrystalSideCladdingThickness", clad_side)
    put("DS_DetectorMountingThickness", mounting)
    p = os.path.join(dst, name + ".in")
    open(p, "wb").write(b)
    print(name, refl_front, refl_side, clad_front, clad_side, mounting)

make("slab_ptfe20", 2.0, 2.0, 0.0, 0.0, 0.0)     # толстая PTFE
make("slab_al20", 0.0, 0.0, 2.0, 2.0, 0.0)       # толстая Al
make("slab_ptfe1", 0.1, 0.1, 0.0, 0.0, 0.0)      # PTFE 1 мм + пустота
make("slab_ptfe03", 0.03, 0.03, 0.0, 0.0, 0.0)   # PTFE 0.3 мм + пустота
make("slab_al1", 0.0, 0.0, 0.1, 0.1, 0.0)        # Al 1 мм + пустота
