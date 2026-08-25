using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Счёт ESTAR (ICRU 37) из базы: тормозная способность электрона, пробег
    /// CSDA и радиационный выход по составу вещества.
    ///
    /// Зачем это здесь. Поставка NIST возит ВХОДЫ — заселённости и энергии
    /// связи оболочек, радиационную тормозную по элементам, средние энергии
    /// возбуждения, — но не свой выход; `ESTAR.EXE` шестнадцатибитный и на
    /// 64-битной Windows не идёт. Раньше выход считался питоном
    /// (`tools/estar/estar.py`) один раз, а в <see cref="ElectronData"/>
    /// вписывались готовые числа. Теперь тот же алгоритм считается программой,
    /// а вшитая таблица осталась ЭТАЛОНОМ СВЕРКИ.
    ///
    /// Читаются четыре таблицы `matdb.sqlite`, до этого лежавшие без читателя
    /// (`N9`): `estar_shells`, `estar_radiative_stopping`,
    /// `estar_element_potential`, плюс `star_materials` /
    /// `star_material_composition` для табличного I. Пятая, `estar_collision_stopping`,
    /// — эталон самого NIST на CsI и NaI, и читается не отсюда, а поверкой.
    ///
    /// Алгоритм повторён по исходнику `ESTAR.f` (строки 106-302, 390-530,
    /// 596-695), а не по учебнику: мелочи вроде ALF последней оболочки видны
    /// только по коду.
    ///
    /// ⛔ ГРАНИЦА С `N4`. Этот класс умеет считать ЛЮБОЙ состав, но зовут его
    /// только на тринадцати вшитых (<see cref="ElectronData"/>). Пустить сюда
    /// произвольную оправу, стенку и пробу — это `N4`, физика, и она обесценит
    /// все посчитанные матрицы отклика. Здесь такого хода нет намеренно.
    /// </summary>
    public static class EstarCalculator
    {
        /// <summary>Масса покоя электрона, МэВ — `ESTAR.f`, DATA RMASS.</summary>
        const double RMASS = 0.510999906;

        /// <summary>Множитель формулы Бете — `ESTAR.f`, DATA COFF.</summary>
        const double COFF = 0.307072;

        // Сетка по Q для эффекта плотности — `ESTAR.f`.
        const double QBEG = 1.0e-04;
        const int NUMQ = 50;
        const int LMAX = 1101;

        /// <summary>Узлов Симпсона на интервал сетки при интегрировании.</summary>
        const int MGRD = 21;

        /// <summary>Вещество на входе: элементы, числа атомов в формуле, плотность.</summary>
        public sealed class Compound
        {
            public string Name;
            public int[] Z;
            public double[] Atoms;
            public double DensityGCm3;

            /// <summary>
            /// Средняя энергия возбуждения, эВ, если её задают снаружи. null —
            /// взять табличную из `star_materials`, а не нашлась — по Брэггу.
            /// </summary>
            public double? PotentialEv;
        }

        /// <summary>Что посчиталось: пробег, выход и использованное I.</summary>
        public sealed class Result
        {
            /// <summary>Кинетическая энергия, МэВ — та сетка, что просили.</summary>
            public double[] EnergyMev;

            /// <summary>Пробег CSDA, г/см².</summary>
            public double[] RangeGCm2;

            /// <summary>Доля энергии, ушедшая в тормозное излучение.</summary>
            public double[] Yield;

            /// <summary>Средняя энергия возбуждения, которой считали, эВ.</summary>
            public double PotentialEv;
        }

        /// <summary>
        /// Тормозная способность на СОБСТВЕННОЙ сетке ESTAR (113 точек из
        /// `estar_radiative_stopping`). Отдельным методом, потому что её
        /// сверяет поверка с эталоном NIST `estar_collision_stopping`.
        /// </summary>
        public static void Stopping(Compound compound, out double[] energyMev,
                                    out double[] collision, out double[] radiative,
                                    out double potentialEv)
        {
            Dictionary<int, double> fractions = MassFractions(compound);
            Dictionary<int, double> weights = AtomicWeights();

            double zav = 0.0;
            foreach (KeyValuePair<int, double> pair in fractions)
            {
                zav += pair.Value * pair.Key / weights[pair.Key];
            }

            potentialEv = compound.PotentialEv
                ?? TabulatedPotential(fractions)
                ?? BraggPotential(fractions, weights);
            double potl = Math.Log(potentialEv * 1.0e-06);

            double[] logYq, dd;
            double ycut;
            DensityEffect(compound.DensityGCm3, fractions, weights, zav, potentialEv,
                          out logYq, out dd, out ycut);

            energyMev = EnergyGrid();
            int n = energyMev.Length;
            radiative = new double[n];
            foreach (KeyValuePair<int, double> pair in fractions)
            {
                double[] rad = RadiativeStopping(pair.Key);
                for (int i = 0; i < n; i++)
                {
                    radiative[i] += pair.Value * rad[i];
                }
            }

            CubicSpline deltaSpline = new CubicSpline(logYq, dd);
            double yqFirst = Math.Exp(logYq[0]);
            collision = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = energyMev[i];
                double tau = t / RMASS;
                double y = tau * (tau + 2.0);
                double betq = y / ((tau + 1.0) * (tau + 1.0));
                double delta = (y >= yqFirst && y > ycut)
                    ? deltaSpline.Evaluate(Math.Log(y)) : 0.0;
                double spart = Math.Log(t) - potl + 0.5 * Math.Log(1.0 + 0.5 * tau)
                               - 0.5 * delta;
                double term = (1.0 - betq)
                              * (1.0 + tau * tau / 8.0
                                 - (2.0 * tau + 1.0) * Math.Log(2.0));
                collision[i] = COFF * zav * (spart + 0.5 * term) / betq;
            }
        }

        /// <summary>
        /// Пробег CSDA и радиационный выход на заданной сетке энергий (МэВ).
        /// </summary>
        public static Result Compute(Compound compound, double[] outGridMev)
        {
            double[] t, collision, radiative;
            double potential;
            Stopping(compound, out t, out collision, out radiative, out potential);

            double[] rg, yield;
            RangeAndYield(t, collision, radiative, out rg, out yield);

            int n = t.Length;
            double[] logT = new double[n];
            double[] logRange = new double[n];
            double[] logYield = new double[n];
            for (int i = 0; i < n; i++)
            {
                logT[i] = Math.Log(t[i]);
                logRange[i] = Math.Log(rg[i]);
                logYield[i] = Math.Log(yield[i]);
            }

            CubicSpline rangeSpline = new CubicSpline(logT, logRange);
            CubicSpline yieldSpline = new CubicSpline(logT, logYield);

            double[] outRange = new double[outGridMev.Length];
            double[] outYield = new double[outGridMev.Length];
            for (int i = 0; i < outGridMev.Length; i++)
            {
                double l = Math.Log(outGridMev[i]);
                outRange[i] = Math.Exp(rangeSpline.Evaluate(l));
                outYield[i] = Math.Exp(yieldSpline.Evaluate(l));
            }

            return new Result
            {
                EnergyMev = (double[])outGridMev.Clone(),
                RangeGCm2 = outRange,
                Yield = outYield,
                PotentialEv = potential,
            };
        }

        /// <summary>
        /// Пробег CSDA (г/см²) и выход тормозного — `ESTAR.f:596-695`.
        ///
        /// Интегрируется не по узлам сетки, а по сплайну ln S от ln T,
        /// Симпсоном по MGRD точкам внутри каждого интервала: сетка ESTAR
        /// редкая, и трапеции по её узлам дают у нижнего края проценты.
        ///
        /// Первый узел особый: ниже него формула Бете уже не работает, и ESTAR
        /// берёт для этого куска линейное приближение R = T/(2·S).
        /// </summary>
        static void RangeAndYield(double[] t, double[] collision, double[] radiative,
                                  out double[] range, out double[] yield)
        {
            int n = t.Length;
            double[] logT = new double[n];
            double[] logTotal = new double[n];
            double[] logRad = new double[n];
            for (int i = 0; i < n; i++)
            {
                double total = collision[i] + radiative[i];
                logT[i] = Math.Log(t[i]);
                logTotal[i] = Math.Log(total);
                logRad[i] = Math.Log(radiative[i]);
            }

            CubicSpline totalSpline = new CubicSpline(logT, logTotal);
            CubicSpline radSpline = new CubicSpline(logT, logRad);

            range = new double[n];
            double[] radiated = new double[n];
            double firstTotal = collision[0] + radiative[0];
            range[0] = 0.5 * t[0] / firstTotal;
            radiated[0] = 0.5 * t[0] * radiative[0] / firstTotal;

            double[] inv = new double[MGRD];
            double[] emitted = new double[MGRD];
            for (int i = 1; i < n; i++)
            {
                double lo = t[i - 1], hi = t[i];
                double step = (hi - lo) / (MGRD - 1);
                for (int j = 0; j < MGRD; j++)
                {
                    double point = Math.Log(hi - step * j);
                    inv[j] = Math.Exp(-totalSpline.Evaluate(point));
                    emitted[j] = Math.Exp(radSpline.Evaluate(point)) * inv[j];
                }

                range[i] = range[i - 1] + Simpson(inv, step / 3.0);
                radiated[i] = radiated[i - 1] + Simpson(emitted, step / 3.0);
            }

            yield = new double[n];
            for (int i = 0; i < n; i++)
            {
                yield[i] = radiated[i] / t[i];
            }
        }

        /// <summary>GRAL из `ESTAR.f` для нечётного числа узлов: множитель уже делён на 3.</summary>
        static double Simpson(double[] values, double thirdStep)
        {
            int m = values.Length;
            double sigma = values[0] + values[m - 1];
            for (int j = 1; j < m - 1; j += 2)
            {
                sigma += 4.0 * values[j];
            }

            for (int j = 2; j < m - 2; j += 2)
            {
                sigma += 2.0 * values[j];
            }

            return thirdStep * sigma;
        }

        /// <summary>
        /// Поправка на плотность по Штернхеймеру — `ESTAR.f:166-230, 390-530`.
        ///
        /// Осцилляторы строятся из заселённостей и энергий связи оболочек: у
        /// каждой оболочки своя сила f(n), а масштаб подбирается ньютоновской
        /// итерацией так, чтобы правило сумм сошлось с заданным I.
        /// </summary>
        static void DensityEffect(double density, Dictionary<int, double> fractions,
                                  Dictionary<int, double> weights, double zav,
                                  double potentialEv,
                                  out double[] logYq, out double[] dd, out double ycut)
        {
            double hom = 28.81593 * Math.Sqrt(density * zav);   // плазменная энергия, эВ
            double phil = 2.0 * Math.Log(potentialEv / hom);

            List<int> zs = new List<int>(fractions.Keys);
            zs.Sort();
            bool single = zs.Count == 1;

            List<double> fl = new List<double>();
            List<double> enl = new List<double>();
            foreach (int z in zs)
            {
                double g = fractions[z] * z / weights[z] / zav;
                int[] occ;
                double[] bind;
                Shells(z, out occ, out bind);
                occ = (int[])occ.Clone();
                bind = (double[])bind.Clone();

                // знак заселённости значащий: отрицательная метит проводящую оболочку
                int last = occ.Length - 1;
                if (occ[last] < 0)
                {
                    occ[last] = -occ[last];
                    if (single)
                    {
                        bind[last] = 0.0;
                    }
                }

                double nsum = 0.0;
                for (int k = 0; k < occ.Length; k++)
                {
                    nsum += occ[k];
                }

                for (int k = 0; k < occ.Length; k++)
                {
                    fl.Add(occ[k] * g / nsum);
                    enl.Add(bind[k]);
                }
            }

            double[] f = fl.ToArray();
            double[] en = enl.ToArray();
            int m = f.Length;
            double[] alf = new double[m];
            for (int k = 0; k < m; k++)
            {
                alf[k] = 2.0 / 3.0;
            }

            if (en[m - 1] <= 0.0)
            {
                alf[m - 1] = 1.0;
            }

            double[] eps = new double[m];
            for (int k = 0; k < m; k++)
            {
                double r = en[k] / hom;
                eps[k] = r * r;
            }

            double root = 1.0;
            for (int it = 0; it < 200; it++)
            {
                double fun = -phil;
                double der = 0.0;
                for (int k = 0; k < m; k++)
                {
                    double trm = root * eps[k] + alf[k] * f[k];
                    fun += f[k] * Math.Log(trm);
                    der += f[k] * eps[k] / trm;
                }

                double droot = fun / der;
                root -= droot;
                if (Math.Abs(droot) <= 1.0e-5)
                {
                    break;
                }
            }

            for (int k = 0; k < m; k++)
            {
                eps[k] *= root;
            }

            if (en[m - 1] <= 0.0)
            {
                ycut = 0.0;
            }
            else
            {
                double acc = 0.0;
                for (int k = 0; k < m; k++)
                {
                    acc += f[k] / eps[k];
                }

                ycut = 1.0 / acc;
            }

            logYq = new double[LMAX];
            dd = new double[LMAX];
            double[] shifted = new double[m];
            for (int k = 0; k < m; k++)
            {
                shifted[k] = eps[k] + alf[k] * f[k];
            }

            for (int l = 0; l < LMAX; l++)
            {
                double q = QBEG * Math.Pow(10.0, l / (double)NUMQ);
                double sum = 0.0;
                double d = 0.0;
                for (int k = 0; k < m; k++)
                {
                    sum += f[k] / (eps[k] + q);
                    d += f[k] * Math.Log(1.0 + q / shifted[k]);
                }

                double yq = 1.0 / sum;
                logYq[l] = Math.Log(yq);
                dd[l] = d - q / (yq + 1.0);
            }
        }

        /// <summary>Массовые доли по Z из формулы — как ATB у ESTAR.</summary>
        static Dictionary<int, double> MassFractions(Compound compound)
        {
            Dictionary<int, double> weights = AtomicWeights();
            Dictionary<int, double> atoms = new Dictionary<int, double>();
            for (int i = 0; i < compound.Z.Length; i++)
            {
                double have;
                atoms.TryGetValue(compound.Z[i], out have);
                atoms[compound.Z[i]] = have + compound.Atoms[i];
            }

            double total = 0.0;
            foreach (KeyValuePair<int, double> pair in atoms)
            {
                total += pair.Value * weights[pair.Key];
            }

            Dictionary<int, double> fractions = new Dictionary<int, double>();
            foreach (KeyValuePair<int, double> pair in atoms)
            {
                fractions[pair.Key] = pair.Value * weights[pair.Key] / total;
            }

            return fractions;
        }

        /// <summary>
        /// I готового вещества из `star_materials`, если состав с ним совпал.
        ///
        /// ESTAR берёт I ИЗ ТАБЛИЦЫ, когда вещество выбрано из списка, и
        /// считает по Брэггу, только когда состав ввели руками. Разница не
        /// всегда мелкая: у иодида цезия и иодида натрия правило Брэгга даёт
        /// табличное значение до сотых (553.10 и 452.01 против 553.1 и 452.0),
        /// а у германата висмута — 523.5 против табличных 534.1, и это уже
        /// 0.4 % в пробеге.
        ///
        /// ⚠ Порядок по `id` значащий: у воды состав совпадает у ДВУХ строк —
        /// жидкой (I = 75.0 эВ, id 276) и пара (71.6 эВ, id 277). Берётся
        /// первая, то есть жидкая.
        /// </summary>
        static double? TabulatedPotential(Dictionary<int, double> fractions)
        {
            LoadStarMaterials();
            foreach (StarMaterial material in starMaterials)
            {
                if (material.Composition.Count != fractions.Count)
                {
                    continue;
                }

                bool same = true;
                foreach (KeyValuePair<int, double> pair in material.Composition)
                {
                    double mine;
                    if (!fractions.TryGetValue(pair.Key, out mine)
                        || Math.Abs(pair.Value - mine) >= 1.0e-3)
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return material.PotentialEv;
                }
            }

            return null;
        }

        /// <summary>
        /// I смеси по правилу Брэгга — `ESTAR.f:714-734`.
        ///
        /// ln I = Σ wᵢ (Z/A)ᵢ ln Iᵢ / Σ wᵢ (Z/A)ᵢ, причём для элементов ТЯЖЕЛЕЕ
        /// неона ESTAR берёт не табличное I элемента, а 1.13·I: в соединении
        /// электроны связаны сильнее, чем в чистом веществе.
        /// </summary>
        static double BraggPotential(Dictionary<int, double> fractions,
                                     Dictionary<int, double> weights)
        {
            LoadElementPotentials();
            double zav = 0.0;
            double acc = 0.0;
            foreach (KeyValuePair<int, double> pair in fractions)
            {
                int z = pair.Key;
                double za = z / weights[z];
                double value = z < 10 ? elementPotential[z] : 1.13 * elementPotential[z];
                zav += pair.Value * za;
                acc += pair.Value * za * Math.Log(value);
            }

            return Math.Exp(acc / zav);
        }

        // --- чтение базы; всё грузится один раз и держится ---

        sealed class StarMaterial
        {
            public double PotentialEv;
            public Dictionary<int, double> Composition;
        }

        static readonly object Gate = new object();
        static Dictionary<int, double> atomicWeight;
        static Dictionary<int, double> elementPotential;
        static List<StarMaterial> starMaterials;
        static double[] energyGrid;
        static readonly Dictionary<int, int[]> shellOccupation = new Dictionary<int, int[]>();
        static readonly Dictionary<int, double[]> shellBinding = new Dictionary<int, double[]>();
        static readonly Dictionary<int, double[]> radiativeStopping = new Dictionary<int, double[]>();

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite");
        }

        static SqliteConnection Open()
        {
            string path = DatabasePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "matdb.sqlite не найдена рядом с программой: " + path, path);
            }

            SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;");
            connection.Open();
            return connection;
        }

        static Dictionary<int, double> AtomicWeights()
        {
            lock (Gate)
            {
                if (atomicWeight != null)
                {
                    return atomicWeight;
                }

                Dictionary<int, double> loaded = new Dictionary<int, double>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select z, atomic_weight from xcom_elements";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            loaded[reader.GetInt32(0)] = reader.GetDouble(1);
                        }
                    }
                }

                atomicWeight = loaded;
                return atomicWeight;
            }
        }

        static void LoadElementPotentials()
        {
            lock (Gate)
            {
                if (elementPotential != null)
                {
                    return;
                }

                Dictionary<int, double> loaded = new Dictionary<int, double>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select z, potential_ev from estar_element_potential";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            loaded[reader.GetInt32(0)] = reader.GetDouble(1);
                        }
                    }
                }

                elementPotential = loaded;
            }
        }

        static void LoadStarMaterials()
        {
            lock (Gate)
            {
                if (starMaterials != null)
                {
                    return;
                }

                Dictionary<int, StarMaterial> byId = new Dictionary<int, StarMaterial>();
                List<int> order = new List<int>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select id, potential_ev from star_materials order by id";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int id = reader.GetInt32(0);
                            byId[id] = new StarMaterial
                            {
                                PotentialEv = reader.GetDouble(1),
                                Composition = new Dictionary<int, double>(),
                            };
                            order.Add(id);
                        }
                    }

                    command.CommandText =
                        "select material_id, z, weight_fraction from star_material_composition";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            StarMaterial material;
                            if (byId.TryGetValue(reader.GetInt32(0), out material))
                            {
                                material.Composition[reader.GetInt32(1)] = reader.GetDouble(2);
                            }
                        }
                    }
                }

                List<StarMaterial> list = new List<StarMaterial>();
                foreach (int id in order)
                {
                    list.Add(byId[id]);
                }

                starMaterials = list;
            }
        }

        /// <summary>Сетка энергий ESTAR (113 точек, МэВ) — DATA ER в `ESTAR.f`.</summary>
        static double[] EnergyGrid()
        {
            lock (Gate)
            {
                if (energyGrid != null)
                {
                    return energyGrid;
                }

                List<double> grid = new List<double>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select distinct energy_mev from"
                        + " estar_radiative_stopping order by energy_mev";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            grid.Add(reader.GetDouble(0));
                        }
                    }
                }

                energyGrid = grid.ToArray();
                return energyGrid;
            }
        }

        static void Shells(int z, out int[] occupation, out double[] binding)
        {
            lock (Gate)
            {
                if (shellOccupation.TryGetValue(z, out occupation))
                {
                    binding = shellBinding[z];
                    return;
                }

                List<int> occ = new List<int>();
                List<double> bind = new List<double>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select occupation, binding_ev from estar_shells"
                        + " where z=" + z + " order by shell_index";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            occ.Add(reader.GetInt32(0));
                            bind.Add(reader.GetDouble(1));
                        }
                    }
                }

                if (occ.Count == 0)
                {
                    throw new InvalidOperationException(
                        "в estar_shells нет оболочек для Z=" + z);
                }

                occupation = occ.ToArray();
                binding = bind.ToArray();
                shellOccupation[z] = occupation;
                shellBinding[z] = binding;
            }
        }

        static double[] RadiativeStopping(int z)
        {
            lock (Gate)
            {
                double[] cached;
                if (radiativeStopping.TryGetValue(z, out cached))
                {
                    return cached;
                }

                List<double> values = new List<double>();
                using (SqliteConnection connection = Open())
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select stopping_mev_cm2_g from"
                        + " estar_radiative_stopping where z=" + z + " order by energy_mev";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            values.Add(reader.GetDouble(0));
                        }
                    }
                }

                double[] grid = EnergyGrid();
                if (values.Count != grid.Length)
                {
                    throw new InvalidOperationException(
                        "estar_radiative_stopping: у Z=" + z + " " + values.Count
                        + " точек против " + grid.Length + " в сетке");
                }

                cached = values.ToArray();
                radiativeStopping[z] = cached;
                return cached;
            }
        }

        /// <summary>
        /// Кубический сплайн с краевым условием «не узел» — тот же, что у
        /// `scipy.interpolate.CubicSpline` по умолчанию. В BCL сплайна нет, а
        /// заменить его линейной интерполяцией нельзя: у ESTAR сетка редкая,
        /// и интеграл по ней собирается именно по сплайну.
        ///
        /// Система на первые производные трёхдиагональная; решается прогонкой
        /// с выбором главного элемента (LAPACK dgtsv), потому что первая и
        /// последняя строки условием «не узел» диагонального преобладания
        /// лишены.
        /// </summary>
        public sealed class CubicSpline
        {
            readonly double[] x;
            readonly double[] c0, c1, c2, c3;

            public CubicSpline(double[] xs, double[] ys)
            {
                int n = xs.Length;
                if (n != ys.Length)
                {
                    throw new ArgumentException("длины узлов и значений разошлись");
                }

                if (n < 4)
                {
                    throw new ArgumentException(
                        "краевому условию «не узел» нужно не меньше четырёх узлов");
                }

                this.x = (double[])xs.Clone();

                double[] dx = new double[n - 1];
                double[] slope = new double[n - 1];
                for (int i = 0; i < n - 1; i++)
                {
                    dx[i] = xs[i + 1] - xs[i];
                    slope[i] = (ys[i + 1] - ys[i]) / dx[i];
                }

                double[] diag = new double[n];
                double[] upper = new double[n - 1];    // элемент (i, i+1)
                double[] lower = new double[n - 1];    // элемент (i+1, i)
                double[] rhs = new double[n];

                for (int i = 1; i < n - 1; i++)
                {
                    diag[i] = 2.0 * (dx[i - 1] + dx[i]);
                    upper[i] = dx[i - 1];
                    lower[i - 1] = dx[i];
                    rhs[i] = 3.0 * (dx[i] * slope[i - 1] + dx[i - 1] * slope[i]);
                }

                double dl = xs[2] - xs[0];
                diag[0] = dx[1];
                upper[0] = dl;
                rhs[0] = ((dx[0] + 2.0 * dl) * dx[1] * slope[0]
                          + dx[0] * dx[0] * slope[1]) / dl;

                double dr = xs[n - 1] - xs[n - 3];
                diag[n - 1] = dx[n - 3];
                lower[n - 2] = dr;
                rhs[n - 1] = (dx[n - 2] * dx[n - 2] * slope[n - 3]
                              + (2.0 * dr + dx[n - 2]) * dx[n - 3] * slope[n - 2]) / dr;

                double[] s = SolveTridiagonal(lower, diag, upper, rhs);

                this.c0 = new double[n - 1];
                this.c1 = new double[n - 1];
                this.c2 = new double[n - 1];
                this.c3 = new double[n - 1];
                for (int i = 0; i < n - 1; i++)
                {
                    double tt = (s[i] + s[i + 1] - 2.0 * slope[i]) / dx[i];
                    this.c0[i] = tt / dx[i];
                    this.c1[i] = (slope[i] - s[i]) / dx[i] - tt;
                    this.c2[i] = s[i];
                    this.c3[i] = ys[i];
                }
            }

            /// <summary>Значение сплайна; вне сетки — продолжение крайним куском.</summary>
            public double Evaluate(double v)
            {
                int n = this.x.Length;
                int i;
                if (v <= this.x[0])
                {
                    i = 0;
                }
                else if (v >= this.x[n - 1])
                {
                    i = n - 2;
                }
                else
                {
                    int lo = 0, hi = n - 1;
                    while (hi - lo > 1)
                    {
                        int mid = (lo + hi) >> 1;
                        if (this.x[mid] <= v)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }

                    i = lo;
                }

                double d = v - this.x[i];
                return ((this.c0[i] * d + this.c1[i]) * d + this.c2[i]) * d + this.c3[i];
            }

            /// <summary>
            /// Прогонка с выбором главного элемента — LAPACK `dgtsv`. Выбор
            /// порождает вторую наддиагональ (`du2`), поэтому она заведена явно.
            /// </summary>
            static double[] SolveTridiagonal(double[] lower, double[] diag,
                                             double[] upper, double[] rhs)
            {
                int n = diag.Length;
                double[] d = (double[])diag.Clone();
                double[] du = (double[])upper.Clone();
                double[] dlv = (double[])lower.Clone();
                double[] b = (double[])rhs.Clone();
                double[] du2 = new double[Math.Max(n - 2, 0)];

                for (int i = 0; i < n - 1; i++)
                {
                    if (Math.Abs(d[i]) >= Math.Abs(dlv[i]))
                    {
                        if (d[i] == 0.0)
                        {
                            throw new InvalidOperationException("вырожденная система сплайна");
                        }

                        double mult = dlv[i] / d[i];
                        d[i + 1] -= mult * du[i];
                        b[i + 1] -= mult * b[i];
                        if (i < n - 2)
                        {
                            du2[i] = 0.0;
                        }
                    }
                    else
                    {
                        double mult = d[i] / dlv[i];
                        d[i] = dlv[i];
                        double temp = d[i + 1];
                        d[i + 1] = du[i] - mult * temp;
                        if (i < n - 2)
                        {
                            du2[i] = du[i + 1];
                            du[i + 1] = -mult * du[i + 1];
                        }

                        du[i] = temp;
                        temp = b[i];
                        b[i] = b[i + 1];
                        b[i + 1] = temp - mult * b[i + 1];
                    }
                }

                if (d[n - 1] == 0.0)
                {
                    throw new InvalidOperationException("вырожденная система сплайна");
                }

                b[n - 1] /= d[n - 1];
                if (n > 1)
                {
                    b[n - 2] = (b[n - 2] - du[n - 2] * b[n - 1]) / d[n - 2];
                }

                for (int i = n - 3; i >= 0; i--)
                {
                    b[i] = (b[i] - du[i] * b[i + 1] - du2[i] * b[i + 2]) / d[i];
                }

                return b;
            }
        }
    }
}
