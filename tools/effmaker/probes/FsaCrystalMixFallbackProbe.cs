using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace FsaCrystalMixFallbackProbe
{
    /// <summary>
    /// ⛔ ЗАСЛОН СВЕДЕНИЯ РЕНТГЕНА КРИСТАЛЛА (`A30`) — ИСКУССТВЕННЫЙ ВХОД,
    /// НА КОТОРОМ ВЕТКА СРАБАТЫВАЕТ, И ЕЁ ЦЕНА ЧИСЛОМ.
    ///
    /// Решением Amber 01.09.2026 (`A29`) собственный рентген кристалла идёт
    /// ОДНИМ образом на вещество (`Xray-CsI`): элементы CsI разделить данными
    /// нельзя — Kα иода 28.61 и цезия 30.97 кэВ отстоят на 2.4 кэВ при ПШПВ
    /// прибора около девяти в этой полосе. Но в
    /// <c>FsaSampleLibrary.AddCrystalFluorescence</c> стоит заслон: если элемент
    /// кристалла УЖЕ занят образом пробы или защиты, вещество не сводится и
    /// возвращается прежний поэлементный путь. Заслон поставлен намеренно
    /// (общий образ спорил бы за те же отсчёты со своей же половиной), но
    /// случай не был разобран: у корпуса такого совпадения нет, и цена его
    /// не измерена (`A30`).
    ///
    /// ⛔ Случай НЕ выдуман: элементы защиты приложение выводит ИЗ ПОДПИСЕЙ
    /// ПИКОВ (<c>FsaCompositionInference</c>: всякий пик с подписью-элементом
    /// уходит в <c>ShieldElements</c>), а элементы пробы — из вещества
    /// источника геометрии. Иодная проба на иодном кристалле (I-131 на NaI),
    /// цезиевая на CsI, свинцовая на LaBr3 — вход, который заслон роняет.
    /// Поставочная библиотека нуклидов подписей-элементов не несёт, поэтому у
    /// корпуса совпадения и нет; у человека с его набором — бывает.
    ///
    /// Четыре раздела, и каждое утверждение — числом:
    ///
    ///   1. ЗАСЛОН СРАБАТЫВАЕТ И НАЗВАН. Три спецификации: чистая (сведение
    ///      есть), с занятым элементом кристалла (сведения нет, две колонки),
    ///      с ПОСТОРОННИМ элементом (сведение на месте — отрицательный
    ///      контроль: рушит не всякий элемент).
    ///   2. ЦЕНА БЕЗ МАТРИЦЫ. Искусственный спектр строится ИЗ сведённого
    ///      образа, поэтому истина известна до знака: площадь рентгена
    ///      кристалла задана. Оба плеча считают одно и то же, а расходятся
    ///      тем, ДОЖИВАЕТ ЛИ образ до разбора.
    ///   3. ЦЕНА ПРИ ЖИВОЙ МАТРИЦЕ — ТЕЧЬ ГЕЙТА `AMBER4`. Гейт снимает
    ///      колонки по флагу <c>FromCrystal</c>. У сведённого образа флаг один
    ///      на всё вещество, у запасного пути занятая половина приходит из
    ///      ПРОБЫ и флага не несёт — значит остаётся свободной колонкой на
    ///      отсчёты, которые матрица уже несёт сама.
    ///   4. ДВА ПОЛОЖИТЕЛЬНЫХ КОНТРОЛЯ, и оба обязаны показать ВЫЖИВШУЮ пару,
    ///      иначе число раздела 2 меряет не вырожденность, а мою ошибку:
    ///      (а) ТА ЖЕ пара CsI при сигнале в двадцать раз сильнее — колонки
    ///      те же и так же коллинеарны, меняется одна статистика; (б) ТОТ ЖЕ
    ///      слабый сигнал, но пара РАЗДЕЛИМАЯ — CdWO4, Kα кадмия 23.17 и
    ///      вольфрама 59.32 кэВ.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        /// <summary>Шкала: 1 кэВ на канал, 2048 каналов.</summary>
        const int Channels = 2048;

        /// <summary>
        /// Порог АЦП искусственного спектра, каналов.
        ///
        /// ⛔ НОЛЬ НАРОЧНО, И ЭТО СТОИЛО ПРОГОНА. При пороге 4 канала спектр
        /// ниже него РОВНО НОЛЬ, а континуум-сплайн модели идёт от нулевого
        /// канала: ступенька в четыре канала высотой во весь континуум сплайну
        /// не по силам, и невязку от неё подхватывали свободные колонки
        /// рентгена — они брали впятеро больше заданной площади при χ²/ndf 1.65
        /// БЕЗ ШУМА. Мерился бы срез полосы, а не вырожденность пары.
        /// </summary>
        const int AdcFloor = 0;

        /// <summary>
        /// Истинная площадь рентгена кристалла в искусственном спектре.
        ///
        /// ⛔ ВЕЛИЧИНА ВЗЯТА ПО ЖИЗНИ, А НЕ ПОБОЛЬШЕ. Первым здесь стояло
        /// 60 000 отсчётов — рентген вдесятеро выше континуума, — и опыт
        /// показывал, что вырожденная пара делится ПРЕКРАСНО (разброс 0.9
        /// пункта): при таком отношении сигнала к шуму 2.4 кэВ между Kα хватает
        /// на разделение. У `A29` образ кристалла брал 1.15 % спектра; здесь он
        /// берёт 0.2 %, и это тот случай, ради которого вещество и сводится.
        /// </summary>
        const double XrayArea = 3000.0;

        /// <summary>Истинная площадь нуклидного образа.</summary>
        const double NuclideArea = 400000.0;

        /// <summary>Сколько пуассоновских розыгрышей идёт в разброс дележа.</summary>
        const int Draws = 12;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ Культура ЦЕЛИКОМ инвариантная, а не клон с подменённым
            //    разделителем (`T245`): печать и разбор чинятся вместе.
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО всего.
            FsaTuningReport.Snapshot();

            foreach (string a in args)
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }

            Console.WriteLine("=== ЗАСЛОН СВЕДЕНИЯ РЕНТГЕНА КРИСТАЛЛА (`A30`) ===");

            Section1();
            Section2();
            Section3();
            Section4();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. Заслон срабатывает и назван
        // ------------------------------------------------------------------

        static void Section1()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. ЗАСЛОН: КОГДА СРАБАТЫВАЕТ И ЧТО ГОВОРИТ ===");

            FsaSampleLibrary.Report clean;
            List<FsaComponent> free = Csi(new int[0], out clean);
            Report("чистая сцена", free, clean);
            Same("сведение есть: ровно один образ рентгена кристалла", 1, CrystalXrayCount(free));
            Same("имя сведённого образа", "Xray-CsI", FirstCrystalXrayName(free));
            Same("заслон молчит", false, Noted(clean));

            FsaSampleLibrary.Report taken;
            List<FsaComponent> split = Csi(new[] { 55 }, out taken);
            Report("цезий занят защитой", split, taken);
            Same("сведения нет: образа `Xray-CsI` не построено", 0, Count(split, "Xray-CsI"));
            Same("вернулся поэлементный путь: `Xray-Cs` есть", 1, Count(split, "Xray-Cs"));
            Same("вернулся поэлементный путь: `Xray-I` есть", 1, Count(split, "Xray-I"));
            Same("от кристалла помечена ТОЛЬКО одна колонка из двух", 1, CrystalXrayCount(split));
            Same("заслон НАЗВАН в отчёте сборки", true, Noted(taken));

            // ⛔ ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ. Без него «заслон сработал» и «заслон
            // срабатывает всегда» неразличимы: свинец защиты элементом
            // кристалла не является, и сведение обязано устоять.
            FsaSampleLibrary.Report alien;
            List<FsaComponent> intact = Csi(new[] { 82 }, out alien);
            Report("свинец в защите (посторонний элемент)", intact, alien);
            Same("посторонний элемент сведения НЕ рушит", 1, CrystalXrayCount(intact));
            Same("и заслон при нём молчит", false, Noted(alien));
        }

        // ------------------------------------------------------------------
        // 2. Цена без матрицы: искусственный спектр с известной истиной
        // ------------------------------------------------------------------

        static void Section2()
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. ЦЕНА БЕЗ МАТРИЦЫ: ИСКУССТВЕННЫЙ СПЕКТР, ИСТИНА ИЗВЕСТНА ===");
            Console.WriteLine("  CsI: Kα иода 28.61 и цезия 30.97 кэВ — 2.4 кэВ при ПШПВ около девяти");
            Price("CsI", 53, 55, new[] { 0.4884, 0.5116 }, "Xray-I", "Xray-Cs", XrayArea, 0, 0);
        }

        // ------------------------------------------------------------------
        // 3. Цена при живой матрице: течь гейта AMBER4
        // ------------------------------------------------------------------

        static void Section3()
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. ЦЕНА ПРИ ЖИВОЙ МАТРИЦЕ: ТЕЧЬ ГЕЙТА `AMBER4` ===");

            ResponseMatrix matrix = Matrix();

            FsaSampleLibrary.Report r1;
            List<FsaComponent> merged = Csi(new int[0], out r1);
            FsaSampleLibrary.Report r2;
            List<FsaComponent> split = Csi(new[] { 55 }, out r2);

            int[] spectrum = Sample(Expected(merged, "Xray-CsI", XrayArea), null);

            int droppedMerged, droppedSplit;
            List<string> leftMerged = XrayAfterGate(spectrum, merged, matrix, "сведено", out droppedMerged);
            List<string> leftSplit = XrayAfterGate(spectrum, split, matrix, "запасное", out droppedSplit);

            Console.WriteLine("  сведено : снято гейтом {0}, свободных колонок рентгена кристалла осталось {1}",
                              droppedMerged, leftMerged.Count);
            Console.WriteLine("  запасное: снято гейтом {0}, свободных колонок рентгена кристалла осталось {1} ({2})",
                              droppedSplit, leftSplit.Count, Join(leftSplit));

            Same("сведено: гейт снял образ кристалла", 1, droppedMerged);
            Same("сведено: свободного рентгена кристалла в разборе НЕТ", 0, leftMerged.Count);
            Same("запасное: гейт снял только помеченную половину", 1, droppedSplit);

            // ⛔ ЭТО И ЕСТЬ ЦЕНА ЗАСЛОНА, и она названа числом: половина
            // K-серии кристалла остаётся свободной колонкой при живой матрице,
            // то есть ровно тем вторым счётом, ради снятия которого заведён
            // гейт `AMBER4`.
            Same("запасное: половина рентгена кристалла ОСТАЛАСЬ свободной", 1, leftSplit.Count);
            Same("и это именно цезий кристалла", "Xray-Cs", leftSplit.Count > 0 ? leftSplit[0] : "(нет)");

            double weight = CrystalWeightShare(merged, "Xray-CsI", 55);
            Console.WriteLine("  доля веса K-серии вещества, оставшаяся свободной: {0:F1} %", 100.0 * weight);
            Same("оставшаяся доля веса больше трети K-серии", true, weight > 0.33);
        }

        // ------------------------------------------------------------------
        // 4. Положительный контроль различающей способности
        // ------------------------------------------------------------------

        static void Section4()
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. ДВА КОНТРОЛЯ: ГДЕ ПАРА ОБЯЗАНА ВЫЖИТЬ ===");
            Console.WriteLine();
            Console.WriteLine("  4а. ТА ЖЕ пара CsI, но сигнал в двадцать раз сильнее:");
            Console.WriteLine("      колонки те же и так же коллинеарны — меняется только статистика");
            Price("CsI", 53, 55, new[] { 0.4884, 0.5116 }, "Xray-I", "Xray-Cs", 20.0 * XrayArea, Draws, Draws);

            Console.WriteLine();
            Console.WriteLine("  4б. ТОТ ЖЕ слабый сигнал, но пара РАЗДЕЛИМАЯ — CdWO4:");
            Console.WriteLine("      Kα кадмия 23.17 и вольфрама 59.32 кэВ, между ними 36 кэВ");
            Price("CdWO4", 48, 74, new[] { 0.3120, 0.5103 }, "Xray-Cd", "Xray-W", XrayArea, 1, Draws);
        }

        // ------------------------------------------------------------------
        // Опыт «сведено против запасного» на одном искусственном спектре
        // ------------------------------------------------------------------

        /// <summary>
        /// Один и тот же спектр разбирается двумя библиотеками: сведённой и
        /// запасной поэлементной. Спектр построен ИЗ СВЕДЁННОГО образа, значит
        /// истина лежит в области значений обеих — расхождение плеч приходит от
        /// фита, а не от того, что модель чего-то не может выразить.
        /// </summary>
        /// <param name="xrayArea">Заданная площадь рентгена кристалла, отсчётов.</param>
        /// <param name="pairAliveMin">
        /// Сколько розыгрышей из <see cref="Draws"/> ОБЯЗАНЫ сохранить ОБЕ
        /// колонки запасного пути, наименьшее допустимое. Ноль-ноль — «пара не
        /// доживает никогда» (вырожденный случай); двенадцать-двенадцать —
        /// «доживает всегда». Границы заданы числами, а не признаком «вырождена
        /// ли», нарочно: у разделимой пары на слабом сигнале ответ посередине,
        /// и признак пришлось бы выбирать под ожидание.
        /// </param>
        /// <param name="pairAliveMax">То же, наибольшее допустимое.</param>
        static void Price(string crystal, int zLight, int zHeavy, double[] fractions,
                          string nameLight, string nameHeavy, double xrayArea,
                          int pairAliveMin, int pairAliveMax)
        {
            FsaSampleLibrary.Report r1;
            List<FsaComponent> merged = Library(crystal, zLight, zHeavy, fractions, new int[0], out r1);
            FsaSampleLibrary.Report r2;
            List<FsaComponent> split = Library(crystal, zLight, zHeavy, fractions, new[] { zHeavy }, out r2);

            string mergedName = "Xray-" + crystal;
            string mark = crystal + " " + xrayArea.ToString("F0", CultureInfo.InvariantCulture);
            Same(mark + ": сведённая библиотека несёт `" + mergedName + "`", 1, Count(merged, mergedName));
            Same(mark + ": запасная несёт две колонки вместо одной", 2,
                 Count(split, nameLight) + Count(split, nameHeavy));

            // Истина: доли элементов ВНУТРИ сведённого образа. Их задаёт
            // вещество (массовая доля на выход флуоресценции), а не фит.
            double trueLight = CrystalWeightShare(merged, mergedName, zLight);
            double trueHeavy = CrystalWeightShare(merged, mergedName, zHeavy);
            Console.WriteLine("  истина: площадь {0:F0} отсчётов, дележ {1} {2:F1} % / {3} {4:F1} % K-серии",
                              xrayArea, nameLight, 100.0 * trueLight, nameHeavy, 100.0 * trueHeavy);

            double[] expected = Expected(merged, mergedName, xrayArea);

            // (а) БЕЗ ШУМА: сведённое плечо обязано взять истину.
            // ⛔ Это НЕ украшение опыта, а его опора: истина построена из
            // сведённого образа, и плечо без шума показывает, что модель
            // выразить её МОЖЕТ. Всё, что расходится ниже, приходит от шума и
            // вырожденности, а не от того, что колонки поставлены не туда.
            int[] clean = Sample(expected, null);
            FsaResult a = Analyze(clean, merged, null, mark + " сведено, без шума");
            FsaResult b = Analyze(clean, split, null, mark + " запасное, без шума");
            Same(mark + ": плечо «сведено» разобралось", true, a != null);
            Same(mark + ": плечо «запасное» разобралось", true, b != null);
            if (a == null || b == null)
            {
                return;
            }

            // ⛔ Площадь берётся ПО КРИВОЙ КОМПОНЕНТА (сумма его модельных
            // отсчётов), а не по `PeakCounts`: последнее считает отсчёты В
            // ПИКОВЫХ ОКНАХ, и у рентгена кристалла — шести линий на
            // 28…35 кэВ при ПШПВ девять — окна перекрываются, то есть число
            // мерило бы не то, что задано истиной. Ноль значит «колонки в
            // разборе нет»: либо не построена, либо снята отсевом значимости.
            double areaMerged = Area(a, mergedName);
            double areaLight = Area(b, nameLight);
            double areaHeavy = Area(b, nameHeavy);
            double areaSplit = areaLight + areaHeavy;

            Console.WriteLine("  без шума, сведено : площадь {0:F0} ({1:F1} % истины), z {2:F2}, χ²/ndf {3:F3}",
                              areaMerged, 100.0 * areaMerged / xrayArea, Z(a, mergedName), a.Chi2Ndf);
            Console.WriteLine("  без шума, запасное: {0} {1:F0} (z {2:F2}) + {3} {4:F0} (z {5:F2})"
                              + " = {6:F0} ({7:F1} % истины), χ²/ndf {8:F3}",
                              nameLight, areaLight, Z(b, nameLight),
                              nameHeavy, areaHeavy, Z(b, nameHeavy),
                              areaSplit, 100.0 * areaSplit / xrayArea, b.Chi2Ndf);

            Same(mark + ": без шума сведённое плечо берёт истину ±10 %", true,
                 Math.Abs(areaMerged - xrayArea) < 0.10 * xrayArea);

            // (б) С ПУАССОНОВСКИМ ШУМОМ, ЗЕРНО 20260910.
            // ⛔ ОДНОЙ РЕАЛИЗАЦИИ МАЛО: вопрос «выживает ли колонка» решается
            // отсевом значимости, а он про σ, то есть про случай. Розыгрышей
            // несколько, и считается, В СКОЛЬКИХ из них образ дожил до разбора.
            var mergedAreas = new List<double>();
            var pairTotals = new List<double>();
            var pairShares = new List<double>();
            int mergedAlive = 0, pairAlive = 0, halfAlive = 0;
            var rnd = new Random(20260910);
            for (int i = 0; i < Draws; i++)
            {
                int[] noisy = Sample(expected, rnd);
                FsaResult ra = Analyze(noisy, merged, null, i == 0 ? mark + " сведено, шум" : null);
                FsaResult rb = Analyze(noisy, split, null, i == 0 ? mark + " запасное, шум" : null);
                if (ra == null || rb == null)
                {
                    continue;
                }

                double one = Area(ra, mergedName);
                if (one > 0.0)
                {
                    mergedAlive++;
                    mergedAreas.Add(one);
                }

                double light = Area(rb, nameLight);
                double heavy = Area(rb, nameHeavy);
                double total = light + heavy;
                if (light > 0.0 && heavy > 0.0)
                {
                    pairAlive++;
                    pairShares.Add(light / total);
                }
                else if (total > 0.0)
                {
                    halfAlive++;
                }

                pairTotals.Add(total);
            }

            Console.WriteLine("  шум, {0} розыгрышей, сведено : образ дожил {1} раз, площадь {2:F0} ({3:F1} % истины)",
                              Draws, mergedAlive, Mean(mergedAreas), 100.0 * Mean(mergedAreas) / xrayArea);
            Console.WriteLine("  шум, {0} розыгрышей, запасное: обе колонки дожили {1} раз,"
                              + " одна из двух {2} раз, ни одной {3} раз; площадь {4:F0} ({5:F1} % истины)",
                              Draws, pairAlive, halfAlive, Draws - pairAlive - halfAlive,
                              Mean(pairTotals), 100.0 * Mean(pairTotals) / xrayArea);
            if (pairShares.Count > 1)
            {
                Console.WriteLine("  шум: дележ там, где пара дожила, — {0} {1:F1} % ± {2:F1}"
                                  + " против истинных {3:F1} %",
                                  nameLight, 100.0 * Mean(pairShares), 100.0 * Sigma(pairShares),
                                  100.0 * trueLight);
            }

            // Сведённое плечо — общая опора обоих исходов: пока ОНО держит
            // образ, «пара потерялась» значит именно вырожденность, а не то,
            // что образа в спектре нет.
            Same(mark + ": сведённый образ доживает во ВСЕХ розыгрышах", Draws, mergedAlive);

            // ⛔ ЦЕНА ЗАСЛОНА, НАЗВАННАЯ ЧИСЛОМ: разложив ту же площадь на две
            // почти коллинеарные колонки, фит теряет их обе — у каждой половины
            // амплитуда вдвое меньше, а неопределённость от вырожденности
            // больше, и отсев значимости (`RefitZ` = 3) снимает их. Человек
            // видит спектр БЕЗ рентгена кристалла, хотя тот в спектре есть.
            Same(mark + ": обе колонки дожили в " + pairAliveMin.ToString(CultureInfo.InvariantCulture)
                 + "…" + pairAliveMax.ToString(CultureInfo.InvariantCulture) + " розыгрышах", true,
                 pairAlive >= pairAliveMin && pairAlive <= pairAliveMax);

            if (pairShares.Count > 1)
            {
                // Там, где пара всё-таки дожила, дележ обязан быть верным:
                // иначе «пара выжила» ничего не значило бы.
                Same(mark + ": дележ уцелевшей пары держится в 15 пунктах истины", true,
                     Math.Abs(Mean(pairShares) - trueLight) < 0.15);
            }
        }

        // ------------------------------------------------------------------
        // Сцены
        // ------------------------------------------------------------------

        static List<FsaComponent> Csi(int[] shield, out FsaSampleLibrary.Report report)
        {
            return Library("CsI", 53, 55, new[] { 0.4884, 0.5116 }, shield, out report);
        }

        static List<FsaComponent> Library(string crystal, int zLight, int zHeavy, double[] fractions,
                                          int[] shield, out FsaSampleLibrary.Report report)
        {
            var spec = new FsaSampleSpec();
            spec.MinEnergyKev = 10.0;
            spec.MaxEnergyKev = 2000.0;
            // ⛔ НУКЛИД ВЗЯТ БЕЗ РЕНТГЕНА, И ЭТО НЕ ПРИДИРКА. Первым здесь стоял
            // `137CS`, и опыт мерил не то: в его излучениях лежат K-линии бария
            // 31.82 / 32.19 / 36.4 кэВ — ровно поверх K-серии кристалла CsI.
            // Колонка нуклида забирала часть рентгена себе, и «истинная»
            // площадь переставала быть истинной (сведённое плечо брало 62 %
            // заданного). У калия-40 ниже 1461 кэВ нет ничего.
            spec.Nuclides.Add("40K");
            spec.CrystalElements.Add(zLight);
            spec.CrystalElements.Add(zHeavy);
            spec.CrystalFractions[zLight] = fractions[0];
            spec.CrystalFractions[zHeavy] = fractions[1];
            spec.CrystalName = crystal;
            foreach (int z in shield)
            {
                spec.ShieldElements.Add(z);
            }

            return FsaSampleLibrary.Build(spec, out report);
        }

        /// <summary>
        /// Ожидание искусственного спектра: рентген кристалла заданной площади
        /// плюс нуклидный образ плюс гладкий континуум. Строится ТЕМ ЖЕ
        /// <see cref="PeakShapeModel"/>, каким анализатор строит столбцы, —
        /// иначе расхождение плеч мерило бы разницу форм.
        /// </summary>
        static double[] Expected(List<FsaComponent> library, string xrayName, double xrayArea)
        {
            FwhmCalibration fwhm = Fwhm();
            double[] value = new double[Channels];

            Add(value, Template(Find(library, xrayName), fwhm), xrayArea);
            Add(value, Template(Find(library, "K-40"), fwhm), NuclideArea);

            for (int i = 0; i < Channels; i++)
            {
                // ⛔ КОНТИНУУМ — ПРЯМАЯ, И ЭТО НЕ ЛЕНЬ. Он обязан лежать В
                // ОБЛАСТИ ЗНАЧЕНИЙ сплайна модели ТОЧНО, иначе его невязку
                // подберут свободные колонки рентгена, и опыт станет мерить
                // качество сплайна вместо вырожденности пары (мерено:
                // экспонента давала колонкам впятеро больше заданного).
                // Высоким он взят по делу: рентген кристалла в жизни сидит НА
                // комптоновском подъёме, и вырожденной пару делает именно
                // слабый сигнал на сильном фоне — на пустом поле она делится.
                value[i] += 2600.0 - i;
            }

            return value;
        }

        /// <summary>
        /// Выборка спектра из ожидания: <paramref name="rnd"/> = null — округление
        /// (плечо «без шума»), иначе пуассоновский розыгрыш. Ниже порога АЦП —
        /// чистый ноль, как у настоящего прибора.
        /// </summary>
        static int[] Sample(double[] expected, Random rnd)
        {
            int[] counts = new int[Channels];
            for (int i = AdcFloor; i < Channels; i++)
            {
                counts[i] = rnd == null
                    ? (int)Math.Round(expected[i])
                    : Poisson(rnd, expected[i]);
            }

            return counts;
        }

        /// <summary>
        /// Пуассоновский розыгрыш: точный по Кнуту внизу и нормальное
        /// приближение выше 30, где точный обходится в тысячи умножений на
        /// канал. Зерно задаётся вызывающим и напечатано — прогон повторим.
        /// </summary>
        static int Poisson(Random rnd, double lambda)
        {
            if (!(lambda > 0.0))
            {
                return 0;
            }

            if (lambda < 30.0)
            {
                double limit = Math.Exp(-lambda);
                double product = 1.0;
                int k = 0;
                do
                {
                    k++;
                    product *= rnd.NextDouble();
                }
                while (product > limit);

                return k - 1;
            }

            double u1 = rnd.NextDouble();
            double u2 = rnd.NextDouble();
            if (u1 < 1e-12)
            {
                u1 = 1e-12;
            }

            double gauss = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            int value = (int)Math.Round(lambda + gauss * Math.Sqrt(lambda));
            return value < 0 ? 0 : value;
        }

        static double Mean(List<double> items)
        {
            double sum = 0.0;
            foreach (double v in items)
            {
                sum += v;
            }

            return items.Count > 0 ? sum / items.Count : double.NaN;
        }

        static double Sigma(List<double> items)
        {
            if (items.Count < 2)
            {
                return double.NaN;
            }

            double mean = Mean(items);
            double sum = 0.0;
            foreach (double v in items)
            {
                sum += (v - mean) * (v - mean);
            }

            return Math.Sqrt(sum / (items.Count - 1));
        }

        static double Min(List<double> items)
        {
            double best = double.PositiveInfinity;
            foreach (double v in items)
            {
                if (v < best)
                {
                    best = v;
                }
            }

            return best;
        }

        static double Max(List<double> items)
        {
            double best = double.NegativeInfinity;
            foreach (double v in items)
            {
                if (v > best)
                {
                    best = v;
                }
            }

            return best;
        }

        /// <summary>
        /// Матрица отклика: узлы 5…2000 кэВ, в каждом — чистый фотопик. Физики
        /// в ней нет и не нужно: раздел 3 спрашивает, КАКИЕ КОЛОНКИ снимает
        /// гейт, а гейт судит по флагу образа и по наличию матрицы, а не по её
        /// содержимому.
        /// </summary>
        static ResponseMatrix Matrix()
        {
            var energies = new List<double>();
            for (double e = 5.0; e < 2000.0; e *= 1.2)
            {
                energies.Add(Math.Round(e, 2));
            }

            energies.Add(2000.0);
            var rows = new float[energies.Count][];
            for (int i = 0; i < energies.Count; i++)
            {
                float[] row = new float[Channels];
                int bin = (int)Math.Round(energies[i]);
                if (bin >= 0 && bin < Channels)
                {
                    row[bin] = 1.0f;
                }

                rows[i] = row;
            }

            return new ResponseMatrix
            {
                Energies = energies.ToArray(),
                BinKev = 1.0,
                Rows = rows
            };
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        static FsaResult Analyze(int[] counts, List<FsaComponent> library,
                                 ResponseMatrix matrix, string arm)
        {
            FsaAnalyzer analyzer;
            return Analyze(counts, library, matrix, arm, out analyzer);
        }

        static FsaResult Analyze(int[] counts, List<FsaComponent> library,
                                 ResponseMatrix matrix, string arm, out FsaAnalyzer analyzer)
        {
            var spectrum = new EnergySpectrum(1.0, Channels)
            {
                EnergyCalibration = new PolynomialEnergyCalibration
                {
                    Coefficients = new[] { 0.0, 1.0 }
                },
                LiveTime = 1000.0
            };
            Array.Copy(counts, spectrum.Spectrum, Channels);

            analyzer = new FsaAnalyzer
            {
                MinEnergy = AdcFloor,
                MaxEnergy = 1800.0,
                CascadeSumming = false,
                CascadeSumPeaks = false,
                Backscatter = false,
                PileUp = false,
                // ⛔ Гейт геометрии (`A277`) СНЯТ НАРОЧНО: сцена искусственная,
                // геометрии у неё нет вовсе, а вопрос раздела — библиотека и
                // гейт рентгена, а не наличие геометрии. Снятие видно в отчёте
                // настроек строкой `RequireGeometry`, то есть молча не проходит.
                RequireGeometry = false,
                ResponseMatrix = matrix
            };

            // (`T243`) ЧЕМ СЧИТАЛИ — ДО СЧЁТА И ВСЛУХ, с пометкой плеча:
            // без неё два отчёта в одном выводе неразличимы.
            //
            // ⚠ `arm == null` — молчание НАРОЧНО и ровно в одном месте: в
            // розыгрышах шума анализатор собирается ЭТИМ ЖЕ приёмом с теми же
            // полями, и двенадцать одинаковых строк подряд не сообщают ничего,
            // а прячут те, что сообщают. Первый розыгрыш пометку получает.
            if (arm != null)
            {
                FsaTuningReport.Print(analyzer, arm);
            }

            // ⛔ Библиотека копируется: `Analyze` вправе выбросить из неё
            // колонки (гейты, нож полосы), а оба плеча обязаны получить один и
            // тот же вход.
            return analyzer.Analyze(spectrum, null, Fwhm(),
                                    new List<FsaComponent>(library), null);
        }

        /// <summary>
        /// Разбор при живой матрице: сколько образов кристалла снял гейт и
        /// какие свободные колонки рентгена КРИСТАЛЛА всё-таки дошли до модели.
        /// </summary>
        static List<string> XrayAfterGate(int[] counts, List<FsaComponent> library,
                                          ResponseMatrix matrix, string arm, out int dropped)
        {
            FsaAnalyzer analyzer;
            FsaResult result = Analyze(counts, library, matrix, arm, out analyzer);
            dropped = analyzer.CrystalXrayDropped;

            // Имена элементов кристалла, дошедшие до разбора. Читается по
            // СОСТАВУ библиотеки, а не по имени: колонка `Xray-Cs` в запасном
            // пути приходит из ПРОБЫ/ЗАЩИТЫ и флага `FromCrystal` не несёт —
            // именно поэтому гейт её и не видит.
            var crystalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsaComponent c in library)
            {
                if (c != null && c.Name != null && c.Name.StartsWith("Xray-", StringComparison.Ordinal))
                {
                    crystalNames.Add(c.Name);
                }
            }

            var left = new List<string>();
            if (result != null && result.Components != null)
            {
                foreach (FsaComponentResult c in result.Components)
                {
                    if (c != null && crystalNames.Contains(c.Name)
                        && LinesInCrystalBand(library, c.Name))
                    {
                        left.Add(c.Name);
                    }
                }
            }

            return left;
        }

        /// <summary>
        /// У образа есть линия в полосе K-серии кристалла (10…40 кэВ). Признак
        /// нужен, чтобы отделить рентген кристалла от рентгена свинца защиты,
        /// у которого своя полоса.
        /// </summary>
        static bool LinesInCrystalBand(List<FsaComponent> library, string name)
        {
            foreach (FsaComponent c in library)
            {
                if (c == null || !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (FsaLine line in c.Lines)
                {
                    if (line.Energy >= 10.0 && line.Energy <= 40.0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        /// <summary>ПШПВ² = 2.7·ch, то есть 9 кэВ на 30 и 42 кэВ на 662.</summary>
        static FwhmCalibration Fwhm()
        {
            return new SimpleSqrtFwhmCalibration
            {
                Coefficients = new[] { 0.0, 2.7 }
            };
        }

        static double[] Template(FsaComponent component, FwhmCalibration fwhm)
        {
            double[] value = new double[Channels];
            if (component == null)
            {
                return value;
            }

            foreach (FsaLine line in component.Lines)
            {
                double center = line.Energy;
                double width = fwhm.ChannelToFwhm(center);
                if (!(width > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                for (int i = 0; i < Channels; i++)
                {
                    value[i] += line.Intensity * PeakShapeModel.RelativeValue(i - center, width, fwhm);
                }
            }

            double sum = 0.0;
            for (int i = 0; i < Channels; i++)
            {
                sum += value[i];
            }

            if (sum > 0.0)
            {
                for (int i = 0; i < Channels; i++)
                {
                    value[i] /= sum;
                }
            }

            return value;
        }

        static void Add(double[] target, double[] shape, double area)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] += area * shape[i];
            }
        }

        static FsaComponent Find(List<FsaComponent> library, string name)
        {
            foreach (FsaComponent c in library)
            {
                if (c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
        }

        static int Count(List<FsaComponent> library, string name)
        {
            int n = 0;
            foreach (FsaComponent c in library)
            {
                if (c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    n++;
                }
            }

            return n;
        }

        static int CrystalXrayCount(List<FsaComponent> library)
        {
            int n = 0;
            foreach (FsaComponent c in library)
            {
                if (c != null && c.FromCrystal && c.Name != null
                    && c.Name.StartsWith("Xray-", StringComparison.Ordinal))
                {
                    n++;
                }
            }

            return n;
        }

        static string FirstCrystalXrayName(List<FsaComponent> library)
        {
            foreach (FsaComponent c in library)
            {
                if (c != null && c.FromCrystal && c.Name != null
                    && c.Name.StartsWith("Xray-", StringComparison.Ordinal))
                {
                    return c.Name;
                }
            }

            return "(нет)";
        }

        /// <summary>
        /// Доля веса K-серии, приходящаяся на элемент внутри сведённого образа.
        /// Метка линии — свой элемент («Xray-Cs»), см. `AddCrystalFluorescence`.
        /// </summary>
        static double CrystalWeightShare(List<FsaComponent> library, string name, int z)
        {
            FsaComponent component = Find(library, name);
            if (component == null)
            {
                return double.NaN;
            }

            string tag = "Xray-" + MaterialDatabase.SymbolOf(z);
            double own = 0.0, all = 0.0;
            foreach (FsaLine line in component.Lines)
            {
                all += line.Intensity;
                if (string.Equals(line.Nuclide, tag, StringComparison.OrdinalIgnoreCase))
                {
                    own += line.Intensity;
                }
            }

            return all > 0.0 ? own / all : double.NaN;
        }

        /// <summary>
        /// Площадь компонента в модели — сумма его кривой по каналам. Ноль
        /// значит «колонки в разборе нет»: либо она не построена, либо снята
        /// отсевом значимости.
        /// </summary>
        static double Area(FsaResult result, string name)
        {
            if (result == null || result.Components == null)
            {
                return 0.0;
            }

            foreach (FsaComponentResult c in result.Components)
            {
                if (c == null || !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                    || c.Curve == null)
                {
                    continue;
                }

                double sum = 0.0;
                foreach (double v in c.Curve)
                {
                    sum += v;
                }

                return sum;
            }

            return 0.0;
        }

        static double Z(FsaResult result, string name)
        {
            if (result == null || result.Components == null)
            {
                return double.NaN;
            }

            foreach (FsaComponentResult c in result.Components)
            {
                if (c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c.Z;
                }
            }

            return double.NaN;
        }

        static bool Noted(FsaSampleLibrary.Report report)
        {
            if (report == null || report.Notes == null)
            {
                return false;
            }

            foreach (string note in report.Notes)
            {
                if (note != null && note.IndexOf("уже занят образом", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        static void Report(string what, List<FsaComponent> library, FsaSampleLibrary.Report report)
        {
            var names = new List<string>();
            foreach (FsaComponent c in library)
            {
                if (c != null && c.Name != null && c.Name.StartsWith("Xray-", StringComparison.Ordinal))
                {
                    names.Add(c.Name + (c.FromCrystal ? "*" : ""));
                }
            }

            Console.WriteLine("  {0,-38} рентген: {1}", what, Join(names));
        }

        static string Join(List<string> items)
        {
            return items.Count == 0 ? "(нет)" : string.Join(", ", items.ToArray());
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-62} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(CultureInfo.InvariantCulture, " вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
