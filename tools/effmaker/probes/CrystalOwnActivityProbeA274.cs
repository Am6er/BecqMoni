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
    ///      χ²/ndf, активность пробы и отсчёты слоя ДО и ПОСЛЕ; сторож привязки (`--mode=fsa`,
    ///      13.11.2026, `a274g`) — опор со слоем не меньше и положение слоя против данных в
    ///      окнах K 33–47 и La-138 1420–1530 кэВ не дальше 0.15 ПШПВ при амплитуде 1 ± 0.2, отданная
    ///      привязка со слоем не дальше 0.15 ПШПВ от привязки без слоя; мутанты `--mut-shift=кэВ[:от:до]`
    ///      (сдвиг гистограммы слоя), `--own-scale=k`, `--crit-amp=k[:от:до]`, `--crit-width=f`, `--crit-cal=g`;
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
        static readonly HashSet<string> armFilter = new HashSet<string>(StringComparer.Ordinal);

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
            double mutShift = 0.0, mutLo = -1.0, mutHi = 1e9;
            double camp = 1.0, campLo = -1.0, campHi = 1e9, cwidth = 1.0, ccal = 1.0;
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
                else if (a.StartsWith("--mut-shift=", StringComparison.Ordinal))
                {
                    // (критик `crit295z`, перенесено полосой `a274g`) МУТАНТ сторожа привязки: сдвиг
                    // гистограммы слоя на кэВ, по желанию только в полосе [от, до] кэВ.
                    string[] mp = a.Substring(12).Split(':');
                    mutShift = double.Parse(mp[0], CultureInfo.InvariantCulture);
                    if (mp.Length == 3)
                    {
                        mutLo = double.Parse(mp[1], CultureInfo.InvariantCulture);
                        mutHi = double.Parse(mp[2], CultureInfo.InvariantCulture);
                    }
                }
                else if (a.StartsWith("--crit-amp=", StringComparison.Ordinal))
                {
                    string[] mp = a.Substring(11).Split(':');
                    camp = double.Parse(mp[0], CultureInfo.InvariantCulture);
                    if (mp.Length == 3) { campLo = double.Parse(mp[1], CultureInfo.InvariantCulture); campHi = double.Parse(mp[2], CultureInfo.InvariantCulture); }
                }
                else if (a.StartsWith("--crit-width=", StringComparison.Ordinal))
                {
                    cwidth = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--crit-cal=", StringComparison.Ordinal))
                {
                    ccal = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
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
                else if (a.StartsWith("--arms=", StringComparison.Ordinal))
                {
                    // (`a274t`) только плечи с этими номерами (с нуля, порядок таблицы)
                    foreach (string part in a.Substring(7).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        armFilter.Add(part.Trim());
                    }
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

            if (mode == "kpeak")
            {
                // (`a274t`) ширина K-пика слоя у 37 кэВ: подгонка, синтетика, мутант
                ResponseMatrix kMatrix = Matrix(lanthanum, matrixMode);
                KPeakChecks(spectrumPath, useBackground, lanthanum, kMatrix, histories);
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "recipe")
            {
                // (`a274t`) клеймо раздела слоя с версией рецепта
                RecipeChecks(lanthanum, Math.Min(histories, 20000));
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
                return bad == 0 ? 0 : 1;
            }

            if (mode == "sphere")
            {
                // (`a274t`) λ заряженной сферы: решатель, сходимость, средние к базе, мутант точечного ядра
                SphereChecks();
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
                MutShift(section, mutShift, mutLo, mutHi);
                CritAmp(section, camp, campLo, campHi);
                CritWidth(section, rd, cwidth);
                CritCal(rd, ccal);
                calMutated = ccal != 1.0;
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

        /// <summary>
        /// МУТАНТ (критик `crit295z`, 13.11.2026): гистограмма слоя сдвинута на <paramref name="shift"/> кэВ
        /// (линейная интерполяция, площадь сохраняется) в полосе [lo, hi] кэВ. Сторож привязки обязан его поймать.
        /// </summary>
        static void MutShift(ResponseCrystalSection section, double shift, double lo, double hi)
        {
            if (section == null || shift == 0.0)
            {
                return;
            }

            double bin = section.BinKev;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double[] src = entry.PerDecay;
                int n = src.Length;
                var dst = new double[n];
                double before = 0.0, after = 0.0;
                for (int i = 0; i < n; i++)
                {
                    before += src[i];
                }

                for (int i = 0; i < n; i++)
                {
                    double e = i * bin;
                    if (e < lo || e > hi)
                    {
                        dst[i] += src[i];
                        continue;
                    }

                    double target = i + shift / bin;
                    int j = (int)Math.Floor(target);
                    double f = target - j;
                    if (j >= 0 && j < n) dst[j] += src[i] * (1.0 - f);
                    if (j + 1 >= 0 && j + 1 < n) dst[j + 1] += src[i] * f;
                }

                for (int i = 0; i < n; i++)
                {
                    after += dst[i];
                }

                entry.PerDecay = dst;
                Console.WriteLine("МУТАНТ: {0} сдвиг {1:F2} кэВ в полосе {2:F0}…{3:F0} кэВ, сумма {4:F5} -> {5:F5}",
                                  entry.Nuclide, shift, lo, hi, before, after);
            }
        }

        // ---- МУТАНТЫ КРИТИКА crit274g (перенесены полосой `a274g`): амплитуда в полосе, ширина, шкала спектра ----
        static void CritAmp(ResponseCrystalSection section, double k, double lo, double hi)
        {
            if (section == null || k == 1.0) return;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double[] h = (double[])entry.PerDecay.Clone();
                double b = 0, c = 0;
                for (int i = 0; i < h.Length; i++) { b += h[i]; double e = i * section.BinKev; if (e >= lo && e <= hi) h[i] *= k; c += h[i]; }
                entry.PerDecay = h;
                Console.WriteLine("КРИТИК: {0} амплитуда ×{1:F3} в полосе {2:F0}…{3:F0} кэВ, сумма {4:F5} -> {5:F5}", entry.Nuclide, k, lo, hi, b, c);
            }
        }

        static void CritWidth(ResponseCrystalSection section, ResultData rd, double f)
        {
            if (section == null || f == 1.0) return;
            double bin = section.BinKev;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double[] src = entry.PerDecay;
                var dst = new double[src.Length];
                double b = 0, c = 0;
                for (int i = 0; i < src.Length; i++)
                {
                    b += src[i];
                    if (src[i] == 0.0) continue;
                    double e = Math.Max(i * bin, 5.0);
                    double sig = Math.Sqrt(f * f - 1.0) * DeviceFwhmKev(rd, e) / 2.3548;
                    int half = (int)Math.Ceiling(5.0 * sig / bin) + 1;
                    double norm = 0;
                    var kern = new double[2 * half + 1];
                    for (int k = -half; k <= half; k++) { kern[k + half] = Math.Exp(-0.5 * (k * bin / sig) * (k * bin / sig)); norm += kern[k + half]; }
                    for (int k = -half; k <= half; k++) { int t = i + k; if (t >= 0 && t < dst.Length) dst[t] += src[i] * kern[k + half] / norm; }
                }
                for (int i = 0; i < dst.Length; i++) c += dst[i];
                entry.PerDecay = dst;
                Console.WriteLine("КРИТИК: {0} ширина ×{1:F2} (добавочная σ), сумма {2:F5} -> {3:F5}", entry.Nuclide, f, b, c);
            }
        }

        static void CritCal(ResultData rd, double g)
        {
            if (g == 1.0) return;
            var pc = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pc == null) { Console.WriteLine("КРИТИК: калибровка не полином"); return; }
            var c = (double[])pc.Coefficients.Clone();
            for (int i = 0; i < c.Length; i++) c[i] *= g;
            pc.Coefficients = c;
            Console.WriteLine("КРИТИК: шкала спектра ×{0:F4}: 1468 кэВ данных теперь читается как {1:F2} кэВ", g, 1468.0 * g);
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
            double[] layerWith = lastFixed;
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

            foreach (FsaComponentResult component in with.Components)
            {
                if (component.Kind != FsaComponentKind.Nuisance && component.CountRate > 0.0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "z со слоем: {0} {1:F2}", component.Name, component.Z));
                }
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
            // ⛔ (`AMBER274`; полосы `a295z`, `a274g`; критики `crit295z`, `crit274g`) СТОРОЖ ПРИВЯЗКИ ПРИ ВКЛЮЧЁННОМ СЛОЕ.
            //
            // Чем он НЕ является. Прежний сторож требовал «усиление со слоем то же до 1e-9» на ОТДАННОМ разборе.
            // Это неверно дважды. (1) Слой вычитает известную часть данных, и опоры, лежащие на его линиях
            // (Sm K 40.1 кэВ на K-пике слоя у 37 кэВ, Eu-152 1408 кэВ рядом с La-138 1436/1468 кэВ), ОБЯЗАНЫ
            // сдвинуться (`a295z`: Σ(сдвиг/σ)² взятых опор 5.22 → 4.89 — слой опоры исправляет). (2) Отданный разбор
            // несёт решение отката привязки (`AnalyzeRolledBack`), а у `LaBrBril_Eu152` его переключают каналы
            // 22.9 и 25.6 кэВ — полка данных ниже K-пика. Это НЕ ширина K-пика: пик данных 5.0–5.3 кэВ при
            // 4.66 кэВ прибора (`a274t`), а избыток 24–33 кэВ — собственный, механизм не установлен (`a274x`).
            // «Средний (сдвиг/σ)² опор не больше» (`a295z`) тоже не мера (`crit295z`): σ опоры растёт по Бирге
            // вместе с невязкой окна, и мера награждает худшую модель. Её число печатается, но не судится.
            //
            // Что судится:
            //   1. Опор со слоем не меньше, чем без него (откат выкл).
            //   2. ОТДАННАЯ ПРИВЯЗКА: усиление и нуль со слоем против разбора без слоя — сдвиг шкалы у энергий
            //      взятых опор не больше 0.15 ПШПВ (`crit274g`: слой ×2 уводил усиление Th228 на 0.009).
            //   3. ПОЛОЖЕНИЕ И АМПЛИТУДА СЛОЯ ПРОТИВ ДАННЫХ в окнах его линий: y ≈ прочее + a·слой(E − d) на
            //      ОТДАННОМ разборе со слоем (`FitLayerShift`; прочее — модель разбора, слой — вычтенный на шкале его
            //      привязки, a — линейно, d — сеткой). Плечо «откат выкл» печатается: с правкой `a274g` слой следует
            //      привязке, а привязку этого плеча у `LaBrBril_Th228` разбор и откатывает (у K она на 2 кэВ мимо). Окна: K 33–47 кэВ (выше полки ниже 33 кэВ, к которой мера съезжала бы) и La-138
            //      1420–1530 кэВ (1436 и сумма с K у 1468; ядро Eu-152 1408 кэВ вне окна). Окно судится, если
            //      поданный слой в нём главный (доля в модели окна ≥ 0.5). В судимом окне: минимум χ² на краю сетки
            //      d или неочерченный минимум (σ_d = ∞) — ПРОВАЛ (`crit274g`: так проходили K +8, K −6); σ_d не
            //      меньше порога — окно не меряет; иначе |d| ≤ 0.15 ПШПВ и |a − 1| ≤ 0.2. Порог строгий, без запаса
            //      σ_d — решение Amber 13.11.2026 вопросником, дословно: «Строгий 0.15 (Рекомендую)». Провал у
            //      `LaBrBril_Eu152` при шкале спектра ×1.005 — тревога ИСТИННАЯ (`crit274g2`, заход 3): со слоем разбор
            //      откатывает верное усиление 1.005512 в 1.000000 (шкала результата −7.76 кэВ у 1408), откат решает
            //      полоса 28–47 кэВ, которую модель слоя не описывает (полка под K-пиком) — до включения слоя.
            // У `LaBrBril_Eu152` K-окно занято Sm K источника (доля слоя 0.31): K там не судится, сдвиг слоя у K на
            // этом спектре ловит только `LaBrBril_Th228`. Ширина слоя печатается, но не судится (журнал `a274g`).
            rollbackOff = true;
            FsaResult bareA = Run(rd, background, geometry, matrix, null);
            FsaResult layerA = Run(rd, background, geometry, matrix, section);
            double[] layerFixed = lastFixed;
            rollbackOff = false;
            if (bareA == null || layerA == null || layerFixed == null)
            {
                Check("разбор со слоем при выключенном откате прошёл", false);
                return;
            }

            int n0, n1;
            double s0 = AnchorChi2(bareA, out n0), s1 = AnchorChi2(layerA, out n1);
            Console.WriteLine("привязка при выключенном откате: gain {0:F6} → {1:F6}; опор {2} → {3}; Σ(сдвиг/σ)² {4:F3} → {5:F3} (не судится)",
                              bareA.Gain, layerA.Gain, n0, n1, s0, s1);
            Check("опор со слоем не меньше, чем без него (откат выкл)", n0 > 0 && n1 >= n0);
            foreach (FsaComponentResult component in layerA.Components)
            {
                if (component.Kind != FsaComponentKind.Nuisance && component.CountRate > 0.0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "z со слоем, откат выкл: {0} {1:F2}", component.Name, component.Z));
                }
            }
            AnchorTable("без слоя, откат выкл", bareA);
            AnchorTable("со слоем, откат выкл", layerA);

            // Отданная привязка: сдвиг шкалы слоем у энергий взятых опор разбора без слоя.
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            double worst = 0.0, worstKev = 0.0, worstShift = 0.0;
            foreach (FsaScaleAnchor anchor in bareA.ScaleAnchors ?? new List<FsaScaleAnchor>())
            {
                if (!anchor.Used || double.IsNaN(anchor.LineKev))
                {
                    continue;
                }

                double e = anchor.LineKev;
                int c = (int)Math.Round(cal.EnergyToChannel(e, rd.EnergySpectrum.NumberOfChannels));
                double kevPerChannel = cal.ChannelToEnergy(c + 1) - cal.ChannelToEnergy(c);
                double shift = (with.Gain - without.Gain) * e + (with.OffsetChannels - without.OffsetChannels) * kevPerChannel;
                double ratio = Math.Abs(shift) / DeviceFwhmKev(rd, e);
                if (ratio >= worst)
                {
                    worst = ratio;
                    worstKev = e;
                    worstShift = shift;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "отданная привязка: gain {0:F6} → {1:F6}, нуль {2:F3} → {3:F3} кан.; наибольший сдвиг шкалы {4:+0.00;-0.00} кэВ у {5:F1} кэВ = {6:F3} ПШПВ",
                without.Gain, with.Gain, without.OffsetChannels, with.OffsetChannels, worstShift, worstKev, worst));
            Check("отданная привязка со слоем не дальше 0.15 ПШПВ от привязки без слоя", worst <= 0.15);

            var data = new double[rd.EnergySpectrum.NumberOfChannels];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = rd.EnergySpectrum.Spectrum[i];
            }

            int judged = 0;
            foreach (double[] w in new[] { new[] { 33.0, 47.0, 37.0, 5.0, 0.05 }, new[] { 1420.0, 1530.0, 1468.0, 20.0, 0.25 } })
            {
                double tolerance = 0.15 * DeviceFwhmKev(rd, w[2]);
                // Судится ОТДАННЫЙ разбор: слой следует его привязке (`FsaAnalyzer.CrystalOwnLayerAt`), и мера видит
                // и слой, и шкалу, на которой его вычли. Плечо «откат выкл» — печать: его привязку разбор и откатывает.
                LayerShift off = FitLayerShift(data, layerA.Model, layerFixed, cal, w[0], w[1], w[3], w[4], true);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  (не судится) откат выкл, gain {0:F6}: d {1:+0.00;-0.00} ± {2:F2} кэВ{3}, a {4:F3}",
                    layerA.Gain, off.ShiftKev, off.SigmaKev, off.AtEdge ? " на краю" : "", off.Amp));
                LayerShift ls = FitLayerShift(data, with.Model, layerWith, cal, w[0], w[1], w[3], w[4], true);
                double stretch = FitLayerStretch(data, with.Model, layerWith, cal, w[0], w[1], ls.ShiftKev);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "положение слоя {0:F0}–{1:F0} кэВ: d {2:+0.00;-0.00} ± {3:F2} кэВ{4} (a {5:F3}, χ² {6:F0} на {7} кан., при d = 0 — {8:F0}); доля слоя {9:F2}; порог 0.15 ПШПВ = {10:F2} кэВ; растяжение слоя (не судится) {11:F2}",
                    w[0], w[1], ls.ShiftKev, ls.SigmaKev, ls.AtEdge ? " НА КРАЮ СЕТКИ" : "", ls.Amp, ls.Chi2, ls.Points, ls.Chi2AtZero, ls.Share, tolerance, stretch));
                if (ls.Share < 0.5)
                {
                    Console.WriteLine("  окно занято линиями источника (слой меньше половины модели) — положение слоя здесь не судится");
                    continue;
                }

                string where = string.Format(CultureInfo.InvariantCulture, "{0:F0}–{1:F0} кэВ", w[0], w[1]);
                if (ls.AtEdge)
                {
                    judged++;
                    Check(string.Format(CultureInfo.InvariantCulture, "минимум χ² по сдвигу слоя внутри сетки ±{0:F0} кэВ, окно {1}", w[3], where), false);
                    continue;
                }

                if (double.IsInfinity(ls.SigmaKev))
                {
                    double gap = ls.Chi2AtZero - ls.Chi2;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  минимум не очерчен: χ²(0) − χ²min = {0:F0} при Бирге {1:F1}", gap, ls.Birge));
                    // Слой в окне главный, а своё положение не задаёт: образ не похож на данные (K +8: a = −0.13).
                    judged++;
                    Check("минимум χ² по сдвигу слоя очерчен там, где слой главный — окно " + where, false);
                    continue;
                }

                if (!(ls.SigmaKev < tolerance))
                {
                    Console.WriteLine("  σ положения не меньше порога — окно положение не меряет, не судится");
                    continue;
                }

                judged++;
                bool placed = Math.Abs(ls.ShiftKev) <= tolerance;
                if (!placed && calMutated && w[0] > 1000.0)
                {
                    // Решение Amber 13.11.2026 вопросником, дословно: «Строгий 0.15 (Рекомендую)».
                    Console.WriteLine("  ⚠ шкала спектра изменена мутантом (`--crit-cal`). Если разбор со слоем откатил привязку"
                                      + " и отдал данные на неверной шкале, провал верный: так у `LaBrBril_Th228`, и так у"
                                      + " `LaBrBril_Eu152` при ×1.005 — откат бросает верное усиление 1.0055, его решает полоса"
                                      + " 28–47 кэВ, которую модель слоя не описывает (`crit274g2`; порог — решение Amber"
                                      + " 13.11.2026: «Строгий 0.15 (Рекомендую)»).");
                }

                Check("слой на месте в окне " + where + ": |d| ≤ 0.15 ПШПВ", placed);
                Check("амплитуда слоя в окне " + where + ": |a − 1| ≤ 0.2", Math.Abs(ls.Amp - 1.0) <= 0.2);
            }

            Check("положение слоя судилось хотя бы в одном окне", judged > 0);
        }

        /// <summary>
        /// Ширина слоя против данных (печать, не суд): слой растянут вокруг своего центра тяжести в окне в s раз
        /// (s меньше 1 — уже), со сдвигом d, амплитуда линейно; s — сеткой 0.70…1.40 с шагом 0.01.
        /// </summary>
        static double FitLayerStretch(double[] y, double[] other, double[] layer, EnergyCalibration calibration,
                                      double lo, double hi, double shiftKev)
        {
            int c0 = -1, c1 = -1;
            for (int i = 0; i < y.Length; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                if (e >= lo && e <= hi)
                {
                    if (c0 < 0) c0 = i;
                    c1 = i;
                }
            }

            if (c0 < 0)
            {
                return double.NaN;
            }

            double kevPerCh = (calibration.ChannelToEnergy(c1 + 1) - calibration.ChannelToEnergy(c0)) / (c1 + 1 - c0);
            const int Margin = 40;
            const int Sub = 10;
            int w0 = Math.Max(0, c0 - Margin), w1 = Math.Min(layer.Length - 1, c1 + Margin);
            int n = w1 - w0 + 1;
            double centre = 0.0, mass = 0.0;
            for (int i = c0; i <= c1; i++)
            {
                centre += layer[i] * (i - w0 + 0.5);
                mass += layer[i];
            }

            centre = mass > 0.0 ? centre / mass : 0.5 * n;
            double bestChi = double.MaxValue, best = double.NaN;
            for (int k = 70; k <= 140; k++)
            {
                double sf = k / 100.0;
                var stretched = new double[n];
                for (int f = 0; f < n * Sub; f++)
                {
                    double x = (f + 0.5) / Sub;
                    double src = centre + (x - centre) / sf;
                    int j = (int)Math.Floor(src);
                    if (j >= 0 && j < n)
                    {
                        stretched[f / Sub] += layer[w0 + j] / Sub / sf;
                    }
                }

                double[] part = Reshape(stretched, shiftKev / kevPerCh, 0.0);
                double num = 0.0, den = 0.0;
                for (int i = c0; i <= c1; i++)
                {
                    double t = part[i - w0];
                    double wt = 1.0 / Math.Max(y[i], 1.0);
                    num += (y[i] - other[i]) * t * wt;
                    den += t * t * wt;
                }

                double a = den > 0.0 ? num / den : 0.0;
                double chi = 0.0;
                for (int i = c0; i <= c1; i++)
                {
                    double r = y[i] - other[i] - a * part[i - w0];
                    chi += r * r / Math.Max(y[i], 1.0);
                }

                if (chi < bestChi)
                {
                    bestChi = chi;
                    best = sf;
                }
            }

            return best;
        }

        /// <summary>ПШПВ прибора у энергии <paramref name="kev"/>, кэВ (кривая спектра).</summary>
        static double DeviceFwhmKev(ResultData rd, double kev)
        {
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;
            double channel = calibration.EnergyToChannel(kev, channels);
            int c = (int)Math.Round(channel);
            double kevPerChannel = calibration.ChannelToEnergy(c + 1) - calibration.ChannelToEnergy(c);
            return rd.FwhmCalibration.ChannelToFwhm(channel) * kevPerChannel;
        }

        static void AnchorTable(string title, FsaResult result)
        {
            Console.WriteLine("опоры [{0}] gain {1:F6}:", title, result.Gain);
            if (result.ScaleAnchors == null)
            {
                return;
            }

            foreach (FsaScaleAnchor a in result.ScaleAnchors)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} линия {1,8:F2} модель {2,8:F2} данные {3,8:F2} сдвиг {4,7:F3} σ {5,6:F3} доля {6:F2} z {7,7:F1} {8} {9}",
                    a.Component, a.LineKev, a.ModelKev, a.MeasuredKev, a.ShiftKev, a.SigmaKev, a.PeakShare, a.Z,
                    a.Used ? "ВЗЯТА" : "-", a.Refusal ?? ""));
            }
        }

        sealed class LayerShift
        {
            public double ShiftKev, SigmaKev, Amp, Chi2, Chi2AtZero, Share, Birge;
            public int Points;
            public bool AtEdge;
        }

        /// <summary>
        /// Положение вычтенного слоя против данных в окне [lo, hi] кэВ: y ≈ прочее + a·слой(E − d);
        /// d — сеткой ±dmax с шагом step, a — линейно (или 1). σ_d — по Δχ² = χ²/ndf (Бирге окна).
        /// </summary>
        static LayerShift FitLayerShift(double[] y, double[] other, double[] layer, EnergyCalibration calibration,
                                        double lo, double hi, double dmax, double step, bool freeAmp)
        {
            int c0 = -1, c1 = -1;
            for (int i = 0; i < y.Length; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                if (e >= lo && e <= hi)
                {
                    if (c0 < 0) c0 = i;
                    c1 = i;
                }
            }

            var res = new LayerShift { Chi2 = double.MaxValue };
            if (c0 < 0)
            {
                return res;
            }

            double kevPerCh = (calibration.ChannelToEnergy(c1 + 1) - calibration.ChannelToEnergy(c0)) / (c1 + 1 - c0);
            int margin = (int)Math.Ceiling(dmax / kevPerCh) + 15;
            int w0 = Math.Max(0, c0 - margin), w1 = Math.Min(layer.Length - 1, c1 + margin);
            var piece = new double[w1 - w0 + 1];
            Array.Copy(layer, w0, piece, 0, piece.Length);
            var ds = new List<double>();
            var chis = new List<double>();
            int steps = (int)Math.Round(dmax / step);
            for (int s = -steps; s <= steps; s++)
            {
                double d = s * step;
                double[] part = Reshape(piece, d / kevPerCh, 0.0);
                double num = 0.0, den = 0.0;
                for (int i = c0; i <= c1; i++)
                {
                    double t = part[i - w0];
                    double w = 1.0 / Math.Max(y[i], 1.0);
                    num += (y[i] - other[i]) * t * w;
                    den += t * t * w;
                }

                double a = freeAmp ? (den > 0.0 ? num / den : 0.0) : 1.0;
                double chi = 0.0;
                for (int i = c0; i <= c1; i++)
                {
                    double r = y[i] - other[i] - a * part[i - w0];
                    chi += r * r / Math.Max(y[i], 1.0);
                }

                ds.Add(d);
                chis.Add(chi);
                if (s == 0)
                {
                    res.Chi2AtZero = chi;
                }

                if (chi < res.Chi2)
                {
                    res.Chi2 = chi;
                    res.ShiftKev = d;
                    res.Amp = a;
                }
            }

            res.Points = c1 - c0 + 1;
            double sl = 0.0, so = 0.0;
            for (int i = c0; i <= c1; i++)
            {
                sl += layer[i];
                so += other[i];
            }

            // Доля слоя — поданного разделом (амплитуда 1): судится ли окно, решает модель, а не подгонка.
            res.Share = sl / Math.Max(sl + so, 1.0);
            int ndf = Math.Max(1, res.Points - (freeAmp ? 2 : 1));
            double birge = Math.Max(1.0, res.Chi2 / ndf);
            // σ_d: полуширина интервала, где χ² ≤ χ²min + birge
            double left = double.NaN, right = double.NaN;
            int best = ds.IndexOf(res.ShiftKev);
            res.AtEdge = best == 0 || best == ds.Count - 1;
            res.Birge = birge;
            for (int k = best; k >= 0; k--)
            {
                if (chis[k] > res.Chi2 + birge) { left = ds[k]; break; }
            }

            for (int k = best; k < ds.Count; k++)
            {
                if (chis[k] > res.Chi2 + birge) { right = ds[k]; break; }
            }

            res.SigmaKev = double.IsNaN(left) || double.IsNaN(right) ? double.PositiveInfinity : 0.5 * (right - left);
            return res;
        }

        static double AnchorChi2(FsaResult result, out int used)
        {
            used = 0;
            double sum = 0.0;
            if (result.ScaleAnchors == null)
            {
                return 0.0;
            }

            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                if (!anchor.Used || !(anchor.SigmaKev > 0.0) || double.IsNaN(anchor.ShiftKev))
                {
                    continue;
                }

                used++;
                double z = anchor.ShiftKev / anchor.SigmaKev;
                sum += z * z;
            }

            return sum;
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

        /// <summary>(`a274g`) Шкала спектра изменена мутантом `--crit-cal` — для подписи известной ложной тревоги.</summary>
        static bool calMutated;
        static bool rollbackOff;

        static FsaResult Run(ResultData rd, EnergySpectrum background, GeometryModel geometry,
                             ResponseMatrix matrix, ResponseCrystalSection section)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            // Проба идёт по матрице без кривой эффективности: сцену она задаёт сама,
            // и требование «кривая с геометрией» (RequireGeometry) к ней не относится.
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = section != null;
            if (rollbackOff)
            {
                analyzer.AnchorRollback = false;
            }
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
            // (`a274g`) вычтенный слой — на шкале привязки отданного разбора (слой следует привязке, `crit274g`)
            lastFixed = result != null
                ? analyzer.CrystalOwnFixedCountsAt(result.Gain, result.OffsetChannels)
                : analyzer.CrystalOwnFixedCountsOfLastRun();
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
            // (`a274t`) Эти проверки — формы точечного ядра (`a274s`): сверка с копией её формулы.
            // Умолчание ключа теперь — заряженная сфера (её проверки — `--mode=sphere`).
            bool savedPoint = FsaBetaLibrary.CoulombLambdaPointNucleus;
            FsaBetaLibrary.CoulombLambdaPointNucleus = true;
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
            FsaBetaLibrary.CoulombLambdaPointNucleus = savedPoint;
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

        // ------------------------------------------------------------------
        // (`a274t`, 13.11.2026) λ ЗАРЯЖЕННОЙ СФЕРЫ — `--mode=sphere`
        // ------------------------------------------------------------------

        /// <summary>Нуклиды сверки средних: уникальные запреты лёгких/средних и тяжёлых ядер, β⁺.</summary>
        static readonly string[] SphereNuclides =
        {
            "40K", "138LA", "89SR", "91Y", "85KR", "36CL", "87RB", "10BE", "22NA", "26AL",
            "204TL", "228AC", "210TL", "234PA", "237U", "214BI", "214PB", "212BI", "208TL", "212PB", "210BI",
        };

        /// <summary>Таблица I Уилкинсона (1990) для электронов по Hayen и др. 2018: строки a₋₁, a₀…a₅, столбцы b₁…b₆.</summary>
        static readonly double[,] WilkinsonB =
        {
            { 0.115, -1.8123, 8.2498, -11.223, -14.854, 32.086 },
            { -0.00062, 0.007165, 0.01841, -0.53736, 1.2691, -1.5467 },
            { 0.02482, -0.5975, 4.84199, -15.3374, 23.9774, -12.6534 },
            { -0.14038, 3.64953, -38.8143, 172.1368, -346.708, 288.7873 },
            { 0.008152, -1.15664, 49.9663, -273.711, 657.6292, -603.7033 },
            { 1.2145, -23.9931, 149.9718, -471.2985, 662.1909, -305.6804 },
            { -1.5632, 33.4192, -255.1333, 938.5297, -1641.2845, 1095.358 },
        };

        /// <summary>L₀ Уилкинсона для электронов (Hayen 2018, ур. 16–17); R — в единицах ħ/(mₑc).</summary>
        static double WilkinsonL0(int z, double w, double radius)
        {
            double az = z / 137.035999084;
            double g = Math.Sqrt(1.0 - az * az);
            var a = new double[7];
            for (int n = 0; n < 7; n++)
            {
                for (int x = 1; x <= 6; x++)
                {
                    a[n] += WilkinsonB[n, x - 1] * Math.Pow(az, x);
                }
            }

            double sum = a[0] * radius / w;
            for (int n = 0; n <= 5; n++)
            {
                sum += a[n + 1] * Math.Pow(w * radius, n);
            }

            return 1.0 + 13.0 / 60.0 * az * az - az * w * radius * (41.0 - 26.0 * g) / (15.0 * (2.0 * g - 1.0))
                   - az * radius * g * (17.0 - 2.0 * g) / (30.0 * w * (2.0 * g - 1.0)) + sum
                   + 0.41 * (radius - 0.0164) * Math.Pow(az, 4.5);
        }

        /// <summary>F₀ точечного ядра в записи Hayen 2018, ур. 6 (множитель 4): к нему относится L₀ ур. 13.</summary>
        static double HayenF0(int z, double w, double radius)
        {
            double az = z / 137.035999084;
            double g = Math.Sqrt(1.0 - az * az);
            double p = Math.Sqrt(w * w - 1.0);
            double y = az * w / p;
            double ln = Math.Log(4.0) + 2.0 * (g - 1.0) * Math.Log(2.0 * p * radius) + Math.PI * y
                        + 2.0 * LnAbsGamma(g, y) - 2.0 * LnAbsGamma(1.0 + 2.0 * g, 0.0);
            return Math.Exp(ln);
        }

        /// <summary>ln|Γ(x + iy)| — Стирлинг со сдвигом аргумента (своя реализация, не кода приложения).</summary>
        static double LnAbsGamma(double x, double y)
        {
            double shift = 0.0;
            while (x < 12.0)
            {
                shift -= 0.5 * Math.Log(x * x + y * y);
                x += 1.0;
            }

            double r2 = x * x + y * y;
            double lnr = 0.5 * Math.Log(r2);
            double th = Math.Atan2(y, x);
            // Re[(z − ½)ln z − z + ½ln 2π + 1/(12z) − 1/(360z³) + 1/(1260z⁵)]
            double re = (x - 0.5) * lnr - y * th - x + 0.5 * Math.Log(2.0 * Math.PI);
            double invRe = x / r2;
            double z3re = Math.Cos(3 * th) / Math.Pow(r2, 1.5), z5re = Math.Cos(5 * th) / Math.Pow(r2, 2.5);
            re += invRe / 12.0 - z3re / 360.0 + z5re / 1260.0;
            return re + shift;
        }

        static void SphereChecks()
        {
            bool savedKey = FsaBetaLibrary.CoulombLambda, savedPoint = FsaBetaLibrary.CoulombLambdaPointNucleus;
            Check("умолчание ключа λ — ВЫКЛ", !FsaBetaLibrary.CoulombLambda);
            Check("умолчание формы при ВКЛ — заряженная сфера (рычаг точечного ядра выключен)", !FsaBetaLibrary.CoulombLambdaPointNucleus);

            // 1. Положительный контроль решателя: L₀ = (α₋₁² + α₁²)/(2p²F₀) против Уилкинсона (Z ≤ 60).
            Console.WriteLine();
            Console.WriteLine("=== 1. L₀ решателя сферы против формулы Уилкинсона (Hayen 2018, ур. 16) ===");
            Console.WriteLine("{0,4} {1,4} {2,9} {3,10} {4,10} {5,10}", "Z", "A", "T, кэВ", "L₀ счёт", "L₀ Уилк.", "разница");
            double worstL0 = 0.0;
            int[][] pairs = { new[] { 20, 40 }, new[] { 38, 89 }, new[] { 58, 138 } };
            foreach (int[] za in pairs)
            {
                double radius = CoulombSphere.Radius(za[1]);
                foreach (double t in new[] { 5.0, 20.0, 100.0, 300.0, 1000.0, 2500.0 })
                {
                    double w = 1.0 + t / 510.99895, p = Math.Sqrt(w * w - 1.0);
                    double az = za[0] / 137.035999084;
                    double sum = CoulombSphere.AmplitudeSquared(-1, az, radius, w, 1.0) + CoulombSphere.AmplitudeSquared(1, az, radius, w, 1.0);
                    double l0 = sum / (2.0 * p * p * HayenF0(za[0], w, radius));
                    double wl = WilkinsonL0(za[0], w, radius);
                    worstL0 = Math.Max(worstL0, Math.Abs(l0 / wl - 1.0));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1,4} {2,9:F1} {3,10:F6} {4,10:F6} {5,10:E2}", za[0], za[1], t, l0, wl, l0 / wl - 1.0));
                }
            }

            Check("L₀ решателя = Уилкинсон до 1·10⁻⁴ (Z = 20, 38, 58; 5…2500 кэВ; формула точна до 10⁻⁴)", worstL0 < 1e-4);

            // Мутант нормировки: без множителя p/W нормировки (α² = 1/A²) L₀ уходит от Уилкинсона.
            {
                double radius = CoulombSphere.Radius(138), w = 1.0 + 100.0 / 510.99895, p = Math.Sqrt(w * w - 1.0), az = 58 / 137.035999084;
                double sum = (CoulombSphere.AmplitudeSquared(-1, az, radius, w, 1.0) + CoulombSphere.AmplitudeSquared(1, az, radius, w, 1.0)) / (p / w);
                double l0 = sum / (2.0 * p * p * HayenF0(58, w, radius));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "мутант «нормировка без p/W»: L₀ {0:F4} против {1:F4}", l0, WilkinsonL0(58, w, radius)));
                Check("мутант нормировки той же проверкой пойман", Math.Abs(l0 / WilkinsonL0(58, w, radius) - 1.0) >= 1e-4);
            }

            // 2. Z = 0: λ ≡ 1 (свободные волны), сходимость по дальности, таблица против прямого счёта.
            Console.WriteLine();
            Console.WriteLine("=== 2. Z = 0, сходимость, интерполяция ===");
            double worstFree = 0.0;
            foreach (double t in new[] { 1.0, 30.0, 300.0, 3000.0 })
            {
                double w = 1.0 + t / 510.99895, radius = CoulombSphere.Radius(100);
                for (int k = 2; k <= 3; k++)
                {
                    double lam = (CoulombSphere.AmplitudeSquared(-k, 0.0, radius, w, 1.0) + CoulombSphere.AmplitudeSquared(k, 0.0, radius, w, 1.0))
                                 / (CoulombSphere.AmplitudeSquared(-1, 0.0, radius, w, 1.0) + CoulombSphere.AmplitudeSquared(1, 0.0, radius, w, 1.0));
                    worstFree = Math.Max(worstFree, Math.Abs(lam - 1.0));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Z = 0: наибольшее |λ₂,₃ − 1| = {0:E2}", worstFree));
            Check("Z = 0: λ₂ = λ₃ = 1 до 1·10⁻⁴ (точность усреднения инварианта, та же, что у сходимости)", worstFree < 1e-4);

            double worstReach = 0.0, worstTable = 0.0;
            foreach (int[] za in new[] { new[] { 82, 204 }, new[] { 90, 228 }, new[] { 58, 138 }, new[] { 20, 40 } })
            {
                foreach (double t in new[] { 3.0, 17.3, 95.0, 420.0, 1700.0 })
                {
                    double w = 1.0 + t / 510.99895, p = Math.Sqrt(w * w - 1.0);
                    for (int k = 2; k <= 3; k++)
                    {
                        foreach (bool positron in new[] { false, true })
                        {
                            double direct = CoulombSphere.LambdaDirect(k, za[0], za[1], positron, p, 1.0);
                            double far = CoulombSphere.LambdaDirect(k, za[0], za[1], positron, p, 4.0);
                            double table = CoulombSphere.Lambda(k, za[0], za[1], positron, p);
                            if (positron && t < 10.0)
                            {
                                continue;   // под барьером плотность β⁺ — нуль; λ там не нужна
                            }

                            worstReach = Math.Max(worstReach, Math.Abs(far / direct - 1.0));
                            worstTable = Math.Max(worstTable, Math.Abs(table / direct - 1.0));
                        }
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "дальность ×4 против ×1: наибольшее отн. {0:E2}; таблица против прямого счёта: {1:E2}", worstReach, worstTable));
            Check("сходимость по дальности до 1·10⁻⁴", worstReach < 1e-4);
            Check("таблица (160 узлов ln p) = прямой счёт до 5·10⁻⁴", worstTable < 5e-4);

            // λ₂ у трёх ядер: точечное ядро против сферы — что меняется.
            Console.WriteLine();
            Console.WriteLine("λ₂: точечное ядро (a274s) / сфера (a274t), β⁻");
            Console.WriteLine("{0,4} {1,4} {2,9} {3,9} {4,9}", "Z", "A", "T, кэВ", "точка", "сфера");
            foreach (int[] za in new[] { new[] { 58, 138 }, new[] { 82, 204 }, new[] { 90, 228 } })
            {
                foreach (double t in new[] { 5.0, 20.0, 60.0, 120.0, 250.0, 700.0 })
                {
                    double w = 1.0 + t / 510.99895, p = Math.Sqrt(w * w - 1.0);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1,4} {2,9:F1} {3,9:F4} {4,9:F4}", za[0], za[1], t,
                        PointLambda(2, za[0], za[1], w, p), CoulombSphere.Lambda(2, za[0], za[1], false, p)));
                }
            }

            // 3. Средние уникальных ветвей к средним базы: ВЫКЛ, точечное ядро, сфера.
            Console.WriteLine();
            Console.WriteLine("=== 3. Средние форм к средней базы (`nucdb`): λ = 1 / точечное ядро / сфера ===");
            Console.WriteLine("{0,-7} {1,2} {2,4} {3,9} {4,8} {5,-24} {6,8} {7,8} {8,8} {9,8}",
                              "нуклид", "±", "Z", "E0, кэВ", "выход %", "переход", "база", "λ=1/б", "точка/б", "сфера/б");
            int heavy = 0, heavyBetter = 0, heavyInside = 0, light = 0, mutantHeavy = 0, positrons = 0, positronCloser = 0;
            double lightWorstSphere = 0.0, lightWorstPoint = 0.0;
            foreach (string nucid in SphereNuclides)
            {
                foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
                {
                    if (beta == null || beta.IsLine || beta.Intensity < 0.0005 || !(beta.MeanKev > 0.0)
                        || beta.UniqueOrder < 1 || beta.ShapeT != null || beta.ShapeFactor != null)
                    {
                        continue;
                    }

                    double off, point, sphere;
                    FsaBetaLibrary.CoulombLambda = false;
                    FsaBetaLibrary.Integral(beta, out off);
                    FsaBetaLibrary.CoulombLambda = true;
                    FsaBetaLibrary.CoulombLambdaPointNucleus = true;
                    FsaBetaLibrary.Integral(beta, out point);
                    FsaBetaLibrary.CoulombLambdaPointNucleus = false;
                    FsaBetaLibrary.Integral(beta, out sphere);
                    FsaBetaLibrary.CoulombLambda = false;

                    string kind = beta.Transition ?? "";
                    bool xi = kind.IndexOf("неуник", StringComparison.OrdinalIgnoreCase) >= 0 || kind.IndexOf("non", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Мерка — ветви с записанным в базе уникальным запретом и выходом ≥ 0.5 %:
                    // «ΔJ=2 (π?)» — характер не установлен, малые ветви — справка.
                    bool measured = !xi && kind.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0 && beta.Intensity >= 0.5;
                    double rOff = off / beta.MeanKev, rPoint = point / beta.MeanKev, rSphere = sphere / beta.MeanKev;
                    string verdict = "";
                    if (beta.Positron)
                    {
                        // β⁺: точечное ядро (`a274s`) брало λ = 1; сфера — та же формула с обратным знаком заряда.
                        if (!xi)
                        {
                            positrons++;
                            bool closer = Math.Abs(rSphere - 1.0) < Math.Abs(rOff - 1.0);
                            if (closer) positronCloser++;
                            verdict = closer ? "β⁺, ближе λ = 1" : "β⁺, ✗ не ближе λ = 1";
                        }
                        else
                        {
                            verdict = "β⁺, ξ (справка)";
                        }
                    }
                    else if (!measured)
                    {
                        verdict = xi ? "ξ (справка)" : "справка";
                    }
                    else if (beta.DaughterZ >= 81)
                    {
                        heavy++;
                        bool better = Math.Abs(rSphere - 1.0) < 0.5 * Math.Abs(rPoint - 1.0);
                        bool inside = rSphere >= 0.975 && rSphere <= 1.01;
                        if (better) heavyBetter++;
                        if (inside) heavyInside++;
                        if (!(rPoint >= 0.975 && rPoint <= 1.01)) mutantHeavy++;
                        verdict = (better ? "отклонение вдвое меньше точки" : "✗ не вдвое меньше точки") + (inside ? ", в 0.975…1.01" : ", ✗ вне 0.975…1.01");
                    }
                    else if (beta.DaughterZ <= 58 && Math.Abs(rOff - 1.0) > 0.1 && Math.Abs(rPoint - rOff) < 0.01)
                    {
                        // Be-10 (Z = 5): λ ≈ 1, расхождение формы с базой не от кулоновских λ (находка `a274s`).
                        verdict = "справка: расхождение не от λ";
                    }
                    else if (beta.DaughterZ <= 58)
                    {
                        light++;
                        lightWorstSphere = Math.Max(lightWorstSphere, Math.Abs(rSphere - 1.0));
                        lightWorstPoint = Math.Max(lightWorstPoint, Math.Abs(rPoint - 1.0));
                        verdict = "Z ≤ 58";
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-7} {1,2} {2,4} {3,9:F1} {4,8:F4} {5,-24} {6,8:F2} {7,8:F4} {8,8:F4} {9,8:F4}  {10}",
                        nucid, beta.Positron ? "+" : "-", beta.DaughterZ, beta.EndpointKev, beta.Intensity,
                        kind.Length > 24 ? kind.Substring(0, 24) : kind, beta.MeanKev, rOff, rPoint, rSphere, verdict));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "уникальные β⁻ Z ≥ 81 (выход ≥ 0.5 %): {0}, отклонение вдвое меньше точки у {1}, в 0.975…1.01 у {2}; Z ≤ 58: {3}, наибольшее |откл.| сфера {4:F4} / точка {5:F4}; β⁺ уникальных: {6}, сфера ближе λ = 1 у {7}",
                heavy, heavyBetter, heavyInside, light, lightWorstSphere, lightWorstPoint, positrons, positronCloser));
            Check("Z ≥ 81: отклонение от средней базы у сферы вдвое меньше, чем у точечного ядра", heavy > 0 && heavyBetter == heavy);
            Check("Z ≥ 81: сфера в 0.975…1.01 средней базы", heavy > 0 && heavyInside == heavy);
            Check("Z ≤ 58: наибольшее отклонение сферы не больше точечного ядра и не больше 0.3 %", light > 0 && lightWorstSphere <= lightWorstPoint && lightWorstSphere <= 0.003);
            Check("β⁺ (знак заряда обратный): сфера ближе к средней базы, чем λ = 1", positrons > 0 && positronCloser == positrons);
            Console.WriteLine(mutantHeavy > 0
                ? "мутант «точечное ядро» пойман: вне 0.975…1.01 у " + mutantHeavy.ToString(CultureInfo.InvariantCulture) + " из " + heavy.ToString(CultureInfo.InvariantCulture)
                : "⛔ МУТАНТ НЕ ПОЙМАН: точечное ядро проходит проверку 0.975…1.01");
            Check("мутант «точечное ядро» той же проверкой пойман", mutantHeavy > 0);

            // 4. Клеймо: версия формы в поле cl= при ВКЛ, при ВЫКЛ — поля нет.
            FsaBetaLibrary.CoulombLambda = true;
            string sphereVersion = FsaBetaLibrary.CoulombLambdaVersion;
            FsaBetaLibrary.CoulombLambdaPointNucleus = true;
            string pointVersion = FsaBetaLibrary.CoulombLambdaVersion;
            FsaBetaLibrary.CoulombLambdaPointNucleus = false;
            FsaBetaLibrary.CoulombLambda = false;
            Check("версия формы в клейме: сфера «2», точечное ядро «1»", sphereVersion == "2" && pointVersion == "1");

            FsaBetaLibrary.CoulombLambda = savedKey;
            FsaBetaLibrary.CoulombLambdaPointNucleus = savedPoint;
        }

        /// <summary>λₖ точечного ядра — своя копия формулы `a274s` (F₍ₖ₋₁₎/F₀ с R = 1.2·A^⅓ фм).</summary>
        static double PointLambda(int k, int z, int a, double w, double p)
        {
            double az = z / 137.035999084, y = az * w / p, radius = CoulombSphere.Radius(a);
            Func<int, double> lnF = m =>
            {
                double g = Math.Sqrt(m * m - az * az), odd = 1.0;
                for (int j = 2 * m - 1; j > 1; j -= 2)
                {
                    odd *= j;
                }

                return 2.0 * Math.Log(m * odd) + m * Math.Log(4.0) + 2.0 * (g - m) * Math.Log(2.0 * p * radius)
                       + Math.PI * y + 2.0 * LnAbsGamma(g, y) - 2.0 * LnAbsGamma(1.0 + 2.0 * g, 0.0);
            };
            return Math.Exp(lnF(k) - lnF(1));
        }

        // ------------------------------------------------------------------
        // (`a274t`, 13.11.2026) КЛЕЙМО РЕЦЕПТА РАЗДЕЛА СЛОЯ — `--mode=recipe`
        // ------------------------------------------------------------------

        /// <summary>Клеймо без поля рецепта — каким оно было до 13.11.2026 (и мутант проверки «без rcp=»).</summary>
        static string WithoutRecipe(string stamp)
        {
            var parts = new List<string>();
            foreach (string part in (stamp ?? "").Split(';'))
            {
                if (!part.StartsWith("rcp=", StringComparison.Ordinal))
                {
                    parts.Add(part);
                }
            }

            return string.Join(";", parts.ToArray());
        }

        /// <summary>
        /// (`a274t`) Раздел слоя несёт рецепт, которым собран (`rcp=`), разбор берёт только рецепт
        /// умолчания. Положительный контроль: раздел нынешнего рецепта годен и сквозь файл матрицы,
        /// и сквозь привязку разбора. Отказ: прежний рецепт (энергия, без осадка конверсии, L 4.97),
        /// раздел без поля (до 13.11.2026), другая версия рецепта, подмены замера. Мутант — сверка
        /// клейм без поля `rcp=`: прежний рецепт ею проходит (то есть отказ держит именно поле).
        /// </summary>
        static void RecipeChecks(GeometryModel geometry, int histories)
        {
            const double Bin = 2.0;
            string note;
            ResponseCrystalSection current = CrystalOwnActivityBuilder.Build(geometry, Bin, histories, 0x274, out note);
            string expected = ResponseCrystalSection.ComputeStamp(geometry, Bin);
            Console.WriteLine("клеймо раздела: {0}", current.Stamp);
            Console.WriteLine("рецепт умолчания: rcp={0}", ResponseCrystalSection.StampField(expected, "rcp"));
            Check("раздел нынешнего рецепта годен сцене (положительный контроль)", current.UsableFor(geometry, Bin));
            string version = CrystalOwnActivityBuilder.RecipeVersion.ToString(CultureInfo.InvariantCulture);
            Check("в клейме поле рецепта с версией " + version,
                  (ResponseCrystalSection.StampField(current.Stamp, "rcp") ?? "").StartsWith(version + ":", StringComparison.Ordinal));

            // Прежний рецепт (до ревизии 12.11.2026): энергия, без осадка конверсии, L — линия nucdb.
            var oldParts = new CrystalOwnParts { Scale = CrystalOwnActivityBuilder.ScaleEnergy, ConversionRelaxation = false, LBindingFromEadl = false };
            ResponseCrystalSection old = CrystalOwnActivityBuilder.Build(geometry, Bin, histories, 0x274, oldParts, out note);
            Console.WriteLine("клеймо прежнего рецепта: rcp={0}", ResponseCrystalSection.StampField(old.Stamp, "rcp"));
            Check("раздел прежнего рецепта разбору не годен", !old.UsableFor(geometry, Bin));

            // По одному рычагу: каждый меняет клеймо.
            int distinct = 0, levers = 0;
            foreach (CrystalOwnParts lever in new[]
            {
                new CrystalOwnParts { Scale = CrystalOwnActivityBuilder.ScaleAnchor },
                new CrystalOwnParts { ConversionRelaxation = false },
                new CrystalOwnParts { LBindingFromEadl = false },
                new CrystalOwnParts { DaughterRelaxation = false },
                new CrystalOwnParts { Series = false },
                new CrystalOwnParts { FreeNatural = true },
                new CrystalOwnParts { Beta = false },
                new CrystalOwnParts { BranchGamma = false },
                new CrystalOwnParts { Capture = false },
                new CrystalOwnParts { KCaptureOverride = 0.670 },
                new CrystalOwnParts { BetaOverride = b => b },
            })
            {
                levers++;
                string stamp = ResponseCrystalSection.ComputeStamp(geometry, Bin, lever);
                if (!string.Equals(stamp, expected, StringComparison.Ordinal))
                {
                    distinct++;
                }
            }

            Console.WriteLine("рычагов рецепта {0}, клеймо отличает {1}", levers, distinct);
            Check("каждый рычаг рецепта меняет клеймо", distinct == levers);

            // Раздел без поля (так писался до 13.11.2026) и с другой версией рецепта.
            var bare = new ResponseCrystalSection { Stamp = WithoutRecipe(current.Stamp), BinKev = current.BinKev, Entries = current.Entries };
            Check("раздел без поля rcp= (до 13.11.2026) разбору не годен", !bare.UsableFor(geometry, Bin));
            string next = (CrystalOwnActivityBuilder.RecipeVersion + 1).ToString(CultureInfo.InvariantCulture);
            var bumped = new ResponseCrystalSection
            {
                Stamp = current.Stamp.Replace(";rcp=" + version + ":", ";rcp=" + next + ":"),
                BinKev = current.BinKev,
                Entries = current.Entries,
            };
            Check("раздел другой версии рецепта разбору не годен", !bumped.UsableFor(geometry, Bin));

            // Мутант: сверка без поля rcp= — прежний рецепт ею проходит.
            bool mutantAccepts = current.Count > 0 && string.Equals(WithoutRecipe(old.Stamp), WithoutRecipe(expected), StringComparison.Ordinal);
            Console.WriteLine(mutantAccepts
                ? "мутант пойман: без поля rcp= клейма прежнего и нынешнего рецепта совпадают — раздел старого рецепта взялся бы молча"
                : "⛔ МУТАНТ НЕ ПОЙМАН: клейма различаются и без поля rcp=");
            Check("мутант «сверка без rcp=» пропускает прежний рецепт (отказ держит именно поле)", mutantAccepts);

            // Сквозь файл матрицы и привязку разбора.
            ResponseMatrix matrix = Matrix(geometry, "file");
            if (matrix == null)
            {
                bad++;
                return;
            }

            string path = Path.Combine(cacheDir ?? Path.GetTempPath(), "a274t_recipe.rmx");
            foreach (KeyValuePair<string, ResponseCrystalSection> arm in new[]
            {
                new KeyValuePair<string, ResponseCrystalSection>("нынешний", current),
                new KeyValuePair<string, ResponseCrystalSection>("прежний", old),
            })
            {
                matrix.CrystalSection = arm.Value;
                matrix.Save(path);
                MatrixRefusal refusal;
                int format;
                ResponseMatrix loaded = ResponseMatrix.Load(path, out refusal, out format);
                var analyzer = new FsaAnalyzer();
                if (loaded != null)
                {
                    FsaMatrixBinding.Bind(analyzer, geometry, loaded);
                }

                bool stampKept = loaded != null && loaded.CrystalSection != null
                                 && string.Equals(loaded.CrystalSection.Stamp, arm.Value.Stamp, StringComparison.Ordinal);
                bool given = analyzer.CrystalOwnSection != null;
                Console.WriteLine("файл, рецепт {0}: клеймо сохранено {1}, разбору раздел {2}", arm.Key, stampKept ? "да" : "нет", given ? "дан" : "не дан");
                Check("файл, рецепт " + arm.Key + ": клеймо раздела сохранено побитно", stampKept);
                Check("файл, рецепт " + arm.Key + ": привязка " + (arm.Key == "нынешний" ? "даёт" : "не даёт") + " раздел разбору",
                      given == (arm.Key == "нынешний"));
            }

            matrix.CrystalSection = null;
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        // ------------------------------------------------------------------
        // (`a274t`, 13.11.2026) ШИРИНА K-ПИКА СЛОЯ У 37 кэВ — `--mode=kpeak`
        // ------------------------------------------------------------------

        /// <summary>
        /// Вычтенный слой (по каналам, уже уширенный ПШПВ прибора) со сдвигом <paramref name="shiftCh"/>
        /// и добавочным гауссом <paramref name="sigmaCh"/> (в каналах): ступенчатая плотность на сетке
        /// 1/10 канала, сдвиг, свёртка, обратно в каналы. Площадь сохраняется.
        /// </summary>
        static double[] Reshape(double[] layer, double shiftCh, double sigmaCh)
        {
            const int Sub = 10;
            int n = layer.Length;
            var fine = new double[n * Sub];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < Sub; j++)
                {
                    fine[i * Sub + j] = layer[i] / Sub;
                }
            }

            // Сдвиг дробный: доля ячейки сетки делится между двумя соседними (площадь та же).
            var moved = new double[fine.Length];
            double exact = shiftCh * Sub;
            int shift = (int)Math.Floor(exact);
            double frac = exact - shift;
            for (int f = 0; f < fine.Length; f++)
            {
                int t = f + shift;
                if (t >= 0 && t < fine.Length)
                {
                    moved[t] += fine[f] * (1.0 - frac);
                }

                if (t + 1 >= 0 && t + 1 < fine.Length)
                {
                    moved[t + 1] += fine[f] * frac;
                }
            }

            double[] spread = moved;
            if (sigmaCh > 1e-6)
            {
                double s = sigmaCh * Sub;
                int half = (int)Math.Ceiling(5.0 * s);
                var kernel = new double[2 * half + 1];
                double norm = 0.0;
                for (int k = -half; k <= half; k++)
                {
                    kernel[k + half] = Math.Exp(-0.5 * (k / s) * (k / s));
                    norm += kernel[k + half];
                }

                spread = new double[fine.Length];
                for (int f = 0; f < fine.Length; f++)
                {
                    if (moved[f] == 0.0)
                    {
                        continue;
                    }

                    for (int k = -half; k <= half; k++)
                    {
                        int t = f + k;
                        if (t >= 0 && t < fine.Length)
                        {
                            spread[t] += moved[f] * kernel[k + half] / norm;
                        }
                    }
                }
            }

            var result = new double[n];
            for (int f = 0; f < fine.Length; f++)
            {
                result[f / Sub] += spread[f];
            }

            return result;
        }

        sealed class KFit
        {
            public double Amp = 1.0, ShiftKev, SigmaKev, Chi2;
        }

        /// <summary>
        /// Подгонка K-пика слоя в полосе [lo, hi] кэВ: y = прочее + a·Reshape(слой, d, σx); a — линейно,
        /// d и σx — сеткой (шаг 0.1 кэВ). χ² Пуассона по данным.
        /// </summary>
        static KFit FitKPeak(double[] y, double[] other, double[] layer, EnergyCalibration calibration,
                             double lo, double hi, bool freeShift, bool freeSigma)
        {
            int c0 = -1, c1 = -1;
            for (int i = 0; i < y.Length; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                if (e >= lo && e <= hi)
                {
                    if (c0 < 0) c0 = i;
                    c1 = i;
                }
            }

            double kevPerCh = (calibration.ChannelToEnergy(c1 + 1) - calibration.ChannelToEnergy(c0)) / (c1 + 1 - c0);
            // Окно с запасом 15 каналов (≈ 40 кэВ > 5σ наибольшего уширения): так подгонка — секунды.
            int w0 = Math.Max(0, c0 - 15), w1 = Math.Min(layer.Length - 1, c1 + 15);
            var piece = new double[w1 - w0 + 1];
            Array.Copy(layer, w0, piece, 0, piece.Length);
            var best = new KFit { Chi2 = double.MaxValue };
            for (double d = freeShift ? -1.5 : 0.0; d <= (freeShift ? 1.5001 : 0.0); d += 0.1)
            {
                for (double sx = 0.0; sx <= (freeSigma ? 6.0001 : 0.0); sx += 0.1)
                {
                    double[] part = Reshape(piece, d / kevPerCh, sx / kevPerCh);
                    var t = new double[layer.Length];
                    Array.Copy(part, 0, t, w0, part.Length);
                    double num = 0.0, den = 0.0;
                    for (int i = c0; i <= c1; i++)
                    {
                        double w = 1.0 / Math.Max(y[i], 1.0);
                        num += (y[i] - other[i]) * t[i] * w;
                        den += t[i] * t[i] * w;
                    }

                    double a = den > 0.0 ? num / den : 0.0;
                    double chi = 0.0;
                    for (int i = c0; i <= c1; i++)
                    {
                        double r = y[i] - other[i] - a * t[i];
                        chi += r * r / Math.Max(y[i], 1.0);
                    }

                    if (chi < best.Chi2)
                    {
                        best = new KFit { Amp = a, ShiftKev = d, SigmaKev = sx, Chi2 = chi };
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// (`a274t`) ШИРИНА K-ПИКА СЛОЯ. Мерило «данные ≈ 10 кэВ против 4.6 по кривой прибора» (`fixE2`):
        /// гистограмма слоя до уширения (ширина самого события), ПШПВ прибора у 37 кэВ, подгонка слоя
        /// в 29–47 кэВ сдвигом и добавочным гауссом, крылья пика по отдельности. Положительный
        /// контроль — синтетика: модель + слой с известными сдвигом и уширением (в том числе «ПШПВ 10
        /// кэВ») + Пуассон, подгонка обязана вернуть заложенное. Мутант — та же синтетика, подгонка без
        /// уширения: χ² обязан вырасти.
        /// </summary>
        static void KPeakChecks(string path, bool useBackground, GeometryModel geometry, ResponseMatrix matrix, int histories)
        {
            ResultData rd = Load(path);
            if (useBackground)
            {
                EnergySpectrum own = rd.BackgroundEnergySpectrum;
                own.EnergyCalibration = rd.EnergySpectrum.EnergyCalibration;
                rd.EnergySpectrum = own;
            }

            string note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(geometry, 2.0, histories, 0x274, out note);
            CrystalOwnEntry la = section.Find("138LA", "beta");
            double sum = 0.0, moment = 0.0, moment2 = 0.0;
            for (int i = 0; la != null && i < la.PerDecay.Length; i++)
            {
                double e = i * section.BinKev;
                if (e >= 30.0 && e <= 44.0)
                {
                    sum += la.PerDecay[i];
                    moment += la.PerDecay[i] * e;
                    moment2 += la.PerDecay[i] * e * e;
                }
            }

            double centre = sum > 0.0 ? moment / sum : 0.0;
            double rms = sum > 0.0 ? Math.Sqrt(Math.Max(0.0, moment2 / sum - centre * centre)) : 0.0;
            EnergySpectrum data = rd.EnergySpectrum;
            int ch37 = (int)Math.Round(data.EnergyCalibration.EnergyToChannel(37.0, data.NumberOfChannels));
            double deviceFwhm = rd.FwhmCalibration.ChannelToFwhm(ch37) * (data.EnergyCalibration.ChannelToEnergy(ch37 + 1) - data.EnergyCalibration.ChannelToEnergy(ch37));
            Console.WriteLine();
            Console.WriteLine("=== K-пик слоя: {0}{1} ===", Path.GetFileName(path), useBackground ? " (встроенный фон)" : "");
            Console.WriteLine("гистограмма слоя до уширения, 30–44 кэВ: доля {0:F4} на распад, центр {1:F2} кэВ, СКО {2:F2} кэВ (шаг сетки 2 кэВ: СКО ступеньки 0.58)",
                              sum, centre, rms);
            Console.WriteLine("ПШПВ прибора у 37 кэВ: {0:F2} кэВ (кривая спектра)", deviceFwhm);

            FsaResult result = Run(rd, null, geometry, matrix, section);
            double[] layer = lastFixed;
            if (result == null || layer == null)
            {
                Check("разбор со слоем прошёл", false);
                return;
            }

            var y = new double[data.Spectrum.Length];
            for (int i = 0; i < y.Length; i++)
            {
                y[i] = data.Spectrum[i];
            }

            double[] other = result.Model;
            KFit asIs = FitKPeak(y, other, layer, data.EnergyCalibration, 29.0, 47.0, false, false);
            KFit width = FitKPeak(y, other, layer, data.EnergyCalibration, 29.0, 47.0, false, true);
            KFit both = FitKPeak(y, other, layer, data.EnergyCalibration, 29.0, 47.0, true, true);
            KFit shift = FitKPeak(y, other, layer, data.EnergyCalibration, 29.0, 47.0, true, false);
            Func<KFit, double> total = f => Math.Sqrt(deviceFwhm * deviceFwhm + Math.Pow(2.3548200450309493 * f.SigmaKev, 2));
            Console.WriteLine("подгонка 29–47 кэВ (χ² Пуассона, амплитуда a к расчёту по массе):");
            foreach (KeyValuePair<string, KFit> f in new[]
            {
                new KeyValuePair<string, KFit>("амплитуда", asIs),
                new KeyValuePair<string, KFit>("+ уширение", width),
                new KeyValuePair<string, KFit>("+ сдвиг", shift),
                new KeyValuePair<string, KFit>("+ сдвиг + уширение", both),
            })
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-20} χ² {1,9:F0}  a {2:F3}  сдвиг {3:+0.00;-0.00} кэВ  доб. σ {4:F2} кэВ → ПШПВ {5:F2} кэВ",
                    f.Key, f.Value.Chi2, f.Value.Amp, f.Value.ShiftKev, f.Value.SigmaKev, total(f.Value)));
            }

            // Крылья: где данные расходятся с моделью — по каналам 26–47 кэВ.
            Console.WriteLine("каналы (данные / модель со слоем, σ):");
            double lowExcess = 0.0;
            for (int i = 0; i < y.Length; i++)
            {
                double e = data.EnergyCalibration.ChannelToEnergy(i);
                if (e >= 24.0 && e <= 47.0)
                {
                    double m = other[i] + layer[i];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,6:F1} кэВ  {1,7:F0} / {2,8:F0}  {3,6:F1}", e, y[i], m, (y[i] - m) / Math.Sqrt(Math.Max(y[i], 1.0))));
                    if (e >= 28.0 && e < 33.0)
                    {
                        lowExcess += y[i] - m;
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "избыток данных 28–33 кэВ: {0:F0} отсчётов", lowExcess));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ИТОГ: ширина пика данных по подгонке {0:F2} кэВ при ПШПВ прибора {1:F2} (гипотеза `fixE2` — около 10)", total(both), deviceFwhm));
            // Мерой ширины слоя окно служит, только если слой в нём главный: у `LaBrBril_Eu152` под ним
            // Sm Kα/Kβ 40–46 кэВ источника, и подгонка уширением слоя закрывает их невязку.
            double layerArea = 0.0, otherArea = 0.0;
            for (int i = 0; i < y.Length; i++)
            {
                double e = data.EnergyCalibration.ChannelToEnergy(i);
                if (e >= 29.0 && e <= 47.0)
                {
                    layerArea += layer[i];
                    otherArea += other[i];
                }
            }

            double share = layerArea / Math.Max(layerArea + otherArea, 1.0);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "доля слоя в модели окна 29–47 кэВ: {0:F3}", share));
            if (share >= 0.5)
            {
                Check("ширина K-пика данных по подгонке меньше 7 кэВ (гипотеза «около 10» опровергнута)", total(both) < 7.0);
            }
            else
            {
                Console.WriteLine("окно занято линиями источника (слой меньше половины модели) — ширину слоя этот спектр не меряет, проверка пропущена");
            }

            // Положительный контроль и мутант — синтетика с известным сдвигом и уширением.
            var random = new Random(0x2747);
            foreach (double[] truth in new[] { new[] { 0.4, 1.5 }, new[] { 0.0, Math.Sqrt(Math.Max(0.0, 100.0 - deviceFwhm * deviceFwhm)) / 2.3548200450309493 } })
            {
                double kevPerCh = data.EnergyCalibration.ChannelToEnergy(ch37 + 1) - data.EnergyCalibration.ChannelToEnergy(ch37);
                double[] t = Reshape(layer, truth[0] / kevPerCh, truth[1] / kevPerCh);
                var syn = new double[y.Length];
                for (int i = 0; i < y.Length; i++)
                {
                    syn[i] = Poisson(other[i] + t[i], random);
                }

                KFit got = FitKPeak(syn, other, layer, data.EnergyCalibration, 29.0, 47.0, true, true);
                KFit narrow = FitKPeak(syn, other, layer, data.EnergyCalibration, 29.0, 47.0, true, false);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "синтетика: заложено сдвиг {0:+0.00;-0.00}, доб. σ {1:F2} (ПШПВ {2:F2}) → найдено {3:+0.00;-0.00}, {4:F2} (ПШПВ {5:F2}), χ² {6:F0}; мутант без уширения χ² {7:F0}",
                    truth[0], truth[1], Math.Sqrt(deviceFwhm * deviceFwhm + Math.Pow(2.3548200450309493 * truth[1], 2)),
                    got.ShiftKev, got.SigmaKev, total(got), got.Chi2, narrow.Chi2));
                Check(string.Format(CultureInfo.InvariantCulture, "положительный контроль: уширение {0:F2} кэВ найдено до 0.3 кэВ, сдвиг до 0.2 кэВ", truth[1]),
                      Math.Abs(got.SigmaKev - truth[1]) <= 0.3 && Math.Abs(got.ShiftKev - truth[0]) <= 0.2);
                Check("мутант «без уширения» на синтетике пойман: χ² вдвое больше", narrow.Chi2 > 2.0 * Math.Max(got.Chi2, 1.0));
            }
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
                if (armFilter.Count > 0 && !armFilter.Contains(arms.IndexOf(arm).ToString(CultureInfo.InvariantCulture)))
                {
                    continue;
                }

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
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    // (`a274t`) поканальный дамп плеча: данные, модель, вычтенный слой, ПШПВ прибора
                    string armFile = dumpPath + "." + arms.IndexOf(arm).ToString(CultureInfo.InvariantCulture) + ".csv";
                    using (var writer = new StreamWriter(armFile, false, new UTF8Encoding(false)))
                    {
                        writer.WriteLine("# " + arm.Name);
                        writer.WriteLine("channel;kev;fwhm_kev;raw;model;layer");
                        for (int i = 0; i < raw.Length; i++)
                        {
                            double kev = data.EnergyCalibration.ChannelToEnergy(i);
                            double width = rd.FwhmCalibration.ChannelToFwhm(i) * (data.EnergyCalibration.ChannelToEnergy(i + 1) - kev);
                            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0};{1:F3};{2:F3};{3};{4:F3};{5:F3}", i, kev, width, raw[i],
                                                           result.Model[i], fixedLayer != null ? fixedLayer[i] : 0.0));
                        }
                    }

                    CrystalOwnEntry la = section != null ? section.Find("138LA", "beta") : null;
                    if (la != null)
                    {
                        // гистограмма слоя на распад до уширения (шаг сетки раздела)
                        using (var writer = new StreamWriter(armFile + ".hist.csv", false, new UTF8Encoding(false)))
                        {
                            writer.WriteLine("kev;per_decay");
                            for (int i = 0; i < la.PerDecay.Length; i++)
                            {
                                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3};{1:R}", i * section.BinKev, la.PerDecay[i]));
                            }
                        }
                    }
                }

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
