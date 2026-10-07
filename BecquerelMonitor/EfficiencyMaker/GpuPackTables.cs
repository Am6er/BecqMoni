using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    // ⚡ GPU-путь матрицы отклика (`AMBER160`, полоса П221, полоса А): разделы упаковки
    // классов ДАННЫХ — сечения XCOM, флуоресценция K/L, EPICS по оболочкам, разрядка EADL,
    // рассеяние на атоме, ESTAR, тормозное, кривая света. Читатель — `tools/effmaker/gpu/
    // host_tables.h`, порядок полей там и здесь ОДИН; после каждой записи — `w.End()`.
    //
    // ⛔ Прямой доступ (`AMBER219`, П245-R; решение Amber 07.10.2026 «Перевести на прямой
    // доступ сейчас»): закрытые поля и методы классов данных открыты как `internal`, тела
    // не тронуты; ленивые таблицы вынуждаются вызовом их же метода ДО чтения — числа те
    // же, что увидел бы счёт CPU.
    //
    // Порядок записей таблицы = индекс на устройстве. Таблицы по Z идут через реестр
    // (`ctx.Reg`, таблицы "elements", "fluor", "photoShell", "relax", "atoms"): если полоса
    // сцены уже зарегистрировала объект, его индекс сохраняется; остальные дописываются по
    // возрастанию Z из `ctx.Zs`. Таблицы "electronMats", "brems", "lightYields" заполняет
    // только сбор сцены — пишутся В ПОРЯДКЕ СПИСКА реестра.
    public static partial class GpuPack
    {
        static void WriteTables(GpuPackContext ctx, GpuWriter w)
        {
            WriteElements(ctx, w);
            WriteFluor(ctx, w);
            WritePhotoShell(ctx, w);
            WriteRelax(ctx, w);
            WriteAtoms(ctx, w);
            WriteElectronMats(ctx, w);
            WriteBrems(ctx, w);
            WriteLightYields(ctx, w);
        }

        /// <summary>
        /// Объекты таблицы по Z в порядке реестра: сначала уже зарегистрированные (их Z
        /// узнаётся по ссылке среди `ctx.Zs`), затем новые по возрастанию Z. Объект без Z
        /// из `ctx.Zs` — отказ: записи по Z без Z не бывает.
        /// </summary>
        static List<KeyValuePair<int, object>> TablesByZ(GpuPackContext ctx, string table, Func<int, object> of)
        {
            var zOf = new Dictionary<object, int>(new TablesRefEq());
            foreach (int z in ctx.Zs)
            {
                object o = of(z);
                if (o == null)
                {
                    continue;
                }

                if (!zOf.ContainsKey(o))
                {
                    zOf[o] = z;
                }

                ctx.Reg.Add(table, o);
            }

            var list = new List<KeyValuePair<int, object>>();
            foreach (object o in ctx.Reg.List(table))
            {
                int z;
                if (!zOf.TryGetValue(o, out z))
                {
                    throw new InvalidOperationException("GPU-упаковка: объект таблицы «" + table
                        + "» зарегистрирован, но его Z нет среди ctx.Zs — добавьте Z в CollectScene");
                }

                list.Add(new KeyValuePair<int, object>(z, o));
            }

            return list;
        }

        sealed class TablesRefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        // ------------------------------------------------------------------
        // MaterialDatabase.Element — сечения XCOM по каналам
        // ------------------------------------------------------------------
        static void WriteElements(GpuPackContext ctx, GpuWriter w)
        {
            var list = TablesByZ(ctx, "elements", z =>
            {
                MaterialDatabase.Element e;
                return MaterialDatabase.TryGet(z, out e) ? e : null;
            });

            w.Tag("elements", list.Count);
            foreach (var pair in list)
            {
                var e = (MaterialDatabase.Element)pair.Value;
                w.Int(pair.Key);
                w.Reals(e.EnergyKev);
                w.Real(e.AtomicWeight);
                w.Jagged(e.Channels);
                w.Reals(e.LogEnergyKev);
                w.Jagged(e.LogChannels);
                w.Reals(e.LogPairNuclearShape);
                w.Reals(e.LogPairElectronShape);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // MaterialDatabase.Fluorescence — K/L, выходы, Костер—Крониг, линии
        // ------------------------------------------------------------------
        static void WriteFluor(GpuPackContext ctx, GpuWriter w)
        {
            var list = TablesByZ(ctx, "fluor", z => MaterialDatabase.FluorescenceOf(z));
            w.Tag("fluor", list.Count);
            foreach (var pair in list)
            {
                var f = (MaterialDatabase.Fluorescence)pair.Value;
                w.Int(pair.Key);
                w.Real(f.KEdgeKev);
                w.Real(f.KFraction);
                w.Real(f.OmegaK);
                w.Real(f.OmegaKMeasured);
                w.Reals(f.LineKev);
                w.Reals(f.LineWeight);
                w.Reals(f.LEdgeKev);
                w.Reals(f.OmegaL);
                w.Reals(f.OmegaLSupply);
                w.Reals(f.CkEadl);
                w.Reals(f.CkSupply);
                w.Jagged(f.LineKevL);
                w.Jagged(f.LineWeightL);
                // null в потоке неотличим от пустого массива — значение свойства кладётся явно
                w.Bool(f.HasL);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // MaterialDatabase.PhotoShellModel — EPICS2017 по оболочкам (поля internal)
        // ------------------------------------------------------------------
        static void WritePhotoShell(GpuPackContext ctx, GpuWriter w)
        {
            var list = TablesByZ(ctx, "photoShell", z => MaterialDatabase.PhotoShellOf(z));
            w.Tag("photoShell", list.Count);
            foreach (var pair in list)
            {
                var m = (MaterialDatabase.PhotoShellModel)pair.Value;
                w.Int(pair.Key);
                w.Real(m.kEdgeKev);
                w.Real(m.lowFromKev);
                w.Real(m.highFromKev);
                w.Reals(m.lowK);
                w.Reals(m.lowTotal);
                w.Reals(m.highK);
                w.Reals(m.highTotal);
                w.Jagged(m.tableE);
                w.Jagged(m.tableCs);
                // Логарифмы строит загрузка (`IndexLogs`); null — устройство идёт запасным
                // путём `InterpTable`, как и CPU. Памятка `lastFracs` не переносится.
                w.Jagged(m.logTableE);
                w.Jagged(m.logTableCs);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // MaterialDatabase.Relaxation — разрядка EADL (поля и вложенный тип internal)
        // ------------------------------------------------------------------
        static void WriteRelax(GpuPackContext ctx, GpuWriter w)
        {
            var list = TablesByZ(ctx, "relax", z => MaterialDatabase.RelaxationOf(z));
            w.Tag("relax", list.Count);
            foreach (var pair in list)
            {
                var r = (MaterialDatabase.Relaxation)pair.Value;
                double[] bindingByShell = r.bindingByShell;
                MaterialDatabase.Relaxation.Transitions[] transitionsByShell = r.transitionsByShell;
                if (bindingByShell == null || transitionsByShell == null)
                {
                    // CPU в этом случае идёт запасным путём по словарям — на устройстве его нет
                    throw new InvalidOperationException("GPU-упаковка: у релаксации Z=" + pair.Key
                        + " нет массивов по обозначению EADL (IndexByShell не звался)");
                }

                w.Int(pair.Key);
                w.Reals(bindingByShell);
                w.Ints(r.shellsByBinding);
                w.Reals(r.bindingByOrder);
                w.Int(transitionsByShell.Length);
                foreach (MaterialDatabase.Relaxation.Transitions t in transitionsByShell)
                {
                    w.Bool(t != null);
                    if (t == null)
                    {
                        continue;
                    }

                    w.Reals(t.radCum);
                    w.Reals(t.radKev);
                    w.Ints(t.radFrom);
                    w.Real(t.radSum);
                    w.Reals(t.augCum);
                    w.Reals(t.augKev);
                    w.Ints(t.augFrom);
                    w.Ints(t.augEjected);
                    w.Real(t.augSum);
                }

                w.End();
            }
        }

        // ------------------------------------------------------------------
        // ScatteringData.Atom — S(x,Z), F², профили Комптона, нормировки (AMBER79)
        // ------------------------------------------------------------------
        static void WriteAtoms(GpuPackContext ctx, GpuWriter w)
        {
            var list = TablesByZ(ctx, "atoms", z => ScatteringData.Of(z));
            // Сетка импульсов профилей — статическая, одна на процесс; заполнена загрузкой
            // первого атома (`LoadMomentumGrid`), поэтому читается ПОСЛЕ `ScatteringData.Of`.
            double[] momentumGrid = ScatteringData.momentumGrid;
            w.Tag("atoms", list.Count);
            foreach (var pair in list)
            {
                var a = (ScatteringData.Atom)pair.Value;
                // Ленивые нормировки — построить ТЕМ ЖЕ кодом до чтения.
                a.EnsureNorms();
                w.Int(pair.Key);
                w.Reals(a.sfX);
                w.Reals(a.sfV);
                w.Reals(a.ffT);
                w.Reals(a.ffF2);
                w.Reals(a.ffCum);
                w.Reals(a.shellCum);
                w.Reals(a.shellBindKev);
                w.Jagged(a.profCum);
                w.Reals(momentumGrid);
                w.Reals(a.cohNormLog);
                w.Reals(a.incNormLog);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // ElectronData.Material — ESTAR: пробег, выход и их логарифмы
        // ------------------------------------------------------------------
        static void WriteElectronMats(GpuPackContext ctx, GpuWriter w)
        {
            IList<object> list = ctx.Reg.List("electronMats");
            w.Tag("electronMats", list.Count);
            foreach (object o in list)
            {
                var m = (ElectronData.Material)o;
                // Памятка логарифмов — вынудить `LogsNow` (сверяет ссылки, строит при нужде).
                ElectronData.Material.Logs logs = m.LogsNow();
                w.Reals(m.Energy);
                w.Reals(m.Range);
                w.Reals(m.Yield);
                w.Reals(logs.Energy);
                w.Reals(logs.Range);
                w.Reals(logs.Yield);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // ThickTargetBrem — толстая и тонкая мишень (поля internal)
        // ------------------------------------------------------------------
        static void WriteBrems(GpuPackContext ctx, GpuWriter w)
        {
            IList<object> list = ctx.Reg.List("brems");
            w.Tag("brems", list.Count);
            foreach (object o in list)
            {
                var b = (ThickTargetBrem)o;
                w.Real(b.MinKev);
                w.Reals(b.node);
                w.Reals(b.logNode);
                w.Jagged(b.cumulative);
                w.Reals(b.photons);
                w.Reals(b.radiatedKev);
                w.Reals(b.anchorFactor);
                w.Jagged(b.thinAbove);
                w.Reals(b.thinPhotons);
                w.Reals(b.thinRadiated);
                w.End();
            }
        }

        // ------------------------------------------------------------------
        // MaterialDatabase.LightYieldCurve — кривая света (поля и памятка internal)
        // ------------------------------------------------------------------
        static void WriteLightYields(GpuPackContext ctx, GpuWriter w)
        {
            IList<object> list = ctx.Reg.List("lightYields");
            w.Tag("lightYields", list.Count);
            foreach (object o in list)
            {
                var c = (MaterialDatabase.LightYieldCurve)o;
                double[] energyKev = c.energyKev;
                // Памятка логарифмов узлов — вынудить `LogNodes` от ТОГО ЖЕ массива.
                double[] logNodes = c.LogNodes(energyKev);
                w.Reals(energyKev);
                w.Reals(c.yieldRel);
                w.Reals(logNodes);
                w.End();
            }
        }
    }
}
