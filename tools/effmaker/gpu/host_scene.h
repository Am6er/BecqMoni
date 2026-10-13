// host_scene.h — читатели разделов СЦЕНЫ упаковки (пишет `tools/effmaker/probes/GpuPackScene.cs`,
// `WriteScene`): materials, fluorescers, scatterers, scatterElements, regions, scene — в этом
// порядке. Полоса П221 (`AMBER160`), часть Б. Формат и `BlobReader` — host.h.
//
// Каждый читатель сам читает число записей раздела (`Int`) — имя раздела (`Tag`) уже
// прочёл разборщик, позвавший `Read_<имя>`. После каждой записи — `r.End()`.
//
// ⚠ Разделы сцены идут ПОСЛЕ разделов полосы А (`GpuPack.Pack`: WriteTables, затем
// WriteScene): индексы в таблицы полосы А (`elements`, `fluor`, `photoShell`, `atoms`)
// здесь берутся из её поиска по Z (`elementsByZ`, `fluorByZ`, `photoShellByZ`,
// `atomsByZ`), а C# пишет только Z. Так ни одна сторона не повторяет реестр другой; что
// C# и поиск по Z указывают на ОДИН объект, писатель проверил до записи (`GpuPackScene.cs`).
#pragma once
#include "host.h"

namespace rm_scene_detail
{
    inline int ByZ(BlobReader& r, const std::vector<int>& byZ, int z, const char* what)
    {
        if (z < 0 || z >= (int)byZ.size())
        {
            r.Fail(what);
        }

        return byZ[(size_t)z];
    }
}

// Вещества сцены — `MaterialG`.
inline void Read_materials(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    for (int k = 0; k < count; k++)
    {
        MaterialG m;
        std::memset(&m, 0, sizeof(m));
        m.Density = r.Real();
        m.Z = r.Ints(m.ZLen);
        m.Fraction = r.Reals(m.FractionLen);
        if (m.FractionLen != m.ZLen)
        {
            r.Fail("у вещества число Z и долей разошлось");
        }

        // Элемент поставки на каждый Z — поиск по Z полосы А (= MaterialDatabase.TryGet).
        std::vector<int> zs(h.I.begin() + m.Z, h.I.begin() + m.Z + m.ZLen);
        m.ElementLen = m.ZLen;
        m.Element = m.ZLen == 0 ? 0 : (int)h.I.size();
        for (int i = 0; i < m.ZLen; i++)
        {
            h.I.push_back(rm_scene_detail::ByZ(r, h.elementsByZ, zs[(size_t)i], "Z вещества вне таблицы elementsByZ"));
        }

        m.fluorescers = r.Int();
        m.scatterers = r.Int();
        m.CarryMedium = r.Int();
        m.LayerBrem = r.Int();
        m.LayerRadiationLength = r.Real();
        m.LayerScatterElements = r.Int();
        m.LayerScatterElementsLen = r.Int();
        m.LayerBremZ = r.Real();
        r.End();
        // Рабочие массивы нити фиксированной длины: веса розыгрыша элемента
        // (`Sim::PhMaxElements`, sim_photon.cuh) и доли жёстких столкновений слоя
        // (`RM_LAYER_SCATTER_MAX`, decl_electron.inc). Вещество длиннее — ОТКАЗ здесь, а
        // не `__trap()` посреди узла и не запись за край массива.
        if (m.ZLen > Sim::PhMaxElements)
        {
            r.Fail("в веществе больше элементов, чем держит нить (Sim::PhMaxElements)");
        }

        if (m.LayerScatterElementsLen > RM_LAYER_SCATTER_MAX)
        {
            r.Fail("у вещества слоя больше элементов рассеяния, чем RM_LAYER_SCATTER_MAX (decl_electron.inc)");
        }

        h.materials.push_back(m);
    }
}

