using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace CrystalOwnActivityProbeA274


{
    /// <summary>
    /// (`AMBER274`, этап Э2, 04.11.2026) ПРИЁМКА СЛОЯ «СОБСТВЕННАЯ АКТИВНОСТЬ КРИСТАЛЛА».
    ///
    /// Что проверяется числом:
    ///
    ///   1. ФИЗИКА СЛОЯ на сцене 38×38: лантановый кристалл (`LABR_BRIL` — LaBr3:Ce,
    ///      `AMBER289`). Активность La-138 из распространённости, периода и природной
    ///      атомной массы, LaBr3:5%Ce (ждём 1.4535 Бк/см³), сумма 1436 + K Ba, линия
    ///      788.7 погашена каскадом, у Lu-176 — 307/202 кэВ, у ряда Ac-227 — бета
    ///      без альф, по одному распаду члена на распад головы;
    ///   2. РАЗБОР на спектрах корпуса `LABR_BRIL` (`LaBrBril_Th228`, `LaBrBril_Eu152`):
    ///      χ²/ndf, активность пробы и отсчёты слоя ДО и ПОСЛЕ;
    ///   3. ПУСТОЙ ФОН: синтетический спектр из одного слоя — слой включён, проба
    ///      должна выйти нулевой; выключен — активность уезжает в K-40 и прочих;
    ///   4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: NaI (своей активности нет) — побитово прежний
    ///      разбор, слой не берётся вовсе;
    ///   5. КЛЮЧ (`--mode=key`): выключен умолчанием — раздел не считается и не читается,
    ///      файл матрицы и разбор побитно прежние, включён — слой есть;
    ///   7. ПОРОГ (`--mode=floor`): одиночные отсчёты в пустых каналах образ слоя не включают;
    ///   8. МАТЕРИАЛ (`--mode=material`): чем стал бы цериевый состав в библиотеке для переноса;
    ///   6. ЧАСТИ (`--mode=parts`, 06.11.2026): отключение La-138 β, 788.7, 1436, Ac-227,
    ///      релаксация дочернего атома, свободная амплитуда против массы кристалла,
    ///      χ² Пуассона по полосам, сырой спектр без вычитания фона; `--inject=k` —
    ///      положительный контроль: в данные подмешан слой с множителем k.
    ///
    ///     CrystalOwnActivityProbeA274 --spectrum=... [--background=...] [--mode=all]
    ///                                 [--histories=100000] [--matrix=build|file]
    ///                                 [--nai=...] [--use-background] [--subtract-embedded]
    ///                                 [--mnodes=16] [--mhist=20000] [--cache=каталог]
    /// </summary>
    static class Program
    {
        static int bad;
        static string matrixPath;
        static string dumpPath;
        static readonly List<string> chains = new List<string>();
        static readonly List<string> nuclides = new List<string>();
        static int matrixNodes = 16;
        static int matrixHistories = 20000;
        static string cacheDir;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика.
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            string backgroundPath = null;
            string naiPath = null;
            string mode = "all";
            int histories = 100000;
            double ownScale = 1.0;
            string matrixMode = "build";
            string sceneName = "labr";
            double sceneHeight = 38.0;
            bool useBackground = false;
            bool subtractEmbedded = false;
            double inject = 0.0;
            foreach (string a in args)
            {
                if (a == "--use-background")
                {
                    useBackground = true;
                    continue;
                }

                if (a == "--subtract-embedded")
                {
                    subtractEmbedded = true;
                    continue;
                }

                if (a.StartsWith("--inject=", StringComparison.Ordinal))
                {
                    inject = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--mnodes=", StringComparison.Ordinal))
                {
                    matrixNodes = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--mhist=", StringComparison.Ordinal))
                {
                    matrixHistories = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                    continue;
                }

                if (a.StartsWith("--cache=", StringComparison.Ordinal))
                {
                    cacheDir = a.Substring(8);
                    continue;
                }

                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else if (a.StartsWith("--background=", StringComparison.Ordinal))
                {
                    backgroundPath = a.Substring(13);
                }
                else if (a.StartsWith("--nai=", StringComparison.Ordinal))
                {
                    naiPath = a.Substring(6);
                }
                else if (a.StartsWith("--mode=", StringComparison.Ordinal))
                {
                    mode = a.Substring(7);
                }
                else if (a.StartsWith("--histories=", StringComparison.Ordinal))
                {
                    histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--matrix=", StringComparison.Ordinal))
                {
                    matrixMode = a.Substring(9);
                }
                else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    dumpPath = a.Substring(7);
                }
                else if (a.StartsWith("--scene=", StringComparison.Ordinal))
                {
                    sceneName = a.Substring(8);
                }
                else if (a.StartsWith("--height=", StringComparison.Ordinal))
                {
                    sceneHeight = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--own-scale=", StringComparison.Ordinal))
                {
                    ownScale = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--chains=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Trim().Length > 0) chains.Add(part.Trim());
                    }
                }
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Trim().Length > 0) nuclides.Add(part.Trim());
                    }
                }
                else
                {
                    Console.Error.WriteLine("непонятный ключ: " + a);
                    return 2;
                }
            }

            // (`AMBER274`, Э2, 05.11.2026, `crit12`) ПРОБА НЕ ЗАДАНА — БЕРЁМ Cs-137.
            // Иначе NaI-контроль as shipped отказывает `LibraryEmptied`: пустой
            // список пробы даёт пустую библиотеку, и контроль нечего сравнивать.
            // Своя проба — `--nuclides=<nucid>` или `--chains=<метка>`.
            if (chains.Count == 0 && nuclides.Count == 0)
            {
                nuclides.Add("137CS");
                Console.WriteLine("проба не задана: беру --nuclides=137CS (умолчание приёмки);"
                                  + " своя — --nuclides=... или --chains=...");
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            GeometryModel lanthanum = string.Equals(sceneName, "cebr", StringComparison.OrdinalIgnoreCase)
                ? CeriumScene(sceneHeight)
                : string.Equals(sceneName, "lu", StringComparison.OrdinalIgnoreCase)
                    ? LutetiumScene(sceneHeight)
                    : LanthanumScene(sceneHeight);
            Console.WriteLine("=== Сцена ===");
            Console.WriteLine("кристалл: {0} Ø{1:F1}×{2:F1} мм, плотность {3:F2} г/см³, объём {4:F2} см³",
                              lanthanum.Crystal.Name, lanthanum.CrystalDiameter, lanthanum.CrystalHeight,
                              lanthanum.Crystal.Density, GeometryScenes.CrystalVolumeCm3(lanthanum));
            foreach (int z in lanthanum.Crystal.Fractions.Keys)
            {
                Console.WriteLine("  Z={0}: {1}", z, lanthanum.Crystal.Fractions[z]);
            }

            // ---- 1. Физика слоя -----------------------------------------
            ResponseCrystalSection section = null;
            if (mode == "all" || mode == "section")
            {
                section = Section(lanthanum, histories);
                Console.WriteLine();
                Console.WriteLine("=== Слой: физика ===");
                ReportSection(section);
                PhysicsChecks(section);
            }

            if (mode == "section")
            {
                return bad == 0 ? 0 : 1;
            }
            if (mode == "key")
            {
                KeyChecks(lanthanum, histories, spectrumPath, naiPath);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "floor")
            {
                FloorChecks(spectrumPath, lanthanum, histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "material")
            {
                MaterialChecks(lanthanum);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "gpu")
            {
                GpuChecks(lanthanum);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "parts" || mode == "dump")
            {
                ResponseMatrix partsMatrix = Matrix(lanthanum, matrixMode);
                if (mode == "dump")
                {
                    DumpParts(spectrumPath, lanthanum, histories, partsMatrix);
                    return 0;
                }

                Parts(spectrumPath, useBackground, subtractEmbedded, inject, lanthanum, partsMatrix, histories);
                return 0;
            }

            if (mode == "roundtrip")
            {
                RoundTrip(lanthanum, histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }


            // ---- 2. Матрица ---------------------------------------------
            ResponseMatrix matrix = null;
            if (spectrumPath != null)
            {
                if (section == null)
                {
                    section = Section(lanthanum, histories);
                }

                matrix = Matrix(lanthanum, matrixMode);
                if (matrix == null)
                {
                    Console.Error.WriteLine("матрица не собрана");
                    return 2;
                }

                matrix.CrystalSection = section;
                matrix.CrystalSectionDamage = null;
                Console.WriteLine();
                Console.WriteLine("=== Матрица ===");
                Console.WriteLine("узлов {0}, шаг {1:F2} кэВ, физика {2}, раздел слоя: {3}",
                                  matrix.Energies != null ? matrix.Energies.Length : 0, matrix.BinKev,
                                  ResponseMatrix.PhysicsFromStamp(matrix.Stamp),
                                  matrix.CrystalSection != null ? matrix.CrystalSection.Count + " записей" : "нет");
            }

            // ---- 3. Спектры корпуса -------------------------------------
            if (spectrumPath != null && (mode == "all" || mode == "fsa"))
            {
                ResultData rd = Load(spectrumPath);
                EnergySpectrum background = backgroundPath != null ? Load(backgroundPath).EnergySpectrum : null;
                Console.WriteLine();
                Console.WriteLine("=== Разбор: {0} ===", Path.GetFileName(spectrumPath));
                ScaleOwn(section, ownScale);
                RunPair(rd, background, lanthanum, matrix, section);
            }

            // ---- 4. Пустой фон ------------------------------------------
            if (spectrumPath != null && (mode == "all" || mode == "empty"))
            {
                ResultData rd = Load(spectrumPath);
                Console.WriteLine();
                Console.WriteLine("=== Пустой фон: синтетика из одного слоя ===");
                EmptyBackground(rd, lanthanum, matrix, section);
            }

            // ---- 5. Положительный контроль NaI --------------------------
            if (naiPath != null && (mode == "all" || mode == "control"))
            {
                Console.WriteLine();
                Console.WriteLine("=== Положительный контроль: NaI ===");
                NaiControl(naiPath);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Сцена
        // ------------------------------------------------------------------

        /// <summary>
        /// Сцена 38×38 мм в обвязке BrilLanCe 380 (корпус `LABR_BRIL`, LaBr3:Ce — `AMBER289`).
        /// Отражатель, зазор и оболочка — как у прочих сцинтилляторов пресета
        /// (`GeometryPresets`): PTFE 1.0/1.5 мм, воздух, алюминий 1.0/1.0 мм.
        /// </summary>
        static GeometryModel LanthanumScene(double height = 38.0)
        {
            var g = new GeometryModel
            {
                Name = "LaBr3 38x38 (AMBER274)",
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 38.0,
                CrystalHeight = height,
                Crystal = Material("Lanthanum bromide"),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 0.0,
            };
            return g;
        }

        /// <summary>
        /// CeBr3 той же обвязки и размера: у CeBr3 природного La-138 нет, остаётся только
        /// свободная строка Ac-227. Спектра этого вещества в корпусе нет.
        /// </summary>
        static GeometryModel CeriumScene(double height = 38.0)
        {
            var g = new GeometryModel
            {
                Name = "CeBr3 38x38 (AMBER274)",
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 38.0,
                CrystalHeight = height,
                Crystal = Material("Cerium bromide"),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 0.0,
            };
            return g;
        }

        /// <summary>
        /// Лютециевое вещество той же обвязки: проверка физики Lu-176 в разделе. Спектра с
        /// лютециевым кристаллом в корпусе нет: `*_Lu176` — лютеций в пробе, а не в кристалле.
        /// </summary>
        static GeometryModel LutetiumScene(double height = 38.0)
        {
            GeometryModel g = CeriumScene(height);
            g.Name = "Lu2O3 38x38 (AMBER274)";
            g.Crystal = Material("Lutetium oxide");
            return g;
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null
                ? GeometryMaterialLibrary.Make(entry, entry.Density)
                : new GeometryMaterial();
        }

        // ------------------------------------------------------------------
        // Слой
        // ------------------------------------------------------------------

        static ResponseCrystalSection Section(GeometryModel geometry, int histories)
        {
            string note;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, out note);
            watch.Stop();
            Console.WriteLine("слой собран за {0:F1} с, записей {1}{2}",
                              watch.Elapsed.TotalSeconds, section.Count,
                              string.IsNullOrEmpty(note) ? "" : "; " + note);
            return section;
        }

        /// <summary>Рычаг пробы: множитель природной активности слоя (сверка «χ² против амплитуды»).</summary>
        static void ScaleOwn(ResponseCrystalSection section, double scale)
        {
            if (section == null || scale == 1.0)
            {
                return;
            }

            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (!entry.Free)
                {
                    entry.ActivityBq *= scale;
                }
            }

            Console.WriteLine("амплитуда слоя умножена на {0:F3}", scale);
        }

        static void ReportSection(ResponseCrystalSection section)
        {
            Console.WriteLine("{0,-10} {1,-6} {2,10} {3,12} {4,10} {5,10} {6,10}",
                              "нуклид", "слой", "активность", "распадов", "отсч/расп", "сред. E", "макс. бин");
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double sum = 0.0, mean = 0.0;
                var tops = new List<int>();
                for (int i2 = 0; i2 < entry.PerDecay.Length; i2++) if (entry.PerDecay[i2] > 0.0) tops.Add(i2);
                tops.Sort((a, b) => entry.PerDecay[b].CompareTo(entry.PerDecay[a]));
                var sb = new StringBuilder();
                for (int k = 0; k < tops.Count && k < 8; k++) sb.AppendFormat("{0:F0}:{1:F4} ", tops[k] * section.BinKev, entry.PerDecay[tops[k]]);
                Console.WriteLine("   верх: " + sb);
                int top = 0;
                for (int i = 0; i < entry.PerDecay.Length; i++)
                {
                    sum += entry.PerDecay[i];
                    mean += entry.PerDecay[i] * i * section.BinKev;
                    if (entry.PerDecay[i] > entry.PerDecay[top])
                    {
                        top = i;
                    }
                }

                Console.WriteLine("{0,-10} {1,-6} {2,10:F1} {3,12} {4,10:F3} {5,10:F1} {6,10:F1}",
                                  entry.Nuclide, entry.Layer,
                                  entry.Free ? 0.0 : entry.ActivityBq,
                                  entry.Histories.ToString(CultureInfo.InvariantCulture),
                                  sum, sum > 0.0 ? mean / sum : 0.0, top * section.BinKev);
            }
        }

        /// <summary>
        /// Числа физики сборки по умолчанию (с релаксацией дочернего атома, решение Amber
        /// 06.11.2026): La-138 — сумма 1436 + K выше голой линии, K-пик 37 кэВ, линия 788.7
        /// погашена каскадом; Lu-176 — 307 и 202 кэВ.
        /// </summary>
        static void PhysicsChecks(ResponseCrystalSection section)
        {
            CrystalOwnEntry la = section.Find("138LA", "beta");
            if (la != null)
            {
                double at1473 = Band(la, section.BinKev, 1468.0, 1478.0);
                double at1436 = Band(la, section.BinKev, 1431.0, 1441.0);
                double at789 = Band(la, section.BinKev, 786.0, 791.0);
                double at1000 = Band(la, section.BinKev, 800.0, 1060.0);
                Console.WriteLine();
                Console.WriteLine("La-138: 1468–1478 {0:F4} отсч/расп, 1431–1441 {1:F4}, 786–791 {2:F4}, 800–1060 {3:F4}",
                                  at1473, at1436, at789, at1000);
                double atK = Band(la, section.BinKev, 28.0, 42.0);
                Console.WriteLine("La-138: 28–42 {0:F4} отсч/расп", atK);
                Check("захват La-138: сумма 1468–1478 больше голой линии 1431–1441 (K-вакансия 63 % захватов)",
                      at1473 > at1436 && at1436 > 0.005);
                Check("захват La-138: K-пик 28–42 кэВ есть (γ ушла, K поглощён)", atK > 0.2);
                Check("линия 788.7 погашена каскадом (в окне 786–791 меньше 10 % полосы 800–1060)",
                      at789 < 0.10 * at1000);
            }

            CrystalOwnEntry lu = section.Find("176LU", "beta");
            if (lu != null)
            {
                double at307 = Band(lu, section.BinKev, 304.0, 310.0);
                double at202 = Band(lu, section.BinKev, 199.0, 205.0);
                Console.WriteLine("Lu-176: 304–310 {0:F4}, 199–205 {1:F4}", at307, at202);
                Check("Lu-176: линии 307 и 202 кэВ есть", at307 > 0.0 && at202 > 0.0);
            }

            CrystalOwnEntry ac = section.Find("227AC", "beta");
            if (ac != null)
            {
                double total = 0.0, above3000 = 0.0;
                for (int i = 0; i < ac.PerDecay.Length; i++)
                {
                    total += ac.PerDecay[i];
                    if (i * section.BinKev > 3000.0)
                    {
                        above3000 += ac.PerDecay[i];
                    }
                }

                Console.WriteLine("Ac-227: всего {0:F3} отсч/расп, выше 3000 кэВ {1:F4}", total, above3000);
                Check("Ac-227: альф нет — выше 3000 кэВ меньше 0.1 % (хвост каскадных сумм)", above3000 < 0.001 * total);
                Check("Ac-227: сумма на распад головы 0.9862 + 1 + 0.00276 + 0.99724 = 2.986 (вес члена без доли ветви)",
                      Math.Abs(total - 2.986) < 0.01);
            }

            double perGram = 0.0;
            string why;
            GeometryModel scene = LanthanumScene();
            double activity = CrystalOwnActivityBuilder.NaturalActivityBq(scene, "138LA", out why);
            double volume = GeometryScenes.CrystalVolumeCm3(scene);
            perGram = volume > 0.0 ? activity / volume : 0.0;
            Console.WriteLine("La-138 в кристалле: {0:F1} Бк на {1:F2} см³ = {2:F3} Бк/см³",
                              activity, volume, perGram);
            Check("La-138 = 1.4535 Бк/см³ (LaBr3:5%Ce при 5.08, природная масса 138.905)", Math.Abs(perGram - 1.4535) < 0.001);
        }

        static double Band(CrystalOwnEntry entry, double binKev, double lo, double hi)
        {
            double sum = 0.0;
            // Бин i — центр i·шаг: построитель кладёт энергию округлением до ближайшего (`Add`).
            for (int i = 0; i < entry.PerDecay.Length; i++)
            {
                double e = i * binKev;
                if (e >= lo && e <= hi)
                {
                    sum += entry.PerDecay[i];
                }
            }

            return sum;
        }

        // ------------------------------------------------------------------
        // Матрица
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER274`, Э2, 05.11.2026, `crit12`) Имя файла кэша матрицы: вещество
        /// кристалла и его размер. Два разных кристалла не делят один файл.
        /// </summary>
        static string MatrixCacheName(GeometryModel geometry)
        {
            string material = geometry != null && geometry.Crystal != null ? geometry.Crystal.Name : "";
            var sb = new StringBuilder();
            foreach (char c in material)
            {
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            }

            string slug = sb.ToString().Trim('_');
            if (slug.Length == 0)
            {
                slug = "crystal";
            }

            return string.Format(CultureInfo.InvariantCulture, "a274_{0}_{1:F0}x{2:F0}.rmx",
                                 slug, geometry != null ? geometry.CrystalDiameter : 0.0,
                                 geometry != null ? geometry.CrystalHeight : 0.0);
        }

        static ResponseMatrix Matrix(GeometryModel geometry, string mode)
        {
            // (`AMBER274`, Э2, 05.11.2026, `crit12`) КЛЮЧ КЭША — ВЕЩЕСТВО И РАЗМЕР,
            // а не «CeBr3 / всё прочее»: иначе NaI (и любая третья сцена) писала
            // свою матрицу в `a274_labr3.rmx` и затирала лантановую. Ключ прежних
            // файлов (`a274_labr3.rmx`) не читается намеренно — он общий и мог
            // хранить чужую матрицу.
            matrixPath = Path.Combine(cacheDir ?? Path.GetTempPath(), MatrixCacheName(geometry));
            if (mode == "file" && File.Exists(matrixPath))
            {
                MatrixRefusal refusal;
                int format;
                ResponseMatrix loaded = ResponseMatrix.Load(matrixPath, out refusal, out format);
                if (loaded != null && loaded.IsValidFor(geometry))
                {
                    Console.WriteLine("матрица из файла: {0}", matrixPath);
                    return loaded;
                }
            }

            var options = new ResponseMatrixOptions
            {
                NodeCount = matrixNodes,
                Histories = matrixHistories,
                BinKev = 2.0,
                MinEnergyKev = 30.0,
                MaxEnergyKev = 3000.0,
            };

            Console.WriteLine("сборка матрицы: {0} узлов × {1} историй...", options.NodeCount, options.Histories);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null,
                                                                System.Threading.CancellationToken.None);
            watch.Stop();
            Console.WriteLine("матрица собрана за {0:F1} с, тело {1:F1} МБ, раздел слоя: {2}",
                              watch.Elapsed.TotalSeconds, matrix.DataBytes / 1048576.0,
                              matrix.CrystalSection != null ? matrix.CrystalSection.Count + " записей" : "нет");
            try
            {
                matrix.Save(matrixPath);
            }
            catch (Exception error)
            {
                Console.WriteLine("матрица не сохранена: {0}", error.Message);
            }

            return matrix;
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        static void RunPair(ResultData rd, EnergySpectrum background, GeometryModel geometry,
                            ResponseMatrix matrix, ResponseCrystalSection section)
        {
            FsaResult without = Run(rd, background, geometry, matrix, null);
            FsaResult with = Run(rd, background, geometry, matrix, section);
            if (without == null || with == null)
            {
                Console.Error.WriteLine("разбор не прошёл");
                bad++;
                return;
            }

            Console.WriteLine("{0,-22} {1,10} {2,10} {3,10} {4,12}",
                              "строка", "без слоя", "со слоем", "разность", "слой, отсч");
            Console.WriteLine("{0,-22} {1,10:F3} {2,10:F3} {3,10:F3}",
                              "χ²/ndf (хубер)", without.Chi2Ndf, with.Chi2Ndf, with.Chi2Ndf - without.Chi2Ndf);

            // ⛔ (`AMBER274`, Э2, 05.11.2026, `crit8`) МЕТРИКА ПРИЁМКИ —
            // ПУАССОНОВСКИЙ χ² по каналам (1/y), а не χ²/ndf: хьюберовская
            // метрика скрывала невязку −24 912 отсч в полосе 1450–1490.
            // Считается по ПОЛНОЙ модели: без слоя y − model_without, со слоем
            // y − model_with − layer (разбор вычитает слой из данных ДО фита и
            // в Model его не возвращает).
            double[] layer = lastFixed ?? new double[rd.EnergySpectrum.NumberOfChannels];
            double chi0 = 0.0, chi1 = 0.0;
            double chiSeen0 = 0.0, chiSeen1 = 0.0;
            double band0 = 0.0, band1 = 0.0;
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            for (int i = 0; i < rd.EnergySpectrum.NumberOfChannels; i++)
            {
                double y = rd.EnergySpectrum.Spectrum[i];
                double m0 = without.Model[i];
                double m1 = with.Model[i];
                double w = 1.0 / Math.Max(y, 1.0);
                chi0 += (y - m0) * (y - m0) * w;
                chi1 += (y - m1 - layer[i]) * (y - m1 - layer[i]) * w;
                double kev = calibration != null ? calibration.ChannelToEnergy(i) : 0.0;
                if (kev >= BandEdges[BandFromSeen])
                {
                    chiSeen0 += (y - m0) * (y - m0) * w;
                    chiSeen1 += (y - m1 - layer[i]) * (y - m1 - layer[i]) * w;
                }

                if (kev >= 1450.0 && kev < 1490.0)
                {
                    band0 += y - m0;
                    band1 += y - m1 - layer[i];
                }
            }

            // ⛔ (`fixE2`, 06.11.2026) Прежний приговор «χ² со слоем не хуже» судил и каналы НИЖЕ
            // порога прибора, где данных нет, а слой есть: это 90 % «роста в девять раз». Здесь только
            // сведение: слой выключен, таблица частей — `--mode=parts`, суд — критики `AMBER301`.
            Console.WriteLine("χ² Пуассона все каналы: без слоя {0:F1}, со слоем {1:F1}", chi0, chi1);
            Console.WriteLine("χ² Пуассона ≥ 28 кэВ: без слоя {0:F1}, со слоем {1:F1}", chiSeen0, chiSeen1);
            Console.WriteLine("полоса 1450–1490, остаток: без слоя {0:F0}, со слоем {1:F0} отсч", band0, band1);

            if (!string.IsNullOrEmpty(dumpPath))
            {
                using (var writer = new StreamWriter(dumpPath, false, Encoding.UTF8))
                {
                    writer.WriteLine("channel;energy_kev;data;model_without;model_with;layer");
                    for (int i = 0; i < rd.EnergySpectrum.NumberOfChannels; i++)
                    {
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0};{1:F3};{2};{3:F3};{4:F3};{5:F3}",
                                                       i, calibration != null ? calibration.ChannelToEnergy(i) : 0.0,
                                                       rd.EnergySpectrum.Spectrum[i], without.Model[i], with.Model[i], layer[i]));
                    }
                }

                Console.WriteLine("дамп: {0}", dumpPath);
            }

            foreach (FsaComponentResult component in without.Components)
            {
                if (component.Kind == FsaComponentKind.Nuisance || component.CountRate <= 0.0)
                {
                    continue;
                }

                FsaComponentResult paired = Find(with, component.Name);
                if (paired == null)
                {
                    continue;
                }

                Console.WriteLine("{0,-22} {1,10:F2} {2,10:F2} {3,10:F2}",
                                  component.Name, component.CountRate, paired.CountRate,
                                  paired.CountRate - component.CountRate);
            }

            Console.WriteLine();
            Console.WriteLine("слой в разборе: {0}",
                              with.CrystalOwn != null && with.CrystalOwn.Count > 0 ? "взят" : "не взят");
            if (with.CrystalOwn != null)
            {
                foreach (FsaCrystalOwnResult row in with.CrystalOwn)
                {
                    Console.WriteLine("  {0,-8} {1,-6} {2,10:F2} Бк {3,12:F1} отсч{4}",
                                      row.Nuclide, row.Layer, row.ActivityBq, row.Counts,
                                      row.Free ? " (свободная)" : "");
                }
            }

            if (!string.IsNullOrEmpty(with.CrystalOwnNote))
            {
                Console.WriteLine("  примечание: {0}", with.CrystalOwnNote);
            }

            // Каскадное суммирование и привязка шкалы: слой не должен их сдвигать.
            Console.WriteLine("каскад: {0}, привязка шкалы: gain {1:F6} / offset {2:F3} кан. (без слоя {3:F6} / {4:F3})",
                              with.CascadeSummingUsed ? "учтён" : "нет",
                              with.Gain, with.OffsetChannels, without.Gain, without.OffsetChannels);
            Check("каскадное суммирование не изменилось слоем", with.CascadeSummingUsed == without.CascadeSummingUsed);
            Check("привязка шкалы не сдвинута слоем",
                  Math.Abs(with.Gain - without.Gain) < 1e-9
                  && Math.Abs(with.OffsetChannels - without.OffsetChannels) < 1e-9);
        }

        /// <summary>
        /// ПУСТОЙ ФОН: данные = один слой (своя активность прибора), проба пустая.
        /// Со слоем разбор обязан сказать «пробы нет», без слоя — приписать её K-40.
        /// </summary>
        static void EmptyBackground(ResultData rd, GeometryModel geometry, ResponseMatrix matrix,
                                    ResponseCrystalSection section)
        {
            EnergySpectrum source = rd.EnergySpectrum;
            int channels = source.NumberOfChannels;
            var counts = new double[channels];
            double live = source.EffectiveLiveTime > 0.0 ? source.EffectiveLiveTime : 1.0;

            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (entry.Free || !(entry.ActivityBq > 0.0))
                {
                    continue;
                }

                for (int i = 0; i < entry.PerDecay.Length; i++)
                {
                    double weight = entry.PerDecay[i] * entry.ActivityBq * live;
                    if (weight <= 0.0)
                    {
                        continue;
                    }

                    // Уширение тем же правилом, что у разбора: гистограмма слоя живёт на
                    // сетке матрицы (2 кэВ), а шкала прибора — своя. Без уширения
                    // синтетика «острее» модели, и χ² меряет не слой, а эту разницу.
                    Spread(counts, source.EnergyCalibration, rd.FwhmCalibration,
                           i * section.BinKev, weight, channels);
                }
            }

            var synthetic = (EnergySpectrum)source.Clone();
            synthetic.Spectrum = new int[channels];
            long total = 0;
            for (int i = 0; i < channels; i++)
            {
                synthetic.Spectrum[i] = (int)Math.Round(counts[i]);
                total += synthetic.Spectrum[i];
            }

            synthetic.TotalPulseCount = total;
            synthetic.ValidPulseCount = total;
            var copy = (ResultData)rd.Clone();
            copy.EnergySpectrum = synthetic;

            Console.WriteLine("синтетика: {0} отсч. в {1} каналах за {2:F0} с", total, channels, live);
            if (total <= 0)
            {
                bad++;
                Console.WriteLine("НЕ СОШЛОСЬ: слой пуст");
                return;
            }

            FsaResult without = Run(copy, null, geometry, matrix, null);
            FsaResult with = Run(copy, null, geometry, matrix, section);
            if (without == null || with == null)
            {
                bad++;
                return;
            }

            Console.WriteLine("{0,-24} {1,12} {2,12}", "нуклид", "без слоя, Бк", "со слоем, Бк");
            foreach (FsaComponentResult component in without.Components)
            {
                if (component.Kind == FsaComponentKind.Nuisance || component.CountRate <= 0.0)
                {
                    continue;
                }

                FsaComponentResult paired = Find(with, component.Name);
                Console.WriteLine("{0,-24} {1,12:F2} {2,12:F2}",
                                  component.Name, component.CountRate / live,
                                  paired != null ? paired.CountRate / live : 0.0);
            }

            Console.WriteLine("χ²/ndf: без слоя {0:F3}, со слоем {1:F3}", without.Chi2Ndf, with.Chi2Ndf);
            double sumWithout = 0.0, sumWith = 0.0;
            foreach (FsaComponentResult component in without.Components)
            {
                if (component.Kind != FsaComponentKind.Nuisance)
                {
                    sumWithout += component.CountRate;
                }
            }

            foreach (FsaComponentResult component in with.Components)
            {
                if (component.Kind != FsaComponentKind.Nuisance)
                {
                    sumWith += component.CountRate;
                }
            }

            Console.WriteLine("активность пробы (сумма скоростей счёта): без слоя {0:F2}, со слоем {1:F2} имп/с",
                              sumWithout, sumWith);
            Check("со слоем проба пустая (скорость счёта пробы упала не меньше чем в 10 раз)",
                  sumWith < 0.1 * sumWithout);
            Check("со слоем χ²/ndf не хуже 3", with.Chi2Ndf < 3.0);
        }

        /// <summary>
        /// (`AMBER274`, Э2) РАЗДЕЛ ЧЕРЕЗ ФАЙЛ: запись `CWNA` перед `ELSC`, чтение от конца.
        /// Раздел обязан вернуться побитово, а у сцены без собственной активности
        /// (NaI) — не появиться вовсе: иначе старые матрицы поменялись бы молча.
        /// </summary>
        static void RoundTrip(GeometryModel geometry, int histories)
        {
            string note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, out note);
            ResponseMatrix matrix = Matrix(geometry, "build");
            if (matrix == null)
            {
                bad++;
                return;
            }

            matrix.CrystalSection = section;
            matrix.Save(matrixPath);
            MatrixRefusal refusal;
            int format;
            ResponseMatrix loaded = ResponseMatrix.Load(matrixPath, out refusal, out format);
            Check("матрица с разделом читается", loaded != null && refusal == MatrixRefusal.None);
            if (loaded == null)
            {
                return;
            }

            Check("раздел CWNA прочитан из файла",
                  loaded.CrystalSection != null && loaded.CrystalSection.Count == section.Count);
            if (loaded.CrystalSection == null)
            {
                return;
            }

            bool same = string.Equals(loaded.CrystalSection.Stamp, section.Stamp, StringComparison.Ordinal);
            foreach (CrystalOwnEntry a in section.Entries)
            {
                CrystalOwnEntry b = loaded.CrystalSection.Find(a.Nuclide, a.Layer);
                same &= b != null && b.Free == a.Free && Math.Abs(b.ActivityBq - a.ActivityBq) == 0.0
                        && Same(a.PerDecay, b.PerDecay);
            }

            Check("клеймо и гистограммы раздела побитово те же", same);

            GeometryModel nai = NaiScene();
            ResponseMatrix naiMatrix = Matrix(nai, "build");
            Check("у NaI раздела нет — файл прежний", naiMatrix != null && naiMatrix.CrystalSection == null);
        }

        /// <summary>
        /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: у NaI собственной активности нет — разбор с пустым
        /// разделом и без раздела обязан совпасть побитово (модель, компоненты, χ²).
        /// </summary>
        static void NaiControl(string path)
        {
            ResultData rd = Load(path);
            GeometryModel nai = NaiScene();
            ResponseMatrix matrix = Matrix(nai, "file");
            if (matrix == null)
            {
                bad++;
                return;
            }

            Check("у NaI раздела собственной активности нет", matrix.CrystalSection == null);
            if (matrix.CrystalSection != null)
            {
                return;
            }

            FsaResult plain = Run(rd, null, nai, matrix, null);
            var empty = new ResponseCrystalSection
            {
                Stamp = ResponseCrystalSection.ComputeStamp(nai, matrix.BinKev),
                BinKev = matrix.BinKev,
            };
            FsaResult control = Run(rd, null, nai, matrix, empty);
            if (plain == null || control == null)
            {
                bad++;
                return;
            }

            bool same = Math.Abs(plain.Chi2Ndf - control.Chi2Ndf) == 0.0
                        && Same(plain.Model, control.Model)
                        && plain.Components.Count == control.Components.Count;
            for (int i = 0; same && i < plain.Components.Count; i++)
            {
                same = string.Equals(plain.Components[i].Name, control.Components[i].Name, StringComparison.Ordinal)
                       && Same(plain.Components[i].Curve, control.Components[i].Curve);
            }

            Console.WriteLine("NaI: χ²/ndf {0:F6} против {1:F6}, компонентов {2}/{3}",
                              plain.Chi2Ndf, control.Chi2Ndf, plain.Components.Count, control.Components.Count);
            Check("NaI: разбор побитово прежний (слой не берётся)", same);
        }

        static GeometryModel NaiScene()
        {
            var g = new GeometryModel
            {
                Name = "NaI 63x63 (AMBER274 control)",
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 63.0,
                CrystalHeight = 63.0,
                Crystal = Material("Sodium iodide"),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                MountingThickness = 0.0,
            };
            return g;
        }

        /// <summary>Отсчёты слоя, которые разбор вычел на самом деле (не пересчёт рядом).</summary>
        static double[] lastFixed;

        /// <summary>Плечо замера: свободная колонка без отсева по значимости.</summary>
        static bool noGate;

        static FsaResult Run(ResultData rd, EnergySpectrum background, GeometryModel geometry,
                             ResponseMatrix matrix, ResponseCrystalSection section)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            // Проба идёт по матрице без кривой эффективности: сцену она задаёт сама,
            // и требование «кривая с геометрией» (RequireGeometry) к ней не относится.
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = section != null;
            if (noGate)
            {
                // Свободная колонка без отсева по значимости: что выбирает сам фит.
                analyzer.RefitZ = 0.0;
                analyzer.RefitZRelative = 0.0;
            }

            FsaTuningReport.Print(analyzer, "слой собственной активности");
            if (matrix != null)
            {
                FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            }

            analyzer.CrystalOwnSection = section;

            // Геометрия сцены — в конфигурацию эффективности: без неё разбор
            // отказывает `Geometry` (кривой нет, а сцена есть — это законно).
            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData { Geometry = geometry };
            }
            else
            {
                rd.Efficiency.Geometry = geometry;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста: задайте пробу ключом --nuclides=<nucid>"
                                        + " (например, --nuclides=137CS) или --chains=<метка>;"
                                        + " список ключей — в шапке пробы");
                return null;
            }

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration, library, efficiency);
            lastFixed = analyzer.CrystalOwnFixedCountsOfLastRun();
            if (result == null)
            {
                Console.Error.WriteLine("отказ разбора: {0} — {1}", analyzer.Refusal, analyzer.RefusalNote);
            }

            return result;
        }

        static FsaComponentResult Find(FsaResult result, string name)
        {
            foreach (FsaComponentResult component in result.Components)
            {
                if (string.Equals(component.Name, name, StringComparison.Ordinal))
                {
                    return component;
                }
            }

            return null;
        }

        static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static int ChannelOf(EnergyCalibration calibration, double energyKev, int channels)
        {
            if (calibration == null)
            {
                return -1;
            }

            double channel = calibration.EnergyToChannel(energyKev, channels);
            int index = (int)Math.Round(channel);
            return index < 0 ? 0 : index >= channels ? channels - 1 : index;
        }

        /// <summary>
        /// Разложить отсчёты одного бина гистограммы слоя по каналам прибора с гауссовым
        /// ядром ПШПВ: синтетика пустого фона обязана быть уширена ТЕМ ЖЕ правилом,
        /// каким разбор уширяет образ слоя, иначе χ² меряет разницу уширений.
        /// </summary>
        static void Spread(double[] counts, EnergyCalibration calibration, FwhmCalibration fwhm,
                           double energyKev, double weight, int channels)
        {
            if (counts == null || calibration == null || !(weight > 0.0))
            {
                return;
            }

            double centre = calibration.EnergyToChannel(energyKev, channels);
            double fwhmChannels = 3.0;
            if (fwhm != null)
            {
                double value = fwhm.ChannelToFwhm(centre);
                if (value > 0.5)
                {
                    fwhmChannels = value;
                }
            }

            double sigma = Math.Max(0.6, fwhmChannels / 2.3548200450309493);
            int lo = Math.Max(0, (int)Math.Floor(centre - 4.0 * sigma));
            int hi = Math.Min(channels - 1, (int)Math.Ceiling(centre + 4.0 * sigma));
            double sum = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                double d = (i - centre) / sigma;
                sum += Math.Exp(-0.5 * d * d);
            }

            if (!(sum > 0.0))
            {
                return;
            }

            for (int i = lo; i <= hi; i++)
            {
                double d = (i - centre) / sigma;
                counts[i] += weight * Math.Exp(-0.5 * d * d) / sum;
            }
        }

        /// <summary>
        /// Отсчёты слоя по каналам гауссовым ядром пробы — для дампа и подмешивания
        /// положительного контроля (не для χ²: χ² берёт то, что вычел разбор, `lastFixed`).
        /// </summary>
        static double[] LayerCounts(ResultData rd, ResponseCrystalSection section)
        {
            EnergySpectrum spectrum = rd.EnergySpectrum;
            var counts = new double[spectrum.NumberOfChannels];
            if (section == null || !(section.BinKev > 0.0))
            {
                return counts;
            }

            double live = spectrum.EffectiveLiveTime > 0.0 ? spectrum.EffectiveLiveTime : 1.0;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (entry == null || entry.Free || !(entry.ActivityBq > 0.0) || entry.PerDecay == null)
                {
                    continue;
                }

                for (int i = 0; i < entry.PerDecay.Length; i++)
                {
                    double weight = entry.PerDecay[i] * entry.ActivityBq * live;
                    if (weight <= 0.0)
                    {
                        continue;
                    }

                    Spread(counts, spectrum.EnergyCalibration, rd.FwhmCalibration,
                           i * section.BinKev, weight, counts.Length);
                }
            }

            return counts;
        }

        // ------------------------------------------------------------------
        // Ключ: выключен — побитно прежнее
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) КЛЮЧ. Построитель раздела не считает и не пишет, файл
        /// матрицы побитно тот, что и без раздела; разбор с разделом в матрице при выключенном
        /// ключе побитно равен разбору без раздела. Явный вход и ключ — раздел есть, строки слоя есть.
        /// Контроль наоборот: включённый ключ обязан разойтись по файлу и по модели.
        /// </summary>
        static void KeyChecks(GeometryModel geometry, int histories, string spectrumPath, string naiPath)
        {
            var off = new ResponseMatrixOptions { NodeCount = 6, Histories = 4000, BinKev = 2.0, MinEnergyKev = 30.0, MaxEnergyKev = 3000.0 };
            Check("умолчание разбора: ключ выключен", !new FsaAnalyzer().CrystalOwnLayer);
            ResponseMatrix mOff = ResponseMatrixBuilder.Build(geometry, off, null, System.Threading.CancellationToken.None);
            ResponseMatrix mOn = ResponseMatrixBuilder.Build(geometry, off, null, System.Threading.CancellationToken.None);
            Check("построитель сам раздела не считает", mOff.CrystalSection == null && mOn.CrystalSection == null);
            ResponseMatrixBuilder.AttachCrystalSection(geometry, mOn);
            Check("явный вход: раздел есть", mOn.CrystalSection != null && mOn.CrystalSection.Count > 0);
            string dir = cacheDir ?? Path.GetTempPath();
            string fOff = Path.Combine(dir, "a274_key_off.rmx"), fOn = Path.Combine(dir, "a274_key_on.rmx"), fStrip = Path.Combine(dir, "a274_key_strip.rmx");
            mOff.Save(fOff);
            mOn.Save(fOn);
            mOn.CrystalSection = null;
            mOn.Save(fStrip);
            string shaOff = Sha(fOff), shaOn = Sha(fOn), shaStrip = Sha(fStrip);
            Console.WriteLine("файл матрицы: выкл {0}, вкл {1}, вкл без раздела {2}", shaOff, shaOn, shaStrip);
            Check("контроль: файл с разделом отличается от файла без него", shaOn != shaStrip);
            byte[] bytes = File.ReadAllBytes(fOff);
            Check("файл при выключенном ключе не несёт метки CWNA", IndexOf(bytes, Encoding.ASCII.GetBytes("CWNA")) < 0);

            MatrixRefusal refusal;
            int format;
            ResponseMatrix read = ResponseMatrix.Load(fOn, out refusal, out format);
            Check("файл с разделом читается, раздел на месте", read != null && read.CrystalSection != null);
            ResponseMatrix readOff = ResponseMatrix.Load(fOff, out refusal, out format);
            Check("файл без раздела читается, раздела нет, порчи нет", readOff != null && readOff.CrystalSection == null && readOff.CrystalSectionDamage == null);

            if (spectrumPath == null)
            {
                return;
            }

            // Разбор: раздел в матрице при выключенном ключе не действует.
            ResultData rd = Load(spectrumPath);
            FsaResult plain = RunKeyed(rd, geometry, readOff, null, false);
            FsaResult sectionOff = RunKeyed(rd, geometry, read, read.CrystalSection, false);
            FsaResult sectionOn = RunKeyed(rd, geometry, read, read.CrystalSection, true);
            bool same = plain != null && sectionOff != null && Same(plain.Model, sectionOff.Model)
                        && BitConverter.DoubleToInt64Bits(plain.Chi2Ndf) == BitConverter.DoubleToInt64Bits(sectionOff.Chi2Ndf);
            Check("разбор: раздел в файле при выключенном ключе — побитно как без раздела", same);
            Check("разбор: при выключенном ключе строк слоя нет", sectionOff != null && (sectionOff.CrystalOwn == null || sectionOff.CrystalOwn.Count == 0));
            Check("контроль: при включённом ключе модель расходится", sectionOn != null && plain != null && !Same(plain.Model, sectionOn.Model));
            Check("контроль: при включённом ключе строки слоя есть", sectionOn != null && sectionOn.CrystalOwn != null && sectionOn.CrystalOwn.Count > 0);
        }

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) ПУТЬ GPU: тот же вход раздела. Без него раздела нет;
        /// с ним — есть, а фотонное тело файла то же (раздел считается на ЦП и фотонных строк не трогает).
        /// </summary>
        static void GpuChecks(GeometryModel geometry)
        {
            var off = new ResponseMatrixOptions { NodeCount = 6, Histories = 20000, BinKev = 2.0, MinEnergyKev = 30.0, MaxEnergyKev = 3000.0, ContinuumErrorTarget = 0.0 };
            using (var gpu = new RmGpu(RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
            {
                ResponseMatrix mOff = GpuBuild.Build(gpu, geometry, off, TextWriter.Null);
                ResponseMatrix mOn = GpuBuild.Build(gpu, geometry, off, TextWriter.Null);
                Check("GPU: построитель сам раздела не считает", mOff.CrystalSection == null && mOn.CrystalSection == null);
                ResponseMatrixBuilder.AttachCrystalSection(geometry, mOn);
                Check("GPU: явный вход: раздел есть", mOn.CrystalSection != null && mOn.CrystalSection.Count > 0);
                string dir = cacheDir ?? Path.GetTempPath();
                string a = Path.Combine(dir, "a274_gpu_off.rmx"), b = Path.Combine(dir, "a274_gpu_on.rmx");
                mOff.Save(a);
                mOn.Save(b);
                MatrixRefusal refusal;
                int format;
                ResponseMatrix ra = ResponseMatrix.Load(a, out refusal, out format);
                ResponseMatrix rb = ResponseMatrix.Load(b, out refusal, out format);
                Console.WriteLine("тело файла: выкл {0}, вкл {1}; раздел после чтения: {2}", ra.BodyFingerprint, rb.BodyFingerprint, rb.CrystalSection != null ? rb.CrystalSection.Count : 0);
                Check("GPU: тело файла при включённом ключе то же", string.Equals(ra.BodyFingerprint, rb.BodyFingerprint, StringComparison.Ordinal));
                Check("GPU: раздел прочитан из файла", rb.CrystalSection != null && rb.CrystalSection.Count > 0);
                Check("GPU: без раздела файл не несёт метки CWNA", IndexOf(File.ReadAllBytes(a), Encoding.ASCII.GetBytes("CWNA")) < 0);
            }
        }

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) ПОРОГ. Одиночные отсчёты в пустых каналах не включают образ
        /// слоя ниже порога: вычтенное побитно то же, что без них. Прежнее правило «первый непустой
        /// канал» снималось одним отсчётом в канале 0, поэтому эта проверка на нём обязана упасть:
        /// мутант — возврат прежнего правила в `FsaBand.FirstSeenChannel`.
        /// </summary>
        static void FloorChecks(string path, GeometryModel geometry, int histories)
        {
            ResultData rd = Load(path);
            string note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, new CrystalOwnParts { Series = false }, out note);
            ResponseMatrix matrix = Matrix(geometry, "file");
            EnergySpectrum source = rd.EnergySpectrum;
            lastFixed = null;
            FsaResult plain = Run(rd, null, geometry, matrix, section);
            double[] reference = lastFixed;
            if (plain == null || reference == null)
            {
                Check("разбор со слоем прошёл", false);
                return;
            }

            int first = 0;
            while (first < reference.Length && reference[first] == 0.0)
            {
                first++;
            }

            Console.WriteLine("данные: каналы 0…7 {0}; первый канал образа {1}, {2:F1} кэВ", string.Join(" ", new List<int>(source.Spectrum).GetRange(0, 8).ConvertAll(v => v.ToString(CultureInfo.InvariantCulture)).ToArray()), first, source.EnergyCalibration.ChannelToEnergy(first));
            Check("образа нет в пустых каналах, есть в первом записанном", first > 0 && source.Spectrum[first] > 0 && source.Spectrum[first - 1] < 11);
            foreach (int[] spike in new[] { new[] { 0, 1 }, new[] { 0, 500 }, new[] { 1, 1 }, new[] { 2, 3 } })
            {
                var copy = (ResultData)rd.Clone();
                var spectrum = (EnergySpectrum)source.Clone();
                spectrum.Spectrum = (int[])source.Spectrum.Clone();
                spectrum.Spectrum[spike[0]] += spike[1];
                spectrum.TotalPulseCount += spike[1];
                spectrum.ValidPulseCount += spike[1];
                copy.EnergySpectrum = spectrum;
                lastFixed = null;
                Run(copy, null, geometry, matrix, section);
                Check(string.Format(CultureInfo.InvariantCulture, "канал {0} + {1} отсч: вычтенное побитно то же", spike[0], spike[1]), lastFixed != null && Same(lastFixed, reference));
            }
        }

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) ЧЕМ СТАЛ БЫ ЦЕРИЕВЫЙ СОСТАВ В БИБЛИОТЕКЕ. Вещество опознаётся
        /// набором Z: 5 % церия увели бы LaBr3 из таблиц переноса и кривой света при любом положении
        /// ключа. Поэтому легирование учтено только в доле La-138. Контроль: вещество со своим церием
        /// считается как задано, без второго вычитания.
        /// </summary>
        static void MaterialChecks(GeometryModel geometry)
        {
            GeometryModel doped = geometry.Clone();
            var fractions = new Dictionary<int, double>(geometry.Crystal.Fractions);
            double la = fractions[57], br = fractions[35];
            double kept = la * 0.95, ce = la * 0.05 * 140.116 / 138.905, sum = kept + ce + br;
            doped.Crystal = new GeometryMaterial { Name = geometry.Crystal.Name, Density = geometry.Crystal.Density };
            doped.Crystal.Fractions[57] = kept / sum;
            doped.Crystal.Fractions[58] = ce / sum;
            doped.Crystal.Fractions[35] = br / sum;
            string pureName = EfficiencySimulator.ScintillatorNameOf(geometry), dopedName = EfficiencySimulator.ScintillatorNameOf(doped);
            Console.WriteLine("имя кривой света: La1 Br3 «{0}», La0.95 Ce0.05 Br3 «{1}»", pureName, dopedName);
            ElectronData.Material a = ElectronData.Match(geometry.Crystal), b = ElectronData.Match(doped.Crystal);
            Console.WriteLine("ElectronData.Match: La1 Br3 {0}, с церием {1}", a != null ? a.Name : "нет", b != null ? b.Name : "нет");
            Check("La1 Br3 опознан таблицами", a != null);
            Check("контроль: с церием таблицы его не опознают, поэтому библиотека не менялась", b == null);
            string why;
            double volume = GeometryScenes.CrystalVolumeCm3(geometry);
            double pure = CrystalOwnActivityBuilder.NaturalActivityBq(geometry, "138LA", out why) / volume;
            double own = CrystalOwnActivityBuilder.NaturalActivityBq(doped, "138LA", out why) / volume;
            Console.WriteLine("La-138: вещество La1 Br3 {0:F4} Бк/см³, вещество со своим церием {1:F4} Бк/см³", pure, own);
            Check("обе записи дают 1.4535 Бк/см³: легирование учтено один раз", Math.Abs(pure - 1.4535) < 0.001 && Math.Abs(own - 1.4535) < 0.001);
        }

        static FsaResult RunKeyed(ResultData rd, GeometryModel geometry, ResponseMatrix matrix, ResponseCrystalSection section, bool key)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = key;
            FsaTuningReport.Print(analyzer, key ? "ключ вкл" : "ключ выкл");
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            analyzer.CrystalOwnSection = section;
            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData { Geometry = geometry };
            }
            else
            {
                rd.Efficiency.Geometry = geometry;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            return analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, FsaSampleLibrary.Build(spec), efficiency);
        }

        static string Sha(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 16);
            }
        }

        static int IndexOf(byte[] data, byte[] mark)
        {
            for (int i = 0; i + mark.Length <= data.Length; i++)
            {
                int k = 0;
                while (k < mark.Length && data[i + k] == mark[k])
                {
                    k++;
                }

                if (k == mark.Length)
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------
        // Части слоя: причина χ²
        // ------------------------------------------------------------------

        static readonly double[] BandEdges = { 0.0, 28.0, 45.0, 100.0, 300.0, 700.0, 785.0, 1050.0, 1400.0, 1425.0, 1450.0, 1490.0, 1550.0, 2700.0, 4000.0 };

        /// <summary>С какой полосы прибор видит спектр: ниже 28 кэВ у `LABR_BRIL` порог (e_lo `detectors.csv` — 30).</summary>
        const int BandFromSeen = 1;

        sealed class Arm
        {
            public string Name;
            public CrystalOwnParts Parts;
            public double Scale = 1.0;
            public bool None;
            public bool NoGate;
            public double FloorKev;
        }

        static double[] BandChi(double[] y, double[] model, double[] layer, EnergyCalibration calibration)
        {
            var bands = new double[BandEdges.Length - 1];
            for (int i = 0; i < y.Length; i++)
            {
                double kev = calibration.ChannelToEnergy(i);
                double residual = y[i] - model[i] - (layer != null ? layer[i] : 0.0);
                double chi = residual * residual / Math.Max(y[i], 1.0);
                for (int b = 0; b < bands.Length; b++)
                {
                    if (kev >= BandEdges[b] && kev < BandEdges[b + 1])
                    {
                        bands[b] += chi;
                        break;
                    }
                }
            }

            return bands;
        }

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) ТАБЛИЦА ЧАСТЕЙ. Плечо — состав слоя и масштаб; χ² Пуассона
        /// считается по исходным каналам данных против (модель + вычтенный слой, `lastFixed`).
        /// Свободные плечи печатают подобранную активность и её отношение к расчётной по массе.
        /// </summary>
        static void Parts(string path, bool useBackground, bool subtractEmbedded, double inject,
                          GeometryModel geometry, ResponseMatrix matrix, int histories)
        {
            ResultData rd = Load(path);
            EnergySpectrum background = null;
            if (useBackground)
            {
                EnergySpectrum own = rd.BackgroundEnergySpectrum;
                if (own == null || own.Spectrum == null)
                {
                    Console.WriteLine("⛔ в файле нет встроенного фона");
                    bad++;
                    return;
                }

                own.EnergyCalibration = rd.EnergySpectrum.EnergyCalibration;
                rd.EnergySpectrum = own;
            }

            if (subtractEmbedded)
            {
                background = rd.BackgroundEnergySpectrum;
            }

            if (inject > 0.0)
            {
                string injectNote;
                ResponseCrystalSection clean = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x275, new CrystalOwnParts { DaughterRelaxation = true, Series = false }, out injectNote);
                double[] add = LayerCounts(rd, clean);
                var random = new Random(0x274);
                var copy = (EnergySpectrum)rd.EnergySpectrum.Clone();
                copy.Spectrum = (int[])rd.EnergySpectrum.Spectrum.Clone();
                long total = 0;
                for (int i = 0; i < copy.Spectrum.Length; i++)
                {
                    copy.Spectrum[i] += Poisson(add[i] * inject, random);
                    total += copy.Spectrum[i];
                }

                copy.TotalPulseCount = total;
                copy.ValidPulseCount = total;
                rd.EnergySpectrum = copy;
                Console.WriteLine("положительный контроль: в данные подмешан слой × {0:F2}", inject);
            }

            EnergySpectrum data = rd.EnergySpectrum;
            double live = data.EffectiveLiveTime > 0.0 ? data.EffectiveLiveTime : data.LiveTime;
            long totalCounts = 0;
            var raw = new double[data.Spectrum.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = data.Spectrum[i];
                totalCounts += data.Spectrum[i];
            }

            Console.WriteLine();
            Console.WriteLine("=== Части: {0}{1}{2} ===", Path.GetFileName(path), useBackground ? " (встроенный фон как спектр)" : "",
                              subtractEmbedded ? " (встроенный фон вычитается)" : "");
            Console.WriteLine("отсчётов {0}, живое время {1:F0} с, состав: {2} {3}", totalCounts, live, string.Join(",", chains.ToArray()), string.Join(",", nuclides.ToArray()));
            string why;
            double nominal = CrystalOwnActivityBuilder.NaturalActivityBq(geometry, "138LA", out why);
            Console.WriteLine("La-138 по массе кристалла: {0:F2} Бк, распадов за время {1:F0}", nominal, nominal * live);

            var arms = new List<Arm>
            {
                new Arm { Name = "без слоя", None = true },
                new Arm { Name = "умолчание", Parts = new CrystalOwnParts() },
                new Arm { Name = "без релаксации", Parts = new CrystalOwnParts { DaughterRelaxation = false, Series = false } },
                new Arm { Name = "без β La-138", Parts = new CrystalOwnParts { DaughterRelaxation = false, Beta = false, Series = false } },
                new Arm { Name = "без 788.7", Parts = new CrystalOwnParts { DaughterRelaxation = false, BranchGamma = false, Series = false } },
                new Arm { Name = "без 1436", Parts = new CrystalOwnParts { DaughterRelaxation = false, Capture = false, Series = false } },
                new Arm { Name = "только 1436", Parts = new CrystalOwnParts { DaughterRelaxation = false, Beta = false, BranchGamma = false, Series = false } },
                new Arm { Name = "+релаксация", Parts = new CrystalOwnParts { Series = false } },
                new Arm { Name = "×0.5", Parts = new CrystalOwnParts { DaughterRelaxation = false, Series = false }, Scale = 0.5 },
                new Arm { Name = "×0.3", Parts = new CrystalOwnParts { DaughterRelaxation = false, Series = false }, Scale = 0.3 },
                new Arm { Name = "×0.1", Parts = new CrystalOwnParts { DaughterRelaxation = false, Series = false }, Scale = 0.1 },
                new Arm { Name = "+релакс ×0.85", Parts = new CrystalOwnParts { DaughterRelaxation = true, Series = false }, Scale = 0.85 },
                new Arm { Name = "+релакс ×0.7", Parts = new CrystalOwnParts { DaughterRelaxation = true, Series = false }, Scale = 0.7 },
                new Arm { Name = "+релакс ×0.5", Parts = new CrystalOwnParts { DaughterRelaxation = true, Series = false }, Scale = 0.5 },
                new Arm { Name = "+релакс ×0.3", Parts = new CrystalOwnParts { DaughterRelaxation = true, Series = false }, Scale = 0.3 },
                new Arm { Name = "своб.", Parts = new CrystalOwnParts { DaughterRelaxation = false, FreeNatural = true, Series = false } },
                new Arm { Name = "своб.+релакс", Parts = new CrystalOwnParts { FreeNatural = true, DaughterRelaxation = true, Series = false } },
                new Arm { Name = "своб. п20", Parts = new CrystalOwnParts { DaughterRelaxation = false, FreeNatural = true, Series = false }, FloorKev = 20.0 },
                new Arm { Name = "своб.+рел. п20", Parts = new CrystalOwnParts { FreeNatural = true, DaughterRelaxation = true, Series = false }, FloorKev = 20.0 },
                new Arm { Name = "своб. z≥0", Parts = new CrystalOwnParts { DaughterRelaxation = false, FreeNatural = true, Series = false }, NoGate = true },
                new Arm { Name = "своб.+рел. z≥0", Parts = new CrystalOwnParts { FreeNatural = true, DaughterRelaxation = true, Series = false }, NoGate = true },
                new Arm { Name = "+рел.,Ac z≥0", Parts = new CrystalOwnParts { DaughterRelaxation = true }, NoGate = true },
            };

            var sbHead = new StringBuilder();
            sbHead.AppendFormat("{0,-16} {1,9} {2,12} {3,10} {4,9} {5,9}", "плечо", "хубер", "χ²Пуас", "≥порога", "gain", "слой отсч");
            for (int b = 0; b + 1 < BandEdges.Length; b++)
            {
                sbHead.AppendFormat(" {0,8}", BandEdges[b].ToString("F0", CultureInfo.InvariantCulture) + "-");
            }

            sbHead.Append("  K-40 скор.  амплитуда");
            Console.WriteLine(sbHead);
            foreach (Arm arm in arms)
            {
                ResponseCrystalSection section = null;
                if (!arm.None)
                {
                    string note;
                    section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, arm.Parts, out note);
                    foreach (CrystalOwnEntry entry in section.Entries)
                    {
                        if (!entry.Free)
                        {
                            entry.ActivityBq *= arm.Scale;
                        }

                        // Что ниже порога прибора, прибор не записывает: плечо с вырезанным низом гистограммы.
                        for (int i = 0; arm.FloorKev > 0.0 && i < entry.PerDecay.Length && i * section.BinKev < arm.FloorKev; i++)
                        {
                            entry.PerDecay[i] = 0.0;
                        }
                    }
                }

                lastFixed = null;
                noGate = arm.NoGate;
                FsaResult result = Run(rd, background, geometry, matrix, section);
                noGate = false;
                if (result == null)
                {
                    Console.WriteLine("{0,-16} отказ", arm.Name);
                    continue;
                }

                double[] fixedLayer = lastFixed;
                double[] bands = BandChi(raw, result.Model, fixedLayer, data.EnergyCalibration);
                double chi = 0.0;
                foreach (double v in bands)
                {
                    chi += v;
                }

                double layerSum = 0.0;
                if (fixedLayer != null)
                {
                    foreach (double v in fixedLayer)
                    {
                        layerSum += v;
                    }
                }

                var sb = new StringBuilder();
                // ⛔ Метрика прежней приёмки считала и каналы НИЖЕ порога прибора, где данных нет,
                // а слой есть: (70 отсч)²/1 на канал. Столбец «≥порога» — только каналы, что видит прибор.
                double chiSeen = 0.0;
                for (int b = BandFromSeen; b < bands.Length; b++)
                {
                    chiSeen += bands[b];
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, "{0,-16} {1,9:F3} {2,12:F0} {3,10:F0} {4,9:F6} {5,9:F0}", arm.Name, result.Chi2Ndf, chi, chiSeen, result.Gain, layerSum);
                foreach (double v in bands)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0,8:F0}", v);
                }

                FsaComponentResult k40 = Find(result, "K-40");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,8:F3}", k40 != null ? k40.CountRate : 0.0);
                if (result.CrystalOwn != null)
                {
                    foreach (FsaCrystalOwnResult row in result.CrystalOwn)
                    {
                        if (row.Free)
                        {
                            FsaComponentResult column = Find(result, FsaAnalyzer.CrystalOwnFreeName + row.Nuclide);
                            sb.AppendFormat(CultureInfo.InvariantCulture, " [колонка {0}, z={1:F1}]", column != null ? "есть" : "снята", column != null ? column.Z : 0.0);
                            sb.AppendFormat(CultureInfo.InvariantCulture, "  {0}={1:F2}±{2:F2} Бк{3}", row.Nuclide, row.ActivityBq, row.ActivitySigmaBq,
                                            row.Nuclide == "138LA" ? string.Format(CultureInfo.InvariantCulture, " (к массе {0:F3})", row.ActivityBq / nominal) : "");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(result.CrystalOwnNote))
                {
                    sb.Append("  [" + result.CrystalOwnNote + "]");
                }

                Console.WriteLine(sb);
            }
        }

        static int Poisson(double mean, Random random)
        {
            if (!(mean > 0.0))
            {
                return 0;
            }

            if (mean > 50.0)
            {
                double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * g));
            }

            double limit = Math.Exp(-mean), p = 1.0;
            int k = 0;
            do
            {
                k++;
                p *= random.NextDouble();
            }
            while (p > limit);
            return k - 1;
        }

        /// <summary>
        /// (`AMBER274`, Э2, 06.11.2026) ДАМП ЧАСТЕЙ для независимой сверки вне разбора
        /// (`--dump=файл.csv`): канал, энергия, спектр, встроенный фон, части La-138 в
        /// отсчётах при активности по массе, уширение гауссом ПШПВ прибора.
        /// </summary>
        static void DumpParts(string path, GeometryModel geometry, int histories, ResponseMatrix matrix)
        {
            ResultData rd = Load(path);
            var arms = new List<KeyValuePair<string, CrystalOwnParts>>
            {
                new KeyValuePair<string, CrystalOwnParts>("beta", new CrystalOwnParts { DaughterRelaxation = false, BranchGamma = false, Capture = false, Series = false }),
                new KeyValuePair<string, CrystalOwnParts>("branch", new CrystalOwnParts { DaughterRelaxation = false, Capture = false, Series = false }),
                new KeyValuePair<string, CrystalOwnParts>("g1436", new CrystalOwnParts { DaughterRelaxation = false, Beta = false, BranchGamma = false, Series = false }),
                new KeyValuePair<string, CrystalOwnParts>("capture_relax", new CrystalOwnParts { Beta = false, BranchGamma = false, DaughterRelaxation = true, Series = false }),
            };
            var columns = new List<double[]>();
            foreach (KeyValuePair<string, CrystalOwnParts> arm in arms)
            {
                string note;
                ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, arm.Value, out note);
                columns.Add(LayerCounts(rd, section));
                foreach (CrystalOwnEntry entry in section.Entries)
                {
                    double sum = 0.0, b1 = 0.0, b2 = 0.0, b3 = 0.0;
                    for (int i = 0; i < entry.PerDecay.Length; i++)
                    {
                        double e = i * section.BinKev;
                        sum += entry.PerDecay[i];
                        if (e >= 1431.0 && e <= 1441.0) b1 += entry.PerDecay[i];
                        if (e >= 1461.0 && e <= 1478.0) b2 += entry.PerDecay[i];
                        if (e >= 28.0 && e <= 42.0) b3 += entry.PerDecay[i];
                    }

                    Console.WriteLine("{0,-14} {1}: {2:F2} Бк, отсч/расп {3:F4}, 1431–1441 {4:F4}, 1461–1478 {5:F4}, 28–42 {6:F4}", arm.Key, entry.Nuclide, entry.ActivityBq, sum, b1, b2, b3);
                }
            }

            EnergySpectrum s = rd.EnergySpectrum;
            EnergySpectrum b = rd.BackgroundEnergySpectrum;
            using (var writer = new StreamWriter(dumpPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("channel;kev;fwhm_kev;spectrum;background;beta;branch;g1436;capture_relax");
                for (int i = 0; i < s.NumberOfChannels; i++)
                {
                    double kev = s.EnergyCalibration.ChannelToEnergy(i);
                    double width = rd.FwhmCalibration.ChannelToFwhm(i) * (s.EnergyCalibration.ChannelToEnergy(i + 1) - kev);
                    var sb = new StringBuilder();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0};{1:F3};{2:F3};{3};{4}", i, kev, width, s.Spectrum[i], b != null ? b.Spectrum[i] : 0);
                    foreach (double[] column in columns)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, ";{0:F3}", column[i]);
                    }

                    writer.WriteLine(sb);
                }
            }

            Console.WriteLine("дамп: {0}", dumpPath);
        }

        // ------------------------------------------------------------------
        // Общее
        // ------------------------------------------------------------------

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            return rd;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}: {1}", ok ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }
    }
}
