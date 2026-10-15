using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace P317EstarProbe
{
    /// <summary>
    /// П317 (15.10.2026): ТОРМОЗНАЯ СПОСОБНОСТЬ ЭЛЕКТРОНОВ И ПРОБЕГ CSDA
    /// (<see cref="EstarCalculator"/>) — сверка ЧИСЛОМ с эталоном.
    ///
    ///     p317estarprobe
    ///
    /// 1. CsI и NaI против выхода самого NIST ESTAR (`estar_collision_stopping`,
    ///    82 энергии 1 кэВ … 1 ГэВ): столкновительная и радиационная тормозная
    ///    способность и ПОПРАВКА НА ПЛОТНОСТЬ δ. δ у нас не печатается — она
    ///    восстанавливается из S_col обращением формулы Бете (ICRU 37), и это
    ///    проверяет сам модуль Штернхеймера, а не только итоговую сумму.
    /// 2. Пробег CSDA и радиационный выход против вшитой таблицы, снятой с
    ///    НАСТОЯЩЕГО ESTAR (<see cref="ElectronData.BuiltinByName"/>: CsI, NaI,
    ///    BGO, LaBr₃ — 55 узлов 1 кэВ … 3 МэВ) — обоими рабочими путями:
    ///    опознание по набору Z (<see cref="ElectronData.Match"/>) и счёт по
    ///    составу и плотности сцены (<see cref="ElectronData.ForComposition"/>).
    /// 3. АСИМПТОТА δ на 10 ГэВ (верх сетки ESTAR) — у всех веществ сцены, в том числе проводников
    ///    (Al, Cu, Fe, Pb, W, Ge), которых нет ни в одном эталоне базы: δ обязана
    ///    выйти на 2 ln(βγ) − 2 ln(I/ħωp) − 1 (Штернхеймер). Положительный
    ///    контроль — тот же счёт у δ самого NIST (CsI, NaI) и плотность ×1.1 в
    ///    ħωp, которую проверка обязана увидеть.
    /// 4. ТАБЛИЧНОЕ I: какое вещество NIST STAR опознано по составу, и
    ///    совпадает ли оно с ближайшим по ПЛОТНОСТИ из совпавших по составу
    ///    (полиэтилен, полистирол, графит и вода делят состав с жидкостями и
    ///    газами — циклогексан, ацетилен, аморфный углерод, пар). Цена —
    ///    пробег на 100 кэВ и 1 МэВ при одном и другом I.
    ///
    /// ⚠ Культура потока — ru-RU нарочно (как в <c>P317XcomProbe</c>).
    ///
    /// Код возврата: 0 — сошлось и контроль сработал; 1 — есть выброс; 2 — нет базы.
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double Rmass = 0.510999906;   // `ESTAR.f`, DATA RMASS
        const double Coff = 0.307072;       // `ESTAR.f`, DATA COFF

        static string dbPath;
        static readonly Dictionary<int, double> weight = new Dictionary<int, double>();

        sealed class Star
        {
            public int Id;
            public string Name;
            public double Density;
            public double PotentialEv;
            public Dictionary<int, double> Composition = new Dictionary<int, double>();
        }

        static readonly List<Star> stars = new List<Star>();

        static string F(string format, params object[] args)
        {
            return string.Format(Inv, format, args);
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length > 0)
            {
                Console.Error.WriteLine("ключей нет: " + string.Join(" ", args));
                return 2;
            }

            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
            dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
            if (!File.Exists(dbPath))
            {
                Console.WriteLine("нет matdb.sqlite рядом с пробой: " + dbPath);
                return 2;
            }

            LoadStars();
            int fails = 0;
            fails += SectionNist();
            fails += SectionRange();
            fails += SectionAsymptote();
            fails += SectionPotential();
            Console.WriteLine();
            Console.WriteLine(fails == 0
                ? "ИТОГ: 0 выбросов, контроль сработал."
                : string.Format(CultureInfo.InvariantCulture, "ИТОГ: {0} выброс(ов) — перечислены выше.", fails));
            return fails == 0 ? 0 : 1;
        }

        static SqliteConnection Open()
        {
            SqliteConnection c = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            c.Open();
            return c;
        }

        static void LoadStars()
        {
            using (SqliteConnection c = Open())
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select z, atomic_weight from xcom_elements";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        weight[r.GetInt32(0)] = r.GetDouble(1);
                    }
                }

                Dictionary<int, Star> byId = new Dictionary<int, Star>();
                cmd.CommandText = "select id, name, density_g_cm3, potential_ev from star_materials order by id";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        Star s = new Star
                        {
                            Id = r.GetInt32(0), Name = r.GetString(1),
                            Density = r.GetDouble(2), PotentialEv = r.GetDouble(3),
                        };
                        byId[s.Id] = s;
                        stars.Add(s);
                    }
                }

                cmd.CommandText = "select material_id, z, weight_fraction from star_material_composition";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        Star s;
                        if (byId.TryGetValue(r.GetInt32(0), out s))
                        {
                            s.Composition[r.GetInt32(1)] = r.GetDouble(2);
                        }
                    }
                }
            }
        }

        /// <summary>Вещество ESTAR из массовых долей — ровно как его строит <see cref="ElectronData.ForComposition"/>.</summary>
        static EstarCalculator.Compound CompoundOf(string name, Dictionary<int, double> fractions, double density, double? potential)
        {
            List<int> zs = new List<int>();
            List<double> atoms = new List<double>();
            foreach (KeyValuePair<int, double> p in fractions)
            {
                if (p.Value > 0.0)
                {
                    zs.Add(p.Key);
                    atoms.Add(p.Value / MaterialDatabase.AtomicMass[p.Key]);
                }
            }

            return new EstarCalculator.Compound
            {
                Name = name, Z = zs.ToArray(), Atoms = atoms.ToArray(),
                DensityGCm3 = density, PotentialEv = potential,
            };
        }

        static double ZOverA(Dictionary<int, double> fractions)
        {
            double s = 0.0, w = 0.0;
            foreach (KeyValuePair<int, double> p in fractions)
            {
                w += p.Value;
            }

            foreach (KeyValuePair<int, double> p in fractions)
            {
                s += p.Value / w * p.Key / weight[p.Key];
            }

            return s;
        }

        /// <summary>
        /// δ из S_col обращением формулы ICRU 37 (та же, что в `ESTAR.f`):
        /// S = COFF·⟨Z/A⟩/β²·[ln T − ln I + ½ ln(1 + τ/2) + ½F − ½δ].
        /// </summary>
        static double DeltaFromCollision(double t, double collision, double potentialEv, double zav)
        {
            double tau = t / Rmass;
            double y = tau * (tau + 2.0);
            double betq = y / ((tau + 1.0) * (tau + 1.0));
            double term = (1.0 - betq) * (1.0 + tau * tau / 8.0 - (2.0 * tau + 1.0) * Math.Log(2.0));
            double bracket = collision * betq / (Coff * zav);
            return 2.0 * (Math.Log(t) - Math.Log(potentialEv * 1e-6) + 0.5 * Math.Log(1.0 + 0.5 * tau) + 0.5 * term - bracket);
        }

        /// <summary>Асимптота Штернхеймера: 2 ln(βγ) − 2 ln(I/ħωp) − 1, ħωp = 28.816·√(ρ⟨Z/A⟩) эВ.</summary>
        static double DeltaAsymptote(double t, double potentialEv, double density, double zav)
        {
            double tau = t / Rmass;
            double betaGamma = Math.Sqrt(tau * (tau + 2.0));
            double hom = 28.81593 * Math.Sqrt(density * zav);
            return 2.0 * Math.Log(betaGamma) - 2.0 * Math.Log(potentialEv / hom) - 1.0;
        }

        static Dictionary<int, double> Formula(string formula)
        {
            Dictionary<int, double> atoms = GeometryMaterialLibrary.ParseFormula(formula);
            double total = 0.0;
            foreach (KeyValuePair<int, double> p in atoms)
            {
                total += p.Value * weight[p.Key];
            }

            Dictionary<int, double> w = new Dictionary<int, double>();
            foreach (KeyValuePair<int, double> p in atoms)
            {
                w[p.Key] = p.Value * weight[p.Key] / total;
            }

            return w;
        }

        // ---------------------------------------------------------------- 1

        static int SectionNist()
        {
            Console.WriteLine("1. CsI / NaI против NIST ESTAR (`estar_collision_stopping`): S_col, S_rad, МэВ·см²/г, и δ");
            int fails = 0;
            string[][] cases =
            {
                new[] { "141", "CsI", "Cs1 I1", "4.51" },
                new[] { "252", "NaI", "Na1 I1", "3.667" },
            };
            double[] show = { 0.001, 0.01, 0.03, 0.1, 0.3, 0.6, 1.0, 3.0, 10.0, 100.0, 1000.0 };
            double residualNist = 0.0;
            foreach (string[] c in cases)
            {
                Dictionary<int, double> fr = Formula(c[2]);
                double density = double.Parse(c[3], Inv);
                double[] t, col, rad;
                double potential;
                EstarCalculator.Stopping(CompoundOf(c[1], fr, density, null), out t, out col, out rad, out potential);
                double zav = ZOverA(fr);

                List<double[]> nist = new List<double[]>();
                using (SqliteConnection cn = Open())
                using (SqliteCommand cmd = cn.CreateCommand())
                {
                    cmd.CommandText = "select energy_mev, collision_mev_cm2_g, radiative_mev_cm2_g, delta"
                                      + " from estar_collision_stopping where material_star_id=" + c[0] + " order by energy_mev";
                    using (SqliteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            nist.Add(new[] { r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3) });
                        }
                    }
                }

                double worstCol = 0.0, worstRad = 0.0, worstDelta = 0.0;
                string atCol = "", atRad = "", atDelta = "";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0}: I = {1} эВ, ρ = {2}, ⟨Z/A⟩ = {3:F6}; точек NIST {4}", c[1], potential, density, zav, nist.Count));
                Console.WriteLine("     T, МэВ     S_col наше   NIST      откл.    S_rad наше    NIST       откл.    δ наше    δ NIST   разн.");
                foreach (double[] n in nist)
                {
                    int i = Array.FindIndex(t, x => Math.Abs(x / n[0] - 1.0) < 1e-9);
                    if (i < 0)
                    {
                        fails++;
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   ⛔ {0} МэВ NIST нет в сетке счёта", n[0]));
                        continue;
                    }

                    double dc = col[i] / n[1] - 1.0;
                    double dr = rad[i] / n[2] - 1.0;
                    double delta = DeltaFromCollision(t[i], col[i], potential, zav);
                    double dd = delta - n[3];
                    if (Math.Abs(dc) > Math.Abs(worstCol)) { worstCol = dc; atCol = string.Format(CultureInfo.InvariantCulture, "{0} МэВ", n[0]); }
                    if (Math.Abs(dr) > Math.Abs(worstRad)) { worstRad = dr; atRad = string.Format(CultureInfo.InvariantCulture, "{0} МэВ", n[0]); }
                    if (Math.Abs(dd) > Math.Abs(worstDelta)) { worstDelta = dd; atDelta = string.Format(CultureInfo.InvariantCulture, "{0} МэВ", n[0]); }
                    if (Array.IndexOf(show, n[0]) >= 0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     {0,-9} {1,10:G5} {2,10:G4} {3,8:+0.000%;-0.000%} {4,11:G4} {5,10:G4} {6,8:+0.00%;-0.00%} {7,8:F4} {8,8:F4} {9,7:+0.0000;-0.0000}",
                                            n[0], col[i], n[1], dc, rad[i], n[2], dr, delta, n[3], dd));
                    }

                    if (n[0] == 1000.0)
                    {
                        double residual = n[3] - DeltaAsymptote(n[0], potential, density, zav);
                        residualNist = Math.Max(residualNist, Math.Abs(residual));
                    }
                }

                bool ok = Math.Abs(worstCol) < 0.001 && Math.Abs(worstRad) < 0.001 && Math.Abs(worstDelta) < 0.01;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     худшее: S_col {0:+0.000%;-0.000%} ({1}), S_rad {2:+0.000%;-0.000%} ({3}), δ {4:+0.0000;-0.0000} ({5}) — {6}",
                                    worstCol, atCol, worstRad, atRad, worstDelta, atDelta, ok ? "сошлось (0.1 %, δ 0.01)" : "⛔ РАСХОЖДЕНИЕ"));
                if (!ok)
                {
                    fails++;
                }
            }

            asymptoteControl = residualNist;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   δ NIST на 1 ГэВ против асимптоты Штернхеймера: |остаток| ≤ {0:F4} — эталон для раздела 3", residualNist));
            Console.WriteLine();
            return fails;
        }

        static double asymptoteControl;

        // ---------------------------------------------------------------- 2

        static int SectionRange()
        {
            Console.WriteLine("2. Пробег CSDA и выход тормозного против НАСТОЯЩЕГО ESTAR (вшитая таблица), 55 узлов 1 кэВ … 3 МэВ");
            int fails = 0;
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            foreach (string abbr in new[] { "CsI", "NaI", "BGO", "LaBr3" })
            {
                GeometryMaterialLibrary.Entry entry = seed.Find(e => e.Abbr == abbr);
                GeometryMaterial m = GeometryMaterialLibrary.Make(entry, entry.Density, n => null);
                ElectronData.Material reference = ElectronData.BuiltinByName(abbr);
                ElectronData.Material match = ElectronData.Match(m);
                ElectronData.Material any = ElectronData.ForComposition(m);
                foreach (KeyValuePair<string, ElectronData.Material> path in new[]
                         {
                             new KeyValuePair<string, ElectronData.Material>("Match", match),
                             new KeyValuePair<string, ElectronData.Material>("ForComposition", any),
                         })
                {
                    double wr = 0.0, wy = 0.0;
                    string ar = "", ay = "";
                    for (int i = 0; i < reference.Energy.Length; i++)
                    {
                        double kev = reference.Energy[i] * 1000.0;
                        double r = ElectronData.RangeOf(path.Value, kev) / reference.Range[i] - 1.0;
                        double y = ElectronData.YieldOf(path.Value, kev) / reference.Yield[i] - 1.0;
                        if (Math.Abs(r) > Math.Abs(wr)) { wr = r; ar = string.Format(CultureInfo.InvariantCulture, "{0} кэВ", kev); }
                        if (Math.Abs(y) > Math.Abs(wy)) { wy = y; ay = string.Format(CultureInfo.InvariantCulture, "{0} кэВ", kev); }
                    }

                    bool ok = Math.Abs(wr) < 0.001 && Math.Abs(wy) < 0.001;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0,-6} {1,-15} пробег {2:+0.000%;-0.000%} ({3,-9}) выход {4:+0.000%;-0.000%} ({5,-9}) — {6}",
                                        abbr, path.Key, wr, ar, wy, ay, ok ? "сошлось (0.1 %)" : "⛔ РАСХОЖДЕНИЕ"));
                    if (!ok)
                    {
                        fails++;
                    }
                }

                int k = Array.IndexOf(reference.Energy, 1.0);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "            на 1 МэВ: пробег {0:G4} г/см² [ESTAR {1:G4}], выход {2:G4} [ESTAR {3:G4}]",
                                    ElectronData.RangeOf(any, 1000.0), reference.Range[k],
                                    ElectronData.YieldOf(any, 1000.0), reference.Yield[k]));
            }

            Console.WriteLine();
            return fails;
        }

        // ---------------------------------------------------------------- 3

        sealed class Item
        {
            public string Label;
            public Dictionary<int, double> Fractions;
            public double Density;
        }

        static List<Item> Materials()
        {
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            Func<string, GeometryMaterialLibrary.Entry> lookup = n => seed.Find(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
            List<Item> items = new List<Item>();
            foreach (string abbr in new[]
                     {
                         "NaI", "CsI", "LaBr3", "CeBr3", "SrI2", "CdTe", "CZT", "GSO", "BGO", "HPGe",
                         "Al", "Fe", "Ti", "C", "PTFE", "MgO", "TiO2", "TiO2 paint", "Al2O3", "PET", "PE", "PP", "PS",
                         "H2O", "Air", "Glass", "SiO2", "CaCO3", "KCl", "Lu2O3", "ThO2",
                     })
            {
                GeometryMaterialLibrary.Entry e = seed.Find(x => x.Abbr == abbr);
                GeometryMaterial m = GeometryMaterialLibrary.Make(e, e.Density, lookup);
                items.Add(new Item { Label = abbr, Fractions = new Dictionary<int, double>(m.Fractions), Density = m.Density });
            }

            foreach (string name in new[] { "Copper", "Lead", "Tungsten", "Water vapor", "Activated charcoal" })
            {
                GeometryMaterialLibrary.Entry e = lookup(name);
                if (e == null)
                {
                    continue;
                }

                GeometryMaterial m = GeometryMaterialLibrary.Make(e, e.Density, lookup);
                items.Add(new Item { Label = name, Fractions = new Dictionary<int, double>(m.Fractions), Density = m.Density });
            }

            items.Add(new Item { Label = "GAGG", Fractions = Formula("Gd3 Al2 Ga3 O12"), Density = 6.63 });
            items.Add(new Item { Label = "LYSO", Fractions = Formula("Lu1.8 Y0.2 Si1 O5"), Density = 7.1 });
            items.Add(new Item
            {
                Label = "steel", Density = 7.9,
                Fractions = new Dictionary<int, double> { { 26, 0.715 }, { 24, 0.19 }, { 28, 0.095 } },
            });
            return items;
        }

        static int SectionAsymptote()
        {
            Console.WriteLine("3. АСИМПТОТА δ (Штернхеймер): δ(10 ГэВ, верх сетки) − [2 ln βγ − 2 ln(I/ħωp) − 1], все вещества сцены");
            int fails = 0;
            // На 1 ГэВ δ ещё не вышла на асимптоту и у самого NIST (раздел 1: остаток
            // CsI/NaI ~0.02); на 10 ГэВ остаток обязан быть на порядок меньше.
            double tolerance = 0.01;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   допуск {0:F3} на 10 ГэВ; столбец «1 ГэВ ост.» — для сверки с остатком NIST {1:F4} на 1 ГэВ", tolerance, asymptoteControl));
            Console.WriteLine("   вещество          ρ       I, эВ    ħωp, эВ  δ(10 ГэВ) асимпт.   остаток   1 ГэВ ост. δ(1 МэВ)");
            foreach (Item it in Materials())
            {
                double[] t, col, rad;
                double potential;
                EstarCalculator.Stopping(CompoundOf(it.Label, it.Fractions, it.Density, null), out t, out col, out rad, out potential);
                double zav = ZOverA(it.Fractions);
                int top = t.Length - 1;
                int mid = Array.FindIndex(t, x => Math.Abs(x - 1000.0) < 1e-9);
                int one = Array.FindIndex(t, x => Math.Abs(x - 1.0) < 1e-9);
                double d1 = DeltaFromCollision(t[top], col[top], potential, zav);
                double a1 = DeltaAsymptote(t[top], potential, it.Density, zav);
                double d2 = DeltaFromCollision(t[mid], col[mid], potential, zav);
                double a2 = DeltaAsymptote(t[mid], potential, it.Density, zav);
                double d3 = DeltaFromCollision(t[one], col[one], potential, zav);
                bool ok = Math.Abs(d1 - a1) < tolerance;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0,-17} {1,8:G4} {2,8:F2} {3,8:F3} {4,9:F4} {5,9:F4} {6,9:+0.0000;-0.0000} {7,9:+0.0000;-0.0000} {8,9:F4} {9}",
                                    it.Label, it.Density, potential, 28.81593 * Math.Sqrt(it.Density * zav),
                                    d1, a1, d1 - a1, d2 - a2, d3, ok ? "" : "⛔"));
                if (!ok)
                {
                    fails++;
                }

                if (it.Label == "Lead")
                {
                    // контроль: та же проверка с ρ×1.1 в ħωp обязана выйти за допуск
                    double wrong = DeltaAsymptote(t[top], potential, it.Density * 1.1, zav);
                    bool seen = Math.Abs(d1 - wrong) > tolerance;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     контроль: ρ×1.1 в ħωp у Pb даёт остаток {0:+0.0000;-0.0000} — {1}", d1 - wrong,
                                        seen ? "увидено" : "⛔ НЕ УВИДЕНО"));
                    if (!seen)
                    {
                        fails++;
                    }
                }
            }

            Console.WriteLine();
            return fails;
        }

        // ---------------------------------------------------------------- 4

        static int SectionPotential()
        {
            Console.WriteLine("4. ТАБЛИЧНОЕ I: вещество NIST STAR, опознанное по составу (|Δw| < 1e-3, первое по id),");
            Console.WriteLine("   против ближайшего по плотности из совпавших (в окне ×1.25, иначе первое по id);");
            Console.WriteLine("   цена — пробег CSDA при одном и другом I");
            int fails = 0;
            List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
            Func<string, GeometryMaterialLibrary.Entry> lookup = n => seed.Find(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase));
            // К засеву — вещество корпуса, которого в засеве нет: активированный уголь
            // сцены `G1S_mar1l_coal_046_p24` (чистый C, насыпная ρ 0.461).
            List<GeometryMaterialLibrary.Entry> cases = new List<GeometryMaterialLibrary.Entry>(seed);
            GeometryMaterialLibrary.Entry coal = new GeometryMaterialLibrary.Entry { Name = "Activated charcoal (корпус)", Density = 0.461 };
            coal.ElementFractions[6] = 1.0;
            cases.Add(coal);
            foreach (GeometryMaterialLibrary.Entry e in cases)
            {
                GeometryMaterial m = GeometryMaterialLibrary.Make(e, e.Density, lookup);
                if (m.Fractions.Count == 0)
                {
                    continue;
                }

                List<Star> same = new List<Star>();
                foreach (Star s in stars)
                {
                    if (s.Composition.Count != m.Fractions.Count)
                    {
                        continue;
                    }

                    bool eq = true;
                    foreach (KeyValuePair<int, double> p in s.Composition)
                    {
                        double mine;
                        if (!m.Fractions.TryGetValue(p.Key, out mine) || Math.Abs(p.Value - mine) >= 1e-3)
                        {
                            eq = false;
                            break;
                        }
                    }

                    if (eq)
                    {
                        same.Add(s);
                    }
                }

                if (same.Count < 2)
                {
                    continue;
                }

                double[] t, col, rad;
                double used;
                EstarCalculator.Stopping(CompoundOf(e.Name, m.Fractions, m.Density, null), out t, out col, out rad, out used);
                // Своё вещество: ближайшее по плотности В ОКНЕ ×1.25 (плотность пробы
                // бывает насыпной — уголь корпуса 0.461 ничьей не равен); вне окна —
                // первое по id, как и прежде.
                Star nearest = same[0];
                double window = Math.Log(1.25);
                foreach (Star s in same)
                {
                    double d = Math.Abs(Math.Log(s.Density / m.Density));
                    if (d < window)
                    {
                        window = d;
                        nearest = s;
                    }
                }

                StringBuilder names = new StringBuilder();
                foreach (Star s in same)
                {
                    names.Append(string.Format(CultureInfo.InvariantCulture, "{0}{1} {2} [I {3}, ρ {4:G4}]", names.Length > 0 ? "; " : "", s.Id, s.Name, s.PotentialEv, s.Density));
                }

                bool sameI = Math.Abs(used - nearest.PotentialEv) < 1e-9;
                string cost = "";
                if (!sameI)
                {
                    fails++;
                    ElectronData.Material now = Built(e.Name, m, null);
                    ElectronData.Material fixedI = Built(e.Name + " (I по плотности)", m, nearest.PotentialEv);
                    cost = string.Format(CultureInfo.InvariantCulture, " пробег 100 кэВ {0:+0.00%;-0.00%}, 1 МэВ {1:+0.00%;-0.00%}, выход 1 МэВ {2:+0.00%;-0.00%}",
                             ElectronData.RangeOf(fixedI, 100.0) / ElectronData.RangeOf(now, 100.0) - 1.0,
                             ElectronData.RangeOf(fixedI, 1000.0) / ElectronData.RangeOf(now, 1000.0) - 1.0,
                             ElectronData.YieldOf(fixedI, 1000.0) / ElectronData.YieldOf(now, 1000.0) - 1.0);
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0} {1,-28} ρ {2,-9:G4} I {3,6:F1} эВ; по плотности — {4}, {5:F1} эВ{6}",
                                    sameI ? " " : "⛔", e.Name, m.Density, used, nearest.Name, nearest.PotentialEv, cost));
                Console.WriteLine("       совпали по составу: " + names);
            }

            Console.WriteLine(fails == 0 ? "   — сошлось" : string.Format(CultureInfo.InvariantCulture, "   ⛔ веществ с I не своего вещества: {0}", fails));
            return fails;
        }

        /// <summary>Пробег и выход ESTAR по составу с заданным I (null — как считает приложение).</summary>
        static ElectronData.Material Built(string name, GeometryMaterial m, double? potential)
        {
            double[] grid = new double[55];
            ElectronData.Material reference = ElectronData.BuiltinByName("CsI");
            Array.Copy(reference.Energy, grid, 55);
            EstarCalculator.Result r = EstarCalculator.Compute(CompoundOf(name, m.Fractions, m.Density, potential), grid);
            return new ElectronData.Material { Name = name, Energy = grid, Range = r.RangeGCm2, Yield = r.Yield };
        }
    }
}