// Флуоресценты вещества — `FluorescersG`. В потоке: Material, Z[], Fraction[],
// признак «есть оболочки» на каждый элемент (Shells[i] != null).
inline void Read_fluorescers(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    for (int k = 0; k < count; k++)
    {
        FluorescersG f;
        std::memset(&f, 0, sizeof(f));
        f.Material = r.Int();
        f.Z = r.Ints(f.ZLen);
        f.Fraction = r.Reals(f.FractionLen);
        int shellLen = 0;
        int shellFlags = r.Bytes(shellLen);
        if (f.FractionLen != f.ZLen || shellLen != f.ZLen)
        {
            r.Fail("у флуоресцентов длины Z, долей и оболочек разошлись");
        }

        std::vector<int> zs(h.I.begin() + f.Z, h.I.begin() + f.Z + f.ZLen);
        std::vector<unsigned char> flags(h.B.begin() + shellFlags, h.B.begin() + shellFlags + shellLen);

        f.DataLen = f.ZLen;
        f.Data = f.ZLen == 0 ? 0 : (int)h.I.size();
        for (int i = 0; i < f.ZLen; i++)
        {
            int idx = rm_scene_detail::ByZ(r, h.fluorByZ, zs[(size_t)i], "Z флуоресцента вне таблицы fluorByZ");
            if (idx < 0)
            {
                r.Fail("у флуоресцента нет записи в fluor полосы А (FluorescenceOf дал объект, а таблица — нет)");
            }

            h.I.push_back(idx);
        }

        f.ShellsLen = f.ZLen;
        f.Shells = f.ZLen == 0 ? 0 : (int)h.I.size();
        for (int i = 0; i < f.ZLen; i++)
        {
            int idx = -1;
            if (flags[(size_t)i])
            {
                idx = rm_scene_detail::ByZ(r, h.photoShellByZ, zs[(size_t)i], "Z оболочек вне таблицы photoShellByZ");
                if (idx < 0)
                {
                    r.Fail("у флуоресцента есть PhotoShellModel, а в photoShell полосы А — нет");
                }
            }

            h.I.push_back(idx);
        }

        r.End();
        h.fluorescers.push_back(f);
    }
}

// Рассеиватели вещества — `ScatterersG`. В потоке: Z[], MassFraction[].
inline void Read_scatterers(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    for (int k = 0; k < count; k++)
    {
        ScatterersG s;
        std::memset(&s, 0, sizeof(s));
        s.Z = r.Ints(s.ZLen);
        s.MassFraction = r.Reals(s.MassFractionLen);
        if (s.MassFractionLen != s.ZLen)
        {
            r.Fail("у рассеивателей число Z и долей разошлось");
        }

        std::vector<int> zs(h.I.begin() + s.Z, h.I.begin() + s.Z + s.ZLen);
        s.AtomLen = s.ZLen;
        s.Atom = s.ZLen == 0 ? 0 : (int)h.I.size();
        for (int i = 0; i < s.ZLen; i++)
        {
            int idx = rm_scene_detail::ByZ(r, h.atomsByZ, zs[(size_t)i], "Z рассеивателя вне таблицы atomsByZ");
            if (idx < 0)
            {
                r.Fail("у рассеивателя нет записи в atoms полосы А (ScatteringData.Of дал объект, а таблица — нет)");
            }

            h.I.push_back(idx);
        }

        r.End();
        h.scatterers.push_back(s);
    }
}

// Элементы слоёв — `ScatterElementG`.
inline void Read_scatterElements(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    for (int k = 0; k < count; k++)
    {
        ScatterElementG e;
        std::memset(&e, 0, sizeof(e));
        e.Z = r.Int();
        e.AtomsPerCm3 = r.Real();
        e.Z13 = r.Real();
        e.ZZ1 = r.Real();
        e.Mott = r.Bool();
        r.End();
        h.scatterElements.push_back(e);
    }
}

