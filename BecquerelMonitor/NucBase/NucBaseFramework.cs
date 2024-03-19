using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Linq;
using System.Windows.Forms;
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
                reader.Read();
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

        List<string> daughters = new List<string>();
        int depth = 0;

        public List<string> GetDaughters(string nucname, double instencity)
        {
            daughters.Clear();
            GetRecursiveDaughters(nucname, instencity);
            return daughters.Distinct().ToList();
        }

        private void GetRecursiveDaughters(string nucname, double intencity)
        {
            DataBase db = new DataBase();
            SqliteDataReader reader = db.ReadData("select daughter_nucid from decay_chain where nucid = '" + nucname +
                "' and perc not null and cast(perc as float) >= " + intencity + ";");
            int count = 1;
            try
            {
                while (count > 0)
                {
                    List<string> d_count = new List<string>();
                    while (reader.Read())
                    {
                        d_count.Add(reader.GetString(0));
                        daughters.Add(reader.GetString(0));
                    }
                    count = d_count.Count;
                    depth++;
                    if (depth > 100) break;
                    foreach (string d in d_count)
                    {
                        GetRecursiveDaughters(d, intencity);
                    }
                }
            } catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.NucBase_DaughtersFetchError, nucname, ex.Message),
                Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            db.Close();
        }
    }
}
