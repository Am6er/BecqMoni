using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// ОДНО правило сборки K-серии рентгена из `decay_radiations` на весь
    /// проект. Двух соглашений о K-серии здесь быть не должно: библиотека
    /// (<see cref="FsaSampleLibrary"/>) и суммирователь совпадений
    /// (<see cref="CascadeAtomicData"/>) читают одну и ту же таблицу, и
    /// разойдясь в Kβ они разойдутся в составе пробы и в числе партнёров
    /// совпадения — при одинаковых с виду числах на экране.
    ///
    /// ⚠ ЛОВУШКА, из-за которой правило заведено (`D30`, `T50`,
    /// `database/scheme.md` §2). `KB` — это НЕ третья линия Kβ, а ИТОГ по всей
    /// Kβ; рядом лежит её же разложение `KpB1` + `KpB2`. Наивная сумма всех
    /// строк `type_c LIKE 'K%'` считает Kβ ДВАЖДЫ: на Lu-176 40.53 % вместо
    /// 33.49 %, в 1.21 раза.
    ///
    /// ⛔ Но и обратное правило — «`KB` пропускать всегда, брать разложение» —
    /// неверно. Наборов «родитель + уровень + тип распада», у которых есть
    /// `KB`, — 1831, и разложение у них устроено ЧЕТЫРЬМЯ способами
    /// (пересчитано 23.08.2026 по всей таблице):
    ///
    /// | разложение | сходится с `KB` | расходится |
    /// |---|---|---|
    /// | обе строки `KpB1` + `KpB2` | 1286 | 244 |
    /// | одна `KpB1` | 186 | 106 |
    /// | нет вовсе | — | 9 |
    ///
    /// И расхождения эти РАЗНОЙ природы, что видно по знаку:
    ///
    ///   * у 244 наборов с ОБЕИМИ строками медиана разложение/итог −0.04 %,
    ///     разброс от −12.5 до +26.7 % — это округление поставки, и точнее
    ///     здесь РАЗЛОЖЕНИЕ: `189PT` 21.0 + 5.8 = 26.8 против целого `KB` 26,
    ///     `200AT` 0.38 против 0.3;
    ///   * у 106 наборов с ОДНОЙ `KpB1` расхождение всегда в минус, медиана
    ///     −12.1 %, до −26 % — здесь в поставке НЕТ `KpB2`, и точнее `KB`:
    ///     `227TH` 0.781 против 1.037, `232PA` 0.594 против 0.8;
    ///   * у девяти (`158HOm`, `158HOm1`, `193HGm`, `230AC`, `231PA`, `243BK`,
    ///     `243PU`, `250BK`, `254ESm1`) разложения нет, и `KB` — единственный
    ///     источник: прежнее правило теряло у них Kβ ЦЕЛИКОМ.
    ///
    /// ПРАВИЛО, которое не теряет ни того, ни другого: **разложение берётся,
    /// когда оно ПОЛНОЕ, иначе берётся итог `KB`.** Полным считается
    /// разложение из двух и более строк ЛИБО сходящееся с `KB` численно —
    /// вторая ветка нужна для 186 наборов, где одна `KpB1` и есть вся Kβ
    /// (лёгкие элементы, `KpB2` не разрешена) и совпадает с `KB` до знака.
    ///
    /// ⚠ Различать эти случаи ЧИСЛОМ нельзя, и это измерено: недостача от
    /// пропущенной `KpB2` (−0.6 … −26 %) и разброс от округления (−12.5 …
    /// +26.7 %) перекрываются. Разделяет их СТРУКТУРА — сколько строк лежит в
    /// поставке, — поэтому правило смотрит на неё, а не на порог.
    /// </summary>
    static class KSeriesRule
    {
        /// <summary>Строка итога по Kβ.</summary>
        public const string BetaTotal = "KB";

        /// <summary>
        /// Допуск сравнения разложения с итогом, ОТНОСИТЕЛЬНЫЙ. У совпадающих
        /// наборов они сходятся до последнего знака поставки, поэтому порог
        /// ничего не «подбирает»: он отделяет побитовое совпадение от
        /// расхождения в проценты и десятки процентов.
        /// </summary>
        const double MatchTolerance = 1.0E-6;

        /// <summary>Строка K-серии вообще (`KA1`, `KA2`, `KB`, `KpB1`, …).</summary>
        public static bool IsSeries(string series)
        {
            return !string.IsNullOrEmpty(series) && series[0] == 'K';
        }

        /// <summary>Итоговая строка Kβ.</summary>
        public static bool IsBetaTotal(string series)
        {
            return string.Equals(series, BetaTotal, StringComparison.Ordinal);
        }

        /// <summary>Разложение Kβ: `KpB1`, `KpB2`, …</summary>
        public static bool IsBetaSplit(string series)
        {
            return !string.IsNullOrEmpty(series)
                && series.StartsWith("Kp", StringComparison.Ordinal);
        }

        /// <summary>
        /// Выбрать между разложением и итогом по правилу из шапки класса.
        /// Возвращается ОДИН из переданных списков — тот, который надо взять;
        /// ни один из них не изменяется.
        /// </summary>
        /// <param name="split">Строки `KpB*`: {энергия, выход %}.</param>
        /// <param name="total">Строки `KB`: {энергия, выход %}.</param>
        /// <param name="splitSeries">
        /// Сколько РАЗНЫХ имён `type_c` дало разложение. Считать по длине
        /// списка нельзя: читатель, собирающий линии по родителю целиком (у
        /// 107 родителей строки лежат в нескольких наборах «уровень + тип
        /// распада»), получит две строки `KpB1` из двух наборов и примет
        /// неполное разложение за полное — у 45 родителей это происходит на
        /// самом деле, проверено 23.08.2026.
        /// </param>
        public static List<double[]> Beta(List<double[]> split, List<double[]> total, int splitSeries)
        {
            if (total == null || total.Count == 0)
            {
                return split;
            }

            if (split == null || split.Count == 0)
            {
                return total;
            }

            if (splitSeries > 1)
            {
                return split;
            }

            double sumSplit = Sum(split);
            double sumTotal = Sum(total);
            bool same = Math.Abs(sumSplit - sumTotal)
                        <= MatchTolerance * Math.Max(1.0, Math.Abs(sumTotal));
            return same ? split : total;
        }

        static double Sum(List<double[]> lines)
        {
            double sum = 0.0;
            foreach (double[] line in lines)
            {
                if (line != null && line.Length > 1)
                {
                    sum += line[1];
                }
            }

            return sum;
        }

        // ------------------------------------------------------------------
        // (`AMBER120`, П168 28.09.2026) Энергия группы K-M (`KpB1`)
        // ------------------------------------------------------------------

        /// <summary>
        /// Относительный допуск, в котором строка разложения Kβ считается
        /// группой K-M (Kβ1 + Kβ3 + Kβ5) своего элемента. Середина диапазона
        /// отходит от центра тяжести группы на 0.24–0.33 % (Pb 84.986 против
        /// 84.784, Hf 63.333 против 63.163, Ba 36.482 против 36.36), а группа
        /// K-N (`KpB2`) лежит выше на 1.1 % (Ge) и больше — её допуск не задевает.
        /// </summary>
        const double GroupTolerance = 0.006;

        /// <summary>Допуск узнавания элемента по Kα1, кэВ: поставки сходятся до эВ.</summary>
        const double AlphaMatchKev = 0.03;

        static readonly object Gate = new object();

        /// <summary>{Z → {Kα1, Kα2, Kβ(K-M) взвешенная}}, кэВ; null — не читалось.</summary>
        static Dictionary<int, double[]> groups;

        /// <summary>
        /// То же, что <see cref="Beta"/>, и у строк группы K-M — энергия ЦЕНТРА
        /// ТЯЖЕСТИ группы, а не середина текстового диапазона.
        ///
        /// ⛔ ЗАЧЕМ (`AMBER120`). Строка `KpB1` в `decay_radiations` несёт
        /// диапазон «62.981 - 63.685» (от Kβ3 до Kβ5), и `energy_num` — его
        /// СЕРЕДИНА 63.333, тогда как Kβ3 и Kβ1 (почти вся группа) лежат ниже:
        /// центр тяжести 63.163 (`matdb.fluorescence_k.kb_ev`, xraylib, K-M по
        /// весам переходов). У Lu-176 середина встала НАД K-краем лютеция
        /// 63.314, и в пробе из Lu₂O₃ матрица отклика ослабила линию как квант
        /// над краем: отклик в пике на 63.333 против 63.163 — −39 % на всех трёх
        /// сценах лютеция (`KbetaEdgeProbeP168`).
        ///
        /// Элемент узнаётся по Kα1 ТОГО ЖЕ набора строк (у родителя с двумя
        /// путями распада — два элемента, и каждый правится своей группой);
        /// строка берёт центр тяжести, только если лежит от него не дальше
        /// <see cref="GroupTolerance"/>. Итог `KB` не правится: в нём и K-N, а
        /// её веса в базе нет — он берётся лишь при неполном разложении.
        /// Нет таблицы, нет Kα, элемент не узнан — строки как есть.
        /// Возвращается НОВЫЙ список; входные не меняются.
        /// </summary>
        public static List<double[]> BetaAtGroupEnergy(List<double[]> split, List<double[]> total,
                                                       int splitSeries, List<double[]> alpha)
        {
            List<double[]> chosen = Beta(split, total, splitSeries);
            var result = new List<double[]>();
            if (chosen == null)
            {
                return result;
            }

            bool fromSplit = !ReferenceEquals(chosen, total) || ReferenceEquals(chosen, split);
            Dictionary<int, double[]> table = Groups();
            var elements = new List<double[]>();
            if (fromSplit && table != null && alpha != null)
            {
                foreach (double[] group in table.Values)
                {
                    foreach (double[] line in alpha)
                    {
                        if (line != null && line.Length > 0
                            && Math.Abs(line[0] - group[0]) <= AlphaMatchKev)
                        {
                            elements.Add(group);
                            break;
                        }
                    }
                }
            }

            foreach (double[] line in chosen)
            {
                if (line == null || line.Length == 0)
                {
                    continue;
                }

                double[] copy = (double[])line.Clone();
                foreach (double[] group in elements)
                {
                    double kb = group[2];
                    if (kb > 0.0 && Math.Abs(copy[0] - kb) <= GroupTolerance * kb)
                    {
                        copy[0] = kb;
                        break;
                    }
                }

                result.Add(copy);
            }

            return result;
        }

        /// <summary>Таблица групп K из `matdb.fluorescence_k`; null — не читается.</summary>
        static Dictionary<int, double[]> Groups()
        {
            lock (Gate)
            {
                if (groups != null)
                {
                    return groups.Count > 0 ? groups : null;
                }

                var read = new Dictionary<int, double[]>();
                try
                {
                    string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
                    if (System.IO.File.Exists(path))
                    {
                        // (`AMBER201`) Строка подключения — построителем, не склейкой:
                        // `;` в имени каталога программы разрезал строку, и база не открывалась.
                        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                                   EfficiencyMaker.MaterialDatabase.ReadOnlyConnection(path, true)))
                        {
                            connection.Open();
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = "select z, ka1_ev, ka2_ev, kb_ev from fluorescence_k";
                                using (var reader = command.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(3))
                                        {
                                            continue;
                                        }

                                        read[reader.GetInt32(0)] = new[]
                                        {
                                            reader.GetDouble(1) / 1000.0,
                                            reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2) / 1000.0,
                                            reader.GetDouble(3) / 1000.0
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    // Таблицы нет или база не читается — строки остаются как
                    // есть (середина диапазона), как было до `AMBER120`.
                    // ⛔ (`AMBER199`, 05.10.2026) Но отказ ЧТЕНИЯ называется и В
                    // КЭШ НЕ КЛАДЁТСЯ: следующий разбор спросит базу снова. Счёт
                    // отказов потока заодно не даёт читателям выше (линии
                    // распада `FsaSampleLibrary`, атомные данные
                    // `CascadeAtomicData`) запомнить строки без групп.
                    FsaDatabaseFailures.Note("matdb.sqlite", "fluorescence_k", error);
                    return null;
                }

                groups = read;
                return read.Count > 0 ? read : null;
            }
        }
    }
}
