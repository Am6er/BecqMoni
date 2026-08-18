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

        static int Main(string[] args)
        {
            double window = FsaCascadeSummer.DefaultCoincidenceWindowSec;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--window=", StringComparison.Ordinal))
                {
                    window = double.Parse(arg.Substring(9), CultureInfo.InvariantCulture);
                }
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Окно совпадения: {0:E3} с", window);
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

                double total = atomic.OmegaK > 0.0
                    ? atomic.KIntensityPct / 100.0 / atomic.OmegaK
                    : 0.0;
                double conversion = 0.0;
                foreach (double[] line in atomic.GammaIntensity)
                {
                    CascadeAtomicData.Transition transition;
                    if (atomic.Gammas.TryGetValue(line[0], out transition))
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

                Console.WriteLine(
                    "  {0,-8} {1,7:F3}  {2,6:F4}  {3,7:F4}    {4,7:F4}  {5,7:F4}   {6,4:F2}…{7,4:F2}  {8}",
                    e.Name, atomic.KIntensityPct, atomic.OmegaK, total, conversion,
                    atomic.PromptVacancy, e.PromptLo, e.PromptHi, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");
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
                    Console.WriteLine("  {0,-8} {1,10:F3}   атомных данных нет", nucid, energy);
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

                Console.WriteLine("  {0,-8} {1,10:F3}   {2,11}    {3,-5}  {4,-5}   {5}   {6}",
                    nucid, energy,
                    delay < 0.0 ? "нет перехода" : delay.ToString("E3", CultureInfo.InvariantCulture),
                    expect ? "да" : "нет", got ? "да" : "нет",
                    ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", why);
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
