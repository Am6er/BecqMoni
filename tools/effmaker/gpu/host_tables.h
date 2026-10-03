// host_tables.h — читатели разделов упаковки для классов ДАННЫХ (сечения, флуоресценция,
// EPICS, EADL, рассеяние, ESTAR, тормозное, свет). Полоса П221 (`AMBER160`), полоса А.
// Писатель — tools/effmaker/probes/GpuPackTables.cs (`GpuPack.WriteTables`): порядок полей
// здесь и там ОДИН, после каждой записи — контрольное слово `End()`.
//
// Вызов: имя раздела (`Tag`) уже прочитал диспетчер (`api.cu`) и по нему позвал
// `Read_<имя>`; функция читает число записей и сами записи. Записи по Z первым полем
// несут Z и заполняют таблицу `h.<таблица>ByZ[Z]`.
//
// Определения `inline` — как у host_scene.h (объявления без inline — в host.h).
#pragma once
#include "host.h"

namespace host_tables_detail
{
    inline int CheckZ(BlobReader& r, int z)
    {
        if (z < 0 || z >= 128) r.Fail("Z вне 0…127");
        return z;
    }

    inline int Count(BlobReader& r)
    {
        int n = r.Int();
        if (n < 0) r.Fail("отрицательное число записей");
        return n;
    }

    // (`AMBER161`, П227) Не убывает ли массив арены R [off, off + len) — разрешение
    // двоичного поиска по накоплению (тот же индекс, что линейный проход C#).
    inline bool NonDecreasing(const std::vector<real>& arena, int off, int len)
    {
        for (int i = 1; i < len; i++)
        {
            if (!(arena[(size_t)(off + i - 1)] <= arena[(size_t)(off + i)])) return false;
        }

        return true;
    }
}

// = GpuPackTables.cs WriteElements ↔ MaterialDatabase.Element
inline void Read_elements(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        ElementG e{};
        e.Z = host_tables_detail::CheckZ(r, r.Int());
        e.EnergyKev = r.Reals(e.EnergyKevLen);
        e.AtomicWeight = r.Real();
        e.Channels = r.Jagged(e.ChannelsLen, e.ChannelsOff);
        e.LogEnergyKev = r.Reals(e.LogEnergyKevLen);
        e.LogChannels = r.Jagged(e.LogChannelsLen, e.LogChannelsOff);
        e.LogPairNuclearShape = r.Reals(e.LogPairNuclearShapeLen);
        e.LogPairElectronShape = r.Reals(e.LogPairElectronShapeLen);
        r.End();
        if (e.ChannelsLen != 5) r.Fail("у элемента не пять каналов");
        if (e.LogChannelsLen != 4) r.Fail("у элемента не четыре строки логарифмов каналов");
        r.h.elementsByZ[(size_t)e.Z] = (int)r.h.elements.size();
        r.h.elements.push_back(e);
    }
}

// = GpuPackTables.cs WriteFluor ↔ MaterialDatabase.Fluorescence
inline void Read_fluor(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        FluorescenceG f{};
        f.Z = host_tables_detail::CheckZ(r, r.Int());
        f.KEdgeKev = r.Real();
        f.KFraction = r.Real();
        f.OmegaK = r.Real();
        f.OmegaKMeasured = r.Real();
        f.LineKev = r.Reals(f.LineKevLen);
        f.LineWeight = r.Reals(f.LineWeightLen);
        f.LEdgeKev = r.Reals(f.LEdgeKevLen);
        f.OmegaL = r.Reals(f.OmegaLLen);
        f.OmegaLSupply = r.Reals(f.OmegaLSupplyLen);
        f.CkEadl = r.Reals(f.CkEadlLen);
        f.CkSupply = r.Reals(f.CkSupplyLen);
        f.LineKevL = r.Jagged(f.LineKevLLen, f.LineKevLOff);
        f.LineWeightL = r.Jagged(f.LineWeightLLen, f.LineWeightLOff);
        f.hasL = r.Bool() ? 1 : 0;
        r.End();
        r.h.fluorByZ[(size_t)f.Z] = (int)r.h.fluor.size();
        r.h.fluor.push_back(f);
    }
}

