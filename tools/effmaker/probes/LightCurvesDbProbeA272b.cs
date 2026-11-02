using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace LightCurvesDbProbeA272b
{
    /// <summary>
    /// (`AMBER272`, полоса `dbdata`, 02.11.2026) Приёмка записи кривых света в `matdb`:
    /// строки кривых электронов LaBr3:Ce, CeBr3, SrI2, GAGG (`scint_electron_light_yield`),
    /// параметры Пейна SrI2(Eu) и GAGG (`scint_npsm_params`), фотонные кривые FSA SrI2 и GAGG
    /// (`scint_photon_light_*`). Решения Amber 29.10.2026 вопросником, дословно: «Записать и
    /// пересчитать склад», «SrI2(Eu), Payne 2009 (Рекомендую)», «Kole &amp; De Angelis (Рекомендую)».
    ///
    ///     lightcurvesdbprobea272b [--db=<matdb.sqlite>] [--before=<matdb до записи>]
    ///                             [--control=<копия с подменённым узлом>] [--scene=<файл.in>]...
    ///
    /// 1. ПОБИТОВО: каждый узел строк `scint_electron_light_yield` четырёх веществ в `--db`
    ///    (энергия и выход) против табличной формы кривой, которую перенос строит в коде,
    ///    `MaterialDatabase.LightYieldPayne(m, 0, false, 0, true, 2)` — параметры базы рядом с
    ///    программой (значит, `--db` обязан быть тем же файлом).
    /// 2. Параметры по умолчанию SrI2 и GAGG — числа решения Amber (η, S_Ons, S_Trap, S_Birks).
    /// 3. `--scene=`: имя кривой света по веществу кристалла (`ScintillatorNameOf`) и кривая,
    ///    которую берёт симулятор при умолчаниях склада (`kdip`, `ecomp`).
    /// 4. Фотонные кривые FSA (`FsaCascadeSummer.PhotonCurveFor`): SrI2, GAGG — не null;
    ///    r(E)/r(661.657) на опорных энергиях.
    /// 5. Отпечаток переноса `--before` против `--db` — печатается; РАВЕНСТВО здесь — отказ:
    ///    строки кривых электронов входят в `SimulatorTables`, клеймо склада обязано смениться.
    /// 6. `--control=`: копия с подменённым узлом — п. 1 обязан поймать расхождение.
    ///
    /// Код возврата: 0 — всё сошлось и контроль пойман; 1 — расхождение; 2 — ошибка запуска.
    /// </summary>
    static class Program
    {
        static readonly string[] Materials = { "LaBr3:Ce", "CeBr3", "SrI2", "GAGG" };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
            string before = null, control = null;
            var scenes = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--db=", StringComparison.Ordinal)) db = a.Substring(5);
                else if (a.StartsWith("--before=", StringComparison.Ordinal)) before = a.Substring(9);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) control = a.Substring(10);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenes.Add(a.Substring(8));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (!File.Exists(db))
            {
                Console.Error.WriteLine("нет базы: " + db);
                return 2;
            }

            bool ok = true;
            Console.WriteLine("база: {0}", db);

            int bad = CompareElectron(db, true);
            Console.WriteLine("ИТОГ 1 (строки кривых электронов против кода, побитово): {0}",
                              bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ " + bad);
            ok &= bad == 0;

            bad = CheckParams(db);
            Console.WriteLine("ИТОГ 2 (параметры SrI2 и GAGG по решению Amber): {0}", bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ " + bad);
            ok &= bad == 0;

            var store = new ResponseMatrixOptions();
            foreach (string scene in scenes)
            {
                GeometryModel geometry = GeometryModel.Load(scene);
                var sim = new EfficiencySimulator(geometry.Clone())
                {
                    LightNonproportionality = true,
                    LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(store.KDipLight),
                    LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(store.KDipLight),
                    ElectronAnyMaterial = store.ElectronAnyMaterial,
                };
                string name = EfficiencySimulator.ScintillatorNameOf(geometry);
                string curve = sim.LightYieldName;
                Console.WriteLine("ИТОГ 3 сцена {0}: ScintillatorNameOf = «{1}», кривая симулятора: {2}",
                                  Path.GetFileName(scene), name, curve == "" ? "НЕТ (шкала пропорциональна)" : curve);
                ok &= curve != "";
            }

            Type scale = typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaLightScale");
            scale.GetMethod("Reload", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { db });
            MethodInfo relative = scale.GetMethod("Relative", BindingFlags.Public | BindingFlags.Static);
            foreach (string m in new[] { "SrI2", "GAGG", "LaBr3:Ce", "CeBr3", "NaI:Tl", "CsI:Tl" })
            {
                string c = FsaCascadeSummer.PhotonCurveFor(m);
                var sb = new StringBuilder();
                if (c != null)
                {
                    double r0 = (double)relative.Invoke(null, new object[] { c, 661.657 });
                    foreach (double e in new[] { 10.0, 15.0, 20.0, 30.0, 40.0, 50.0, 60.0, 81.0, 100.0, 356.0, 1332.5, 2614.0 })
                    {
                        double r = (double)relative.Invoke(null, new object[] { c, e });
                        sb.AppendFormat(CultureInfo.InvariantCulture, " {0:G}:{1:F4}", e, r / r0);
                    }
                }

                Console.WriteLine("ИТОГ 4 PhotonCurveFor(\"{0}\") = {1};{2}", m, c ?? "null", sb);
                if (m == "SrI2" || m == "GAGG")
                {
                    ok &= c != null;
                }
            }

            if (before != null)
            {
                string fpBefore = MaterialDatabase.SimulatorDataFingerprint(before);
                string fpAfter = MaterialDatabase.SimulatorDataFingerprint(db);
                Console.WriteLine("ИТОГ 5 отпечаток переноса: до {0}, после {1} — {2}", fpBefore, fpAfter,
                                  fpBefore != fpAfter ? "СМЕНИЛСЯ (ожидалось)" : "ТОТ ЖЕ — строки не легли в отпечаток");
                ok &= fpBefore != fpAfter;
            }

            if (control != null)
            {
                int caught = CompareElectron(control, false);
                Console.WriteLine("ИТОГ 6 положительный контроль ({0}): расхождений {1} — {2}", control, caught,
                                  caught > 0 ? "ПОЙМАНО" : "НЕ ПОЙМАНО");
                ok &= caught > 0;
            }

            Console.WriteLine(ok ? "ПРИЁМКА: КОД 0" : "ПРИЁМКА: РАСХОЖДЕНИЕ");
            return ok ? 0 : 1;
        }

        static bool Same(double a, double b)
        {
            return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
        }

        static SqliteConnection Open(string path)
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
            c.Open();
            return c;
        }

        static int CompareElectron(string path, bool verbose)
        {
            FieldInfo fe = typeof(MaterialDatabase.LightYieldCurve).GetField("energyKev", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fy = typeof(MaterialDatabase.LightYieldCurve).GetField("yieldRel", BindingFlags.NonPublic | BindingFlags.Instance);
            int bad = 0;
            using (SqliteConnection c = Open(path))
            {
                foreach (string m in Materials)
                {
                    var e = new List<double>();
                    var y = new List<double>();
                    using (SqliteCommand cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "select energy_kev, yield_rel from scint_electron_light_yield where material = $m order by energy_kev";
                        cmd.Parameters.AddWithValue("$m", m);
                        using (SqliteDataReader r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                e.Add(r.GetDouble(0));
                                y.Add(r.GetDouble(1));
                            }
                        }
                    }

                    MaterialDatabase.LightYieldCurve code = MaterialDatabase.LightYieldPayne(m, 0.0, false, 0.0, true, 2.0);
                    if (code == null)
                    {
                        bad++;
                        Console.WriteLine("  {0}: кривой в коде нет (нет строки параметров?)", m);
                        continue;
                    }

                    var ce = (double[])fe.GetValue(code);
                    var cy = (double[])fy.GetValue(code);
                    int nodeBad = 0;
                    if (ce.Length != e.Count)
                    {
                        nodeBad = Math.Max(ce.Length, e.Count);
                    }
                    else
                    {
                        for (int i = 0; i < ce.Length; i++)
                        {
                            if (!Same(ce[i], e[i]) || !Same(cy[i], y[i]))
                            {
                                nodeBad++;
                                if (verbose || nodeBad <= 2)
                                {
                                    Console.WriteLine("    {0}: узел {1} — база {2:R}/{3:R}, код {4:R}/{5:R}", m, i, e[i], y[i], ce[i], cy[i]);
                                }
                            }
                        }
                    }

                    if (verbose)
                    {
                        Console.WriteLine("  {0}: строк базы {1}, узлов кода {2}, не сошлось {3}; L/E(10 кэВ) {4:F4}, (3 МэВ) {5:F4} [{6}]",
                                          m, e.Count, ce.Length, nodeBad, code.Of(10.0), code.Of(3000.0), code.Variant);
                    }

                    bad += nodeBad;
                }
            }

            return bad;
        }

        static int CheckParams(string path)
        {
            // material, source, η, S_Ons, S_Trap, S_Birks
            var want = new[]
            {
                new object[] { "SrI2", "payne2009_tableI_SrI2Eu", 0.22, 27.8, 0.0, 526.0 },
                // (`AMBER272`, полоса `dbdata2`, 02.11.2026) подгонка в модели базы к форме Kole и σ_nPR вместе —
                // решение Amber 02.11.2026 «Подогнать в нашей модели (Рекомендую)»; строка Kole — история
                new object[] { "GAGG", "dbdata2_fit_kole2025_campana2023", 0.656, 36.4, 0.0, 70.1 },
            };
            int bad = 0;
            using (SqliteConnection c = Open(path))
            {
                foreach (object[] w in want)
                {
                    using (SqliteCommand cmd = c.CreateCommand())
                    {
                        cmd.CommandText = "select source, eta_eh, dedx_ons_mev_cm, dedx_trap_mev_cm, dedx_birks_mev_cm"
                                          + " from scint_npsm_params where material = $m and is_default = 1";
                        cmd.Parameters.AddWithValue("$m", (string)w[0]);
                        using (SqliteDataReader r = cmd.ExecuteReader())
                        {
                            if (!r.Read())
                            {
                                bad++;
                                Console.WriteLine("  {0}: строки по умолчанию нет", w[0]);
                                continue;
                            }

                            bool same = r.GetString(0) == (string)w[1] && Same(r.GetDouble(1), (double)w[2])
                                        && Same(r.GetDouble(2), (double)w[3]) && Same(r.GetDouble(3), (double)w[4])
                                        && Same(r.GetDouble(4), (double)w[5]);
                            Console.WriteLine("  {0}: {1} η {2:R}, S_Ons {3:R}, S_Trap {4:R}, S_Birks {5:R} — {6}", w[0], r.GetString(0),
                                              r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), same ? "как в решении" : "НЕ ТО");
                            if (!same)
                            {
                                bad++;
                            }
                        }
                    }
                }
            }

            return bad;
        }
    }
}
