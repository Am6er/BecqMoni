using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GridStampProbeF45
{
    /// <summary>
    /// Полоса F45, `E17` и `E19`: сетка расчёта кривой называет себя честно, а
    /// проба, оставшаяся воздухом при непустом сосуде, перестаёт молчать.
    ///
    ///     gridstampprobef45                 весь прогон
    ///     gridstampprobef45 --modal-control положительный контроль сторожа окон
    ///     gridstampprobef45 --fast          без раздела цены (без счёта 200k)
    ///
    /// Проверяется ЧЕТЫРЬМЯ разделами, и у каждого положительный контроль —
    /// иначе «сошлось» неотличимо от «проверка ничего не смотрит».
    ///
    /// 1. `E17` «а» — верх диапазона. Прежде `BuildGrid` начинал с
    ///    `Math.Max(lo * 1.01, MaxEnergyKev)`, то есть раздвигал ДОЛЕЙ от низа:
    ///    3000…3010 считалось до 3030. Старая формула здесь ВОСПРОИЗВЕДЕНА
    ///    (`OldHi`) — одна строка арифметики, без побочных действий, дословно из
    ///    `HEAD`, — и печатается рядом с новым числом.
    ///
    /// 2. `E17` «б» — имя сетки. Штатная, не нашедшая внутри диапазона двух
    ///    своих узлов, становится логарифмической; журнал и клеймо обязаны
    ///    назвать ПОСЧИТАННУЮ. Имя из журнала сверяется не с ожиданием, а с
    ///    НЕЗАВИСИМЫМ признаком: узлы логарифмической сетки равноотстоят по
    ///    логарифму, штатные — нет (`LooksLogarithmic`).
    ///
    /// 3. Клеймо НЕ ДВИГАЕТСЯ там, где счёт не изменился: на сцене со штатной
    ///    сеткой и названным веществом оно сверяется ПОСИМВОЛЬНО со строкой,
    ///    собранной по СТАРОМУ формату (пять полей, без хвоста).
    ///
    /// 4. `E19` — признак «проба осталась воздухом» и цена вопроса: одна и та
    ///    же геометрия считается с воздухом и с оксидом лютеция, отношение
    ///    печатается числом.
    /// </summary>
    static class Program
    {
        static int bad;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ Сторож модальных окон — ПЕРВЫМ ДЕЛОМ (приём полосы F20,
            //    `CultureProbeO14`): окно, поднятое на безоконном пути, вешает
            //    прогон насмерть, и нажать «ОК» здесь некому.
            ModalWatchStart();

            bool modalControl = false, fast = false;
            foreach (string a in args)
            {
                if (a == "--modal-control") modalControl = true;
                // `A263`: второе условие было ОТДЕЛЬНЫМ `if`; сведено в цепочку,
                // чтобы у неё был хвост. Довод не может быть разом обоими.
                else if (a == "--fast") fast = true;
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }

            try
            {
                if (modalControl)
                {
                    ModalControl();
                }
                else
                {
                    UpperBound();
                    GridNameAndStamp();
                    StampUnchanged();
                    SampleIsAirFlag();
                    if (!fast)
                    {
                        AirCost();
                    }
                    else
                    {
                        Say("");
                        Say("РАЗДЕЛ 4б ПРОПУЩЕН (`--fast`): цена воздуха не мерена");
                    }
                }
            }
            catch (Exception e)
            {
                bad++;
                Say("⛔ ИСКЛЮЧЕНИЕ: " + e);
            }

            ModalWatchStop();
            Say("");
            Say("модальных окон за прогон: " + modalSeen
                + (modalSeen == 0 ? " (ни одного — безоконный путь чист)" : " ⛔"));
            Say(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " ПРОВЕРОК ПРОВАЛЕНО");
            return bad == 0 ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════
        //  1. `E17` «а» — верхняя граница диапазона
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Старая формула, дословно из `HEAD`:
        /// <c>double hi = Math.Max(lo * 1.01, this.MaxEnergyKev);</c>
        /// Воспроизведена здесь, чтобы «до правки» было ЧИСЛОМ, а не словом.
        /// </summary>
        static double OldHi(double lo, double hi)
        {
            return Math.Max(lo * 1.01, hi);
        }

        static void UpperBound()
        {
            Head("РАЗДЕЛ 1. `E17` «а»: верх диапазона больше не раздвигается долей от низа");

            Span("законный узкий диапазон", 3000, 3010, 3010.0, false);
            Span("штатный диапазон", 40, 3000, 3000.0, false);
            Span("широкий диапазон", 20, 5000, 5000.0, false);
            // Вырожденные входы: верх не выше низа. Расчёт обязан идти дальше —
            // из сетки в одну точку кривой нет вовсе.
            Span("вырожденный: верх = низ", 1000, 1000, 1001.0, true);
            Span("вырожденный: верх ниже низа", 3000, 2500, 3001.0, true);
            Span("вырожденный внизу шкалы", 5, 5, 6.0, true);

            Say("");
            Say("  ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ раздела: старая формула действительно");
            Say("  раздвигала законный диапазон — иначе сверять было бы не с чем.");
            double old3010 = OldHi(3000, 3010);
            Check("старая формула на 3000…3010 даёт 3030", Math.Abs(old3010 - 3030.0) < 1e-9);
            Say(string.Format(Inv, "    OldHi(3000, 3010) = {0:0.###} кэВ", old3010));
        }

        /// <summary>Одна сцена диапазона: что вышло сейчас и что вышло бы прежде.</summary>
        static void Span(string title, double lo, double hi, double expectTop, bool widened)
        {
            GeometryModel g = Scene("Air, dry", 0.0);
            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = lo,
                MaxEnergyKev = hi,
                GridMode = EfficiencyGridMode.Standard,
            };

            List<string> notes = new List<string>();
            EfficiencyGridMode used;
            double[] grid = options.BuildGrid(g, notes, out used);
            double top = grid[grid.Length - 1];
            double old = OldHi(Math.Max(1.0, lo), hi);

            Say(string.Format(Inv,
                "  {0,-28} поле {1:0.#}…{2:0.#}: верх сейчас {3:0.###}, прежде было бы {4:0.###}"
                + ", узлов {5}",
                title, lo, hi, top, old, grid.Length));

            // Сетка отвечает и тем, КАКОЙ она вышла: сверяем не с ожиданием, а
            // с независимым признаком — расположением самих узлов.
            Check(title + ": имя сетки сошлось с расположением узлов",
                  (used == EfficiencyGridMode.Logarithmic) == LooksLogarithmic(grid));
            Check(title + ": верх сетки = верх поля", Math.Abs(top - expectTop) < 1e-9);
            Check(title + ": узлов не меньше двух", grid.Length >= 2);

            bool ordered = true;
            for (int i = 1; i < grid.Length; i++)
            {
                ordered &= grid[i] > grid[i - 1];
            }

            Check(title + ": узлы возрастают", ordered);

            // Строка журнала о раздвижке: ровно там, где раздвигали, и нигде
            // больше. Ожидаемый текст СОБИРАЕТСЯ тем же ресурсом, что и в
            // приложении, — сверять по куску текста значило бы сверять с самим
            // собой наполовину.
            string expected = string.Format(Inv, Resources.EfficiencyMakerGridWidened,
                                            hi, Math.Max(1.0, lo), Math.Max(1.0, lo) + 1.0);
            bool said = notes.Contains(expected);
            Check(title + (widened ? ": раздвижка НАЗВАНА в журнале"
                                   : ": лишней строки о раздвижке НЕТ"), said == widened);
            if (said)
            {
                Say("      журнал: " + expected);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  2. `E17` «б» — имя сетки в журнале и в клейме
        // ══════════════════════════════════════════════════════════════════

        static void GridNameAndStamp()
        {
            Head("РАЗДЕЛ 2. `E17` «б»: журнал и клеймо называют ПОСЧИТАННУЮ сетку");

            GeometryModel g = Scene("Lutetium oxide", 2.5);
            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = 2900,
                MaxEnergyKev = 3100,
                GridMode = EfficiencyGridMode.Standard,
                Histories = 1000,
            };

            List<string> journal = new List<string>();
            EfficiencyFitResult result = EfficiencyCalculation.Run(
                g, options, delegate(string s) { journal.Add(s); }, delegate { return false; });

            Check("сцена 2900…3100 посчиталась", result.Ok);
            if (!result.Ok)
            {
                Say("    ошибка: " + result.Error);
                return;
            }

            double[] nodes = new double[result.Curve.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = result.Curve[i].Energy;
            }

            int standardInside = 0;
            foreach (double e in EfficiencyCalculation.DefaultEnergies)
            {
                if (e >= 2900.0 && e <= 3100.0) standardInside++;
            }

            bool logByNodes = LooksLogarithmic(nodes);
            Say(string.Format(Inv, "  штатных узлов внутри диапазона: {0}; узлов кривой {1};"
                              + " по РАСПОЛОЖЕНИЮ узлов сетка {2}",
                              standardInside, nodes.Length,
                              logByNodes ? "логарифмическая" : "штатная"));

            string journalName = GridNameFromJournal(journal);
            Say("  имя сетки в журнале: «" + journalName + "»");
            Say("  клеймо ПОСЛЕ правки:  " + result.ComputeStamp);

            // Клеймо ДО правки — тот же формат, то же всё, кроме имени сетки:
            // старый код печатал ЗАКАЗАННУЮ (`options.GridMode`), то есть `std`.
            string before = OldStamp(result, options.Histories, "std");
            Say("  клеймо ДО правки:     " + before);

            Check("журнал называет логарифмическую", journalName == Resources.EfficiencyMakerGridLogarithmic);
            Check("узлы и правда логарифмические", logByNodes);
            Check("журнал сошёлся с расположением узлов",
                  Verdict(journalName, logByNodes));
            Check("клеймо кончается на log", result.ComputeStamp.EndsWith(" log", StringComparison.Ordinal));
            Check("клеймо ДО правки называло штатную (значит, было чему меняться)",
                  before.EndsWith(" std", StringComparison.Ordinal) && before != result.ComputeStamp);

            // Строка-подмена в журнале: собирается тем же ресурсом.
            string fallback = string.Format(Inv, Resources.EfficiencyMakerGridFallback,
                                            standardInside, 2900.0, 3100.0,
                                            Math.Max(2, options.NodeCount));
            Check("журнал назвал подмену сетки", journal.Contains(fallback));
            if (journal.Contains(fallback))
            {
                Say("      журнал: " + fallback);
            }

            // Клеймо читается ОБРАТНО формой (E23) — иначе восстановленные поля
            // повторили бы не тот счёт.
            double lo, hi, hist, pts;
            bool logGrid;
            bool parsed = ParseStamp(result.ComputeStamp, out lo, out hi, out hist, out pts, out logGrid);
            Say(string.Format(Inv, "  форма читает клеймо обратно: {0}, {1:0.#}…{2:0.#} кэВ,"
                              + " историй {3:0}, узлов {4:0}, сетка {5}",
                              parsed ? "да" : "НЕТ", lo, hi, hist, pts, logGrid ? "log" : "std"));
            Check("клеймо разбирается формой", parsed);
            Check("разбор клейма даёт логарифмическую", logGrid);

            Say("");
            Say("  ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: сетку подменяем нарочно — проверка обязана поймать.");
            Check("подмена «журнал говорит штатная, узлы логарифмические» ПОЙМАНА",
                  !Verdict(Resources.EfficiencyMakerGridStandard, logByNodes));
            Check("подмена «журнал говорит логарифмическая, узлы штатные» ПОЙМАНА",
                  !Verdict(Resources.EfficiencyMakerGridLogarithmic, false));
            Check("клеймо, где `log` подменён на `std`, ПОЙМАНО сверкой",
                  !Same(result.ComputeStamp, result.ComputeStamp.Replace(" log", " std")));
        }

        /// <summary>Журнал и узлы говорят об одной и той же сетке?</summary>
        static bool Verdict(string journalName, bool logByNodes)
        {
            bool journalSaysLog = journalName == Resources.EfficiencyMakerGridLogarithmic;
            return journalSaysLog == logByNodes;
        }

        /// <summary>
        /// Независимый признак логарифмической сетки: отношение соседних узлов
        /// постоянно. Штатная сетка идёт «…2450, 2615, 2800, 3000» — отношения
        /// у неё гуляют, и спутать её с логарифмической нельзя.
        /// </summary>
        static bool LooksLogarithmic(double[] nodes)
        {
            if (nodes.Length < 3) return false;
            double first = nodes[1] / nodes[0];
            for (int i = 2; i < nodes.Length; i++)
            {
                double r = nodes[i] / nodes[i - 1];
                if (Math.Abs(r - first) > 1e-9 * first + 1e-12) return false;
            }

            return true;
        }

        /// <summary>Имя сетки, как его напечатал журнал прогона.</summary>
        static string GridNameFromJournal(List<string> journal)
        {
            foreach (string line in journal)
            {
                if (line.Contains("(" + Resources.EfficiencyMakerGridLogarithmic + ")"))
                {
                    return Resources.EfficiencyMakerGridLogarithmic;
                }

                if (line.Contains("(" + Resources.EfficiencyMakerGridStandard + ")"))
                {
                    return Resources.EfficiencyMakerGridStandard;
                }
            }

            return "(не названа)";
        }

        // ══════════════════════════════════════════════════════════════════
        //  3. Клеймо не двигается там, где счёт не изменился
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Клеймо ПО СТАРОМУ формату, дословно из `HEAD`:
        /// <c>"phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}"</c>.
        /// </summary>
        static string OldStamp(EfficiencyFitResult r, int histories, string grid)
        {
            return string.Format(Inv, "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}",
                                 ResponseMatrix.PhysicsVersion, Math.Max(1000, histories),
                                 r.MinEnergy, r.MaxEnergy, r.Curve.Count, grid);
        }

        static void StampUnchanged()
        {
            Head("РАЗДЕЛ 3. Клеймо ПОСИМВОЛЬНО прежнее там, где счёт тот же");

            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = 40,
                MaxEnergyKev = 3000,
                GridMode = EfficiencyGridMode.Standard,
                Histories = 1000,
            };

            // Названное вещество: штатная сетка, воздуха нет — клеймо обязано
            // совпасть со старым посимвольно.
            List<string> namedLog = new List<string>();
            EfficiencyFitResult named = EfficiencyCalculation.Run(
                Scene("Lutetium oxide", 2.5), options,
                delegate(string s) { namedLog.Add(s); }, delegate { return false; });
            Check("сцена 40…3000 с названным веществом посчиталась", named.Ok);
            if (!named.Ok)
            {
                Say("    ошибка: " + named.Error);
                return;
            }

            string old = OldStamp(named, options.Histories, "std");
            Say("  клеймо сейчас:      " + named.ComputeStamp);
            Say("  клеймо по-старому:  " + old);
            Compare("штатная сетка, вещество названо", named.ComputeStamp, old);

            // Та же сцена с воздухом: единственное отличие — хвост.
            List<string> airLog = new List<string>();
            EfficiencyFitResult air = EfficiencyCalculation.Run(
                Scene("Air, dry", 0.0), options,
                delegate(string s) { airLog.Add(s); }, delegate { return false; });
            Check("та же сцена с воздухом посчиталась", air.Ok);
            if (!air.Ok)
            {
                Say("    ошибка: " + air.Error);
                return;
            }

            // `E19`, решение Amber: форма расчёта обязана СКАЗАТЬ о воздухе
            // видимой строкой — журнал прогона и есть то, что показано в окне.
            // Ожидаемый текст собирается тем же ресурсом, что и в приложении.
            string warning = string.Format(Inv, Resources.EfficiencyMakerSampleIsAir,
                                           "Air, dry", 0.001205, 20.0);
            Say("  журнал сцены с воздухом:");
            Say("      " + warning);
            Check("`E19`: предупреждение о воздухе В ЖУРНАЛЕ прогона", airLog.Contains(warning));
            Check("`E19`: у названного вещества предупреждения НЕТ",
                  !namedLog.Contains(warning) && !JournalMentionsAir(namedLog));

            string oldAir = OldStamp(air, options.Histories, "std");
            Say("  клеймо с воздухом:  " + air.ComputeStamp);
            Say("  оно же по-старому:  " + oldAir);
            Compare("воздух: всё, кроме хвоста", air.ComputeStamp, oldAir + "; sample=air");
            Check("хвост стоит ТОЛЬКО у воздуха",
                  air.ComputeStamp.EndsWith("; sample=air", StringComparison.Ordinal)
                  && named.ComputeStamp.IndexOf("sample=air", StringComparison.Ordinal) < 0);

            // Клеймо с хвостом обязано читаться формой обратно: хвост не должен
            // ломать разбор (E23), иначе поля расчёта перестали бы
            // восстанавливаться у всякой кривой с воздухом.
            double lo, hi, hist, pts;
            bool logGrid;
            bool parsed = ParseStamp(air.ComputeStamp, out lo, out hi, out hist, out pts, out logGrid);
            Say(string.Format(Inv, "  форма читает клеймо с хвостом: {0}, {1:0.#}…{2:0.#} кэВ,"
                              + " сетка {3}", parsed ? "да" : "НЕТ", lo, hi, logGrid ? "log" : "std"));
            Check("хвост `; sample=air` не ломает разбор клейма", parsed && !logGrid);

            Say("");
            Say("  ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: сверка обязана ловить расхождение в один знак.");
            Check("подменённое клеймо ПОЙМАНО",
                  !Same(named.ComputeStamp, named.ComputeStamp.Replace("keV", "kev")));
        }

        /// <summary>
        /// Есть ли в журнале ХОТЬ КАКАЯ строка предупреждения о воздухе. Ищется
        /// не готовый текст, а неизменная голова формата — до первой подстановки:
        /// иначе «нет строки с ЭТИМИ числами» сошло бы за «предупреждения нет».
        /// </summary>
        static bool JournalMentionsAir(List<string> journal)
        {
            string head = Resources.EfficiencyMakerSampleIsAir;
            int brace = head.IndexOf('{');
            if (brace > 0) head = head.Substring(0, brace);
            head = head.Trim();
            if (head.Length == 0) return false;
            foreach (string line in journal)
            {
                if (line.IndexOf(head, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        static void Compare(string title, string actual, string expected)
        {
            bool same = Same(actual, expected);
            if (!same)
            {
                int i = 0;
                while (i < actual.Length && i < expected.Length && actual[i] == expected[i]) i++;
                Say(string.Format(Inv, "    ⛔ расходятся с {0}-го знака: «{1}» против «{2}»",
                                  i,
                                  i < actual.Length ? actual.Substring(i) : "(конец)",
                                  i < expected.Length ? expected.Substring(i) : "(конец)"));
            }
            else
            {
                Say(string.Format(Inv, "    сошлось ПОСИМВОЛЬНО, знаков {0}", actual.Length));
            }

            Check(title, same);
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        // ══════════════════════════════════════════════════════════════════
        //  4. `E19` — проба осталась воздухом
        // ══════════════════════════════════════════════════════════════════

        static void SampleIsAirFlag()
        {
            Head("РАЗДЕЛ 4а. `E19`: признак «проба осталась воздухом» — обе стороны");

            Flag("цилиндр Ø40×20, воздух (заготовка редактора)", Cylinder("Air, dry", 0.0), true);
            Flag("цилиндр Ø40×20, оксид лютеция", Cylinder("Lutetium oxide", 2.5), false);
            Flag("цилиндр Ø40×20, вода", Cylinder("Water, liquid", 0.0), false);
            Flag("точечный источник в воздухе (законный случай)", Point("Air, dry"), false);
            Flag("маринелли, воздух", Marinelli("Air, dry", 0.0), true);
            Flag("коробка, воздух", Box("Air, dry", 0.0), true);
            Flag("цилиндр нулевой высоты, воздух", ZeroHeight(), false);
            // Граница по ПЛОТНОСТИ: имя роли не играет.
            Flag("цилиндр, «воздух» плотностью 0.0100 (порог)", Cylinder("Air, dry", 0.0100), true);
            Flag("цилиндр, «воздух» плотностью 0.0101 (за порогом)", Cylinder("Air, dry", 0.0101), false);
            Flag("цилиндр, оксид лютеция плотностью 0.001 (имя не спасает)",
                 Cylinder("Lutetium oxide", 0.001), true);

            Say(string.Format(Inv, "  порог плотности: {0:0.####} г/см³",
                              GeometryModel.AirSampleDensity));
        }

        static void Flag(string title, GeometryModel g, bool expected)
        {
            bool actual = g.SampleIsAir;
            Say(string.Format(Inv, "  {0,-52} признак {1,-3} (высота пробы {2:0.#} мм,"
                              + " объём {3})",
                              title, actual ? "ДА" : "нет", g.SampleHeightMm,
                              g.HasSampleVolume ? "есть" : "нет"));
            Check(title, actual == expected);
        }

        static void AirCost()
        {
            Head("РАЗДЕЛ 4б. `E19`: ЦЕНА молчания — во сколько раз расходится кривая");

            // Два узла ровно на линиях лютеция: 202 и 307 кэВ. Сетка
            // логарифмическая на двух узлах — это ровно они и есть.
            EfficiencyCalculationOptions options = new EfficiencyCalculationOptions
            {
                MinEnergyKev = 202,
                MaxEnergyKev = 307,
                GridMode = EfficiencyGridMode.Logarithmic,
                NodeCount = 2,
                Histories = 200000,
            };

            EfficiencyFitResult air = EfficiencyCalculation.Run(
                Cylinder("Air, dry", 0.0), options, delegate { }, delegate { return false; });
            EfficiencyFitResult lu25 = EfficiencyCalculation.Run(
                Cylinder("Lutetium oxide", 2.5), options, delegate { }, delegate { return false; });
            EfficiencyFitResult lu45 = EfficiencyCalculation.Run(
                Cylinder("Lutetium oxide", 4.5), options, delegate { }, delegate { return false; });

            Check("прогон с воздухом", air.Ok);
            Check("прогон с оксидом лютеция ρ=2.5", lu25.Ok);
            Check("прогон с оксидом лютеция ρ=4.5", lu45.Ok);
            if (!air.Ok || !lu25.Ok || !lu45.Ok)
            {
                return;
            }

            Say("  сцена: цилиндр Ø40×20 мм на 5 мм от торца, детектор — "
                + GeometryPresets.Items[0].Name + ", 200 000 историй на узел");
            Say("");
            Say("    кэВ   воздух      Lu₂O₃ ρ=2.5   ×      Lu₂O₃ ρ=4.5   ×");
            for (int i = 0; i < air.Curve.Count && i < lu25.Curve.Count && i < lu45.Curve.Count; i++)
            {
                double a = air.Curve[i].Efficiency;
                double b = lu25.Curve[i].Efficiency;
                double c = lu45.Curve[i].Efficiency;
                Say(string.Format(Inv, "   {0,5:0}  {1:0.000000}    {2:0.000000}   {3:0.000}"
                                  + "  {4:0.000000}   {5:0.000}",
                                  air.Curve[i].Energy, a, b, a > 0 ? b / a : 0.0,
                                  c, a > 0 ? c / a : 0.0));
            }

            Say("");
            Say("  Множитель — во сколько раз кривая с НАСТОЯЩИМ веществом ниже кривой");
            Say("  с воздухом на том же месте. Строка `E19` ждала ×0.30…0.18 на 202 кэВ");
            Say("  при ρ = 2.5…4.5 (μ/ρ = 0.630 см²/г, слой 20 мм).");

            Check("на 202 кэВ воздух завышает кривую не меньше чем вдвое",
                  air.Curve[0].Efficiency > 2.0 * lu25.Curve[0].Efficiency);
            Check("плотнее — ниже (ρ=4.5 ниже, чем ρ=2.5)",
                  lu45.Curve[0].Efficiency < lu25.Curve[0].Efficiency);

            Say("");
            Say("  ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: воздух против воздуха обязан дать ЕДИНИЦУ —");
            Say("  иначе разница выше была бы просто шумом счёта.");
            EfficiencyFitResult air2 = EfficiencyCalculation.Run(
                Cylinder("Air, dry", 0.0), options, delegate { }, delegate { return false; });
            double ratio = air2.Ok && air.Curve[0].Efficiency > 0.0
                ? air2.Curve[0].Efficiency / air.Curve[0].Efficiency : 0.0;
            Say(string.Format(Inv, "    воздух / воздух на 202 кэВ = {0:0.000000}", ratio));
            Check("повтор того же счёта даёт то же число", Math.Abs(ratio - 1.0) < 1e-12);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сцены
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Сцена берётся у ЗАГОТОВКИ редактора и первого пресета — свои числа
        /// здесь не набираются, иначе проба разойдётся с формой при первой же
        /// правке заготовки (довод `EffGridRangeProbe`).
        /// </summary>
        static GeometryModel Scene(string sampleName, double density)
        {
            return Cylinder(sampleName, density);
        }

        static GeometryModel Base()
        {
            GeometryModel g = BecquerelMonitor.GeometryEditorPanel.Blank();
            GeometryPresets.Items[0].Apply(g);
            return g;
        }

        static GeometryModel Cylinder(string sampleName, double density)
        {
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Cylinder;
            g.Source = Material(sampleName, density);
            return g;
        }

        static GeometryModel Point(string sampleName)
        {
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Point;
            g.Source = Material(sampleName, 0.0);
            return g;
        }

        static GeometryModel Marinelli(string sampleName, double density)
        {
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Marinelli;
            g.Source = Material(sampleName, density);
            return g;
        }

        static GeometryModel Box(string sampleName, double density)
        {
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Box;
            g.BoxSourceX = 40.0;
            g.BoxSourceY = 40.0;
            g.BoxSourceHeight = 20.0;
            g.Source = Material(sampleName, density);
            return g;
        }

        static GeometryModel ZeroHeight()
        {
            GeometryModel g = Cylinder("Air, dry", 0.0);
            g.SourceHeight = 0.0;
            return g;
        }

        static GeometryMaterial Material(string name, double density)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            if (entry == null)
            {
                bad++;
                Say("⛔ вещества «" + name + "» нет в библиотеке");
                return new GeometryMaterial();
            }

            return GeometryMaterialLibrary.Make(entry, density);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Разбор клейма формой — через отражение: метод закрытый, а читать
        //  его обязаны именно тем кодом, каким читает приложение.
        // ══════════════════════════════════════════════════════════════════

        static bool ParseStamp(string stamp, out double lo, out double hi,
                               out double histories, out double nodes, out bool logGrid)
        {
            lo = hi = histories = nodes = 0.0;
            logGrid = false;
            MethodInfo mi = typeof(BecquerelMonitor.EfficiencyMakerForm).GetMethod(
                "TryParseComputeStamp", BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                bad++;
                Say("⛔ метода TryParseComputeStamp не нашлось — разбор клейма не проверен");
                return false;
            }

            object[] argv = new object[] { stamp, 0.0, 0.0, 0.0, 0.0, false };
            bool ok = (bool)mi.Invoke(null, argv);
            lo = (double)argv[1];
            hi = (double)argv[2];
            histories = (double)argv[3];
            nodes = (double)argv[4];
            logGrid = (bool)argv[5];
            return ok;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Печать
        // ══════════════════════════════════════════════════════════════════

        static void Head(string title)
        {
            Say("");
            Say("══════════════════════════════════════════════════════════════");
            Say(title);
            Say("══════════════════════════════════════════════════════════════");
        }

        static void Check(string title, bool ok)
        {
            if (!ok) bad++;
            Say((ok ? "    ok    " : "    ПЛОХО ") + title);
        }

        static readonly object sayGate = new object();

        static void Say(string line)
        {
            lock (sayGate)
            {
                Console.WriteLine(line);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  СТОРОЖ МОДАЛЬНЫХ ОКОН — приём полосы F20 (`CultureProbeO14`).
        //  Каждые 200 мс перечисляет окна СВОЕГО процесса класса `#32770`,
        //  называет текст, засчитывает расхождение и посылает `WM_CLOSE`:
        //  безоконный прогон обязан отказывать быстро и внятно, а не висеть.
        // ══════════════════════════════════════════════════════════════════

        const string DialogClass = "#32770";
        const uint WM_CLOSE = 0x0010;

        delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        static volatile bool modalWatchStop;
        static Thread modalWatchThread;
        static int modalSeen;

        static void ModalWatchStart()
        {
            modalWatchThread = new Thread(delegate()
            {
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                Dictionary<long, bool> known = new Dictionary<long, bool>();
                while (!modalWatchStop)
                {
                    List<IntPtr> found = new List<IntPtr>();
                    try
                    {
                        EnumWindows(delegate(IntPtr h, IntPtr l)
                        {
                            uint pid;
                            GetWindowThreadProcessId(h, out pid);
                            if (pid != self) return true;
                            StringBuilder cls = new StringBuilder(64);
                            GetClassNameW(h, cls, cls.Capacity);
                            if (cls.ToString() == DialogClass) found.Add(h);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception) { }

                    foreach (IntPtr h in found)
                    {
                        long key = h.ToInt64();
                        if (known.ContainsKey(key)) continue;
                        known[key] = true;
                        modalSeen++;
                        bad++;
                        Say("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                            + "» — сторож закрывает его сам; нажать «ОК» здесь некому");
                        try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                        catch (Exception) { }
                    }

                    Thread.Sleep(200);
                }
            });
            modalWatchThread.IsBackground = true;
            modalWatchThread.Start();
        }

        static string ModalText(IntPtr dialog)
        {
            StringBuilder acc = new StringBuilder();
            try
            {
                EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
                {
                    StringBuilder cls = new StringBuilder(64);
                    GetClassNameW(ch, cls, cls.Capacity);
                    if (cls.ToString() == "Static")
                    {
                        StringBuilder txt = new StringBuilder(512);
                        GetWindowTextW(ch, txt, txt.Capacity);
                        string s = txt.ToString().Trim();
                        if (s.Length > 0)
                        {
                            if (acc.Length > 0) acc.Append(" / ");
                            acc.Append(s);
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
        }

        static void ModalWatchStop()
        {
            modalWatchStop = true;
            if (modalWatchThread != null) modalWatchThread.Join(2000);
        }

        /// <summary>
        /// Положительный контроль сторожа: окно поднимается НАРОЧНО, на фоновом
        /// потоке. Сторож обязан назвать его и закрыть, иначе «окон не было»
        /// неотличимо от «сторож не работает».
        /// </summary>
        static void ModalControl()
        {
            Head("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (`--modal-control`)");
            int before = modalSeen;
            DateTime t0 = DateTime.UtcNow;
            Thread th = new Thread(delegate()
            {
                System.Windows.Forms.MessageBox.Show("контрольное окно полосы F45",
                                                     "контроль",
                                                     System.Windows.Forms.MessageBoxButtons.OK);
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            bool closed = th.Join(20000);
            double sec = (DateTime.UtcNow - t0).TotalSeconds;
            Say("  окно поднято нарочно, закрыто сторожем: " + (closed ? "да" : "НЕТ")
                + ", секунд " + sec.ToString("F1", Inv)
                + ", окон назвал сторож: " + (modalSeen - before));
            if (!closed)
            {
                Say("⛔ сторож окно НЕ закрыл — приёмке безоконных прогонов верить нельзя");
            }
        }
    }
}