// = GpuPackTables.cs WritePhotoShell ↔ MaterialDatabase.PhotoShellModel
inline void Read_photoShell(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        PhotoShellModelG m{};
        m.Z = host_tables_detail::CheckZ(r, r.Int());
        m.kEdgeKev = r.Real();
        m.lowFromKev = r.Real();
        m.highFromKev = r.Real();
        m.lowK = r.Reals(m.lowKLen);
        m.lowTotal = r.Reals(m.lowTotalLen);
        m.highK = r.Reals(m.highKLen);
        m.highTotal = r.Reals(m.highTotalLen);
        m.tableE = r.Jagged(m.tableELen, m.tableEOff);
        m.tableCs = r.Jagged(m.tableCsLen, m.tableCsOff);
        m.logTableE = r.Jagged(m.logTableELen, m.logTableEOff);
        m.logTableCs = r.Jagged(m.logTableCsLen, m.logTableCsOff);
        r.End();
        if (m.tableCsLen != m.tableELen) r.Fail("число оболочек tableE и tableCs разошлось");
        r.h.photoShellByZ[(size_t)m.Z] = (int)r.h.photoShell.size();
        r.h.photoShell.push_back(m);
    }
}

// = GpuPackTables.cs WriteRelax ↔ MaterialDatabase.Relaxation (+ Transitions)
// Переходы вакансии пишутся ВНУТРИ записи релаксации, по оболочкам 0…max: признак
// «есть» и поля `Transitions`; индексы в `h.transitions` ложатся в арену I одним куском
// ПОСЛЕ всех переходов (между ними арена I занята `radFrom`/`augFrom`/`augEjected`).
inline void Read_relax(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        RelaxationG x{};
        x.Z = host_tables_detail::CheckZ(r, r.Int());
        x.bindingByShell = r.Reals(x.bindingByShellLen);
        x.shellsByBinding = r.Ints(x.shellsByBindingLen);
        x.bindingByOrder = r.Reals(x.bindingByOrderLen);
        if (x.shellsByBindingLen != x.bindingByOrderLen) r.Fail("shellsByBinding и bindingByOrder разной длины");
        int shells = r.Int();
        if (shells < 0) r.Fail("отрицательное число оболочек переходов");
        std::vector<int> index((size_t)shells, -1);
        for (int s = 0; s < shells; s++)
        {
            if (!r.Bool())
            {
                continue;
            }

            TransitionsG t{};
            t.radCum = r.Reals(t.radCumLen);
            t.radKev = r.Reals(t.radKevLen);
            t.radFrom = r.Ints(t.radFromLen);
            t.radSum = r.Real();
            t.augCum = r.Reals(t.augCumLen);
            t.augKev = r.Reals(t.augKevLen);
            t.augFrom = r.Ints(t.augFromLen);
            t.augEjected = r.Ints(t.augEjectedLen);
            t.augSum = r.Real();
            if (t.radKevLen != t.radCumLen || t.radFromLen != t.radCumLen
                || t.augKevLen != t.augCumLen || t.augFromLen != t.augCumLen || t.augEjectedLen != t.augCumLen)
            {
                r.Fail("массивы переходов вакансии разной длины");
            }

            t.cumMonotone = host_tables_detail::NonDecreasing(r.h.R, t.radCum, t.radCumLen)
                            && host_tables_detail::NonDecreasing(r.h.R, t.augCum, t.augCumLen) ? 1 : 0;
            index[(size_t)s] = (int)r.h.transitions.size();
            r.h.transitions.push_back(t);
        }

        x.transitionsByShellLen = shells;
        x.transitionsByShell = shells == 0 ? 0 : (int)r.h.I.size();
        for (int s = 0; s < shells; s++) r.h.I.push_back(index[(size_t)s]);
        r.End();
        r.h.relaxByZ[(size_t)x.Z] = (int)r.h.relax.size();
        r.h.relax.push_back(x);
    }
}

