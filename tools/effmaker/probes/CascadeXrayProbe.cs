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
            new object[] { "152EU", 121.7817, 0.00045076392,
                           "наборов два, разведены периодом (0.15 %) — доля ПО УРОВНЮ (S155)" },
            new object[] { "152EU", 1408.013, 0.0,
                           "уровень 1408 населяет только захват — пары нет" },
            new object[] { "44SC", 1157.020, 1.8876234,
                           "наборов два, разведены периодом (откл. 0) — доля по уровню" },
            // ⚠ 1633.600, а не 1633.602 (как в поставке) — НАРОЧНО: плечо
            // заодно судит, что округление вызывающего не превращается в тихий
            // ноль. Первым прогоном оно и поймало точное сравнение `double`.
            new object[] { "20NA", 1633.600, 2.0,
                           "ветвь целиком позитронная (канал 7, не 1); ключ округлён" },
            new object[] { "147TB", 1152.530, 0.0,
                           "⛔ ОТКАЗ (S155): наборы периодом не разведены (3.7 % и 98 %) — пары нет" },
            new object[] { "100RH", 539.512, 0.0,
                           "⛔ ОТКАЗ (S155): 1.5 % и 99.6 % — ближайший дальше порога" },
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

        /// <summary>
        /// Могут ли два перехода случиться в ОДНОМ событии распада (`S158`).
        ///
        /// ⛔ ПОЧЕМУ ЭТОГО НЕ ЛОВИЛА БУХГАЛТЕРИЯ ВАКАНСИЙ ВЫШЕ. Она сводит
        /// ОБЩИЙ баланс на распад — «сколько вакансий всего» — и он сходится
        /// независимо от того, кому именно приписаны слагаемые. Дефект же в
        /// УСЛОВНОСТИ при конкретной гамме: код добавлял событию 356 кэВ
        /// конверсионную вакансию перехода, который в этом событии произойти
        /// НЕ МОГ. Разные вопросы, и первый на второй не отвечает.
        ///
        /// Ждём НЕТ у альтернатив одного уровня и у переходов вне общего пути;
        /// ждём ДА у настоящих каскадов — они здесь отрицательный контроль,
        /// без них правка «всё запретить» прошла бы проверку.
        /// </summary>
        static readonly object[][] Coexist =
        {
            new object[] { "133BA",  356.0129,   53.1622, false,
                           "альтернативы уровня 4: 4→1 против 4→3" },
            new object[] { "133BA",  356.0129,  276.3989, false,
                           "альтернативы уровня 4: 4→1 против 4→2" },
            new object[] { "133BA",  302.8508,  383.8485, false,
                           "альтернативы уровня 3: 3→1 против 3→0" },
            new object[] { "133BA",   53.1622,  302.8508, true,
                           "настоящий каскад 4→3, затем 3→1 (в поставке доля 0.638)" },
            new object[] { "133BA",  356.0129,   80.9979, true,
                           "настоящий каскад 4→1, затем 1→0 (в поставке доля 0.368)" },
            new object[] { "133BA",   79.6142,   80.9979, true,
                           "настоящий каскад 2→1, затем 1→0" },
            new object[] { "60CO",  1173.2280, 1332.4920, true,
                           "настоящий каскад Ni-60: 4→1, затем 1→0" },
        };

        /// <summary>
        /// (`S159`) Явная пара Sandia сильнее конфликтующей схемы Geant4.
        ///
        /// ⛔ ЗАЧЕМ ОТДЕЛЬНОЕ ПЛЕЧО, РАЗ `CanCoexist` УЖЕ ПРОВЕРЕН ВЫШЕ. Тот
        /// раздел зовёт `CanCoexist` НАПРЯМУЮ и не проходит через `Conditional`.
        /// Значит ошибочная перестановка топологического фильтра ВЫШЕ явной пары
        /// оставила бы все семь его плеч зелёными, а реальные пары при этом
        /// отвергались бы. Здесь меряется ПОЛНЫЙ путь.
        ///
        /// ⛔ ВЕЗЁТ НЕ `28NE`, И ЭТО ИЗМЕРЕНО, А НЕ ПРЕДПОЧТЕНИЕ. Разбор назвал
        /// `28NE` (Sandia даёт 864.5↔2063.0 = 0.821053, а схема кладёт обе линии
        /// альтернативами уровня 5), и случай он описал верно. Но у `28NE` в
        /// поставке НЕТ НИ ОДНОЙ строки K-рентгена, поэтому
        /// `CascadeAtomicData.Of` отдаёт для него null раньше, чем дело доходит
        /// до схемы: через этот путь его не проверить вовсе.
        ///
        /// Взят равноценный и корпусно-независимый `103IN`: одна ветвь в
        /// `103CD`, шесть строк K-рентгена, гаммы 740.4 и 552.1 — альтернативы
        /// уровня 6 по схеме, а Sandia прямо задаёт их пару с долей 0.032093.
        /// Свойство проверяется то же самое.
        ///
        /// Отменять прямые данные совпадения схемой НЕЛЬЗЯ — запись стоит в
        /// таблице «Чего делать НЕ надо».
        ///
        /// Два плеча вместе и пришпиливают порядок: с поставкой обязан выйти
        /// 0.032093, без неё — ноль от топологии. Одного мало: первое без
        /// второго прошло бы и при выключенной топологии, второе без первого —
        /// при перестановке.
        /// </summary>
        /// <summary>
        /// (`S157`) Две строки ОДНОЙ энергии в РАЗНЫХ каналах — две личности.
        ///
        /// ⛔ ЧТО ИМЕННО СУДИТСЯ: строки обязаны разойтись по РАЗНЫМ ВЕТВЯМ.
        /// Пока ключом была энергия, первый канал занимал `Gammas`, а вторая
        /// строка получала ЕГО переход, ЕГО ветвь, ЕГО задержку и ЕГО долю β⁺ —
        /// то есть физику чужого дочернего ядра.
        ///
        /// Взят `33NA`: линия 221.0 записана в каналах `2` (дочерний `33MG`,
        /// выход 1.914 %) и `17` (дочерний `31MG`, 0.31 %), и схемы ОБЕИХ
        /// дочек её сопоставляют — 220.9 (3→1) у `33MG` и 220.87 (2→0) у
        /// `31MG`. Значит расхождение видно, а не постулировано.
        ///
        /// ⚠ `46MN`, названный разбором, для проверки НЕ ГОДИТСЯ, и это
        /// измерено: ни 796.1, ни 1118.0 не сопоставляются ни одной схеме
        /// (`46CR` и `45V`) и не встречаются в ENSDF, так что обе его строки
        /// остаются без перехода и без ветви — дубль там есть, а наблюдать его
        /// нечем. Строка об этом печатается, но провалом не считается: это
        /// свойство ПОСТАВКИ, а не кода.
        /// </summary>
        static int ChannelIdentity()
        {
            int bad = 0;

            // Наблюдаемый случай — судим.
            bad += TwoBranches("33NA", 221.0, "2", "17");

            // ⛔ И ДОЛЮ β⁺ ТОЖЕ (`S164`). Плечо выше судит только ВЕТВИ, то
            // есть повторное присвоение строке доли ЧУЖОГО канала осталось бы
            // незамеченным — а это и был дефект `S157`.
            bad += ShareIsPerLine();

            // Названный разбором — печатаем как есть.
            CascadeAtomicData mn = CascadeAtomicData.Of("46MN");
            if (mn != null)
            {
                foreach (double energy in new[] { 796.1, 1118.0 })
                {
                    int rows = 0, withBranch = 0;
                    foreach (CascadeAtomicData.GammaLine line in mn.GammaIntensity)
                    {
                        if (Math.Abs(line.EnergyKev - energy) >= 0.05)
                        {
                            continue;
                        }

                        rows++;
                        if (mn.BranchOfLine(line) != null)
                        {
                            withBranch++;
                        }
                    }

                    Say("  46MN {0,8:F1}: строк {1}, из них с ветвью {2}"
                        + "   — схемы этих линий не знают, наблюдать нечем",
                        energy, rows, withBranch);
                }
            }

            return bad;
        }

        /// <summary>
        /// Строки одной энергии двух названных каналов обязаны дать РАЗНЫЕ
        /// ветви. Возвращает число провалов.
        /// </summary>
        static int TwoBranches(string nucid, double energy, string one, string two)
        {
            CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
            if (atomic == null)
            {
                Say("  ⛔ ОТКАЗ: атомных данных {0} нет — плечо мерило бы пустоту", nucid);
                return 1;
            }

            CascadeAtomicData.Branch first = null, second = null;
            int rows = 0;
            foreach (CascadeAtomicData.GammaLine line in atomic.GammaIntensity)
            {
                if (Math.Abs(line.EnergyKev - energy) >= 0.05)
                {
                    continue;
                }

                rows++;
                CascadeAtomicData.Branch branch = atomic.BranchOfLine(line);
                if (line.Channel == one)
                {
                    first = branch;
                }
                else if (line.Channel == two)
                {
                    second = branch;
                }

                Say("  {0,-6} {1,8:F1}  канал {2,-3} ветвь {3,-6} переход {4}",
                    nucid, energy, line.Channel,
                    branch != null ? branch.Nucid : "—",
                    line.Transition != null
                        ? line.Transition.EnergyKev.ToString("F2", CultureInfo.InvariantCulture)
                        : "—");
            }

            bool ok = rows >= 2 && first != null && second != null
                      && !ReferenceEquals(first, second);
            Say("           строк {0}, ветви разные и обе есть: {1}",
                rows, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// (`S164`) Доля β⁺ берётся У СТРОКИ, а не у первой ветви с такой
        /// энергией.
        ///
        /// ⛔ ВЕХИКУЛА В ПОСТАВКЕ НЕТ, И ЭТО ИЗМЕРЕНО, а не предпочтение.
        /// Межканальный дубль есть ровно у трёх родителей: у `32NA` и `33NA`
        /// позитронов нет вовсе (оба канала дадут честный ноль, различать
        /// нечего), а у `46MN` β⁺ есть, но ни одна из дублирующихся линий не
        /// сопоставляется ни одной схеме и не встречается в ENSDF — доля тоже
        /// ноль у обоих. Поэтому вход собирается РУКАМИ.
        ///
        /// ⚠ Названо честно: это проверка КОДА, а не поставки. Она не
        /// доказывает, что в базе есть такой нуклид, — она доказывает, что
        /// если он появится, доля не утечёт из чужого канала.
        ///
        /// Три плеча: строка позитронного канала со своей ветвью, строка
        /// непозитронного канала со своей ветвью, и строка непозитронного
        /// канала БЕЗ ветви — последняя проверяет запасной путь, где перебор
        /// ветвей и утекал.
        /// </summary>
        static int ShareIsPerLine()
        {
            var positron = new CascadeAtomicData.Branch
            {
                Nucid = "TEST-P", Z = 20, A = 40, Perc = 100.0, DecType = "1",
                BetaPlusShare = 0.5
            };
            positron.BetaPlusOfGamma[500.0] = 0.4;

            var plain = new CascadeAtomicData.Branch
            {
                Nucid = "TEST-B", Z = 21, A = 40, Perc = 100.0, DecType = "2"
            };

            var atomic = new CascadeAtomicData();
            atomic.Branches.Add(positron);
            atomic.Branches.Add(plain);

            var withBranch = new CascadeAtomicData.GammaLine
            {
                EnergyKev = 500.0, IntensityPct = 10.0, Channel = "1",
                Transition = new CascadeAtomicData.Transition { EnergyKev = 500.0, BranchIndex = 0 }
            };
            var otherChannel = new CascadeAtomicData.GammaLine
            {
                EnergyKev = 500.0, IntensityPct = 3.0, Channel = "2",
                Transition = new CascadeAtomicData.Transition { EnergyKev = 500.0, BranchIndex = 1 }
            };
            var noBranch = new CascadeAtomicData.GammaLine
            {
                EnergyKev = 500.0, IntensityPct = 3.0, Channel = "2"
            };
            atomic.GammaIntensity.Add(withBranch);
            atomic.GammaIntensity.Add(otherChannel);
            atomic.GammaIntensity.Add(noBranch);

            var cases = new object[][]
            {
                new object[] { "канал 1, своя ветвь", withBranch, 0.8,
                               "доля 0.4 своей ветви, квантов вдвое" },
                new object[] { "канал 2, своя ветвь", otherChannel, 0.0,
                               "у своей ветви доли нет — чужую брать нельзя" },
                new object[] { "канал 2, ветви нет", noBranch, 0.0,
                               "запасной перебор идёт только по ветвям СВОЕГО канала" },
            };

            int bad = 0;
            foreach (object[] row in cases)
            {
                double want = (double)row[2];
                double got = atomic.AnnihilationQuantaOfLine(
                    (CascadeAtomicData.GammaLine)row[1]);
                bool ok = Math.Abs(got - want) < 1.0E-12;
                if (!ok)
                {
                    bad++;
                }

                Say("  {0,-22} ждём {1:F4}  вышло {2:F4}   {3}   {4}",
                    row[0], want, got, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", row[3]);
            }

            return bad;
        }

        static int SourcePriority()
        {
            const string Nucid = "103IN";
            const double One = 740.4;
            const double Two = 552.1;
            const double Fraction = 0.032093;

            System.Reflection.MethodInfo conditional = typeof(FsaCascadeSummer).GetMethod(
                "Conditional", System.Reflection.BindingFlags.NonPublic
                               | System.Reflection.BindingFlags.Static);
            Type rawType = typeof(FsaCascadeSummer).GetNestedType(
                "NuclideData", System.Reflection.BindingFlags.NonPublic);
            if (conditional == null || rawType == null)
            {
                Say("  ⛔ ОТКАЗ: закрытых Conditional/NuclideData нет — проба мерила бы пустоту");
                return 1;
            }

            CascadeAtomicData atomic = CascadeAtomicData.Of(Nucid);
            CascadeAtomicData.GammaLine mineRow = null, otherRow = null;
            if (atomic != null)
            {
                foreach (CascadeAtomicData.GammaLine line in atomic.GammaIntensity)
                {
                    if (Math.Abs(line.EnergyKev - One) < 0.05) mineRow = line;
                    if (Math.Abs(line.EnergyKev - Two) < 0.05) otherRow = line;
                }
            }

            if (atomic == null || mineRow == null || otherRow == null
                || mineRow.Transition == null || otherRow.Transition == null)
            {
                Say("  ⛔ ОТКАЗ: у {0} нет строк {1}/{2} с переходами", Nucid, One, Two);
                return 1;
            }

            CascadeAtomicData.Branch branch = atomic.BranchOfLine(mineRow);
            object[] call = new object[] { null, One, otherRow, branch,
                                           mineRow.Transition, otherRow.Transition };

            // Схема обязана считать их альтернативами — иначе плечо ничего не
            // различает, и об этом надо сказать, а не тихо пройти.
            System.Reflection.MethodInfo canCoexist = typeof(FsaCascadeSummer).GetMethod(
                "CanCoexist", System.Reflection.BindingFlags.NonPublic
                              | System.Reflection.BindingFlags.Static);
            bool topology = canCoexist != null && (bool)canCoexist.Invoke(
                null, new object[] { branch, mineRow.Transition, otherRow.Transition });

            int bad = 0;
            Say("  схема Geant4 считает пару возможной: {0}   (ждём: нет, иначе плечо слепо)",
                topology ? "да" : "нет");
            if (topology)
            {
                bad++;
            }

            // Плечо 1: поставка совпадений ЕСТЬ — она и отвечает.
            object raw = Activator.CreateInstance(rawType, true);
            var intensity = new Dictionary<double, double> { { One, 1.067 }, { Two, 1.164 } };
            var partners = new Dictionary<double, Dictionary<double, double>>
            {
                { One, new Dictionary<double, double> { { Two, Fraction } } }
            };
            rawType.GetField("Intensity").SetValue(raw, intensity);
            rawType.GetField("Partners").SetValue(raw, partners);

            call[0] = raw;
            double withSandia = (double)conditional.Invoke(null, call);
            bool okOne = Math.Abs(withSandia - Fraction) < 1.0E-9;
            if (!okOne) bad++;
            Say("  с поставкой Sandia:  ждём {0:F6}  вышло {1:F6}   {2}",
                Fraction, withSandia, okOne ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");

            // Плечо 2: поставки нет — судит топология, и она запрещает.
            call[0] = null;
            double without = (double)conditional.Invoke(null, call);
            bool okTwo = Math.Abs(without) < 1.0E-12;
            if (!okTwo) bad++;
            Say("  без поставки:        ждём {0:F6}  вышло {1:F6}   {2}",
                0.0, without, okTwo ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");

            return bad;
        }

        static int Main(string[] args)
        {
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 31).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
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
                foreach (CascadeAtomicData.GammaLine line in atomic.GammaIntensity)
                {
                    // (`S157`) Переход берётся У СТРОКИ: у межканального дубля
                    // поиск по энергии отдал бы переход чужого канала.
                    CascadeAtomicData.Transition transition = line.Transition;
                    if (transition != null
                        && (mainIndex < 0 || transition.BranchIndex < 0
                            || transition.BranchIndex == mainIndex))
                    {
                        conversion += line.IntensityPct / 100.0 * transition.AlphaK;
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
            Console.WriteLine("ВЗАИМОИСКЛЮЧАЮЩИЕ ПЕРЕХОДЫ: может ли пара быть в одном событии (S158)");
            Console.WriteLine();
            System.Reflection.MethodInfo canCoexist = typeof(FsaCascadeSummer).GetMethod(
                "CanCoexist", System.Reflection.BindingFlags.NonPublic
                              | System.Reflection.BindingFlags.Static);
            if (canCoexist == null)
            {
                Console.WriteLine("  ⛔ ОТКАЗ: закрытого CanCoexist в сборке нет — проба мерила бы пустоту");
                failed++;
            }
            else
            {
                Say("  нуклид   гамма, кэВ  партнёр, кэВ   ждём  вышло  ложный K   итог   почему");
                foreach (object[] row in Coexist)
                {
                    string nucid = (string)row[0];
                    double one = (double)row[1];
                    double two = (double)row[2];
                    bool expect = (bool)row[3];
                    string why = (string)row[4];

                    CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
                    CascadeAtomicData.Transition mine = null, other = null;
                    if (atomic != null)
                    {
                        atomic.Gammas.TryGetValue(one, out mine);
                        atomic.Gammas.TryGetValue(two, out other);
                    }

                    CascadeAtomicData.Branch branch =
                        atomic != null ? atomic.BranchOfGamma(one) : null;
                    if (atomic == null || mine == null || other == null || branch == null)
                    {
                        Say("  {0,-8} {1,10:F3} {2,12:F3}   переходов или ветви нет", nucid, one, two);
                        failed++;
                        continue;
                    }

                    bool got = (bool)canCoexist.Invoke(null, new object[] { branch, mine, other });
                    bool ok = got == expect;
                    if (!ok)
                    {
                        failed++;
                    }

                    // Сколько ЛОЖНОГО K-кванта добавлял прежний запасной ход,
                    // если пара невозможна: выход партнёра × α_K × ω_K.
                    double spurious = 0.0;
                    if (!expect)
                    {
                        foreach (CascadeAtomicData.GammaLine line in atomic.GammaIntensity)
                        {
                            if (Math.Abs(line.EnergyKev - two) < 0.05)
                            {
                                spurious = line.IntensityPct / 100.0 * other.AlphaK * branch.OmegaK;
                                break;
                            }
                        }
                    }

                    Say("  {0,-8} {1,10:F3} {2,12:F3}  {3,-5} {4,-5} {5,10:F5}   {6}   {7}",
                        nucid, one, two, expect ? "да" : "нет", got ? "да" : "нет",
                        spurious, ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ", why);
                }
            }

            Console.WriteLine();
            Console.WriteLine("МЕЖКАНАЛЬНЫЙ ДУБЛЬ: у каждой строки своя ветвь и своя доля β⁺ (S157)");
            Console.WriteLine();
            failed += ChannelIdentity();

            Console.WriteLine();
            Console.WriteLine("ПРИОРИТЕТ ИСТОЧНИКОВ: явная пара Sandia сильнее схемы (S159)");
            Console.WriteLine();
            failed += SourcePriority();

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
