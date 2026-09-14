# -*- coding: utf-8 -*-
"""П72 (T258) — ПОДСАДКА для положительного контроля: вернуть в копию генератора (worktree wt_b)
прежний маринелли «из объёма» (`Marinelli(m, 1.5, 70.0, 100.0, vol)`: колодец по прибору + 1.5 мм,
глубина 70, слой 100, внешний Ø из объёма) — и убедиться, что приёмка клеймом (`stamp_check.py`)
ОТКАЗЫВАЕТ на 17 сценах `G1S_mar1l_*` и на сцене угля. Ничего в основном дереве не трогает.

    python oldvessel_patch.py apply  <путь к CorpusGeomProbe.cs в wt_b>
    python oldvessel_patch.py revert <путь к CorpusGeomProbe.cs в wt_b>   (копия из основного дерева)
"""
import io, os, shutil, sys

OLD_METHOD = '''
    /// <summary>ПОДСАДКА П72: прежний маринелли «из объёма» — только для положительного контроля.</summary>
    static double DetectorOuterRadius(GeometryModel g)
    {
        double rCrystal = g.Shape == CrystalShape.Box
            ? 0.5 * Math.Sqrt(g.CrystalBoxX * g.CrystalBoxX + g.CrystalBoxY * g.CrystalBoxY)
            : 0.5 * g.CrystalDiameter;
        return rCrystal + g.SideReflectorThickness + g.SideCladdingThickness + g.MountingThickness;
    }

    static void MarinelliFromVolume(GeometryModel g, double clearanceMm, double wellDepthMm,
                                    double sourceHeightMm, double volumeMl)
    {
        g.SourceType = GeometrySourceType.Marinelli;
        g.MarinelliHoleDiameter = 2.0 * (DetectorOuterRadius(g) + clearanceMm);
        g.MarinelliHoleHeight = wellDepthMm;
        g.MarinelliSourceHeight = sourceHeightMm;
        double rHole = 0.5 * g.MarinelliHoleDiameter + g.MarinelliHoleSideThickness;
        double cap = Math.Max(0.0, sourceHeightMm - wellDepthMm);
        double rSrcOut2 = (volumeMl * 1000.0 / Math.PI - rHole * rHole * cap) / sourceHeightMm + rHole * rHole;
        double rSrcOut = Math.Sqrt(Math.Max(rSrcOut2, rHole * rHole + 1e-9));
        g.MarinelliBeakerDiameter = 2.0 * (rSrcOut + g.MarinelliSideThickness);
        g.MarinelliBeakerHeight = sourceHeightMm + g.MarinelliEndWallThickness + g.MarinelliHoleEndWallThickness;
    }

    static void MarinelliOmasn(GeometryModel g, double volumeMl)
    {
        MarinelliFromVolume(g, 1.5, 70.0, 100.0, volumeMl);
    }

    static void MarinelliOmasnReal(GeometryModel g, double volumeMl)
'''

def main():
    mode, path = sys.argv[1], sys.argv[2]
    here = os.path.dirname(os.path.abspath(__file__))
    main_tree = os.path.abspath(os.path.join(here, '..', '..', 'tools', 'effmaker', 'probes', 'CorpusGeomProbe.cs'))
    if mode == 'revert':
        shutil.copyfile(main_tree, path)
        print('возвращена копия из основного дерева: %s' % path)
        return 0
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    anchor = '\n    static void MarinelliOmasn(GeometryModel g, double volumeMl)\n'
    assert text.count(anchor) == 1, 'якорь не найден или не один'
    text = text.replace(anchor, OLD_METHOD)
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
    print('подсажен прежний сосуд «из объёма»: %s' % path)
    return 0

if __name__ == '__main__':
    sys.exit(main())