// = GpuPackTables.cs WriteAtoms ↔ ScatteringData.Atom
inline void Read_atoms(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        AtomG a{};
        a.Z = host_tables_detail::CheckZ(r, r.Int());
        a.sfX = r.Reals(a.sfXLen);
        a.sfV = r.Reals(a.sfVLen);
        a.ffT = r.Reals(a.ffTLen);
        a.ffF2 = r.Reals(a.ffF2Len);
        a.ffCum = r.Reals(a.ffCumLen);
        a.shellCum = r.Reals(a.shellCumLen);
        a.shellCumMonotone = host_tables_detail::NonDecreasing(r.h.R, a.shellCum, a.shellCumLen) ? 1 : 0;
        a.shellBindKev = r.Reals(a.shellBindKevLen);
        a.profCum = r.Jagged(a.profCumLen, a.profCumOff);
        a.momentumGrid = r.Reals(a.momentumGridLen);
        a.cohNormLog = r.Reals(a.cohNormLogLen);
        a.incNormLog = r.Reals(a.incNormLogLen);
        r.End();
        if (a.sfXLen < 2 || a.sfVLen != a.sfXLen) r.Fail("таблица S(x,Z) пуста или разной длины");
        if (a.ffTLen < 2 || a.ffF2Len != a.ffTLen || a.ffCumLen != a.ffTLen) r.Fail("таблица F² пуста или разной длины");
        if (a.cohNormLogLen != 600 || a.incNormLogLen != 600) r.Fail("нормировки не построены (EnsureNorms) или не 600 узлов");
        r.h.atomsByZ[(size_t)a.Z] = (int)r.h.atoms.size();
        r.h.atoms.push_back(a);
    }
}

// = GpuPackTables.cs WriteElectronMats ↔ ElectronData.Material (порядок — реестр "electronMats")
inline void Read_electronMats(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        ElectronMaterialG m{};
        m.Energy = r.Reals(m.EnergyLen);
        m.Range = r.Reals(m.RangeLen);
        m.Yield = r.Reals(m.YieldLen);
        m.logsEnergy = r.Reals(m.logsEnergyLen);
        m.logsRange = r.Reals(m.logsRangeLen);
        m.logsYield = r.Reals(m.logsYieldLen);
        r.End();
        if (m.EnergyLen < 2 || m.RangeLen != m.EnergyLen || m.YieldLen != m.EnergyLen) r.Fail("таблица ESTAR пуста или разной длины");
        r.h.electronMats.push_back(m);
    }
}

// = GpuPackTables.cs WriteBrems ↔ ThickTargetBrem (порядок — реестр "brems")
inline void Read_brems(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        ThickTargetBremG b{};
        b.MinKev = r.Real();
        b.node = r.Reals(b.nodeLen);
        b.logNode = r.Reals(b.logNodeLen);
        b.cumulative = r.Jagged(b.cumulativeLen, b.cumulativeOff);
        b.photons = r.Reals(b.photonsLen);
        b.radiatedKev = r.Reals(b.radiatedKevLen);
        b.anchorFactor = r.Reals(b.anchorFactorLen);
        b.thinAbove = r.Jagged(b.thinAboveLen, b.thinAboveOff);
        b.thinPhotons = r.Reals(b.thinPhotonsLen);
        b.thinRadiated = r.Reals(b.thinRadiatedLen);
        r.End();
        if (b.nodeLen < 2 || b.logNodeLen != b.nodeLen || b.cumulativeLen != b.nodeLen
            || b.photonsLen != b.nodeLen || b.thinAboveLen != b.nodeLen || b.thinPhotonsLen != b.nodeLen)
        {
            r.Fail("таблица тормозного пуста или разной длины");
        }

        r.h.brems.push_back(b);
    }
}

// = GpuPackTables.cs WriteLightYields ↔ MaterialDatabase.LightYieldCurve (порядок — реестр "lightYields")
inline void Read_lightYields(BlobReader& r)
{
    int n = host_tables_detail::Count(r);
    for (int k = 0; k < n; k++)
    {
        LightYieldCurveG c{};
        c.energyKev = r.Reals(c.energyKevLen);
        c.yieldRel = r.Reals(c.yieldRelLen);
        c.logNodes = r.Reals(c.logNodesLen);
        r.End();
        if (c.energyKevLen < 2 || c.yieldRelLen != c.energyKevLen || c.logNodesLen != c.energyKevLen) r.Fail("кривая света пуста или разной длины");
        r.h.lightYields.push_back(c);
    }
}