// Области — `RegionG`. Снимок состава берётся у вещества области (он с ним совпадает:
// `Region.Snapshot` перечисляет тот же словарь `Fractions`).
inline void Read_regions(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    if (count > RM_MAX_REG)
    {
        r.Fail("областей сцены больше RM_MAX_REG (data_scene.cuh)");
    }

    int snapBase = 0;
    for (int k = 0; k < count; k++)
    {
        RegionG g;
        std::memset(&g, 0, sizeof(g));
        g.IsBox = r.Bool();
        g.RIn = r.Real();
        g.ROut = r.Real();
        g.AX = r.Real();
        g.AY = r.Real();
        g.ZMin = r.Real();
        g.ZMax = r.Real();
        g.Material = r.Int();
        g.IsCrystal = r.Bool();
        g.ThresholdPair = r.Bool();
        g.PhotoScale = r.Real();
        g.regBox = r.Bool();
        g.regZMinE = r.Real();
        g.regZMaxE = r.Real();
        g.regROutE = r.Real();
        g.regRInE = r.Real();
        g.regAXE = r.Real();
        g.regAYE = r.Real();
        r.End();

        if (g.Material < 0 || g.Material >= (int)h.materials.size())
        {
            r.Fail("вещество области вне таблицы materials");
        }

        const MaterialG& m = h.materials[(size_t)g.Material];
        g.SnapElement = m.Element;
        g.SnapZ = m.Z;
        g.SnapFraction = m.Fraction;
        g.SnapLen = m.ZLen;
        g.SnapDensity = m.Density;
        g.SnapBase = snapBase;
        snapBase += m.ZLen;
        if (snapBase > RM_MAX_SNAP_TOTAL)
        {
            r.Fail("сумма элементов составов областей больше RM_MAX_SNAP_TOTAL (data_scene.cuh)");
        }

        h.regions.push_back(g);
    }
}

// Сцена — `SceneG`, ровно одна запись.
inline void Read_scene(BlobReader& r)
{
    HostData& h = r.h;
    int count = r.Int();
    if (count != 1)
    {
        r.Fail("сцена обязана быть ровно одной записью");
    }

    SceneG s;
    std::memset(&s, 0, sizeof(s));
    s.crystal = r.Int();
    s.crystalMaterial = r.Int();
    s.sphereZ = r.Real();
    s.sphereR = r.Real();
    s.pathSceneZ = r.Real();
    s.pathSceneR = r.Real();
    s.sceneRMax = r.Real();
    s.sceneZMin = r.Real();
    s.sceneZMax = r.Real();
    s.sourceKind = r.Int();
    s.PointZ = r.Real();
    s.CylR = r.Real();
    s.CylZ0 = r.Real();
    s.CylZ1 = r.Real();
    s.MarRIn = r.Real();
    s.MarROut = r.Real();
    s.MarZ0 = r.Real();
    s.MarZ1 = r.Real();
    s.MarZCap = r.Real();
    s.MarCapFraction = r.Real();
    s.BoxAX = r.Real();
    s.BoxAY = r.Real();
    s.BoxZ0 = r.Real();
    s.BoxZ1 = r.Real();
    s.electron = r.Int();
    s.bremTable = r.Int();
    s.lightYield = r.Int();
    s.waterTable = r.Int();
    s.crystalHasPartials = r.Bool();
    s.crystalRadiationLength = r.Real();
    r.End();

    // Ветка «кристалл без парциальных сечений» (`CrystalChannels`, :3673–3693) на
    // устройстве не перенесена — нужны таблицы `AttenuationData`. Отказ при загрузке,
    // а не `__trap()` на первой истории.
    if (!s.crystalHasPartials)
    {
        r.Fail("кристалл без парциальных сечений XCOM — ветка CrystalChannels :3673 на GPU не перенесена");
    }

    if (s.crystal < 0 || s.crystal >= (int)h.regions.size() || !h.regions[(size_t)s.crystal].IsCrystal)
    {
        r.Fail("индекс кристалла не указывает на область-кристалл");
    }

    if (s.sourceKind < RM_SOURCE_POINT || s.sourceKind > RM_SOURCE_BOX)
    {
        r.Fail("вид источника вне RM_SOURCE_*");
    }

    h.scene.push_back(s);
}
