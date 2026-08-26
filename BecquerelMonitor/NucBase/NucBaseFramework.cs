using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.NucBase
{
    public class NucBaseFramework
    {

        public NucBaseFramework()
        {
            
        }

        public Nuclide getNuclude(string nucname)
        {
            DataBase db = new DataBase();
            Nuclide nuc = new Nuclide();
            try
            {
                SqliteDataReader reader = db.ReadData("select z, n, ifnull(half_life, '?'), ifnull(half_life_unit, ''), ifnull(half_life_sec, 0), ifnull(abundance, 0) from nuclides where nucid = '" + nucname + "' and half_life not null");
                if (!reader.Read())
                {
                    // Stable isotopes have no half_life row: not an error, just no data.
                    // Reading without this check used to throw and pop an error dialog.
                    db.Close();
                    return null;
                }
                nuc.Z = reader.GetInt32(0);
                nuc.N = reader.GetInt32(1);
                nuc.HalfLife = reader.GetString(2);
                nuc.HalfLifeUOM = reader.GetString(3);
                nuc.HalfLife_Sec = reader.GetDouble(4);
                nuc.Abundance = reader.GetDouble(5);

                reader = db.ReadData("select daughter_nucid, ifnull(perc, '?'), dec_type from decay_chain where nucid = '" + nucname + "'");
                while (reader.Read())
                {
                    Decay dec = new Decay();
                    dec.NucName = reader.GetString(0);
                    dec.DecayPercent = reader.GetString(1);
                    dec.DecayType = Convert.ToInt32(reader.GetString(2));
                    nuc.Daughters.Add(dec);
                }

                reader = db.ReadData("select nucid, ifnull(perc, '?'), dec_type from decay_chain where daughter_nucid = '" + nucname + "'");
                while (reader.Read())
                {
                    Decay dec = new Decay();
                    dec.NucName = reader.GetString(0);
                    dec.DecayPercent = reader.GetString(1);
                    dec.DecayType = Convert.ToInt32(reader.GetString(2));
                    nuc.Parents.Add(dec);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.NucBase_IsotopeFetchError, nucname, ex.Message),
                    Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                nuc = null;
            }
            
            db.Close();
            return nuc;
        }

        /// <summary>
        /// Линии запрошенного родителя для редактора нуклидных сетов и
        /// конструктора ROI.
        ///
        /// ⚠ Набор зажат по уровню родителя ОДНИМ на проект правилом
        /// (<see cref="DecayParentRule.LevelClause"/>, `S89`/`S94`). Без зажима
        /// запрос склеивал наборы разных состояний одного имени — ровно то
        /// двоение распада, которое закрыла `S89` у двух других читателей
        /// (`D39`). Меряно чтением базы 26.08.2026: трогает это РОВНО четырёх
        /// родителей из 2655, у которых строки лежат более чем на одном
        /// `parent_l_seqno`, — `118INm2` 26 → 11 строк, `190Wm2` 25 → 13,
        /// `116AGm2` 82 → 44, `70CUm2` 34 → 10; на `176LU`, `234PAm1`, `137CS`,
        /// `144TBm`, `123CSm2`, `208TL`, `40K` число строк не сдвинулось.
        ///
        /// ⛔ Зажим ставится ТОЛЬКО когда родитель назван. При пустом имени
        /// запрос — не «излучения родителя X», а выгрузка всей таблицы, и
        /// запрошенного родителя у него нет вовсе; подстановка `dr.parent_nucid`
        /// вместо имени делает подзапросы правила коррелированными, а индексов
        /// у `decay_radiations` нет ни одного — меряно там же: 0.09 с на 50054
        /// строки против 77.5 с на те же 49965. Что выгрузка всей таблицы в
        /// таблицу редактора сама по себе бессмысленна — отдельная строка.
        ///
        /// ⚠ Имя подставляется в текст запроса, а не параметром, потому что
        /// <see cref="DataBase.ReadData"/> параметров не принимает вовсе; вся
        /// строка собирается конкатенацией по той же причине. Отдельная строка.
        /// </summary>
        public List<DecayRad> getDecayRad(string nucname, double intensity = 0.0, double lowEnergy = 0.0, double highEnergy = 3000.0, double half_life_sec = 0)
        {
            DataBase db = new DataBase();
            string sql = "select dr.parent_nucid, dr.energy_num, dr.intensity_num, dr.type_a, dr.type_c, dr.dec_type, nuc.half_life, nuc.half_life_unit from decay_radiations as dr, nuclides nuc where dr.parent_nucid = nuc.nucid and dr.type_a in ('G', 'X') and ";
            if (nucname.Length > 0)
            {
                sql += "dr.parent_nucid = " + SqlText(nucname) + " and ";
            }
            if (intensity >= 0.0)
            {
                sql += "cast(dr.intensity_num as float) >= " + intensity + " and ";
            }
            if (highEnergy == 0.0 && lowEnergy == 0.0)
            {
                sql += " 1=1 and ";
            } else
            {
                if (lowEnergy == 0.0)
                {
                    sql += "cast(dr.energy_num as float) <= " + highEnergy + " and ";
                }
                if (highEnergy == 0.0)
                {
                    sql += "cast(dr.energy_num as float) >= " + lowEnergy + " and ";
                }

                if (highEnergy > 0.0 && lowEnergy > 0.0)
                {
                    sql += "cast(dr.energy_num as float) >= " + lowEnergy + " and  cast(dr.energy_num as float) <= " + highEnergy + " and ";
                }
            }
            if (half_life_sec > 0)
            {
                sql += "cast(nuc.half_life_sec as float) > " + half_life_sec + " and ";
            }
            sql += " 1=1";
            if (nucname.Length > 0)
            {
                // ОДНО правило на проект: тот же зажим по уровню родителя, что у
                // `CascadeAtomicData` и `FsaSampleLibrary`, и из того же места.
                // Имя правило ждёт под `$n`; параметров у `ReadData` нет, поэтому
                // подставляется текстом — см. примечание к методу.
                sql += DecayParentRule.LevelClause.Replace("$n", SqlText(nucname));
            }

            List<DecayRad> decayRads = new List<DecayRad>();
            try
            {
                SqliteDataReader reader = db.ReadData(sql);
                while (reader.Read())
                {
                    DecayRad decrad = new DecayRad();
                    decrad.Name = reader.GetString(0);
                    decrad.Energy = Convert.ToDouble(reader.GetDouble(1));
                    string intensitystr = reader.GetString(2);
                    if (intensitystr.IndexOf("(") != -1)
                    {
                        intensitystr = intensitystr.Replace("(", "").Replace(")", "").Trim();
                    }
                    decrad.Intensity = Convert.ToDouble(intensitystr);
                    decrad.DecayLine = reader.GetString(3);
                    decrad.XrayType = reader.GetString(4);
                    decrad.DecayType = Convert.ToInt32(reader.GetString(5));
                    decrad.HalfLife = Convert.ToDouble(reader.GetString(6));
                    decrad.HalfLifeUnit = Convert.ToString(reader.GetString(7));
                    decayRads.Add(decrad);
                }
            } catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.NucBase_DecayRadsFetchError, sql, ex.Message),
                    Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                decayRads = null;
            }
            
            db.Close();
            MarkRedundantKSeries(decayRads);
            return decayRads;
        }

        /// <summary>
        /// Строковая константа для текста запроса: в кавычках, с удвоением
        /// апострофа. В идентификаторах нуклидов апострофа не бывает, но имя
        /// сюда приходит из поля ввода, а <see cref="DataBase.ReadData"/>
        /// параметров не принимает вовсе — значит имя попадает прямо в текст,
        /// и апостроф в нём закрыл бы литерал.
        /// </summary>
        static string SqlText(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        /// <summary>
        /// ЛОВУШКА K-СЕРИИ В РЕДАКТОРЕ (`D33`). В `decay_radiations` Kβ лежит
        /// ДВАЖДЫ: итогом `KB` и разложением `KpB1` + `KpB2`. В таблице они
        /// стоят рядом как равноправные линии, и ничто не говорило, что
        /// складывать их вместе нельзя, — а наивная сумма всех `K*` завышает
        /// K-выход: на Lu-176 40.53 % вместо 33.49 %, в 1.21 раза.
        ///
        /// Здесь у лишней при сложении половины снимается галочка и в колонке
        /// серии появляется пометка. Прятать строку нельзя: она в базе есть, и
        /// взять именно её человек вправе — но взять ОБЕ он теперь может только
        /// нарочно, и при ввозе ему об этом скажут.
        ///
        /// Какая половина лишняя, решает <see cref="KSeriesRule"/> — то же
        /// правило, что у разбора и у суммирователя совпадений; трёх
        /// соглашений о Kβ в проекте быть не должно.
        ///
        /// ⚠ Набор здесь — «родитель + тип распада», без уровня, и этого хватает:
        /// уровень уже зажат САМИМ ЗАПРОСОМ (`D39`, <see cref="DecayParentRule"/>),
        /// так что до сюда доезжают строки одного уровня. Двух уровней сразу в
        /// списке больше нет — кроме выгрузки без имени родителя, где зажима нет
        /// по цене запроса и где сложение K-серии всё равно идёт по родителю.
        /// </summary>
        static void MarkRedundantKSeries(List<DecayRad> lines)
        {
            if (lines == null)
            {
                return;
            }

            var sets = new Dictionary<string, List<DecayRad>>(StringComparer.Ordinal);
            foreach (DecayRad line in lines)
            {
                line.Redundant = false;
                if (line.DecayLine != "X" || !KSeriesRule.IsSeries(line.XrayType))
                {
                    continue;
                }

                string key = line.Name + "\u0001" + line.DecayType.ToString(CultureInfo.InvariantCulture);
                List<DecayRad> set;
                if (!sets.TryGetValue(key, out set))
                {
                    set = new List<DecayRad>();
                    sets[key] = set;
                }

                set.Add(line);
            }

            foreach (KeyValuePair<string, List<DecayRad>> pair in sets)
            {
                var split = new List<DecayRad>();
                var total = new List<DecayRad>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (DecayRad line in pair.Value)
                {
                    if (KSeriesRule.IsBetaTotal(line.XrayType))
                    {
                        total.Add(line);
                    }
                    else if (KSeriesRule.IsBetaSplit(line.XrayType))
                    {
                        split.Add(line);
                        names.Add(line.XrayType);
                    }
                }

                if (split.Count == 0 || total.Count == 0)
                {
                    continue;           // одна половина — выбирать не из чего
                }

                List<double[]> splitPairs = Numbers(split);
                List<double[]> chosen = KSeriesRule.Beta(splitPairs, Numbers(total), names.Count);
                List<DecayRad> loser = ReferenceEquals(chosen, splitPairs) ? total : split;
                foreach (DecayRad line in loser)
                {
                    line.Redundant = true;
                }
            }
        }

        static List<double[]> Numbers(List<DecayRad> lines)
        {
            var pairs = new List<double[]>();
            foreach (DecayRad line in lines)
            {
                pairs.Add(new[] { line.Energy, line.Intensity });
            }

            return pairs;
        }

        /// <summary>
        /// Характеристический рентген ЭЛЕМЕНТА: «W», «Pb» — символ без
        /// массового числа. Это не распад: атом отвечает квантом на дырку в
        /// K-оболочке, откуда бы та ни взялась — от фотопоглощения в электроде,
        /// в свинцовом домике, в корпусе. Поэтому и берётся не из
        /// <c>decay_radiations</c>, а из <c>xray_fluorescence</c>
        /// (<see cref="MaterialDatabase"/>): энергии Kα1, Kα2 и Kβ посчитаны по
        /// краям поглощения XCOM, веса — доли внутри K-серии.
        ///
        /// Выход на распад у таких линий не определён вовсе, поэтому в колонке
        /// интенсивности стоит доля внутри серии, в сумме 100 %.
        ///
        /// Пустой список — про этот элемент в таблице ничего нет: она
        /// заполнена от Z = 30, у более лёгких нет пары краёв L2/L3, по разности
        /// с которыми считаются энергии линий.
        /// </summary>
        public List<DecayRad> GetFluorescence(string symbol, double intensity = 0.0,
                                              double lowEnergy = 0.0, double highEnergy = 0.0)
        {
            List<DecayRad> lines = new List<DecayRad>();
            int z = MaterialDatabase.ZOf(symbol);
            MaterialDatabase.Fluorescence fluorescence = z > 0 ? MaterialDatabase.FluorescenceOf(z) : null;
            if (fluorescence == null)
            {
                return lines;
            }

            string[] labels = { "KA1", "KA2", "KB" };
            for (int i = 0; i < fluorescence.LineKev.Length && i < labels.Length; i++)
            {
                double energy = fluorescence.LineKev[i];
                double percent = fluorescence.LineWeight[i] * 100.0;
                if (energy <= 0.0 || percent < intensity
                    || (lowEnergy > 0.0 && energy < lowEnergy)
                    || (highEnergy > 0.0 && energy > highEnergy))
                {
                    continue;
                }

                lines.Add(new DecayRad
                {
                    Name = symbol,
                    Energy = energy,
                    Intensity = percent,
                    DecayLine = FluorescenceLine,
                    XrayType = labels[i],
                    // Периода полураспада у элемента нет: светит он не сам, а в
                    // ответ на облучение. Ноль здесь и означает «не применимо» —
                    // и с ним же уходит в определение при ввозе.
                    HalfLife = 0.0,
                    HalfLifeUnit = "s",
                    DecayTypeText = Resources.NucBase_Fluorescence
                });
            }

            return lines;
        }

        /// <summary>
        /// Метка строки характеристического рентгена в колонке типа излучения.
        /// По ней же ввоз узнаёт такую строку: у неё нет ни родителя, ни ряда,
        /// ни периода полураспада.
        /// </summary>
        public const string FluorescenceLine = "XF";

        /// <summary>
        /// Ряд от корня: {нуклид -> накопленная доля ветвления}, у корня 1.0.
        ///
        /// Нужна для выходов НА РАСПАД РОДИТЕЛЯ РЯДА. В базе выход линии дан на
        /// распад своего нуклида: у Tl-208 линия 2614 кэВ стоит 99.75 %, но сам
        /// Tl-208 получается лишь из 35.94 % распадов Bi-212, и на распад Th-232
        /// та же линия даёт 35.85 %. Векового равновесия иначе не посчитать —
        /// именно на распад родителя даны все выходы, которыми пользуется и
        /// конструктор кривой, и разложение спектра.
        ///
        /// Две тонкости, без которых числа врут:
        ///
        /// * идти только по НИЖНЕМУ уровню родителя (`l_seqno`): строки с
        ///   большим номером описывают распад возбуждённого уровня и дублируют
        ///   тот же переход с другим ветвлением — у Bi-212 на Tl-208 есть
        ///   35.94 % при уровне 0 и 67 % при уровне 5. Изомер, если он живёт
        ///   сам по себе, имеет собственный nucid (234PAM1);
        /// * пропускать петлю на себя: у 238U в базе есть такая строка.
        /// </summary>
        public Dictionary<string, double> GetChainBranches(string rootNucid, double minFraction = 1e-6)
        {
            Dictionary<string, double> fraction = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // Обход разносит ДЕЛЬТЫ, а не посещает нуклиды по разу: пути разной
            // длины сходятся в один нуклид (234U из 234Th через 234mPa и через
            // IT-ветку 234Pa), и вклад, пришедший к уже пройденному узлу,
            // раньше терялся для его потомков. Каждая запись очереди — «этому
            // нуклиду добавилось столько-то», и добавка проходит вниз ровно
            // один раз независимо от порядка обхода. Отсечка minFraction гасит
            // и хвосты, и циклы грязных данных; ограничение длины очереди —
            // страховка от цикла со 100-процентной веткой.
            List<KeyValuePair<string, double>> queue = new List<KeyValuePair<string, double>>();
            fraction[rootNucid] = 1.0;
            queue.Add(new KeyValuePair<string, double>(rootNucid, 1.0));

            DataBase db = new DataBase();
            try
            {
                for (int i = 0; i < queue.Count && queue.Count <= 1000; i++)
                {
                    string current = queue[i].Key;
                    double share = queue[i].Value;
                    List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
                    // Строки вычитываются целиком до следующего запроса: обходу
                    // нужен ещё один читатель на том же соединении.
                    // Минимальный l_seqno ищется среди строк С ЧИСЛОМ: если у
                    // самой ранней записи perc пуст, дочка бралась бы из неё и
                    // выпадала из ряда целиком, хотя число есть строкой ниже.
                    SqliteDataReader reader = db.ReadData(
                        "select daughter_nucid, perc from decay_chain d where nucid = '" + current +
                        "' and perc not null and l_seqno = (select min(l_seqno) from decay_chain x " +
                        "where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid " +
                        "and x.dec_type = d.dec_type and x.perc not null)");
                    while (reader.Read())
                    {
                        rows.Add(new KeyValuePair<string, string>(reader.GetString(0), reader.GetString(1)));
                    }

                    reader.Close();

                    foreach (KeyValuePair<string, string> row in rows)
                    {
                        double percent;
                        if (string.Equals(row.Key, current, StringComparison.OrdinalIgnoreCase)
                            || !double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                        {
                            continue;
                        }

                        double add = share * percent / 100.0;
                        if (add < minFraction)
                        {
                            continue;
                        }

                        if (fraction.ContainsKey(row.Key))
                        {
                            fraction[row.Key] += add;
                        }
                        else
                        {
                            fraction[row.Key] = add;
                        }

                        queue.Add(new KeyValuePair<string, double>(row.Key, add));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.NucBase_DaughtersFetchError, rootNucid, ex.Message),
                    Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            db.Close();
            return fraction;
        }

    }
}
