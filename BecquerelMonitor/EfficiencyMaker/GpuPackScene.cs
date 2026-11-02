using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    // ⚡ GPU-путь матрицы отклика (`AMBER160`, полоса П221, часть Б): разделы упаковки
    // СЦЕНЫ — вещества (с кэшами симулятора по веществу), флуоресценты, рассеиватели,
    // элементы слоёв, области, сцена и источник. Читатель — `tools/effmaker/gpu/host_scene.h`,
    // структуры — `tools/effmaker/gpu/data_scene.cuh`. Писатель и реестр — `GpuPack.cs`.
    //
    // ⛔ Прямой доступ (`AMBER219`, П245-R; решение Amber 07.10.2026 «Перевести на прямой
    // доступ сейчас»): поля, методы и вложенные типы `EfficiencySimulator` (`Region`,
    // `Fluorescers`, `Scatterers`, `ScatterElement`, источники `*Sampler`) открыты как
    // `internal`, тела не тронуты. Кэши по веществу (`FluorescersOf`, `ScatterersOf`,
    // `CarryMedium`, `LayerBrem`, `LayerRadiationLength`, `LayerScatterElements`,
    // `LayerBremZ`) строит ТОТ ЖЕ код симулятора — здесь он только вынуждается заранее, по
    // каждому веществу сцены, и его ответы переписываются как есть.
    //
    // Порядок: всё, что попадает в реестр (`ctx.Reg.Add`), регистрируется в `CollectScene`
    // — списки `electronMats`/`brems`/`lightYields` пишет полоса А (`GpuPackTables.cs`)
    // ДО разделов сцены, в порядке реестра; `WriteScene` только спрашивает `ctx.Reg.Of`.

    public static partial class GpuPack
    {
        // = RM_SOURCE_* (data_scene.cuh).
        const int SourcePoint = 0, SourceCylinder = 1, SourceMarinelli = 2, SourceBox = 3;

        /// <summary>Области сцены в порядке поиска (`regionArray`, «первая победившая»).</summary>
        static EfficiencySimulator.Region[] SceneRegions(EfficiencySimulator sim)
        {
            EfficiencySimulator.Region[] array = sim.regionArray;
            if (array == null)
            {
                throw new InvalidOperationException(
                    "GPU-упаковка: сцена симулятора не собрана (regionArray = null) — EnsureBuilt не звался");
            }

            var list = new EfficiencySimulator.Region[array.Length];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = array[i];
            }

            return list;
        }

        /// <summary>
        /// Вид источника (= RM_SOURCE_*); ISO, важностный и незнакомый — ОТКАЗ: их
        /// розыгрыш на GPU не перенесён, и тихой подмены равномерным быть не должно.
        /// </summary>
        static int SourceKindOf(EfficiencySimulator.Sampler source)
        {
            if (source == null)
            {
                throw new InvalidOperationException("GPU-упаковка: у сцены нет источника (source = null)");
            }

            switch (source)
            {
                case EfficiencySimulator.PointSampler _: return SourcePoint;
                case EfficiencySimulator.CylinderSampler _: return SourceCylinder;
                case EfficiencySimulator.MarinelliSampler _: return SourceMarinelli;
                case EfficiencySimulator.BoxSampler _: return SourceBox;
                case EfficiencySimulator.ImportanceSampler _:
                    throw new NotSupportedException(
                        "GPU-путь: важностный розыгрыш точки вылета (ImportanceSampler, ключ --imp=1) не перенесён; "
                        + "считать эту сцену на ЦП");
                case EfficiencySimulator.IsoFieldSampler _:
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
            SourceKindOf(sim.source);

            foreach (EfficiencySimulator.Region region in SceneRegions(sim))
            {
                reg.Add("materials", region.Material);
            }

            // Объекты сцены, на которые ссылается `SceneG` (EfficiencySimulator.cs:1647-1657, 7683).
            reg.Add("electronMats", sim.electron);
            reg.Add("electronMats", sim.WaterTable());
            // (`AMBER274`) Таблицы ESTAR капсулы точечного источника: кольцо и плёнка
            // (`CapsuleExit` — тем же `CarryMedium`, что у симулятора).
            GeometryMaterial ring, film;
            CapsuleMaterials(sim, out ring, out film);
            if (ring != null)
            {
                reg.Add("electronMats", sim.CarryMedium(ring));
            }

            if (film != null)
            {
                reg.Add("electronMats", sim.CarryMedium(film));
            }

            reg.Add("brems", sim.bremTable);
            reg.Add("lightYields", sim.lightYield);

            // Кэши по веществу — вынудить построение тем же кодом симулятора.
            var materials = new List<object>(reg.List("materials"));
            foreach (object o in materials)
            {
                var m = (GeometryMaterial)o;
                foreach (int z in m.Fractions.Keys)
                {
                    ctx.Zs.Add(z);
                }

                reg.Add("fluorescers", sim.FluorescersOf(m));
                reg.Add("scatterers", sim.ScatterersOf(m));
                reg.Add("electronMats", sim.CarryMedium(m));
                reg.Add("brems", sim.LayerBrem(m));
                sim.LayerRadiationLength(m);
                EfficiencySimulator.ScatterElement[] elements = sim.LayerScatterElements(m);
                for (int i = 0; i < elements.Length; i++)
                {
                    reg.Add("scatterElements", elements[i]);
                }

                sim.LayerBremZ(m);
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
                w.Int(reg.Of("fluorescers", sim.FluorescersOf(m)));
                w.Int(reg.Of("scatterers", sim.ScatterersOf(m)));
                w.Int(reg.Of("electronMats", sim.CarryMedium(m)));
                w.Int(reg.Of("brems", sim.LayerBrem(m)));
                w.Real(sim.LayerRadiationLength(m));
                EfficiencySimulator.ScatterElement[] elements = sim.LayerScatterElements(m);
                int start = elements.Length == 0 ? 0 : reg.Of("scatterElements", elements[0]);
                for (int i = 0; i < elements.Length; i++)
                {
                    if (reg.Of("scatterElements", elements[i]) != start + i)
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: элементы слоя вещества «" + m.Name + "» легли в реестр не подряд");
                    }
                }

                w.Int(start);
                w.Int(elements.Length);
                w.Real(sim.LayerBremZ(m));
                w.End();
            }

            // --- fluorescers (FluorescersG) ---
            IList<object> fluorescers = reg.List("fluorescers");
            w.Tag("fluorescers", fluorescers.Count);
            foreach (object o in fluorescers)
            {
                var f = (EfficiencySimulator.Fluorescers)o;
                int[] z = f.Z;
                double[] fraction = f.Fraction;
                MaterialDatabase.Fluorescence[] data = f.Data;
                MaterialDatabase.PhotoShellModel[] shells = f.Shells;
                var hasShell = new bool[z.Length];
                for (int i = 0; i < z.Length; i++)
                {
                    // Читатель берёт индексы по Z из таблиц полосы А; убедиться, что объект
                    // симулятора — тот же, что отдаёт поиск по Z.
                    if (!ReferenceEquals(data[i], MaterialDatabase.FluorescenceOf(z[i])))
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: Fluorescers.Data[" + i + "] не совпал с FluorescenceOf(" + z[i] + ")");
                    }

                    object shell = shells[i];
                    hasShell[i] = shell != null;
                    if (shell != null && !ReferenceEquals(shell, MaterialDatabase.PhotoShellOf(z[i])))
                    {
                        throw new InvalidOperationException(
                            "GPU-упаковка: Fluorescers.Shells[" + i + "] не совпал с PhotoShellOf(" + z[i] + ")");
                    }
                }

                w.Int(reg.Of("materials", f.Material));
                w.Ints(z);
                w.Reals(fraction);
                w.Bytes(hasShell);
                w.End();
            }

            // --- scatterers (ScatterersG) ---
            IList<object> scatterers = reg.List("scatterers");
            w.Tag("scatterers", scatterers.Count);
            foreach (object o in scatterers)
            {
                var s = (EfficiencySimulator.Scatterers)o;
                int[] z = s.Z;
                double[] mass = s.MassFraction;
                ScatteringData.Atom[] atoms = s.Atom;
                for (int i = 0; i < z.Length; i++)
                {
                    if (!ReferenceEquals(atoms[i], ScatteringData.Of(z[i])))
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
            foreach (object o in scatterElements)
            {
                var e = (EfficiencySimulator.ScatterElement)o;
                w.Int(e.Z);
                w.Real(e.AtomsPerCm3);
                w.Real(e.Z13);
                w.Real(e.ZZ1);
                w.Bool(e.Mott);
                // (`AMBER274`, физика 29, `phys29b`) Поправка Мотта при Z > 30: признак и
                // 30 коэффициентов R_eff (нули без признака — запись постоянной длины).
                w.Bool(e.MottFit);
                for (int k = 0; k < 30; k++)
                {
                    w.Real(e.MottFit ? e.MottCoef[k] : 0.0);
                }

                // (`AMBER274`, физика 30, `phys29c`) Часть множителя Z(Z+1) и узлы DPWA ядерной части (e⁻,
                // e⁺): по 10 чисел на узел — ln T, ln a, c0…c4, жёсткое, мягкое, мажоранта (те же, что
                // `DpwaElement`), и (n − 1)/(ln T_n − ln T_1). Нет узлов — пустой массив и ноль.
                w.Int(e.Part);
                w.Reals(DpwaNodesFlat(e.DpwaEl));
                w.Real(e.DpwaEl != null ? e.DpwaEl.InvStep : 0.0);
                w.Reals(DpwaNodesFlat(e.DpwaPos));
                w.Real(e.DpwaPos != null ? e.DpwaPos.InvStep : 0.0);
                w.End();
            }

            // --- regions (RegionG) ---
            EfficiencySimulator.Region[] regions = SceneRegions(sim);
            bool[] regBox = sim.regBox;
            double[] regZMinE = sim.regZMinE;
            double[] regZMaxE = sim.regZMaxE;
            double[] regROutE = sim.regROutE;
            double[] regRInE = sim.regRInE;
            double[] regAXE = sim.regAXE;
            double[] regAYE = sim.regAYE;
            w.Tag("regions", regions.Length);
            for (int i = 0; i < regions.Length; i++)
            {
                EfficiencySimulator.Region r = regions[i];
                w.Bool(r.IsBox);
                w.Real(r.RIn);
                w.Real(r.ROut);
                w.Real(r.AX);
                w.Real(r.AY);
                w.Real(r.ZMin);
                w.Real(r.ZMax);
                w.Int(reg.Of("materials", r.Material));
                w.Bool(r.IsCrystal);
                w.Bool(r.ThresholdPair);
                w.Real(r.PhotoScale);
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
            EfficiencySimulator.Region crystal = sim.crystal;
            int crystalIndex = Array.FindIndex(regions, r => ReferenceEquals(r, crystal));
            if (crystalIndex < 0)
            {
                throw new InvalidOperationException("GPU-упаковка: кристалла нет среди областей сцены");
            }

            GeometryModel geometry = sim.geometry;
            EfficiencySimulator.Sampler source = sim.source;
            int kind = SourceKindOf(source);
            var point = source as EfficiencySimulator.PointSampler;
            var cylinder = source as EfficiencySimulator.CylinderSampler;
            var marinelli = source as EfficiencySimulator.MarinelliSampler;
            var box = source as EfficiencySimulator.BoxSampler;

            w.Tag("scene", 1);
            w.Int(crystalIndex);
            w.Int(reg.Of("materials", geometry.Crystal));
            w.Real(sim.sphereZ);
            w.Real(sim.sphereR);
            w.Real(sim.pathSceneZ);
            w.Real(sim.pathSceneR);
            w.Real(sim.sceneRMax);
            w.Real(sim.sceneZMin);
            w.Real(sim.sceneZMax);
            w.Int(kind);
            // Поля всех четырёх видов пишутся всегда (нули у чужих) — запись постоянной длины.
            w.Real(kind == SourcePoint ? point.z : 0.0);
            w.Real(kind == SourceCylinder ? cylinder.r : 0.0);
            w.Real(kind == SourceCylinder ? cylinder.z0 : 0.0);
            w.Real(kind == SourceCylinder ? cylinder.z1 : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.rIn : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.rOut : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.z0 : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.z1 : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.zCap : 0.0);
            w.Real(kind == SourceMarinelli ? marinelli.capFraction : 0.0);
            w.Real(kind == SourceBox ? box.ax : 0.0);
            w.Real(kind == SourceBox ? box.ay : 0.0);
            w.Real(kind == SourceBox ? box.z0 : 0.0);
            w.Real(kind == SourceBox ? box.z1 : 0.0);
            w.Int(reg.Of("electronMats", sim.electron));
            w.Int(reg.Of("brems", sim.bremTable));
            w.Int(reg.Of("lightYields", sim.lightYield));
            w.Int(reg.Of("electronMats", sim.WaterTable()));
            w.Bool(sim.crystalHasPartials);
            w.Real(sim.CrystalRadiationLength());
            // (`AMBER274`, Э1 + физика 28) Капсула точечного источника и воздух до корпуса —
            // те же поля, что читает `EfficiencySimulator.CapsuleExit`.
            GeometryMaterial ringMaterial, filmMaterial;
            bool capsulePoint = CapsuleMaterials(sim, out ringMaterial, out filmMaterial);
            w.Bool(capsulePoint);
            w.Real(capsulePoint ? 0.5 * geometry.SourceCapsuleRingHeight : 0.0);
            w.Real(capsulePoint ? 0.5 * geometry.SourceCapsuleRingInnerDiameter : 0.0);
            w.Real(capsulePoint ? 0.5 * geometry.SourceCapsuleRingOuterDiameter : 0.0);
            w.Int(ringMaterial != null ? reg.Of("electronMats", sim.CarryMedium(ringMaterial)) : -1);
            w.Real(ringMaterial != null ? ringMaterial.Density : 0.0);
            w.Real(capsulePoint ? geometry.SourceCapsuleThickness : 0.0);
            w.Int(filmMaterial != null ? reg.Of("electronMats", sim.CarryMedium(filmMaterial)) : -1);
            w.Real(filmMaterial != null ? filmMaterial.Density : 0.0);
            w.Real(sim.pointAirCm);
            w.Bool(sim.CapsuleAir);
            w.End();
        }

        /// <summary>
        /// (`AMBER274`) Вещества капсулы точечного источника, которые прочтёт
        /// <see cref="EfficiencySimulator.CapsuleExit"/>: кольцо (Al ОСГИ) — когда у сцены есть
        /// кольцо, плёнка — когда у неё есть толщина и плотность. Возвращает, точечная ли сцена
        /// без поля (у прочих капсулы нет вовсе).
        /// </summary>
        static bool CapsuleMaterials(EfficiencySimulator sim, out GeometryMaterial ring, out GeometryMaterial film)
        {
            ring = null;
            film = null;
            GeometryModel g = sim.geometry;
            if (g == null || g.SourceType != GeometrySourceType.Point || g.Scene != GeometrySceneKind.None)
            {
                return false;
            }

            if (g.SourceCapsuleRingHeight > 0.0 && g.SourceCapsuleRingInnerDiameter > 0.0
                && g.SourceCapsuleRingOuterDiameter > g.SourceCapsuleRingInnerDiameter)
            {
                ring = sim.CapsuleRingMaterial();
            }

            if (g.SourceCapsuleThickness > 0.0 && g.SourceCapsule != null && g.SourceCapsule.Density > 0.0)
            {
                film = g.SourceCapsule;
            }

            return true;
        }

        /// <summary>
        /// (`AMBER274`, физика 30, Ф1) Узлы DPWA элемента плоским массивом для ядра: по 10 чисел на узел —
        /// ln T, ln a, c0…c4, жёсткое, мягкое, мажоранта (`ScatterElementG.DpwaEl`, sim_electron.cuh
        /// `RefinedElasticStep`/`RefinedAngle`). null — узлов нет (ключ DPWA выключен или часть e–e).
        /// </summary>
        static double[] DpwaNodesFlat(EfficiencySimulator.DpwaElement d)
        {
            if (d == null)
            {
                return null;
            }

            int n = d.LnT.Length;
            var flat = new double[10 * n];
            for (int k = 0; k < n; k++)
            {
                int o = 10 * k;
                flat[o] = d.LnT[k];
                flat[o + 1] = d.LnA[k];
                for (int j = 0; j < 5; j++)
                {
                    flat[o + 2 + j] = d.C[5 * k + j];
                }

                flat[o + 7] = d.Hard[k];
                flat[o + 8] = d.Soft[k];
                flat[o + 9] = d.Major[k];
            }

            return flat;
        }
    }
}
