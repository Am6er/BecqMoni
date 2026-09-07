using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// Поверка атомной половины каскадного суммирования (S27): K-рентген и
    /// аннигиляционные кванты как партнёры совпадения.
    ///
    /// ⛔ ЧТО ИМЕННО ПОВЕРЯЕТСЯ, и почему это не «печать чисел». Бухгалтерия
    /// вакансий замкнута: полный выход K-рентгена, делённый на ω_K, обязан
    /// равняться сумме вакансий от конверсии ПЛЮС вакансии от захвата. Три
    /// источника независимы (`decay_radiations`, `g4_gamma`,
    /// `fluorescence_yield`), поэтому сойтись они могут только если каждый
    /// прочитан верно. Отсюда две настоящие проверки:
    ///
    ///   * у β-излучателя БЕЗ захвата (Cs-137, Co-60, Lu-176) остаток обязан
    ///     быть НУЛЁМ — весь рентген оттуда только от конверсии;
    ///   * у захватного остаток обязан лечь долей K-захвата, то есть 0.6…0.9, —
    ///     не больше единицы и не меньше нуля.
    ///
    /// Отрицательный остаток означает, что сопоставление линии с переходом
    /// взяло не тот переход (TODO D31), и это ловится здесь, а не в спектре.
    ///
    /// Запуск: CascadeXrayProbe.exe [--window=1e-6]
    /// </summary>
    static class CascadeXrayProbe
    {
        sealed class Expectation
        {
            public string Nucid;
            public string Name;

            /// <summary>Ожидаемый остаток захвата: 0 — β-излучатель без захвата.</summary>
            public double PromptLo;

            public double PromptHi;

            public string Why;
        }

        static readonly Expectation[] Expected =
        {
            new Expectation { Nucid = "176LU", Name = "Lu-176", PromptLo = -0.02, PromptHi = 0.02,
                              Why = "β⁻, захвата нет: весь K-рентген Hf от конверсии 88.34" },
            new Expectation { Nucid = "137CS", Name = "Cs-137", PromptLo = -0.02, PromptHi = 0.02,
                              Why = "β⁻, захвата нет: рентген Ba от конверсии 661.7" },
            new Expectation { Nucid = "60CO",  Name = "Co-60",  PromptLo = -0.02, PromptHi = 0.02,
                              Why = "β⁻, рентгена практически нет вовсе" },
            // ⚠ Th-234 сюда НЕ ЗАВОДИТСЯ, и это измеренный факт, а не забывчивость:
            //   у него в `decay_radiations` только L-рентген (16.228 кэВ), K-серии нет
            //   вовсе, и `CascadeAtomicData.Of` честно отдаёт null раньше, чем дело
            //   доходит до дочернего. Записано затем, чтобы его не добавили снова как
            //   «приёмку D32»: сам разбор изомерного имени поверяется `NucidProbe`.
            new Expectation { Nucid = "133BA", Name = "Ba-133", PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %" },
            new Expectation { Nucid = "139CE", Name = "Ce-139", PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %, одна гамма 165.86" },
            new Expectation { Nucid = "109CD", Name = "Cd-109", PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %" },
            new Expectation { Nucid = "207BI", Name = "Bi-207", PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %" },
            new Expectation { Nucid = "152EU", Name = "Eu-152", PromptLo = 0.35, PromptHi = 0.95,
                              Why = "захват 72 %, β⁻ 28 %" },
            new Expectation { Nucid = "57CO",  Name = "Co-57",  PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %" },
            new Expectation { Nucid = "54MN",  Name = "Mn-54",  PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 100 %, одна гамма 834.8" },
            new Expectation { Nucid = "65ZN",  Name = "Zn-65",  PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват 98.3 %, β⁺ 1.4 %" },
            new Expectation { Nucid = "88Y",   Name = "Y-88",   PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват" },
            new Expectation { Nucid = "44TI",  Name = "Ti-44",  PromptLo = 0.40, PromptHi = 0.95,
                              Why = "захват" },
        };

        /// <summary>
        /// Уровни, чьё время жизни РЕШАЕТ, есть совпадение или нет. Проверяются
        /// поимённо: гейт по времени — единственное, что отделяет настоящую
        /// сумму от выдуманной, и его нельзя оставлять без поверки.
        /// </summary>
        static readonly object[][] Timing =
        {
            //   нуклид   гамма, кэВ  ожидание «совпадает с мгновенным квантом»
            new object[] { "176LU", 306.780, true,  "уровень Hf 596.8 мгновенный" },
            new object[] { "176LU", 201.830, true,  "уровень Hf 290.2 мгновенный" },
            new object[] { "176LU",  88.340, true,  "уровень Hf 88.35 живёт 1.43 нс" },
            new object[] { "109CD",  88.034, false, "уровень Ag 88.03 живёт 39.79 с — Ag-109m" },
            new object[] { "137CS", 661.657, false, "уровень Ba 661.7 живёт 153 с — Ba-137m" },
            new object[] { "44TI",   78.323, false, "уровень Sc 146.2 живёт 51 мкс" },
            new object[] { "44TI",   67.868, false, "он же, ниже по каскаду" },
            new object[] { "139CE", 165.857, true,  "уровень La 165.9 живёт 1.5 нс" },
            new object[] { "22NA", 1274.537, true,  "уровень Ne 1274.5 живёт 3.6 пс" },
        };

        /// <summary>
        /// Условное число квантов 511 при данной гамме (`S150`).
        ///
        /// ⛔ ЖДЁМ НЕ ТО, ЧТО СЧИТАЕТ КОД. Ожидания выведены отдельно, по
        /// `ensdf_feedings` и `ensdf_gammas`, с той же протяжкой каскада сверху
        /// вниз, но своим счётом — иначе проверка сверяла бы код с самим собой.
        /// Нули здесь не «ничего не посчиталось», а ФИЗИКА: позитрон населяет
        /// один уровень, гамма идёт с другого.
        /// </summary>
        static readonly object[][] Annihilation =
        {
            new object[] { "40K",  1460.820, 0.0,
                           "β⁺ только в основное состояние Ar-40, 1461 — за захватом" },
            new object[] { "22NA", 1274.537, 1.80996,
                           "уровень Ne 1274.5 населяют β⁺ 90.498 и захват 9.502" },
            new object[] { "88Y",   898.042, 0.0,
                           "уровень Sr 2734 населяет ТОЛЬКО захват" },
            new object[] { "88Y",  1836.063, 0.00419021,
                           "уровень 1836 набирает почти весь распад сверху: 0.21 %, не 3.8 %" },
            new object[] { "65ZN", 1115.539, 0.0,
                           "β⁺ Zn-65 идёт в основное состояние Cu-65" },
            new object[] { "137CS", 661.657, 0.0,
                           "позитронов нет вовсе" },
            new object[] { "152EU", 121.7817, 0.00071032,
                           "наборов питаний два — доля ВЕТВЕВАЯ: 2·0.0256/72.08" },
            new object[] { "20NA", 1633.600, 2.0,
                           "ветвь целиком позитронная (канал 7, не 1)" },
        };

        /// <summary>
        /// Выход самой линии 511 на распад родителя (`S153`): поставка местами
        /// даёт позитронов больше, чем есть распадов ветви, и это обязано быть
        /// зажато ДО потребителей, а не у одного из них.
        /// </summary>
        static readonly object[][] AnnihilationYield =
        {
            new object[] { "20NA", 2.0,
                           "поставка 196.847 % при ветви 100 % — зажато (было 3.937)" },
            new object[] { "77RB", 2.0,
                           "поставка 110.470 % при ветви 100 % — зажато (было 2.209)" },
            new object[] { "22NA", 1.798, "89.9 % при ветви 100 % — не зажимается" },
            new object[] { "40K",  0.00002, "0.001 % при ветви 10.72 % — не зажимается" },
        };

        /// <summary>
        /// Печать числа ТОЧКОЙ, а не культурой потока (правило Amber
        /// 05.09.2026). У `Console.WriteLine` перегрузки с культурой нет вовсе,
        /// поэтому строка собирается `string.Format` явной инвариантной
        /// культурой. Прежде отчёт пробы выходил с запятыми — `306,780`.
        /// </summary>
        static void Say(string format, params object[] args)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        static int Main(string[] args)
        {
            double window = FsaCascadeSummer.DefaultCoincidenceWindowSec;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--window=", StringComparison.Ordinal))
                {
                    window = double.Parse(arg.Substring(9), CultureInfo.InvariantCulture);
                }
                // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
                else
                {
                    Console.WriteLine("не знаю ключа: " + arg);
                    return 2;
                }
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Say("Окно совпадения: {0:E3} с", window);
            Console.WriteLine();

            int failed = 0;
            Console.WriteLine("БУХГАЛТЕРИЯ ВАКАНСИЙ (остаток = захват)");
            Console.WriteLine();
            Console.WriteLine("  нуклид    I_K, %    ω_K     всего   конверсия   ЗАХВАТ   ждём        итог");
            foreach (Expectation e in Expected)
            {
                CascadeAtomicData atomic = CascadeAtomicData.Of(e.Nucid);
                if (atomic == null)
                {
                    Console.WriteLine("  {0,-8}  — атомных данных нет", e.Name);
                    failed++;
                    continue;
                }

                // ⛔ БАЛАНС ПЕЧАТАЕТСЯ ПО ТОЙ ЖЕ ВЕТВИ, ПО КОТОРОЙ СЧИТАН
                // (`S145`). Сводные `KIntensityPct` и `OmegaK` собраны по ВСЕМ
                // атомам родителя, а `PromptVacancy` — остаток СИЛЬНЕЙШЕЙ ветви;
                // печатать их в одной строке значило бы показывать «всего» и
                // «захват», которые не сходятся между собой. У однoветвевого
                // родителя обе величины совпадают, и строка та же, что прежде.
                CascadeAtomicData.Branch main = null;
                foreach (CascadeAtomicData.Branch branch in atomic.Branches)
                {
                    if (main == null || branch.Perc > main.Perc)
                    {
                        main = branch;
                    }
                }

                double kPct = main != null ? main.KIntensityPct : atomic.KIntensityPct;
                double omega = main != null ? main.OmegaK : atomic.OmegaK;
                int mainIndex = main != null ? atomic.Branches.IndexOf(main) : -1;
                double total = omega > 0.0 ? kPct / 100.0 / omega : 0.0;
                double conversion = 0.0;
                foreach (double[] line in atomic.GammaIntensity)
                {
                    CascadeAtomicData.Transition transition;
                    if (atomic.Gammas.TryGetValue(line[0], out transition)
                        && (mainIndex < 0 || transition.BranchIndex < 0
                            || transition.BranchIndex == mainIndex))
                    {
                        conversion += line[1] / 100.0 * transition.AlphaK;
                    }
                }

                bool ok = atomic.PromptVacancy >= e.PromptLo - 1e-9
                          && atomic.PromptVacancy <= e.PromptHi + 1e-9;
                if (!ok)
                {
                    failed++;
                }

                Say(
                    "  {0,-8} {1,7:F3}  {2,6:F4}  {3,7:F4}    {4,7:F4}  {5,7:F4}   {6,4:F2}…{7,4:F2}  {8}",
                    e.Name, kPct, omega, total, conversion,
                    atomic.PromptVacancy, e.PromptLo, e.PromptHi, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");

                // Родитель со СМЕШАННЫМ распадом обязан быть назван поимённо:
                // без этого «захват 0.60» читается как свойство родителя, а он
                // свойство ОДНОЙ его ветви.
                if (atomic.Branches.Count > 1)
                {
                    for (int bi = 0; bi < atomic.Branches.Count; bi++)
                    {
                        CascadeAtomicData.Branch branch = atomic.Branches[bi];
                        int owns = 0;
                        foreach (KeyValuePair<double, CascadeAtomicData.Transition> g in atomic.Gammas)
                        {
                            if (g.Value.BranchIndex == bi)
                            {
                                owns++;
                            }
                        }

                        Say(
                            "           ветвь {0,-8} {1,6:F2} %  ω_K {2,6:F4}  I_K {3,7:F3} %"
                            + "  захват {4,7:F4}  гамм {5}",
                            branch.Nucid, branch.Perc, branch.OmegaK,
                            branch.KIntensityPct, branch.PromptVacancy, owns);
                    }
                }
                if (!string.IsNullOrEmpty(atomic.Note))
                {
                    Console.WriteLine("           замечание: {0}", atomic.Note);
                }
            }

            Console.WriteLine();
            Console.WriteLine("ГЕЙТ ПО ВРЕМЕНИ (совпадает ли гамма с квантом, рождённым в момент распада)");
            Console.WriteLine();
            Console.WriteLine("  нуклид    гамма, кэВ   задержка, с    ждём   вышло    итог   почему");
            foreach (object[] row in Timing)
            {
                string nucid = (string)row[0];
                double energy = (double)row[1];
                bool expect = (bool)row[2];
                string why = (string)row[3];

                CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
                if (atomic == null)
                {
                    Say("  {0,-8} {1,10:F3}   атомных данных нет", nucid, energy);
                    failed++;
                    continue;
                }

                double delay = -1.0;
                foreach (KeyValuePair<double, CascadeAtomicData.Transition> entry in atomic.Gammas)
                {
                    if (Math.Abs(entry.Key - energy) < 0.3)
                    {
                        delay = entry.Value.EmitDelaySec;
                        break;
                    }
                }

                bool got = delay >= 0.0 && delay < window;
                bool ok = got == expect;
                if (!ok)
                {
                    failed++;
                }

                Say("  {0,-8} {1,10:F3}   {2,11}    {3,-5}  {4,-5}   {5}   {6}",
                    nucid, energy,
                    delay < 0.0 ? "нет перехода" : delay.ToString("E3", CultureInfo.InvariantCulture),
                    expect ? "да" : "нет", got ? "да" : "нет",
                    ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", why);
            }

            Console.WriteLine();
            Console.WriteLine("АННИГИЛЯЦИЯ: условное число квантов 511 ПРИ ГАММЕ (S150, S153)");
            Console.WriteLine();
            Console.WriteLine("  нуклид    гамма, кэВ      ждём     вышло     итог   почему");
            foreach (object[] row in Annihilation)
            {
                string nucid = (string)row[0];
                double energy = (double)row[1];
                double expect = (double)row[2];
                string why = (string)row[3];

                CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
                if (atomic == null)
                {
                    Say("  {0,-8} {1,10:F3}   атомных данных нет", nucid, energy);
                    failed++;
                    continue;
                }

                double got = atomic.AnnihilationQuantaOfGamma(energy);
                bool ok = Math.Abs(got - expect) <= 1.0E-5 * (1.0 + Math.Abs(expect));
                if (!ok)
                {
                    failed++;
                }

                Say("  {0,-8} {1,10:F3} {2,9:F6} {3,9:F6}   {4}   {5}",
                    nucid, energy, expect, got,
                    ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", why);
            }

            Console.WriteLine();
            Console.WriteLine("ВЫХОД ЛИНИИ 511 НА РАСПАД: поставка сверх доли ветви зажата (S153)");
            Console.WriteLine();
            Console.WriteLine("  нуклид      ждём     вышло     итог   почему");
            foreach (object[] row in AnnihilationYield)
            {
                string nucid = (string)row[0];
                double expect = (double)row[1];
                string why = (string)row[2];

                CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
                if (atomic == null)
                {
                    Console.WriteLine("  {0,-8}  атомных данных нет", nucid);
                    failed++;
                    continue;
                }

                double got = atomic.AnnihilationQuanta;
                bool ok = Math.Abs(got - expect) <= 1.0E-5 * (1.0 + Math.Abs(expect));
                if (!ok)
                {
                    failed++;
                }

                Say("  {0,-8} {1,9:F6} {2,9:F6}   {3}   {4}",
                    nucid, expect, got, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", why);
            }

            Console.WriteLine();
            Console.WriteLine("ИЗОМЕРЫ: разбор имени в ключ родителя совпадений");
            foreach (string name in new[] { "Ba-137m", "Ag-110m", "Ho-166m", "Tb-154m2",
                                            "Pb-214", "Cs-137", "X-ray" })
            {
                Console.WriteLine("  {0,-10} → {1}", name,
                    FsaCascadeSummer.ParentKey(name) ?? "(не разбирается)");
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}
