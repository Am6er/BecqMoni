using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    // ⚡ GPU-путь матрицы отклика (`AMBER160`, полоса П221, часть Б): разделы упаковки
    // СЦЕНЫ — вещества (с кэшами симулятора по веществу), флуоресценты, рассеиватели,
    // элементы слоёв, области, сцена и источник. Читатель — `tools/effmaker/gpu/host_scene.h`,
    // структуры — `tools/effmaker/gpu/data_scene.cuh`. Писатель, реестр и отражение —
    // `GpuMatrix.cs`.
    //
    // ⛔ Приложение НЕ правится (решение Amber 02.10.2026 «Только оснастка»): закрытые
    // поля и методы `EfficiencySimulator` читаются и зовутся ОТРАЖЕНИЕМ. Кэши по веществу
    // (`FluorescersOf`, `ScatterersOf`, `CarryMedium`, `LayerBrem`, `LayerRadiationLength`,
    // `LayerScatterElements`, `LayerBremZ`) строит ТОТ ЖЕ код симулятора — здесь он только
    // вынуждается заранее, по каждому веществу сцены, и его ответы переписываются как есть.
    //
    // Порядок: всё, что попадает в реестр (`ctx.Reg.Add`), регистрируется в `CollectScene`
    // — списки `electronMats`/`brems`/`lightYields` пишет полоса А (`GpuPackTables.cs`)
    // ДО разделов сцены, в порядке реестра; `WriteScene` только спрашивает `ctx.Reg.Of`.

    public static partial class GpuPack
    {
        // = RM_SOURCE_* (data_scene.cuh).
        const int SourcePoint = 0, SourceCylinder = 1, SourceMarinelli = 2, SourceBox = 3;

        /// <summary>Области сцены в порядке поиска (`regionArray`, «первая победившая»).</summary>
        static object[] SceneRegions(EfficiencySimulator sim)
        {
            Array array = GpuReflect.Field(sim, "regionArray") as Array;
            if (array == null)
            {
                throw new InvalidOperationException(
                    "GPU-упаковка: сцена симулятора не собрана (regionArray = null) — EnsureBuilt не звался");
            }

            var list = new object[array.Length];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = array.GetValue(i);
            }

            return list;
        }

        /// <summary>
        /// Вид источника (= RM_SOURCE_*); ISO, важностный и незнакомый — ОТКАЗ: их
        /// розыгрыш на GPU не перенесён, и тихой подмены равномерным быть не должно.
        /// </summary>
        static int SourceKindOf(object source)
        {
            if (source == null)
            {
                throw new InvalidOperationException("GPU-упаковка: у сцены нет источника (source = null)");
            }

            switch (source.GetType().Name)
            {
                case "PointSampler": return SourcePoint;
                case "CylinderSampler": return SourceCylinder;
                case "MarinelliSampler": return SourceMarinelli;
                case "BoxSampler": return SourceBox;
                case "ImportanceSampler":
                    throw new NotSupportedException(
                        "GPU-путь: важностный розыгрыш точки вылета (ImportanceSampler, ключ --imp=1) не перенесён; "
                        + "считать эту сцену на ЦП");
                case "IsoFieldSampler":
                    throw new NotSupportedException(
                        "GPU-путь: изотропное поле (IsoFieldSampler, сцена ISO) не перенесено; считать эту сцену на ЦП");
                default:
                    throw new NotSupportedException(
                        "GPU-путь: незнакомый вид источника «" + source.GetType().Name + "» — розыгрыш не перенесён");
            }
        }

        /// <summary>
        /// Зарегистрировать вещества сцены и всё, что симулятор строит по веществу, и собрать
        /// Z всех веществ в <see cref="GpuPackContext.Zs"/> (по ним полоса А пишет таблицы
        /// элементов, флуоресценции, оболочек, релаксации и атомов).
        /// </summary>
        static void CollectScene(GpuPackContext ctx)
        {
            EfficiencySimulator sim = ctx.Sim;
            GpuRegistry reg = ctx.Reg;

            // Источник — сразу: отказ раньше любой работы.
            SourceKindOf(GpuReflect.Field(sim, "source"));

            foreach (object region in SceneRegions(sim))
            {
                reg.Add("materials", GpuReflect.Field(region, "Material"));
            }

            // Объекты сцены, на которые ссылается `SceneG` (EfficiencySimulator.cs:1647-1657, 7683).
            reg.Add("electronMats", GpuReflect.Field(sim, "electron"));
            reg.Add("electronMats", GpuReflect.Call(sim, "WaterTable"));
            reg.Add("brems", GpuReflect.Field(sim, "bremTable"));
            reg.Add("lightYields", GpuReflect.Field(sim, "lightYield"));

            // Кэши по веществу — вынудить построение тем же кодом симулятора.
            var materials = new List<object>(reg.List("materials"));
            foreach (object o in materials)
            {
                var m = (GeometryMaterial)o;
                foreach (int z in m.Fractions.Keys)
                {
                    ctx.Zs.Add(z);
                }

                reg.Add("fluorescers", GpuReflect.Call(sim, "FluorescersOf", m));
                reg.Add("scatterers", GpuReflect.Call(sim, "ScatterersOf", m));
                reg.Add("electronMats", GpuReflect.Call(sim, "CarryMedium", m));
                reg.Add("brems", GpuReflect.Call(sim, "LayerBrem", m));
                GpuReflect.Call(sim, "LayerRadiationLength", m);
                Array elements = (Array)GpuReflect.Call(sim, "LayerScatterElements", m);
                for (int i = 0; i < elements.Length; i++)
                {
                    reg.Add("scatterElements", elements.GetValue(i));
                }

                GpuReflect.Call(sim, "LayerBremZ", m);
            }

            // Вода — таблица ESTAR пустоты (`WaterTable`, запасная у `CarryMedium`).
            // `ElectronData.Material` состава не несёт; H₂O — водород и кислород.
            ctx.Zs.Add(1);
            ctx.Zs.Add(8);
        }

        /// <summary>Разделы сцены: materials, fluorescers, scatterers, scatterElements, regions, scene.</summary>
        static void WriteScene(GpuPackContext ctx, GpuWriter w)
        {
            EfficiencySimulator sim = ctx.Sim;
            GpuRegistry reg = ctx.Reg;

            // --- materials (MaterialG) ---
            IList<object> materials = reg.List("materials");
            w.Tag("materials", materials.Count);
            foreach (object o in materials)
            {
                var m = (GeometryMaterial)o;
                // Состав в порядке перечисления словаря — так его снимают `Region.Snapshot`,
                // `BuildFluorescers`, `ScatterersOf` (`A162`: порядок — вход розыгрыша).
                var zs = new List<int>();
                var fractions = new List<double>();
                foreach (KeyValuePair<int, double> pair in m.Fractions)
                {
                    zs.Add(pair.Key);
                    fractions.Add(pair.Value);
                }

                w.Real(m.Density);
                w.Ints(zs.ToArray());
                w.Reals(fractions.ToArray());
                w.Int(reg.Of("fluorescers", GpuReflect.Call(sim, "FluorescersOf", m)));
                w.Int(reg.Of("scatterers", GpuReflect.Call(sim, "ScatterersOf", m)));
                w.Int(reg.Of("electronMats", GpuReflect.Call(sim, "CarryMedium", m)));
                w.Int(reg.Of("brems", GpuReflect.Call(sim, "LayerBrem", m)));
                w.Real((double)GpuReflect.Call(sim, "LayerRadiationLength", m));
                Array elements = (Array)GpuReflect.Call(sim, "LayerScatterElements", m);
                int start = elements.Length == 0 ? 0 : reg.Of("scatterElements", elements.GetValue(0));
                for (int i = 0; i < elements.Length; i++)
                {
                    if (reg.Of("scatterElements", elements.GetValue(i)) != start + i)
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: элементы слоя вещества «" + m.Name + "» легли в реестр не подряд");
                    }
                }

                w.Int(start);
                w.Int(elements.Length);
                w.Real((double)GpuReflect.Call(sim, "LayerBremZ", m));
                w.End();
            }

            // --- fluorescers (FluorescersG) ---
            IList<object> fluorescers = reg.List("fluorescers");
            w.Tag("fluorescers", fluorescers.Count);
            foreach (object f in fluorescers)
            {
                int[] z = GpuReflect.Get<int[]>(f, "Z");
                double[] fraction = GpuReflect.Get<double[]>(f, "Fraction");
                Array data = GpuReflect.Get<Array>(f, "Data");
                Array shells = GpuReflect.Get<Array>(f, "Shells");
                var hasShell = new bool[z.Length];
                for (int i = 0; i < z.Length; i++)
                {
                    // Читатель берёт индексы по Z из таблиц полосы А; убедиться, что объект
                    // симулятора — тот же, что отдаёт поиск по Z.
                    if (!ReferenceEquals(data.GetValue(i), MaterialDatabase.FluorescenceOf(z[i])))
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: Fluorescers.Data[" + i + "] не совпал с FluorescenceOf(" + z[i] + ")");
                    }

                    object shell = shells.GetValue(i);
                    hasShell[i] = shell != null;
                    if (shell != null && !ReferenceEquals(shell, MaterialDatabase.PhotoShellOf(z[i])))
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: Fluorescers.Shells[" + i + "] не совпал с PhotoShellOf(" + z[i] + ")");
                    }
                }

                w.Int(reg.Of("materials", GpuReflect.Field(f, "Material")));
                w.Ints(z);
                w.Reals(fraction);
                w.Bytes(hasShell);
                w.End();
            }

            // --- scatterers (ScatterersG) ---
            IList<object> scatterers = reg.List("scatterers");
            w.Tag("scatterers", scatterers.Count);
            foreach (object s in scatterers)
            {
                int[] z = GpuReflect.Get<int[]>(s, "Z");
                double[] mass = GpuReflect.Get<double[]>(s, "MassFraction");
                Array atoms = GpuReflect.Get<Array>(s, "Atom");
                for (int i = 0; i < z.Length; i++)
                {
                    if (!ReferenceEquals(atoms.GetValue(i), ScatteringData.Of(z[i])))
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: Scatterers.Atom[" + i + "] не совпал с ScatteringData.Of(" + z[i] + ")");
                    }
                }

                w.Ints(z);
                w.Reals(mass);
                w.End();
            }

            // --- scatterElements (ScatterElementG) ---
            IList<object> scatterElements = reg.List("scatterElements");
            w.Tag("scatterElements", scatterElements.Count);
            foreach (object e in scatterElements)
            {
                w.Int(GpuReflect.I(e, "Z"));
                w.Real(GpuReflect.D(e, "AtomsPerCm3"));
                w.Real(GpuReflect.D(e, "Z13"));
                w.Real(GpuReflect.D(e, "ZZ1"));
                w.Bool(GpuReflect.B(e, "Mott"));
                w.End();
            }

            // --- regions (RegionG) ---
            object[] regions = SceneRegions(sim);
            bool[] regBox = GpuReflect.Get<bool[]>(sim, "regBox");
            double[] regZMinE = GpuReflect.Get<double[]>(sim, "regZMinE");
            double[] regZMaxE = GpuReflect.Get<double[]>(sim, "regZMaxE");
            double[] regROutE = GpuReflect.Get<double[]>(sim, "regROutE");
            double[] regRInE = GpuReflect.Get<double[]>(sim, "regRInE");
            double[] regAXE = GpuReflect.Get<double[]>(sim, "regAXE");
            double[] regAYE = GpuReflect.Get<double[]>(sim, "regAYE");
            w.Tag("regions", regions.Length);
            for (int i = 0; i < regions.Length; i++)
            {
                object r = regions[i];
                w.Bool(GpuReflect.B(r, "IsBox"));
                w.Real(GpuReflect.D(r, "RIn"));
                w.Real(GpuReflect.D(r, "ROut"));
                w.Real(GpuReflect.D(r, "AX"));
                w.Real(GpuReflect.D(r, "AY"));
                w.Real(GpuReflect.D(r, "ZMin"));
                w.Real(GpuReflect.D(r, "ZMax"));
                w.Int(reg.Of("materials", GpuReflect.Field(r, "Material")));
                w.Bool(GpuReflect.B(r, "IsCrystal"));
                w.Bool(GpuReflect.B(r, "ThresholdPair"));
                w.Real(GpuReflect.D(r, "PhotoScale"));
                w.Bool(regBox[i]);
                w.Real(regZMinE[i]);
                w.Real(regZMaxE[i]);
                w.Real(regROutE[i]);
                w.Real(regRInE[i]);
                w.Real(regAXE[i]);
                w.Real(regAYE[i]);
                w.End();
            }

            // --- scene (SceneG) ---
            object crystal = GpuReflect.Field(sim, "crystal");
            int crystalIndex = Array.FindIndex(regions, r => ReferenceEquals(r, crystal));
            if (crystalIndex < 0)
            {
                throw new InvalidOperationException("GPU-упаковка: кристалла нет среди областей сцены");
            }

            var geometry = GpuReflect.Get<GeometryModel>(sim, "geometry");
            object source = GpuReflect.Field(sim, "source");
            int kind = SourceKindOf(source);

            w.Tag("scene", 1);
            w.Int(crystalIndex);
            w.Int(reg.Of("materials", geometry.Crystal));
            w.Real(GpuReflect.D(sim, "sphereZ"));
            w.Real(GpuReflect.D(sim, "sphereR"));
            w.Real(GpuReflect.D(sim, "pathSceneZ"));
            w.Real(GpuReflect.D(sim, "pathSceneR"));
            w.Real(GpuReflect.D(sim, "sceneRMax"));
            w.Real(GpuReflect.D(sim, "sceneZMin"));
            w.Real(GpuReflect.D(sim, "sceneZMax"));
            w.Int(kind);
            // Поля всех четырёх видов пишутся всегда (нули у чужих) — запись постоянной длины.
            w.Real(kind == SourcePoint ? GpuReflect.D(source, "z") : 0.0);
            w.Real(kind == SourceCylinder ? GpuReflect.D(source, "r") : 0.0);
            w.Real(kind == SourceCylinder ? GpuReflect.D(source, "z0") : 0.0);
            w.Real(kind == SourceCylinder ? GpuReflect.D(source, "z1") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "rIn") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "rOut") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "z0") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "z1") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "zCap") : 0.0);
            w.Real(kind == SourceMarinelli ? GpuReflect.D(source, "capFraction") : 0.0);
            w.Real(kind == SourceBox ? GpuReflect.D(source, "ax") : 0.0);
            w.Real(kind == SourceBox ? GpuReflect.D(source, "ay") : 0.0);
            w.Real(kind == SourceBox ? GpuReflect.D(source, "z0") : 0.0);
            w.Real(kind == SourceBox ? GpuReflect.D(source, "z1") : 0.0);
            w.Int(reg.Of("electronMats", GpuReflect.Field(sim, "electron")));
            w.Int(reg.Of("brems", GpuReflect.Field(sim, "bremTable")));
            w.Int(reg.Of("lightYields", GpuReflect.Field(sim, "lightYield")));
            w.Int(reg.Of("electronMats", GpuReflect.Call(sim, "WaterTable")));
            w.Bool(GpuReflect.B(sim, "crystalHasPartials"));
            w.Real((double)GpuReflect.Call(sim, "CrystalRadiationLength"));
            w.End();
        }
    }
}
