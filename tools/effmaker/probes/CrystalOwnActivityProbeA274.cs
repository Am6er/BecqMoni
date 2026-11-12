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
    ///   9. РЕВИЗИЯ ПО СТАТЬЯМ (12.11.2026, полоса `a274r`): `--mode=conv` — энергия связи после
    ///      внутренней конверсии оседает (лютециевая сцена, мутант — рычаг выключен);
    ///      `--mode=light` — слой по свету: фотонная шкала, положение K-пика и пика суммы;
    ///      `--mode=shape` — опытная форма беты La-138 (Sandler 2019) против формы кода;
    ///      `--mode=revision` — плечи ревизии на спектре (шкала, L из EADL, форма беты, доля K).
    ///      `--mode=lambda --nuclides=…` — средние форм уникальных запретов: λ = 1 кода против
    ///      кулоновских λ; `--mode=density` — песочница плотности CeBr3 5.1 против 5.18 г/см³.
    ///  10. КЛЮЧ λ (12.11.2026, полоса `a274s`): `--mode=clambda [--nuclides=…] [--scenes=a.rmx,b.rmx]
    ///      [--histories=N]` — ключ `FsaBetaLibrary.CoulombLambda`: средние форм ВЫКЛ/ВКЛ к базе и к
    ///      независимому расчёту пробы (положительный контроль, мутант λ = 1), прочие формы побитно;
    ///      на сценах склада — хеши строк ветвей ВЫКЛ против записанных (побитно), какие меняются при
    ///      ВКЛ, годность раздела (`cl=`), образ беты весами узлов и строки ветвей заново ВЫКЛ/ВКЛ.
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
        static bool nuclidesGiven;
        static readonly List<string> storeScenes = new List<string>();
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

                    nuclidesGiven = nuclides.Count > 0;
                }
                else if (a.StartsWith("--scenes=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Trim().Length > 0) storeScenes.Add(part.Trim());
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

            if (mode == "conv")
            {
                ConversionChecks(LutetiumScene(sceneHeight), histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "light")
            {
                LightChecks(lanthanum, histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "density")
            {
                DensityChecks(sceneHeight, histories);
                return 0;
            }

            if (mode == "lambda")
            {
                LambdaChecks();
                return 0;
            }

            if (mode == "clambda")
            {
                CoulombKeyChecks(histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "shape")
            {
                ShapeChecks(lanthanum, histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "revision")
            {
                ResponseMatrix revisionMatrix = Matrix(lanthanum, matrixMode);
                BandEdges = new[] { 0.0, 28.0, 33.0, 37.0, 41.0, 45.0, 100.0, 150.0, 270.0, 700.0, 785.0, 1050.0, 1400.0, 1425.0, 1450.0, 1460.0, 1470.0, 1480.0, 1490.0, 1550.0, 2700.0, 4000.0 };
                Parts(spectrumPath, useBackground, subtractEmbedded, inject, lanthanum, revisionMatrix, histories, RevisionArms());
                return 0;
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

        static double[] BandEdges = { 0.0, 28.0, 45.0, 100.0, 300.0, 700.0, 785.0, 1050.0, 1400.0, 1425.0, 1450.0, 1490.0, 1550.0, 2700.0, 4000.0 };

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
            Parts(path, useBackground, subtractEmbedded, inject, geometry, matrix, histories, null);
        }

        // ------------------------------------------------------------------
        // Ревизия по статьям (12.11.2026, полоса `a274r`)
        // ------------------------------------------------------------------

        /// <summary>Опытный фактор формы беты La-138, Sandler и др., Phys. Rev. C 100 (2019) 014308, табл. III (LEBIT).</summary>
        const double SandlerA = -1.319;
        const double SandlerB = 0.4982;
        const double SandlerEndpointKev = 263.68;

        /// <summary>Доля K-захвата La-138 по опыту Quarati и др. 2016 (L/K 0.391, M/K 0.102; табл. IV Sandler 2019).</summary>
        const double MeasuredKCapture = 0.670;

        /// <summary>ln Γ(x + iy), действительная часть: приближение Ланцоша (g = 7, 9 членов).</summary>
        static double LnGammaAbs(double x, double y)
        {
            double[] c =
            {
                0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
                12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
            };
            double zr = x - 1.0, zi = y;
            double ar = c[0], ai = 0.0;
            for (int k = 1; k < c.Length; k++)
            {
                double dr = zr + k;
                double d = dr * dr + zi * zi;
                ar += c[k] * dr / d;
                ai -= c[k] * zi / d;
            }

            double tr = zr + 7.5, ti = zi;
            double lnr = 0.5 * Math.Log(tr * tr + ti * ti);
            double theta = Math.Atan2(ti, tr);
            return 0.5 * Math.Log(2.0 * Math.PI) + (zr + 0.5) * lnr - zi * theta - tr + 0.5 * Math.Log(ar * ar + ai * ai);
        }

        /// <summary>
        /// Отношение кулоновских амплитуд λ_k = F_{k−1}/F₀ точечного ядра (Behrens–Jänecke):
        /// F_{k−1} = [k(2k−1)!!]²·4^k·(2pR)^{2(γ_k−k)}·e^{πy}·|Γ(γ_k+iy)|²/Γ(1+2γ_k)², γ_k = √(k²−(αZ)²),
        /// y = αZW/p. При Z → 0 λ_k → 1. Код формы беты приложения берёт λ_k = 1.
        /// </summary>
        static double Lambda(int k, int z, int a, double w)
        {
            const double Alpha = 1.0 / 137.035999084;
            double p = Math.Sqrt(Math.Max(1e-12, w * w - 1.0));
            double y = Alpha * z * w / p;
            double radius = 1.2 * Math.Pow(a, 1.0 / 3.0) / 386.15926796;
            Func<int, double> lnF = n =>
            {
                double g = Math.Sqrt(n * n - Alpha * z * Alpha * z);
                double odd = 1.0;
                for (int j = 2 * n - 1; j > 1; j -= 2)
                {
                    odd *= j;
                }

                return 2.0 * Math.Log(n * odd) + n * Math.Log(4.0) + 2.0 * (g - n) * Math.Log(2.0 * p * radius)
                       + Math.PI * y + 2.0 * LnGammaAbs(g, y) - 2.0 * LnGammaAbs(1.0 + 2.0 * g, 0.0);
            };
            return Math.Exp(lnF(k) - lnF(1));
        }

        /// <summary>
        /// Форма беты La-138 по Sandler 2019: форма кода (уникальный 2-й запрет, λ = 1), переведённая
        /// к теоретическому фактору ур. (6) q⁴ + (10/3)·λ₂·q²p² + λ₃·p⁴ с кулоновскими λ
        /// (<paramref name="lambdas"/>) и умноженная на C_exp(W) = 1 + a·W + b·W²
        /// (<paramref name="experimental"/>), W — полная энергия в mₑc². Таблица `nucdb.beta_shapes`
        /// не трогается: форма живёт в пробе.
        /// </summary>
        static FsaBeta SandlerShape(FsaBeta beta, bool lambdas, bool experimental)
        {
            if (beta == null || !string.Equals(beta.Nuclide, "138LA", StringComparison.OrdinalIgnoreCase))
            {
                return beta;
            }

            int n = 528;
            var t = new double[n + 1];
            var density = new double[n + 1];
            double w0 = 1.0 + beta.EndpointKev / 510.99895;
            for (int i = 0; i <= n; i++)
            {
                t[i] = beta.EndpointKev * i / n;
                if (i == 0 || i == n)
                {
                    continue;
                }

                double code = FsaBetaLibrary.Density(beta, t[i]);
                double w = 1.0 + t[i] / 510.99895;
                double factor = 1.0;
                if (lambdas)
                {
                    factor *= UniqueWithLambda(beta, w, w0, true) / UniqueWithLambda(beta, w, w0, false);
                }

                if (experimental)
                {
                    factor *= 1.0 + SandlerA * w + SandlerB * w * w;
                }

                density[i] = Math.Max(0.0, code * factor);
            }

            return beta.WithShape("2nd unique, Sandler 2019", -1, "probe", t, density, beta.EndpointKev, null);
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, п. 3) ПЛОТНОСТЬ CeBr3: 5.1 г/см³ библиотеки против 5.18
        /// (Quarati 2013). Песочница: тот же кристалл 38×38, то же зерно, эффективность пика полного
        /// поглощения и полный счёт на историю при двух плотностях. Библиотека веществ не меняется.
        /// </summary>
        static void DensityChecks(double height, int histories)
        {
            Console.WriteLine();
            // (`a274s`) Засев библиотеки после правки и пробег ESTAR (г/см²) при двух плотностях:
            // у `ElectronData` вшитый CeBr3 считается при 5.1, плотность входит только в поправку
            // на плотность (δ) — сколько это даёт.
            foreach (GeometryMaterialLibrary.Entry e in GeometryMaterialLibrary.Seed())
            {
                if (e.Formula == "Ce1 Br3")
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "засев библиотеки: {0} ({1}) {2} г/см³", e.Name, e.Abbr, e.Density));
                }
            }

            var gridMev = new double[40];
            for (int i = 0; i < gridMev.Length; i++)
            {
                gridMev[i] = 0.01 * Math.Pow(300.0, i / (gridMev.Length - 1.0));
            }

            EstarCalculator.Result r510 = EstarCalculator.Compute(new EstarCalculator.Compound { Name = "CeBr3", Z = new[] { 58, 35 }, Atoms = new[] { 1.0, 3.0 }, DensityGCm3 = 5.1 }, gridMev);
            EstarCalculator.Result r518 = EstarCalculator.Compute(new EstarCalculator.Compound { Name = "CeBr3", Z = new[] { 58, 35 }, Atoms = new[] { 1.0, 3.0 }, DensityGCm3 = 5.18 }, gridMev);
            double worst = 0.0, worstAt = 0.0, worstYield = 0.0;
            for (int i = 0; i < gridMev.Length; i++)
            {
                double d = Math.Abs(r518.RangeGCm2[i] / r510.RangeGCm2[i] - 1.0);
                if (d > worst)
                {
                    worst = d;
                    worstAt = gridMev[i];
                }

                worstYield = Math.Max(worstYield, Math.Abs(r518.Yield[i] - r510.Yield[i]));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ESTAR CeBr3, пробег г/см² при 5.18 к 5.1: наибольшее |Δ| {0:E3} (у {1:F3} МэВ), выход тормозного |Δ| ≤ {2:E3}; 1 МэВ: {3:F6} → {4:F6} г/см²",
                worst, worstAt, worstYield, Interp(gridMev, r510.RangeGCm2, 1.0), Interp(gridMev, r518.RangeGCm2, 1.0)));

            Console.WriteLine("=== CeBr3: плотность 5.1 (прежняя библиотека) против 5.18 (Quarati 2013) ===");
            Console.WriteLine("{0,9} {1,12} {2,12} {3,9} {4,12} {5,12} {6,9}", "E, кэВ", "пик 5.10", "пик 5.18", "отн.", "всего 5.10", "всего 5.18", "отн.");
            foreach (double energy in new[] { 59.5, 122.1, 356.0, 661.657, 1173.2, 1460.8, 2614.5 })
            {
                var peak = new double[2];
                var total = new double[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    GeometryModel g = CeriumScene(height);
                    g.Crystal.Density = arm == 0 ? 5.1 : 5.18;
                    var sim = new EfficiencySimulator(g) { Histories = histories, Seed = 20260803 };
                    double error;
                    double[] h = sim.Response(energy, 2.0, out error);
                    peak[arm] = h[h.Length - 1];
                    foreach (double v in h)
                    {
                        total[arm] += v;
                    }
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,9:F1} {1,12:E4} {2,12:E4} {3,9:F4} {4,12:E4} {5,12:E4} {6,9:F4}",
                                                energy, peak[0], peak[1], peak[0] > 0.0 ? peak[1] / peak[0] : 0.0, total[0], total[1], total[0] > 0.0 ? total[1] / total[0] : 0.0));
            }
        }

        static double Interp(double[] x, double[] y, double at)
        {
            for (int i = 1; i < x.Length; i++)
            {
                if (at <= x[i])
                {
                    return y[i - 1] + (y[i] - y[i - 1]) * (at - x[i - 1]) / (x[i] - x[i - 1]);
                }
            }

            return y[y.Length - 1];
        }

        static FsaBeta SandlerShape(FsaBeta beta)
        {
            return SandlerShape(beta, true, true);
        }

        /// <summary>
        /// Фактор уникального n-го запрета Σₖ λₖ·p²⁽ᵏ⁻¹⁾·q²⁽ⁿ⁻ᵏ⁺¹⁾/((2k−1)!·(2(n−k+1)+1)!), k = 1…n+1;
        /// <paramref name="coulomb"/> — с кулоновскими λₖ, иначе λₖ = 1 (так считает форма кода).
        /// </summary>
        static double UniqueWithLambda(FsaBeta beta, double w, double w0, bool coulomb)
        {
            int n = beta.UniqueOrder;
            double p2 = w * w - 1.0;
            double q2 = (w0 - w) * (w0 - w);
            double sum = 0.0;
            for (int k = 1; k <= n + 1; k++)
            {
                double lambda = coulomb && k > 1 ? Lambda(k, beta.DaughterZ, beta.MassNumber, w) : 1.0;
                sum += lambda * Math.Pow(p2, k - 1) * Math.Pow(q2, n - k + 1) / (Factorial(2 * k - 1) * Factorial(2 * (n - k + 1) + 1));
            }

            return sum;
        }

        static double Factorial(int n)
        {
            double f = 1.0;
            for (int i = 2; i <= n; i++)
            {
                f *= i;
            }

            return f;
        }

        /// <summary>Та же ветвь с кулоновскими λₖ в факторе уникального запрета (любой порядок ≥ 1, форма кодом).</summary>
        static FsaBeta CoulombShape(FsaBeta beta)
        {
            if (beta == null || beta.IsLine || beta.Positron || beta.UniqueOrder < 1 || beta.ShapeT != null || beta.ShapeFactor != null)
            {
                return beta;
            }

            int n = Math.Max(400, Math.Min(4000, (int)Math.Ceiling(beta.EndpointKev)));
            var t = new double[n + 1];
            var density = new double[n + 1];
            double w0 = 1.0 + beta.EndpointKev / 510.99895;
            for (int i = 1; i < n; i++)
            {
                t[i] = beta.EndpointKev * i / n;
                double w = 1.0 + t[i] / 510.99895;
                density[i] = Math.Max(0.0, FsaBetaLibrary.Density(beta, t[i]) * UniqueWithLambda(beta, w, w0, true) / UniqueWithLambda(beta, w, w0, false));
            }

            t[n] = beta.EndpointKev;
            return beta.WithShape(beta.Transition + ", Coulomb lambda", -1, "probe", t, density, beta.EndpointKev, null);
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, п. 4) ФОРМА УНИКАЛЬНЫХ ЗАПРЕТОВ: λₖ = 1 в коде против
        /// кулоновских λₖ. По каждой ветви β⁻ нуклидов `--nuclides=`: средняя базы, средняя формы
        /// кода, средняя с λₖ. Таблицы и разрешённые ветви печатаются для сравнения как есть.
        /// </summary>
        static void LambdaChecks()
        {
            Console.WriteLine();
            Console.WriteLine("=== Уникальные запреты: λ = 1 (код) против кулоновских λ ===");
            Console.WriteLine("{0,-8} {1,9} {2,8} {3,-16} {4,9} {5,9} {6,9} {7,8} {8,8}", "нуклид", "E0, кэВ", "выход %", "переход", "ср. базы", "ср. кода", "ср. с λ", "код/база", "λ/база");
            foreach (string nucid in nuclides)
            {
                foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
                {
                    if (beta == null || beta.IsLine || beta.Positron || beta.Intensity < 0.5)
                    {
                        continue;
                    }

                    double meanCode, meanLambda;
                    FsaBetaLibrary.Integral(beta, out meanCode);
                    FsaBetaLibrary.Integral(CoulombShape(beta), out meanLambda);
                    string kind = (beta.Transition ?? "") + (beta.ShapeT != null || beta.ShapeFactor != null ? " [табл.]" : "");
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-8} {1,9:F1} {2,8:F2} {3,-16} {4,9:F1} {5,9:F1} {6,9:F1} {7,8:F3} {8,8:F3}",
                                                    nucid, beta.EndpointKev, beta.Intensity, kind, beta.MeanKev, meanCode, meanLambda,
                                                    beta.MeanKev > 0.0 ? meanCode / beta.MeanKev : 0.0, beta.MeanKev > 0.0 ? meanLambda / beta.MeanKev : 0.0));
                }
            }
        }

        /// <summary>Нуклиды таблицы ключа λ по умолчанию: лёгкие, средние, тяжёлые (Z ≥ 81), таблицы и разрешённые.</summary>
        static readonly string[] CoulombDefaultNuclides =
        {
            "40K", "138LA", "89SR", "91Y", "85KR", "36CL", "10BE", "87RB", "204TL", "228AC", "210BI", "90Y", "90SR", "137CS", "60CO", "212PB", "214PB", "212BI", "208TL",
        };

        /// <summary>Полосы образа беты, кэВ (те же, что у находки `critStore` про K-40).</summary>
        static readonly double[] CoulombBands = { 0.0, 50.0, 100.0, 150.0, 300.0, 600.0, 1000.0, 4000.0 };

        /// <summary>
        /// (`a274s`) КЛЮЧ λ `FsaBetaLibrary.CoulombLambda`. А — средние форм ВЫКЛ/ВКЛ: у уникальных
        /// без таблицы ВКЛ сходится с независимым расчётом пробы (<see cref="CoulombShape"/>), ВЫКЛ —
        /// нет (мутант λ = 1 пойман); у прочих форма ВКЛ побитно та же. Б — кэш весов разводит ключ.
        /// В — сцены склада (`--scenes=`): строки ветвей ВЫКЛ побитно = записанным (хеш таблиц),
        /// при ВКЛ меняются только уникальные; разбор раздел при ВКЛ не берёт (`cl=`); образ беты
        /// весами узлов и строки ветвей заново (одно зерно) ВЫКЛ против ВКЛ.
        /// </summary>
        static void CoulombKeyChecks(int histories)
        {
            bool saved = FsaBetaLibrary.CoulombLambda;
            Check("умолчание ключа λ — ВЫКЛ", !FsaBetaLibrary.CoulombLambda);
            List<string> list = nuclidesGiven ? nuclides : new List<string>(CoulombDefaultNuclides);

            Console.WriteLine();
            Console.WriteLine("=== А. Средние форм β⁻: ключ λ ВЫКЛ / ВКЛ, к базе и к независимому расчёту пробы ===");
            Console.WriteLine("{0,-7} {1,4} {2,9} {3,8} {4,-24} {5,8} {6,8} {7,8} {8,8} {9,8} {10,8} {11,9}",
                              "нуклид", "Z", "E0, кэВ", "выход %", "переход", "база", "ВЫКЛ", "ВКЛ", "проба λ", "ВЫКЛ/б", "ВКЛ/б", "ВКЛ/проба");
            int unique = 0, agree = 0, mutantCaught = 0, sameOther = 0, other = 0, pointwiseAgree = 0, pointwiseMutant = 0;
            double pointwiseWorst = 0.0;
            foreach (string nucid in list)
            {
                foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
                {
                    if (beta == null || beta.IsLine || beta.Positron || beta.Intensity < 0.5)
                    {
                        continue;
                    }

                    bool isUnique = beta.UniqueOrder >= 1 && beta.ShapeT == null && beta.ShapeFactor == null;
                    var samples = new double[2][];
                    double meanOff, meanOn, meanProbe;
                    FsaBetaLibrary.CoulombLambda = false;
                    FsaBetaLibrary.Integral(beta, out meanOff);
                    samples[0] = DensitySamples(beta);
                    FsaBeta probeShape = CoulombShape(beta);
                    FsaBetaLibrary.Integral(probeShape, out meanProbe);
                    FsaBetaLibrary.CoulombLambda = true;
                    FsaBetaLibrary.Integral(beta, out meanOn);
                    samples[1] = DensitySamples(beta);
                    FsaBetaLibrary.CoulombLambda = false;

                    string kind = (beta.Transition ?? "") + (beta.ShapeT != null || beta.ShapeFactor != null ? " [табл.]" : "");
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-7} {1,4} {2,9:F1} {3,8:F3} {4,-24} {5,8:F2} {6,8:F2} {7,8:F2} {8,8:F2} {9,8:F4} {10,8:F4} {11,9:F5}",
                        nucid, beta.DaughterZ, beta.EndpointKev, beta.Intensity, kind.Length > 24 ? kind.Substring(0, 24) : kind,
                        beta.MeanKev, meanOff, meanOn, meanProbe,
                        beta.MeanKev > 0.0 ? meanOff / beta.MeanKev : 0.0, beta.MeanKev > 0.0 ? meanOn / beta.MeanKev : 0.0,
                        meanProbe > 0.0 ? meanOn / meanProbe : 0.0));
                    if (isUnique)
                    {
                        unique++;
                        if (Math.Abs(meanOn / meanProbe - 1.0) < 2e-3)
                        {
                            agree++;
                        }
                        else
                        {
                            Console.WriteLine("   ✗ ВКЛ расходится с независимым расчётом пробы");
                        }

                        // Мутант «λ = 1»: та же проверка на форме ВЫКЛ обязана упасть хоть где-то.
                        if (Math.Abs(meanOff / meanProbe - 1.0) >= 2e-3)
                        {
                            mutantCaught++;
                        }

                        // (З1 критика `crit274s`) Поточечно, допуск 1e-9: плотность ВКЛ против
                        // «ВЫКЛ × S_λ/S₁» формулы пробы в тех же 257 точках; мутант λ = 1 — та же
                        // проверка на плотности ВЫКЛ.
                        double worstOn = PointwiseDeviation(beta, samples[1], samples[0]);
                        double worstMutant = PointwiseDeviation(beta, samples[0], samples[0]);
                        pointwiseWorst = Math.Max(pointwiseWorst, worstOn);
                        if (worstOn <= 1e-9)
                        {
                            pointwiseAgree++;
                        }
                        else
                        {
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   ✗ поточечно ВКЛ расходится с формулой пробы: {0:E3}", worstOn));
                        }

                        if (worstMutant > 1e-9)
                        {
                            pointwiseMutant++;
                        }
                    }
                    else
                    {
                        other++;
                        if (Same(samples[0], samples[1]))
                        {
                            sameOther++;
                        }
                        else
                        {
                            Console.WriteLine("   ✗ форма не уникального запрета сдвинулась ключом");
                        }
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "уникальных без таблицы {0}: ВКЛ = расчёт пробы (< 0.2 %) у {1}; мутант λ = 1 пойман у {2}; прочих {3}: побитно та же форма у {4}",
                unique, agree, mutantCaught, other, sameOther));
            Check("ВКЛ: средние уникальных = независимый расчёт пробы", unique > 0 && agree == unique);
            Check("мутант λ = 1 (форма ВЫКЛ) пойман той же проверкой", mutantCaught > 0);
            Check("ключ не трогает таблицы, факторы и разрешённые формы", sameOther == other);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "поточечно (257 точек на ветвь): ВКЛ = ВЫКЛ × S_λ/S₁ формулы пробы у {0} из {1}, наибольшее отн. отклонение {2:E3}; мутант λ = 1 пойман у {3}",
                pointwiseAgree, unique, pointwiseWorst, pointwiseMutant));
            Check("поточечно ВКЛ = формула пробы (допуск 1e-9)", unique > 0 && pointwiseAgree == unique);
            Check("поточечно мутант λ = 1 пойман", pointwiseMutant > 0);

            // Б. Кэш весов: ВЫКЛ → ВКЛ → ВЫКЛ на одном разделе.
            foreach (string scene in storeScenes)
            {
                CoulombScene(scene, histories);
            }

            FsaBetaLibrary.CoulombLambda = saved;
        }

        /// <summary>
        /// (З1) Наибольшее относительное отклонение <paramref name="tested"/> от
        /// <paramref name="off"/>·S_λ/S₁ (фактор формы пробы с кулоновскими λ и без) в точках
        /// <see cref="DensitySamples"/>; точки с нулевой плотностью пропускаются.
        /// </summary>
        static double PointwiseDeviation(FsaBeta beta, double[] tested, double[] off)
        {
            const int n = 257;
            double w0 = 1.0 + beta.EndpointKev / 510.99895, worst = 0.0;
            for (int i = 0; i < n; i++)
            {
                double w = 1.0 + beta.EndpointKev * (i + 0.5) / n / 510.99895;
                double expected = off[i] * UniqueWithLambda(beta, w, w0, true) / UniqueWithLambda(beta, w, w0, false);
                if (expected > 0.0)
                {
                    worst = Math.Max(worst, Math.Abs(tested[i] / expected - 1.0));
                }
            }

            return worst;
        }

        static double[] DensitySamples(FsaBeta beta)
        {
            const int n = 257;
            var v = new double[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = FsaBetaLibrary.Density(beta, beta.EndpointKev * (i + 0.5) / n);
            }

            return v;
        }

        static void CoulombScene(string rmxPath, int histories)
        {
            Console.WriteLine();
            Console.WriteLine("=== В. Сцена склада: " + Path.GetFileName(rmxPath) + " ===");
            string inPath = Path.ChangeExtension(rmxPath, ".in");
            MatrixRefusal refusal;
            int format;
            ResponseMatrix matrix = ResponseMatrix.Load(rmxPath, out refusal, out format);
            if (matrix == null || !File.Exists(inPath))
            {
                Check("сцена прочитана (" + refusal + ", " + inPath + ")", false);
                return;
            }

            GeometryModel geometry = GeometryModel.Load(inPath);
            FsaBetaLibrary.CoulombLambda = false;
            ElectronSectionRefusal why;
            ResponseElectronSection section = matrix.ElectronSectionFor(geometry, out why);
            Check("ВЫКЛ: раздел годен (" + why + ")", section != null);
            if (section == null)
            {
                return;
            }

            FsaBetaLibrary.CoulombLambda = true;
            ResponseElectronSection sectionOn = matrix.ElectronSectionFor(geometry, out why);
            Console.WriteLine("ВКЛ: раздел склада для разбора — " + (sectionOn != null ? "годен" : "не годен (" + why + ")"));
            Check("ВКЛ: раздел без `cl=1` разбор не берёт", sectionOn == null && why == ElectronSectionRefusal.OtherPhysics);
            FsaBetaLibrary.CoulombLambda = false;
            Check("ВЫКЛ снова: раздел годен", matrix.ElectronSectionFor(geometry, out why) != null);
            Console.WriteLine("клеймо раздела: " + section.Stamp);

            // (З2 критика `crit274s`) Второе плечо: раздел с `cl=1` (клеймо раздела, посчитанного при
            // ВКЛ) при ВКЛ берётся, при ВЫКЛ — нет. Клеймо меняется на месте и возвращается.
            string savedStamp = section.Stamp;
            int capAt = savedStamp.IndexOf(";cap=", StringComparison.Ordinal);
            section.Stamp = capAt >= 0 ? savedStamp.Substring(0, capAt) + ";cl=1" + savedStamp.Substring(capAt) : savedStamp + ";cl=1";
            FsaBetaLibrary.CoulombLambda = true;
            ElectronSectionRefusal whyOn;
            bool takenOn = matrix.ElectronSectionFor(geometry, out whyOn) != null;
            FsaBetaLibrary.CoulombLambda = false;
            ElectronSectionRefusal whyOff;
            bool takenOff = matrix.ElectronSectionFor(geometry, out whyOff) != null;
            section.Stamp = savedStamp;
            Console.WriteLine("раздел с `cl=1`: ВКЛ — " + (takenOn ? "годен" : "не годен (" + whyOn + ")") + ", ВЫКЛ — " + (takenOff ? "годен" : "не годен (" + whyOff + ")"));
            Check("раздел с `cl=1` при ВКЛ берётся", takenOn);
            Check("раздел с `cl=1` при ВЫКЛ не берётся (OtherPhysics)", !takenOff && whyOff == ElectronSectionRefusal.OtherPhysics);
            Check("клеймо раздела возвращено, раздел снова годен", string.Equals(section.Stamp, savedStamp, StringComparison.Ordinal) && matrix.ElectronSectionFor(geometry, out why) != null);

            // В1. Строки ветвей: хеш таблиц ВЫКЛ против записанного; при ВКЛ — какие меняются.
            int rows = section.BranchNuclides != null ? section.BranchNuclides.Length : 0;
            int found = 0, sameOff = 0, changedOn = 0;
            var changed = new List<KeyValuePair<FsaBeta, int>>();
            for (int j = 0; j < rows; j++)
            {
                FsaBeta beta = null;
                foreach (FsaBeta b in FsaBetaLibrary.Branches(section.BranchNuclides[j]))
                {
                    if (!b.IsLine && !b.Positron && Math.Abs(b.EndpointKev - section.BranchEndpoints[j]) <= 0.01)
                    {
                        beta = b;
                        break;
                    }
                }

                if (beta == null)
                {
                    continue;
                }

                found++;
                string reason;
                FsaBetaLibrary.CoulombLambda = false;
                ElectronBranchSpec off = FsaBetaLibrary.EventSpec(beta, out reason);
                FsaBetaLibrary.CoulombLambda = true;
                ElectronBranchSpec on = FsaBetaLibrary.EventSpec(beta, out reason);
                FsaBetaLibrary.CoulombLambda = false;
                if (off != null && section.BranchHashes != null && string.Equals(off.Hash, section.BranchHashes[j], StringComparison.Ordinal))
                {
                    sameOff++;
                }

                if (off != null && on != null && !string.Equals(off.Hash, on.Hash, StringComparison.Ordinal))
                {
                    changedOn++;
                    changed.Add(new KeyValuePair<FsaBeta, int>(beta, j));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   ВКЛ меняет строку ветви {0} {1:F1} кэВ {2:G4} % ({3}, Z = {4})",
                                                    section.BranchNuclides[j], beta.EndpointKev, beta.Intensity, beta.Transition, beta.DaughterZ));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "строк ветвей {0}, найдено в библиотеке {1}; ВЫКЛ: хеш таблиц = записанному у {2}; ВКЛ меняет {3}", rows, found, sameOff, changedOn));
            Check("ВЫКЛ: строки ветвей склада побитно по входу (хеш формы и каскада)", rows > 0 && found == rows && sameOff == rows);

            // В2. Образ беты весами узлов (без пересчёта склада) и полный, ВЫКЛ против ВКЛ.
            int length = (int)(4000.0 / section.BinKev);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("образ беты нуклида разбором (Σ ветвей I·Σwₖ·Rₖ), полосы " + string.Join("/", Array.ConvertAll(CoulombBands, b => b.ToString("F0", CultureInfo.InvariantCulture))) + " кэВ, доли площади:");
            List<string> imageList = nuclidesGiven ? nuclides : new List<string> { "40K", "228AC", "212PB", "212BI", "208TL", "214BI", "214PB" };
            foreach (string nucid in imageList)
            {
                if (!seen.Add(nucid))
                {
                    continue;
                }

                var image = new double[2][];
                var area = new double[2];
                var cacheRepeat = new double[length];
                for (int arm = 0; arm < 2; arm++)
                {
                    FsaBetaLibrary.CoulombLambda = arm == 1;
                    image[arm] = new double[length];
                    foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                    {
                        if (b.IsLine || b.Positron || !(b.Intensity > 0.0))
                        {
                            continue;
                        }

                        double[] w = FsaBetaLibrary.Weights(section, b);
                        if (w == null)
                        {
                            continue;
                        }

                        var scaled = new double[w.Length];
                        for (int k = 0; k < w.Length; k++)
                        {
                            scaled[k] = w[k] * b.Intensity / 100.0;
                        }

                        area[arm] += section.Accumulate(image[arm], scaled, false, 1.0);
                    }
                }

                // Кэш весов: ВЫКЛ после ВКЛ — побитно прежний образ.
                FsaBetaLibrary.CoulombLambda = false;
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine || b.Positron || !(b.Intensity > 0.0))
                    {
                        continue;
                    }

                    double[] w = FsaBetaLibrary.Weights(section, b);
                    if (w == null)
                    {
                        continue;
                    }

                    var scaled = new double[w.Length];
                    for (int k = 0; k < w.Length; k++)
                    {
                        scaled[k] = w[k] * b.Intensity / 100.0;
                    }

                    section.Accumulate(cacheRepeat, scaled, false, 1.0);
                }

                Check("кэш весов: " + nucid + " ВЫКЛ после ВКЛ побитно", Same(image[0], cacheRepeat));
                double meanOff = MeanBin(image[0], section.BinKev), meanOn = MeanBin(image[1], section.BinKev);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} площадь {1:F5} → {2:F5}, средняя {3:F2} → {4:F2} кэВ ({5:+0.00;-0.00} %)",
                                                nucid, area[0], area[1], meanOff, meanOn, meanOff > 0.0 ? 100.0 * (meanOn / meanOff - 1.0) : 0.0));
                Console.WriteLine("         ВЫКЛ " + BandShares(image[0], section.BinKev) + Environment.NewLine + "         ВКЛ  " + BandShares(image[1], section.BinKev));
            }

            // В3. Строки ветвей, которые ВКЛ меняет, — заново одним зерном, ВЫКЛ против ВКЛ
            // (оставленная энергия; рецепт зерна — как у построителя склада, номер 9001 + j).
            if (changed.Count == 0 || histories <= 0)
            {
                return;
            }

            var make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "строки ветвей заново, {0} историй, «событие − каскад» (то, что берёт разбор):", histories));
            foreach (KeyValuePair<FsaBeta, int> pair in changed)
            {
                var row = new double[2][];
                var areaRow = new double[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    FsaBetaLibrary.CoulombLambda = arm == 1;
                    string reason;
                    ElectronBranchSpec spec = FsaBetaLibrary.EventSpec(pair.Key, out reason);
                    var sim = (EfficiencySimulator)make.Invoke(null, new object[] { geometry, matrix.Options, 9001 + pair.Value, spec.EndpointKev });
                    sim.Histories = histories;
                    sim.ResetStream((ulong)sim.Seed + (ulong)(9001 + pair.Value) * 0x9E3779B97F4A7C15UL);
                    EfficiencySimulator.BranchEventStats bs;
                    double[][] r = sim.BranchEventResponse(spec, section.BinKev, 0.0, out bs);
                    row[arm] = new double[length];
                    for (int b = 0; b < r[0].Length && b < length; b++)
                    {
                        row[arm][b] = r[0][b] - (b < r[1].Length ? r[1][b] : 0.0);
                        areaRow[arm] += row[arm][b];
                    }
                }

                FsaBetaLibrary.CoulombLambda = false;
                double mOff = MeanBin(row[0], section.BinKev), mOn = MeanBin(row[1], section.BinKev);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0} {1:F1} кэВ: площадь {2:F5} → {3:F5}, средняя {4:F2} → {5:F2} кэВ ({6:+0.00;-0.00} %)",
                                                pair.Key.Nuclide, pair.Key.EndpointKev, areaRow[0], areaRow[1], mOff, mOn, mOff > 0.0 ? 100.0 * (mOn / mOff - 1.0) : 0.0));
                Console.WriteLine("         ВЫКЛ " + BandShares(row[0], section.BinKev) + Environment.NewLine + "         ВКЛ  " + BandShares(row[1], section.BinKev));
            }
        }

        static double MeanBin(double[] h, double bin)
        {
            double s = 0.0, m = 0.0;
            for (int i = 0; i < h.Length; i++)
            {
                s += h[i];
                m += h[i] * (i + 0.5) * bin;
            }

            return s != 0.0 ? m / s : 0.0;
        }

        static string BandShares(double[] h, double bin)
        {
            double total = 0.0;
            foreach (double v in h)
            {
                total += v;
            }

            var sb = new StringBuilder();
            for (int k = 0; k + 1 < CoulombBands.Length; k++)
            {
                double s = 0.0;
                for (int i = 0; i < h.Length; i++)
                {
                    double e = (i + 0.5) * bin;
                    if (e >= CoulombBands[k] && e < CoulombBands[k + 1])
                    {
                        s += h[i];
                    }
                }

                sb.Append((total != 0.0 ? s / total : 0.0).ToString("F4", CultureInfo.InvariantCulture)).Append(' ');
            }

            return sb.ToString();
        }

        static List<Arm> RevisionArms()
        {
            Func<int, bool, CrystalOwnParts> make = (scale, eadl) => new CrystalOwnParts { Series = false, Scale = scale, LBindingFromEadl = eadl };
            var arms = new List<Arm>
            {
                new Arm { Name = "без слоя", None = true },
                new Arm { Name = "прежний", Parts = make(CrystalOwnActivityBuilder.ScaleEnergy, false) },
                new Arm { Name = "L из EADL", Parts = make(CrystalOwnActivityBuilder.ScaleEnergy, true) },
                new Arm { Name = "свет якорь662", Parts = make(CrystalOwnActivityBuilder.ScaleAnchor, true) },
                new Arm { Name = "свет фотон", Parts = make(CrystalOwnActivityBuilder.ScalePhoton, true) },
            };
            CrystalOwnParts shape = make(CrystalOwnActivityBuilder.ScalePhoton, true);
            shape.BetaOverride = SandlerShape;
            arms.Add(new Arm { Name = "+форма опыт", Parts = shape });
            CrystalOwnParts theory = make(CrystalOwnActivityBuilder.ScalePhoton, true);
            theory.BetaOverride = beta => SandlerShape(beta, true, false);
            arms.Add(new Arm { Name = "+форма λ", Parts = theory });
            CrystalOwnParts capture = make(CrystalOwnActivityBuilder.ScalePhoton, true);
            capture.KCaptureOverride = MeasuredKCapture;
            arms.Add(new Arm { Name = "+P_K 0.670", Parts = capture });
            CrystalOwnParts both = make(CrystalOwnActivityBuilder.ScalePhoton, true);
            both.BetaOverride = SandlerShape;
            both.KCaptureOverride = MeasuredKCapture;
            arms.Add(new Arm { Name = "+форма +P_K", Parts = both });
            CrystalOwnParts energyShape = make(CrystalOwnActivityBuilder.ScaleEnergy, false);
            energyShape.BetaOverride = SandlerShape;
            arms.Add(new Arm { Name = "прежн.+форма", Parts = energyShape });

            // Те же — свободной амплитудой, низ гистограммы ниже 20 кэВ снят (мера `fixE2`).
            foreach (Arm arm in arms.ToArray())
            {
                if (arm.None)
                {
                    continue;
                }

                var free = new CrystalOwnParts
                {
                    Series = false,
                    Scale = arm.Parts.Scale,
                    LBindingFromEadl = arm.Parts.LBindingFromEadl,
                    BetaOverride = arm.Parts.BetaOverride,
                    KCaptureOverride = arm.Parts.KCaptureOverride,
                    FreeNatural = true,
                };
                arms.Add(new Arm { Name = "св:" + arm.Name, Parts = free, FloorKev = 20.0 });
            }

            return arms;
        }

        static double Centroid(CrystalOwnEntry entry, double binKev, double lo, double hi, out double sum)
        {
            sum = 0.0;
            double moment = 0.0;
            for (int i = 0; i < entry.PerDecay.Length; i++)
            {
                double e = i * binKev;
                if (e >= lo && e <= hi)
                {
                    sum += entry.PerDecay[i];
                    moment += entry.PerDecay[i] * e;
                }
            }

            return sum > 0.0 ? moment / sum : 0.0;
        }

        static double MeanDeposit(CrystalOwnEntry entry, double binKev)
        {
            double moment = 0.0;
            for (int i = 0; i < entry.PerDecay.Length; i++)
            {
                moment += entry.PerDecay[i] * i * binKev;
            }

            return moment;
        }

        /// <summary>Ожидаемая энергия связи, уносимая вакансиями конверсии, на один проход каскада с уровня, кэВ.</summary>
        static double BoundExpected(ElectronBranchSpec spec, int level, int depth)
        {
            if (depth > 64 || spec.LevelCount == null || level < 0 || level >= spec.LevelCount.Length || spec.LevelCount[level] <= 0)
            {
                return 0.0;
            }

            double sum = 0.0, prevTr = 0.0;
            int first = spec.LevelFirst[level];
            for (int tr = first; tr < first + spec.LevelCount[level]; tr++)
            {
                double pTr = spec.TransitionCdf[tr] - prevTr;
                prevTr = spec.TransitionCdf[tr];
                double transition = spec.EmitKev[tr * ElectronBranchSpec.EmitKinds];
                double bound = 0.0, prevKind = spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds];
                for (int kind = 1; kind < ElectronBranchSpec.EmitKinds; kind++)
                {
                    double pk = spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds + kind] - prevKind;
                    prevKind = spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds + kind];
                    double e = spec.EmitKev[tr * ElectronBranchSpec.EmitKinds + kind];
                    bound += pk * Math.Max(0.0, transition - Math.Max(0.0, e));
                }

                sum += pTr * (bound + BoundExpected(spec, spec.TransitionTo[tr], depth + 1));
            }

            return sum;
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, п. 1) ЭНЕРГИЯ СВЯЗИ ПОСЛЕ КОНВЕРСИИ ОСЕДАЕТ. Лютециевая
        /// сцена, бета выключена (остаётся каскад уровня 597 кэВ дочки Lu-176). Три меры:
        /// баланс энергии (прибавка средней осевшей энергии = ожидаемой энергии связи из строки
        /// ветви), доля пика полного поглощения каскада и отсутствие событий выше суммы каскада.
        /// Мутант — рычаг <see cref="CrystalOwnParts.ConversionRelaxation"/> выключен: те же проверки
        /// на нём обязаны отказать (печатается «мутант пойман»).
        /// </summary>
        static void ConversionChecks(GeometryModel geometry, int histories)
        {
            Console.WriteLine();
            Console.WriteLine("=== Конверсия: энергия связи вакансии ({0}) ===", geometry.Crystal.Name);
            ElectronBranchSpec spec = null;
            double share = 0.0;
            foreach (FsaBeta beta in FsaBetaLibrary.Branches("176LU"))
            {
                if (beta == null || beta.IsLine || beta.Positron)
                {
                    continue;
                }

                string reason;
                ElectronBranchSpec candidate = FsaBetaLibrary.EventSpec(beta, out reason);
                Console.WriteLine("ветвь {0:F1} кэВ, {1:F2} %, уровень {2}", beta.EndpointKev, beta.Intensity,
                                  candidate != null ? candidate.LevelKev.ToString("F2", CultureInfo.InvariantCulture) : "нет: " + reason);
                if (candidate != null && spec == null)
                {
                    spec = candidate;
                    share = beta.Intensity / 100.0;
                }
            }

            if (spec == null)
            {
                Check("строка ветви Lu-176 собрана", false);
                return;
            }

            for (int tr = 0; tr < spec.TransitionTo.Length; tr++)
            {
                var sb = new StringBuilder();
                double prev = 0.0;
                for (int kind = 0; kind < ElectronBranchSpec.EmitKinds; kind++)
                {
                    double pk = spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds + kind] - prev;
                    prev = spec.EmitCdf[tr * ElectronBranchSpec.EmitKinds + kind];
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0}:{1:F3}@{2:F2}", "γKLMN"[kind], pk, spec.EmitKev[tr * ElectronBranchSpec.EmitKinds + kind]);
                }

                Console.WriteLine("  переход {0}:{1}", tr, sb);
            }

            double expected = share * BoundExpected(spec, spec.StartLevel, 0);
            double cascade = spec.LevelKev;
            Console.WriteLine("ожидаемая энергия связи вакансий конверсии: {0:F3} кэВ на распад; сумма каскада {1:F2} кэВ", expected, cascade);

            var entries = new CrystalOwnEntry[2];
            for (int arm = 0; arm < 2; arm++)
            {
                string note;
                var parts = new CrystalOwnParts { Beta = false, Series = false, ConversionRelaxation = arm == 1, Scale = CrystalOwnActivityBuilder.ScaleEnergy };
                ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, parts, out note);
                entries[arm] = section.Find("176LU", "beta");
                if (entries[arm] == null)
                {
                    Check("запись Lu-176 в разделе есть", false);
                    return;
                }
            }

            var mean = new double[2];
            var peak = new double[2];
            var above = new double[2];
            var low = new double[2];
            for (int arm = 0; arm < 2; arm++)
            {
                mean[arm] = MeanDeposit(entries[arm], 2.0);
                peak[arm] = Band(entries[arm], 2.0, cascade - 3.0, cascade + 3.0);
                above[arm] = Band(entries[arm], 2.0, cascade + 3.01, 3000.0);
                low[arm] = Band(entries[arm], 2.0, 0.0, 84.0);
                Console.WriteLine("{0,-28} средняя {1:F3} кэВ/расп, пик {2:F0}±3: {3:F4}, ниже 84 кэВ: {4:F4}, выше суммы: {5:F5}",
                                  arm == 1 ? "с релаксацией вакансии" : "без неё (прежний счёт)", mean[arm], cascade, peak[arm], low[arm], above[arm]);
            }

            double gain = mean[1] - mean[0];
            Console.WriteLine("прибавка средней: {0:F3} кэВ/расп, {1:F3} от ожидаемой", gain, expected > 0.0 ? gain / expected : 0.0);

            // Одни и те же три проверки — исправленному счёту и мутанту.
            for (int arm = 1; arm >= 0; arm--)
            {
                double got = mean[arm] - mean[0];
                bool balance = expected > 0.0 && got > 0.90 * expected && got < 1.02 * expected;
                bool sharp = peak[arm] > 3.0 * peak[0] && arm == 1 || (arm == 0 && false);
                bool lowGone = low[arm] < 0.5 * low[0] || low[0] <= 0.0;
                bool ok = balance && peak[arm] > 0.4 && lowGone;
                if (arm == 1)
                {
                    Check("баланс: прибавка средней 0.90…1.02 ожидаемой энергии связи (остаток — вылет K-рентгена)", balance);
                    Check("пик полного поглощения каскада: доля больше 0.4 на распад и втрое выше прежней", peak[1] > 0.4 && sharp);
                    Check("ниже 84 кэВ (88.35 минус связь) событий вдвое меньше прежнего", lowGone);
                    Check("выше суммы каскада событий нет (энергия не создаётся)", above[1] < 1e-4);
                }
                else
                {
                    Console.WriteLine(ok ? "⛔ МУТАНТ НЕ ПОЙМАН: прежний счёт проходит те же проверки" : "мутант пойман: прежний счёт (рычаг выключен) проверки не проходит");
                    if (ok)
                    {
                        bad++;
                    }
                }
            }
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, п. 2) СЛОЙ ПО СВЕТУ. Печатает фотонную шкалу сцены и
        /// положения пиков La-138 в трёх шкалах. Литература: K-пик 35.5 кэВ (Quarati 2013, Cheng
        /// 2020), пик суммы 1468–1472. Контроль шкалы: голая линия 1435.8 (без релаксации) в
        /// фотонной шкале обязана остаться на своей энергии — так стоят пики фотонных строк
        /// матрицы; мутант — шкала «якорь 661.657»: линию сдвигает на s(1436)/s(662).
        /// </summary>
        static void LightChecks(GeometryModel geometry, int histories)
        {
            Console.WriteLine();
            Console.WriteLine("=== Слой по свету ({0}) ===", geometry.Crystal.Name);
            CrystalOwnActivityBuilder.PhotonLightScale scale = CrystalOwnActivityBuilder.MeasurePhotonScale(geometry, 2.0, 0x274);
            if (scale == null)
            {
                Check("у кристалла есть кривая света", false);
                return;
            }

            Console.WriteLine("фотонная шкала: свет пика на кэВ, s(E); якорь 661.657 = {0:F5}", scale.AnchorPerKev);
            for (int i = 0; i < scale.EnergyKev.Length; i++)
            {
                Console.WriteLine("  {0,8:F1} кэВ  s = {1:F5}  к якорю {2:F4}", scale.EnergyKev[i], scale.PerKev[i], scale.PerKev[i] / scale.AnchorPerKev);
            }

            string[] names = { "энергия (прежний счёт)", "свет на якорь 661.657", "свет, фотонная шкала" };
            var low = new double[3];
            var sum = new double[3];
            var line = new double[3];
            for (int mode = 0; mode < 3; mode++)
            {
                string note;
                var parts = new CrystalOwnParts { Series = false, Scale = mode };
                CrystalOwnEntry la = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, parts, out note).Find("138LA", "beta");
                var bare = new CrystalOwnParts { Series = false, Scale = mode, Beta = false, BranchGamma = false, DaughterRelaxation = false };
                CrystalOwnEntry only = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, bare, out note).Find("138LA", "beta");
                double a, b, c, d;
                low[mode] = Centroid(la, 2.0, 26.0, 44.0, out a);
                sum[mode] = Centroid(la, 2.0, 1456.0, 1486.0, out b);
                double mid = Centroid(la, 2.0, 1436.0, 1450.0, out d);
                line[mode] = Centroid(only, 2.0, 1424.0, 1448.0, out c);
                Console.WriteLine("{0,-26} K-пик {1:F2} кэВ ({2:F4}/расп), пик суммы {3:F2} ({4:F4}), пик L/M {5:F2} ({6:F4}); голая 1435.8 → {7:F2}",
                                  names[mode], low[mode], a, sum[mode], b, mid, d, line[mode]);
            }

            Check("шкала энергии: K-пик на энергии связи K Ba 37.44 ± 0.7 кэВ, сумма 1473.2 ± 1.0", Math.Abs(low[0] - 37.44) < 0.7 && Math.Abs(sum[0] - 1473.2) < 1.0);
            Check("фотонная шкала: голая линия 1435.8 остаётся на месте (± 0.7 кэВ)", Math.Abs(line[2] - 1435.8) < 0.7);
            Check("фотонная шкала: K-пик ниже энергии связи (свет мелких электронов меньше)", low[2] < low[0] - 0.3);
            Check("фотонная шкала: пик суммы ниже 1473.2", sum[2] < sum[0] - 0.3);
            bool mutant = Math.Abs(line[1] - 1435.8) < 0.7;
            Console.WriteLine(mutant ? "⚠ мутант «якорь 661.657» линию 1435.8 не сдвинул: s(1436) ≈ s(662), шкалы на этой сцене неразличимы по линии"
                                     : "мутант пойман: шкала «якорь 661.657» ставит голую линию 1435.8 на " + line[1].ToString("F2", CultureInfo.InvariantCulture));
            Console.WriteLine("литература: K-пик 35.5 кэВ (Quarati 2013; Cheng 2020), пик суммы 1468–1472 (Quarati 2013, Zeng 2016, Milbrath 2005)");
        }

        /// <summary>
        /// (`AMBER274`, ревизия по статьям, п. 4) ОПЫТНАЯ ФОРМА БЕТЫ La-138 против формы кода: средние
        /// (DDEP 91.1(21) кэВ), отношение плотностей и полосы слоя на распад.
        /// </summary>
        static void ShapeChecks(GeometryModel geometry, int histories)
        {
            Console.WriteLine();
            Console.WriteLine("=== Форма беты La-138: код против опыта (Sandler 2019) ===");
            FsaBeta code = null;
            foreach (FsaBeta beta in FsaBetaLibrary.Branches("138LA"))
            {
                if (beta != null && !beta.IsLine && !beta.Positron)
                {
                    code = beta;
                }
            }

            if (code == null)
            {
                Check("ветвь β⁻ La-138 в базе есть", false);
                return;
            }

            FsaBeta exp = SandlerShape(code);
            double meanCode, meanExp, meanTheory, meanNaive;
            double areaCode = FsaBetaLibrary.Integral(code, out meanCode);
            double areaExp = FsaBetaLibrary.Integral(exp, out meanExp);
            FsaBetaLibrary.Integral(SandlerShape(code, true, false), out meanTheory);
            FsaBetaLibrary.Integral(SandlerShape(code, false, true), out meanNaive);
            Console.WriteLine("ветвь: граница {0:F2} кэВ, {1:F2} %, переход «{2}», порядок {3}, средняя базы {4:F1} кэВ",
                              code.EndpointKev, code.Intensity, code.Transition, code.UniqueOrder, code.MeanKev);
            Console.WriteLine("средняя формы кода (λ = 1) {0:F2} кэВ; теория с кулоновскими λ {1:F2}; теория λ × C_exp {2:F2}; λ = 1 × C_exp {3:F2}; DDEP 91.1(21) кэВ",
                              meanCode, meanTheory, meanExp, meanNaive);
            foreach (double tk in new[] { 5.0, 20.0, 60.0, 120.0, 200.0, 250.0 })
            {
                double wk = 1.0 + tk / 510.99895;
                Console.WriteLine("  λ₂({0:F0} кэВ) = {1:F3}, λ₃ = {2:F3}", tk, Lambda(2, code.DaughterZ, code.MassNumber, wk), Lambda(3, code.DaughterZ, code.MassNumber, wk));
            }
            foreach (double t in new[] { 5.0, 10.0, 20.0, 40.0, 60.0, 85.0, 120.0, 160.0, 200.0, 240.0 })
            {
                double c = FsaBetaLibrary.Density(code, t) / areaCode;
                double e = FsaBetaLibrary.Density(exp, t) / areaExp;
                Console.WriteLine("  T = {0,5:F0} кэВ: код {1:E3}, опыт {2:E3}, опыт/код {3:F3}", t, c, e, c > 0.0 ? e / c : 0.0);
            }

            Check("форма кода жёстче DDEP: средняя кода выше 91.1 + 3σ", meanCode > 91.1 + 3.0 * 2.1);
            Check("опытная форма: средняя в 91.1 ± 3σ (± 6.3 кэВ)", Math.Abs(meanExp - 91.1) < 6.3);

            double[] edges = { 0.0, 20.0, 45.0, 100.0, 150.0, 200.0, 270.0, 785.0, 850.0, 900.0, 950.0, 1000.0, 1060.0 };
            var entries = new CrystalOwnEntry[2];
            for (int arm = 0; arm < 2; arm++)
            {
                string note;
                var parts = new CrystalOwnParts { Series = false, Capture = false };
                if (arm == 1)
                {
                    parts.BetaOverride = SandlerShape;
                }

                entries[arm] = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, parts, out note).Find("138LA", "beta");
            }

            Console.WriteLine("слой, только β-ветвь (бета + 788.7), отсчётов на распад по полосам:");
            for (int b = 0; b + 1 < edges.Length; b++)
            {
                double c = Band(entries[0], 2.0, edges[b], edges[b + 1] - 0.01);
                double e = Band(entries[1], 2.0, edges[b], edges[b + 1] - 0.01);
                Console.WriteLine("  {0,5:F0}–{1,-5:F0} код {2:F5}  опыт {3:F5}  опыт/код {4:F3}", edges[b], edges[b + 1], c, e, c > 0.0 ? e / c : 0.0);
            }
        }

        static void Parts(string path, bool useBackground, bool subtractEmbedded, double inject,
                          GeometryModel geometry, ResponseMatrix matrix, int histories, List<Arm> custom)
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

            var arms = custom ?? new List<Arm>
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
