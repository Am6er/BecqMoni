using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace EstarPotentialProbe
{
    /// <summary>
    /// `AMBER56` (П123, 22.09.2026): средняя энергия возбуждения I соединения по
    /// правилу Брэгга — какой потенциал берётся у элементов Z &lt; 10.
    ///
    ///     estarpotentialprobe
    ///
    /// `ESTAR.f` (Berger, NISTIR 4999) для СОЕДИНЕНИЯ при Z &lt; 10 берёт
    /// потенциал «элемента в соединении» (`POTCON`, ICRU 37 табл. 5.2: C 81,
    /// O 106, F 112), при Z ≥ 10 — 1.13·`POTH`; элементный `POTH` (C 78, O 95)
    /// идёт только чистому элементу. Проверяется, что даёт
    /// <c>EstarCalculator.BraggPotential</c> (частный, зовётся отражением) на
    /// BGO, чей табличный I известен (`star_materials` id 117: 534.1 эВ), и что
    /// от этого меняется у веществ, идущих Брэггом по-настоящему (Lu₂O₃, GSO).
    ///
    /// Печатается инвариантом, дробные — «R»: выдачи ДО и ПОСЛЕ сравниваются
    /// построчно. Разделы:
    ///
    /// 1. I по Брэггу против табличного `star_materials` — BGO, вода, NaI
    ///    (табличные есть), Lu₂O₃ и GSO (табличных нет — Брэгг и есть рабочее I).
    /// 2. Рабочий счёт <see cref="EstarCalculator.Stopping"/> /
    ///    <see cref="EstarCalculator.Compute"/>: I, столкновительная тормозная
    ///    на 0.1 и 1 МэВ (МэВ·см²/г), пробег CSDA (г/см²) и выход на тех же
    ///    энергиях — у задетых (Lu₂O₃, GSO) и у контроля неизменности: NaI и
    ///    вода (табличное I), Al (элемент Z ≥ 10, табличное I), LaBr₃ (Брэгг,
    ///    лёгких элементов нет). Строки контроля обязаны совпасть до знака.
    ///
    /// Код возврата: 0 — Брэгг на BGO сходится с табличными 534.1 эВ ближе 0.5 эВ
    /// (0.1 %); 1 — дефект воспроизведён (так и должно быть ДО правки).
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int bad = 0;

            Type t = typeof(EstarCalculator);
            MethodInfo bragg = t.GetMethod("BraggPotential", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo mass = t.GetMethod("MassFractions", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo weights = t.GetMethod("AtomicWeights", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo tabulated = t.GetMethod("TabulatedPotential", BindingFlags.NonPublic | BindingFlags.Static);
            if (bragg == null || mass == null || weights == null || tabulated == null)
            {
                Console.Error.WriteLine("отражение: не нашлись BraggPotential / MassFractions / AtomicWeights / TabulatedPotential");
                return 2;
            }

            Dictionary<int, double> w = (Dictionary<int, double>)weights.Invoke(null, null);

            Console.WriteLine("0. Потенциалы элементов Z < 10 из matdb (`estar_element_potential`): potential_ev / gas / cond");
            using (SqliteConnection c = Open())
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select z, potential_ev, potential_gas_ev, potential_cond_ev from estar_element_potential where z < 10 order by z";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        Console.WriteLine("   Z={0,2}  {1,6} / {2,6} / {3,6}", r.GetInt32(0), R(r.GetDouble(1)),
                                          r.IsDBNull(2) ? "-" : R(r.GetDouble(2)), r.IsDBNull(3) ? "-" : R(r.GetDouble(3)));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("1. I по Брэггу (BraggPotential) против табличного `star_materials`, эВ");
            Console.WriteLine("   {0,-8} {1,-14} {2,12} {3,12} {4,10}", "вещество", "формула", "Брэгг", "таблица", "Брэгг/табл");
            double braggBgo = 0.0;
            foreach (Spec s in Specs)
            {
                Dictionary<int, double> f = (Dictionary<int, double>)mass.Invoke(null, new object[] { s.Compound });
                double b = (double)bragg.Invoke(null, new object[] { f, w });
                object tab = tabulated.Invoke(null, new object[] { f });
                string tabText = tab == null ? "нет" : R((double)tab);
                string ratio = tab == null ? "" : string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00} %", 100.0 * (b / (double)tab - 1.0));
                Console.WriteLine("   {0,-8} {1,-14} {2,12} {3,12} {4,10}", s.Name, s.Formula, R(b), tabText, ratio);
                if (s.Name == "BGO")
                {
                    braggBgo = b;
                }
            }

            Console.WriteLine();
            Console.WriteLine("2. Рабочий счёт (Stopping / Compute): I, S_col и пробег CSDA на 0.1 и 1 МэВ, выход");
            Console.WriteLine("   {0,-8} {1,10} {2,22} {3,22} {4,22} {5,22}", "вещество", "I, эВ",
                              "S_col(0.1)  S_col(1)", "S_rad(0.1)  S_rad(1)", "R(0.1)  R(1) г/см²", "Y(0.1)  Y(1)");
            double[] grid = { 0.1, 1.0 };
            foreach (Spec s in Specs)
            {
                double[] e, col, rad;
                double potential;
                EstarCalculator.Stopping(s.Compound, out e, out col, out rad, out potential);
                int i01 = IndexOf(e, 0.1), i1 = IndexOf(e, 1.0);
                EstarCalculator.Result res = EstarCalculator.Compute(s.Compound, grid);
                Console.WriteLine("   {0,-8} {1,10} {2} {3}  {4} {5}  {6} {7}  {8} {9}   [{10}]",
                                  s.Name, R(potential), R(col[i01]), R(col[i1]), R(rad[i01]), R(rad[i1]),
                                  R(res.RangeGCm2[0]), R(res.RangeGCm2[1]), R(res.Yield[0]), R(res.Yield[1]), s.Role);
            }

            Console.WriteLine();
            Console.WriteLine("Приёмка:");
            bad += Check(string.Format(CultureInfo.InvariantCulture,
                                       "Брэгг на BGO {0:F2} против табличных 534.1 (ближе 0.5 эВ)", braggBgo),
                         Math.Abs(braggBgo - 534.1) < 0.5);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        sealed class Spec
        {
            public string Name;
            public string Formula;
            public string Role;
            public EstarCalculator.Compound Compound;
        }

        static Spec Make(string name, string formula, double density, string role, int[] z, double[] atoms)
        {
            return new Spec
            {
                Name = name, Formula = formula, Role = role,
                Compound = new EstarCalculator.Compound { Name = name, Z = z, Atoms = atoms, DensityGCm3 = density },
            };
        }

        static readonly Spec[] Specs =
        {
            Make("BGO",   "Bi4 Ge3 O12", 7.13,  "табл. I; Брэгг — только сверка", new[] { 83, 32, 8 }, new[] { 4.0, 3.0, 12.0 }),
            Make("Lu2O3", "Lu2 O3",      9.42,  "ЗАДЕТО: Брэгг рабочий, есть O",  new[] { 71, 8 },     new[] { 2.0, 3.0 }),
            Make("GSO",   "Gd2 Si1 O5",  6.71,  "ЗАДЕТО: Брэгг рабочий, есть O",  new[] { 64, 14, 8 }, new[] { 2.0, 1.0, 5.0 }),
            Make("NaI",   "Na1 I1",      3.667, "контроль: табл. I",              new[] { 11, 53 },    new[] { 1.0, 1.0 }),
            Make("Water", "H2 O1",       1.0,   "контроль: табл. I",              new[] { 1, 8 },      new[] { 2.0, 1.0 }),
            Make("Al",    "Al1",         2.699, "контроль: элемент Z>=10, табл. I", new[] { 13 },      new[] { 1.0 }),
            Make("LaBr3", "La1 Br3",     5.08,  "контроль: Брэгг, Z>=10 только",  new[] { 57, 35 },    new[] { 1.0, 3.0 }),
        };

        static int IndexOf(double[] grid, double value)
        {
            for (int i = 0; i < grid.Length; i++)
            {
                if (Math.Abs(grid[i] - value) < 1e-9)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("в сетке ESTAR нет узла " + R(value));
        }

        static SqliteConnection Open()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
            SqliteConnection c = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;");
            c.Open();
            return c;
        }

        static int Check(string what, bool ok)
        {
            Console.WriteLine("   {0} — {1}", what, ok ? "сошлось" : "НЕ СОШЛОСЬ");
            return ok ? 0 : 1;
        }

        static string R(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
