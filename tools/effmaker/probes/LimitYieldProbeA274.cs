using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// (`AMBER274`, полоса `f8diag` 02.11.2026) ВЫХОД-ПРЕДЕЛ — НЕ ВЫХОД. Решение Amber вопросником,
/// дословно: «Откатить и отсеивать по признаку (Рекомендую)». У строк `decay_radiations` с
/// `intensity_unc` = `LT`/`LE` число выхода — верхняя граница; строки в базе остаются, а
/// читатели выходом их не берут (`DecayParentRule.NotLimitClause`).
///
///     limityieldprobea274 [--parent=<nucid>]
///
/// Что меряется (одна проба ДО и ПОСЛЕ правки; нового API не зовёт — признак показа берётся
/// отражением):
///   (1) у КАЖДОГО родителя со строкой-пределом типа `G` (уровень — правилом
///       `DecayParentRule.LevelClause`) и с другими линиями: попадает ли предел в линии
///       `FsaSampleLibrary.DecayLines` и в состав FSA (`FsaSampleLibrary.Build` по одиночному
///       нуклиду). Сравнение — по выходу на энергии предела: сумма выходов линий состава в
///       ±0.05 кэВ против суммы НЕпредельных строк той же энергии;
///   (2) то же у `B+`-пределов: аннигиляционная линия 511 из `DecayLines`;
///   (3) «База изотопов»: предел виден (`DecayRad.IntensityIsLimit`), подпись «&lt; число»,
///       номер строки `dr_pk` у предела читается `GetDecayRadByRow`, как у прочих.
///
/// Код: 0 — пределов в составе нет, показ предела есть; 1 — предел попал в состав (так ДО
/// правки — положительный контроль) или показа нет; 2 — мерить нечем.
/// </summary>
static class LimitYieldProbeA274
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string only = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--parent=", StringComparison.Ordinal)) only = a.Substring(9);
        }

        string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        if (!File.Exists(db))
        {
            Console.WriteLine("МЕРИТЬ НЕЧЕМ: нет " + db);
            return 2;
        }

        // строки-пределы своего уровня родителя: (родитель, тип, энергия, выход, признак, dr_pk)
        var limits = new List<object[]>();
        var parents = new List<string>();
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select distinct parent_nucid from decay_radiations"
                                  + " where type_a in ('G', 'B+') and intensity_num > 0"
                                  + " and upper(trim(intensity_unc)) in ('LT', 'LE') order by parent_nucid";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read()) parents.Add(r.GetString(0));
                }
            }

            foreach (string p in parents)
            {
                if (only != null && !string.Equals(p, only, StringComparison.OrdinalIgnoreCase)) continue;
                using (SqliteCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select type_a, energy_num, intensity_num, intensity_unc, dr_pk from decay_radiations"
                                      + " where parent_nucid = $n and type_a in ('G', 'B+') and intensity_num > 0"
                                      + " and upper(trim(intensity_unc)) in ('LT', 'LE')" + DecayParentRule.LevelClause;
                    cmd.Parameters.AddWithValue("$n", p);
                    using (SqliteDataReader r = cmd.ExecuteReader())
                    {
                        var read = new List<object[]>();
                        while (r.Read())
                        {
                            read.Add(new object[]
                            {
                                p, r.GetString(0), r.IsDBNull(1) ? double.NaN : r.GetDouble(1), r.GetDouble(2),
                                r.GetString(3), r.GetInt64(4), 0.0,
                            });
                        }

                        r.Close();
                        foreach (object[] row in read)
                        {
                            // у β⁺ — ожидание линии 511 без предела: гамма 511 своих строк + 2·Σ β⁺ без пределов
                            row[6] = (string)row[1] == "B+"
                                ? OtherYieldAt(c, p, FsaAnalyzer.AnnihilationKev) + 2.0 * OtherBetaPlus(p)
                                : OtherYieldAt(c, p, (double)row[2]);
                            limits.Add(row);
                        }
                    }
                }
            }
        }

        int gammaLimits = 0, gammaInLines = 0, gammaInComposition = 0, parentsWithOthers = 0, parentsHit = 0;
        int betaPlusLimits = 0, betaPlusIn511 = 0, skippedXray = 0;
        var seenParents = new HashSet<string>(StringComparer.Ordinal);
        var hitParents = new HashSet<string>(StringComparer.Ordinal);
        var examples = new List<string>();
        foreach (object[] l in limits)
        {
            string p = (string)l[0];
            string type = (string)l[1];
            double e = (double)l[2], y = (double)l[3], other = (double)l[6];
            List<double[]> lines = FsaSampleLibrary.DecayLines(p, new FsaSampleLibrary.Report());
            if (type == "B+")
            {
                betaPlusLimits++;
                double at511 = YieldAt(lines, FsaAnalyzer.AnnihilationKev);
                if (at511 > other + 1e-12 + 1e-9 * other)
                {
                    betaPlusIn511++;
                    if (examples.Count < 12) examples.Add(string.Format(Inv, "{0} β⁺ {1} % {2}: в линии 511 выход {3:G6} %", p, y.ToString("G4", Inv), l[4], at511));
                }

                continue;
            }

            gammaLimits++;
            // Рядом рентген (±0.5 кэВ) — его линия в составе неотличима по энергии от предела:
            // такой предел не судится, а считается (у `105MO` γ 18.4 кэВ рядом Tc Kα 18.37).
            if (XrayNear(p, e) > 0)
            {
                skippedXray++;
                continue;
            }

            bool hasOthers = lines.Count > 0;
            if (seenParents.Add(p) && hasOthers) parentsWithOthers++;
            double inLines = YieldAt(lines, e);
            bool lineHit = inLines > other + 1e-9 * Math.Max(1.0, y);
            if (lineHit) gammaInLines++;

            var spec = new FsaSampleSpec();
            spec.Nuclides.Add(p);
            FsaSampleLibrary.Report rep;
            List<FsaComponent> comps = FsaSampleLibrary.Build(spec, out rep);
            double inComp = 0.0;
            foreach (FsaComponent comp in comps)
            {
                if (comp.Lines == null) continue;
                foreach (FsaLine line in comp.Lines)
                {
                    if (Math.Abs(line.Energy - e) < 0.05) inComp += line.Intensity;
                }
            }

            bool compHit = inComp > other + 1e-9 * Math.Max(1.0, y);
            if (compHit)
            {
                gammaInComposition++;
                if (hitParents.Add(p) && hasOthers) parentsHit++;
                if (examples.Count < 12)
                    examples.Add(string.Format(Inv, "{0} γ {1:F3} кэВ, предел {2} {3} % (dr_pk {4}): в составе {5:G6} % при непредельных {6:G6} %",
                                               p, e, l[4], y.ToString("G4", Inv), l[5], inComp, other));
            }
        }

        Console.WriteLine("строк-пределов своего уровня: γ {0} у {1} родителей (с другими линиями — {2}); β⁺ {3}",
                          gammaLimits, seenParents.Count, parentsWithOthers, betaPlusLimits);
        Console.WriteLine("(1) γ-пределов в линиях DecayLines: {0}; в составе FSA: {1} (родителей с другими линиями: {2}); не судимы (рентген в ±0.5 кэВ): {3}",
                          gammaInLines, gammaInComposition, parentsHit, skippedXray);
        Console.WriteLine("(2) β⁺-пределов в линии 511: {0}", betaPlusIn511);
        foreach (string s in examples) Console.WriteLine("    " + s);

        // (3) «База изотопов»
        int showBad = 0;
        PropertyInfo flag = typeof(DecayRad).GetProperty("IntensityIsLimit");
        MethodInfo caption = typeof(BecquerelMonitor.NucBase.NucBase).GetMethod("IntensityLimitCaption", BindingFlags.Public | BindingFlags.Static);
        var fw = new NucBaseFramework();
        int shown = 0, flagged = 0, rowsOk = 0, checkedRows = 0;
        string firstCaption = null;
        foreach (object[] l in limits)
        {
            if ((string)l[1] != "G") continue;
            checkedRows++;
            List<DecayRad> rows = fw.getDecayRad((string)l[0]);
            DecayRad hit = null;
            if (rows != null)
            {
                foreach (DecayRad d in rows) if (d.DrPk == (long)l[5]) hit = d;
            }

            if (hit != null)
            {
                shown++;
                if (flag != null && (bool)flag.GetValue(hit)) flagged++;
                if (firstCaption == null && caption != null) firstCaption = (string)caption.Invoke(null, new object[] { hit.Intensity }) + " (" + hit.Name + " " + hit.Energy.ToString("F3", Inv) + " кэВ)";
            }

            DecayRad byRow = fw.GetDecayRadByRow((long)l[5]);
            if (byRow != null && byRow.DrPk == (long)l[5] && Math.Abs(byRow.Energy - (double)l[2]) < 1e-9) rowsOk++;
        }

        Console.WriteLine("(3) «База изотопов»: γ-пределов в выдаче {0} из {1}, с признаком предела {2}; по номеру строки читаются {3}; подпись: {4}",
                          shown, checkedRows, flag == null ? "нет признака в сборке" : flagged.ToString(Inv), rowsOk,
                          firstCaption ?? "нет подписи в сборке");
        if (flag == null || flagged != shown || shown != checkedRows) showBad++;
        if (rowsOk != checkedRows) showBad++;

        if (limits.Count == 0)
        {
            Console.WriteLine("МЕРИТЬ НЕЧЕМ: строк-пределов нет");
            return 2;
        }

        bool ok = gammaInLines == 0 && gammaInComposition == 0 && betaPlusIn511 == 0 && showBad == 0;
        Console.WriteLine(ok ? "ИТОГ: ПРЕДЕЛ НЕ ВЫХОД — СОШЛОСЬ" : "ИТОГ: ПРЕДЕЛ ИДЁТ ВЫХОДОМ ИЛИ НЕ ПОКАЗАН (так ДО правки)");
        return ok ? 0 : 1;
    }

    static double YieldAt(List<double[]> lines, double e)
    {
        double s = 0.0;
        foreach (double[] l in lines) if (Math.Abs(l[0] - e) < 0.05) s += l[1];
        return s;
    }

    /// <summary>Сумма НЕпредельных строк G той же энергии (±0.05 кэВ) своего уровня.</summary>
    static double OtherYieldAt(SqliteConnection c, string p, double e)
    {
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "select coalesce(sum(intensity_num), 0) from decay_radiations"
                              + " where parent_nucid = $n and type_a = 'G' and intensity_num > 0"
                              + " and abs(energy_num - $e) < 0.05"
                              + " and (intensity_unc is null or upper(trim(intensity_unc)) not in ('LT', 'LE'))"
                              + DecayParentRule.LevelClause;
            cmd.Parameters.AddWithValue("$n", p);
            cmd.Parameters.AddWithValue("$e", double.IsNaN(e) ? -1.0 : e);
            object v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? 0.0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Строк рентгена своего уровня в ±0.5 кэВ от энергии.</summary>
    static long XrayNear(string p, double e)
    {
        string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select count(*) from decay_radiations"
                                  + " where parent_nucid = $n and type_a = 'X' and intensity_num > 0"
                                  + " and abs(energy_num - $e) < 0.5" + DecayParentRule.LevelClause;
                cmd.Parameters.AddWithValue("$n", p);
                cmd.Parameters.AddWithValue("$e", double.IsNaN(e) ? -1.0 : e);
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }

    static double OtherBetaPlus(string p)
    {
        string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            c.Open();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select coalesce(sum(intensity_num), 0) from decay_radiations"
                                  + " where parent_nucid = $n and type_a = 'B+' and intensity_num > 0"
                                  + " and (intensity_unc is null or upper(trim(intensity_unc)) not in ('LT', 'LE'))"
                                  + DecayParentRule.LevelClause;
                cmd.Parameters.AddWithValue("$n", p);
                object v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0.0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
        }
    }
}
