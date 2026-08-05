using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
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

        public List<DecayRad> getDecayRad(string nucname, double intensity = 0.0, double lowEnergy = 0.0, double highEnergy = 3000.0, double half_life_sec = 0)
        {
            DataBase db = new DataBase();
            string sql = "select dr.parent_nucid, dr.energy_num, dr.intensity_num, dr.type_a, dr.type_c, dr.dec_type, nuc.half_life, nuc.half_life_unit from decay_radiations as dr, nuclides nuc where dr.parent_nucid = nuc.nucid and dr.type_a in ('G', 'X') and ";
            if (nucname.Length > 0)
            {
                sql += "dr.parent_nucid = '" + nucname + "' and ";
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
            return decayRads;
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
            List<string> order = new List<string>();
            fraction[rootNucid] = 1.0;
            order.Add(rootNucid);

            DataBase db = new DataBase();
            try
            {
                for (int i = 0; i < order.Count && order.Count <= 100; i++)
                {
                    string current = order[i];
                    List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
                    // Строки вычитываются целиком до следующего запроса: обходу
                    // нужен ещё один читатель на том же соединении.
                    SqliteDataReader reader = db.ReadData(
                        "select daughter_nucid, perc from decay_chain d where nucid = '" + current +
                        "' and perc not null and l_seqno = (select min(l_seqno) from decay_chain x " +
                        "where x.nucid = d.nucid and x.daughter_nucid = d.daughter_nucid " +
                        "and x.dec_type = d.dec_type)");
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

                        double add = fraction[current] * percent / 100.0;
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
                            order.Add(row.Key);
                        }
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
