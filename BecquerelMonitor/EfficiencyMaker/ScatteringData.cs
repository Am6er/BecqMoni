using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Угловые данные рассеяния из `nucdb.sqlite`: функция некогерентного
    /// рассеяния S(x,Z), атомный форм-фактор F(x,Z) и профили Комптона по
    /// оболочкам (`database/scheme.md`, §5б).
    ///
    /// ЗАЧЕМ. Полные сечения XCOM связанность электрона уже учитывают —
    /// а РАСПРЕДЕЛЕНИЕ по углу до сих пор считалось по голому Клейну — Нишине,
    /// то есть как на свободном покоящемся электроне. Три следствия, каждое
    /// видно в отклике:
    ///
    /// * малые углы завышены. Рассеяние на связанном электроне подавлено там,
    ///   где переданный импульс меньше импульса связи: S(x,Z) → 0 при x → 0.
    ///   Голая формула гонит кванты вперёд с почти неизменной энергией, и
    ///   континуум сразу под пиком выходит выше, чем на самом деле;
    /// * когерентное рассеяние вовсе не меняло направления — оно либо молча
    ///   числилось поглощением, либо проходило насквозь
    ///   (<see cref="EfficiencySimulator.CoherentPassesThrough"/>). А оно
    ///   отклоняет квант, не трогая энергию, — это лишний шанс попасть в
    ///   кристалл и лишний шанс из него уйти;
    /// * комптоновский край и пик обратного рассеяния выходили бесконечно
    ///   резкими. У связанного электрона есть импульс, и рассеянная энергия
    ///   размывается на его проекцию (доплеровское размытие).
    ///
    /// ЧТО ЭТО НЕ МЕНЯЕТ. Полные сечения остаются XCOM: S(x,Z) входит только
    /// множителем отбора в розыгрыш угла, F(x,Z) — только формой углового
    /// распределения уже разыгранного когерентного канала. Иначе связанность
    /// учлась бы дважды.
    ///
    /// Единица аргумента x — обратные сантиметры: x = (E/hc)·sin(θ/2), как в
    /// `G4LivermorePolarizedRayleighModel`. Профили Биггса — в атомных единицах
    /// импульса, каждая оболочка нормирована на единицу: 2∫₀^∞ J dp = 1
    /// (проверено на иоде, худшая оболочка 1.024).
    /// </summary>
    public static class ScatteringData
    {
        /// <summary>x = <see cref="InverseCmPerKev"/>·E[кэВ]·sin(θ/2), см⁻¹.</summary>
        public const double InverseCmPerKev = 8.065543937e6;

        /// <summary>Постоянная тонкой структуры: p[m_e c] = α·p[а.е.].</summary>
        public const double FineStructure = 7.2973525693e-3;

        /// <summary>Сетка импульсов профилей Комптона, атомные единицы.</summary>
        static double[] momentumGrid;

        static readonly object Gate = new object();
        static readonly Dictionary<int, Atom> cache = new Dictionary<int, Atom>();

        /// <summary>
        /// Угловые данные одного элемента; null, если в базе их нет (тогда
        /// вызывающий обязан откатиться на голого Клейна — Нишину, а не
        /// придумывать замену).
        /// </summary>
        public static Atom Of(int z)
        {
            lock (Gate)
            {
                Atom cached;
                if (cache.TryGetValue(z, out cached))
                {
                    return cached;
                }

                Atom atom = Load(z);
                cache[z] = atom;
                return atom;
            }
        }

        /// <summary>Угловые данные одного элемента.</summary>
        public sealed class Atom
        {
            public int Z;

            // S(x,Z): x по возрастанию, S от 0 до Z.
            internal double[] sfX;
            internal double[] sfV;

            // F(x,Z), уложенный под розыгрыш угла когерентного рассеяния:
            // сетка в t = x², значения F², и кумулятивный ∫F² dt по узлам.
            internal double[] ffT;
            internal double[] ffF2;
            internal double[] ffCum;

            // Оболочки профилей Комптона.
            internal double[] shellCum;        // накопленная заселённость / Z
            internal double[] shellBindKev;
            internal double[][] profCum;       // на оболочку: ∫₀^p J dp, нормирован

            /// <summary>Число оболочек с профилем; 0 — профилей нет.</summary>
            public int ShellCount
            {
                get { return this.shellCum == null ? 0 : this.shellCum.Length; }
            }

            /// <summary>
            /// Функция некогерентного рассеяния S(x,Z) — множитель отбора к
            /// Клейну — Нишине: dσ/dΩ = KN(θ)·S(x,Z), S(0)=0, S(∞)=Z.
            /// Интерполяция лог-лог, как у сечений.
            /// </summary>
            public double ScatteringFunction(double xPerCm)
            {
                return LogLog(this.sfX, this.sfV, xPerCm);
            }

            /// <summary>Атомный форм-фактор F(x,Z); F(0)=Z.</summary>
            public double FormFactor(double xPerCm)
            {
                double t = xPerCm * xPerCm;
                int i = Segment(this.ffT, t);
                if (i < 0)
                {
                    return Math.Sqrt(this.ffF2[0]);
                }

                double f2 = this.ffF2[i]
                            + (this.ffF2[i + 1] - this.ffF2[i])
                              * (t - this.ffT[i]) / (this.ffT[i + 1] - this.ffT[i]);
                return Math.Sqrt(Math.Max(0.0, f2));
            }

            /// <summary>
            /// Розыгрыш квадрата переданного импульса t = x² по F²(x) на
            /// отрезке [0, <paramref name="tMax"/>], где tMax = (E/hc)² — предел
            /// при рассеянии назад. Внутри узла F² считается линейной по t: так
            /// же, как эта сетка и строилась.
            ///
            /// Угол берётся из t: cos θ = 1 − 2·t/tMax. Оставшийся множитель
            /// (1+cos²θ)/2 вызывающий доигрывает отбором — он ограничен
            /// единицей, и отбор дёшев.
            /// </summary>
            public double SampleMomentumTransferSq(double u, double tMax)
            {
                double[] t = this.ffT;
                double[] f2 = this.ffF2;
                double[] cum = this.ffCum;
                int n = t.Length;
                if (!(tMax > 0.0))
                {
                    return 0.0;
                }

                if (tMax >= t[n - 1])
                {
                    tMax = t[n - 1];
                }

                int last = Segment(t, tMax);
                if (last < 0)
                {
                    return u * tMax;
                }

                double head = PartialIntegral(t, f2, last, tMax);
                double total = cum[last] + head;
                if (!(total > 0.0))
                {
                    return u * tMax;
                }

                double target = u * total;
                int i = 0, hi = last;
                while (hi - i > 0)
                {
                    int mid = (i + hi + 1) / 2;
                    if (cum[mid] <= target)
                    {
                        i = mid;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                double rest = target - cum[i];
                double t0 = t[i];
                double t1 = i + 1 < n ? t[i + 1] : tMax;
                if (i == last)
                {
                    t1 = tMax;
                }

                double a = f2[i];
                double b = i + 1 < n ? f2[i + 1] : f2[i];
                double dt = t1 - t0;
                if (!(dt > 0.0))
                {
                    return t0;
                }

                // ∫ от t0 до t: a·Δ + (b−a)/dt·Δ²/2 = rest
                double slope = (b - a) / dt;
                double delta;
                if (Math.Abs(slope) < 1e-300)
                {
                    delta = a > 0.0 ? rest / a : 0.0;
                }
                else
                {
                    double disc = a * a + 2.0 * slope * rest;
                    delta = disc > 0.0 ? (Math.Sqrt(disc) - a) / slope : 0.0;
                }

                if (delta < 0.0) delta = 0.0;
                if (delta > dt) delta = dt;
                return t0 + delta;
            }

            /// <summary>Оболочка по равномерному числу: вероятность ∝ заселённости.</summary>
            public int SelectShell(double u)
            {
                double[] c = this.shellCum;
                for (int i = 0; i < c.Length; i++)
                {
                    if (u <= c[i])
                    {
                        return i;
                    }
                }

                return c.Length - 1;
            }

            /// <summary>Энергия связи оболочки, кэВ.</summary>
            public double ShellBindingKev(int shell)
            {
                return this.shellBindKev[shell];
            }

            /// <summary>
            /// Проекция импульса электрона оболочки на направление передачи,
            /// атомные единицы, по профилю Биггса. Возвращается модуль; знак
            /// разыгрывает вызывающий — профиль симметричен.
            /// </summary>
            public double SampleMomentumAu(int shell, double u)
            {
                double[] cum = this.profCum[shell];
                double[] p = momentumGrid;
                int n = cum.Length;
                if (u >= cum[n - 1])
                {
                    return p[n - 1];
                }

                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (cum[mid] <= u)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                double c0 = cum[lo], c1 = cum[hi];
                double f = c1 > c0 ? (u - c0) / (c1 - c0) : 0.0;
                return p[lo] + f * (p[hi] - p[lo]);
            }
        }

        // ------------------------------------------------------------------
        // Интерполяция и вспомогательное
        // ------------------------------------------------------------------

        /// <summary>Номер узла слева от x; −1 — левее сетки.</summary>
        static int Segment(double[] grid, double x)
        {
            int n = grid.Length;
            if (!(x > grid[0]))
            {
                return -1;
            }

            if (x >= grid[n - 1])
            {
                return n - 2;
            }

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (grid[mid] <= x)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>∫ F² dt от узла i до t, F² линейна внутри узла.</summary>
        static double PartialIntegral(double[] t, double[] f2, int i, double x)
        {
            double dt = t[i + 1] - t[i];
            if (!(dt > 0.0))
            {
                return 0.0;
            }

            double delta = x - t[i];
            if (delta <= 0.0)
            {
                return 0.0;
            }

            if (delta > dt)
            {
                delta = dt;
            }

            double slope = (f2[i + 1] - f2[i]) / dt;
            return f2[i] * delta + 0.5 * slope * delta * delta;
        }

        /// <summary>
        /// Лог-лог интерполяция по сетке; за краями — крайние значения.
        /// Нулевые узлы (S(0)=0) обходятся линейно — логарифм там брать не от
        /// чего.
        /// </summary>
        static double LogLog(double[] grid, double[] values, double x)
        {
            int i = Segment(grid, x);
            if (i < 0)
            {
                return values[0];
            }

            double x0 = grid[i], x1 = grid[i + 1];
            double y0 = values[i], y1 = values[i + 1];
            if (x >= grid[grid.Length - 1])
            {
                return values[values.Length - 1];
            }

            if (!(x0 > 0.0) || !(y0 > 0.0) || !(y1 > 0.0))
            {
                double fl = (x - x0) / (x1 - x0);
                return y0 + fl * (y1 - y0);
            }

            double f = (Math.Log(x) - Math.Log(x0)) / (Math.Log(x1) - Math.Log(x0));
            return Math.Exp(Math.Log(y0) + f * (Math.Log(y1) - Math.Log(y0)));
        }

        // ------------------------------------------------------------------
        // Чтение базы
        // ------------------------------------------------------------------

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static Atom Load(int z)
        {
            string path = DatabasePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "nucdb.sqlite не найдена рядом с программой: " + path, path);
            }

            Atom atom = new Atom { Z = z };
            using (SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
            {
                connection.Open();
                LoadMomentumGrid(connection);

                List<double> xs = new List<double>();
                List<double> vs = new List<double>();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select x_percm, sf from epdl_scattering_function where z=" + z
                        + " order by x_percm";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            xs.Add(reader.GetDouble(0));
                            vs.Add(reader.GetDouble(1));
                        }
                    }
                }

                if (xs.Count < 2)
                {
                    return null;
                }

                atom.sfX = xs.ToArray();
                atom.sfV = vs.ToArray();

                xs.Clear();
                vs.Clear();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select x_percm, ff from epdl_form_factor where z=" + z
                        + " order by x_percm";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            xs.Add(reader.GetDouble(0));
                            vs.Add(reader.GetDouble(1));
                        }
                    }
                }

                if (xs.Count < 2)
                {
                    return null;
                }

                int n = xs.Count;
                atom.ffT = new double[n];
                atom.ffF2 = new double[n];
                atom.ffCum = new double[n];
                for (int i = 0; i < n; i++)
                {
                    atom.ffT[i] = xs[i] * xs[i];
                    atom.ffF2[i] = vs[i] * vs[i];
                }

                for (int i = 1; i < n; i++)
                {
                    atom.ffCum[i] = atom.ffCum[i - 1]
                        + 0.5 * (atom.ffF2[i - 1] + atom.ffF2[i])
                              * (atom.ffT[i] - atom.ffT[i - 1]);
                }

                LoadProfiles(connection, z, atom);
            }

            return atom;
        }

        static void LoadMomentumGrid(SqliteConnection connection)
        {
            if (momentumGrid != null)
            {
                return;
            }

            List<double> p = new List<double>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "select p_au from compton_profile_momentum order by p_idx";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        p.Add(reader.GetDouble(0));
                    }
                }
            }

            momentumGrid = p.ToArray();
        }

        /// <summary>
        /// Профили Комптона по оболочкам. Заселённости в сумме дают Z точно
        /// (проверено на всех элементах при импорте), поэтому кумулятивная
        /// таблица оболочек и есть распределение «на каком электроне
        /// рассеялись».
        /// </summary>
        static void LoadProfiles(SqliteConnection connection, int z, Atom atom)
        {
            List<double> occupancy = new List<double>();
            List<double> binding = new List<double>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select occupancy, potential_ev from compton_profile_shell where z=" + z
                    + " order by shell_seq";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        occupancy.Add(reader.GetDouble(0));
                        binding.Add(reader.GetDouble(1) / 1000.0);
                    }
                }
            }

            if (occupancy.Count == 0 || momentumGrid == null || momentumGrid.Length < 2)
            {
                return;
            }

            int shells = occupancy.Count;
            double[][] j = new double[shells][];
            for (int s = 0; s < shells; s++)
            {
                j[s] = new double[momentumGrid.Length];
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select shell_seq, p_idx, j_au from compton_profile where z=" + z;
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int s = reader.GetInt32(0);
                        int i = reader.GetInt32(1);
                        if (s >= 0 && s < shells && i >= 0 && i < momentumGrid.Length)
                        {
                            j[s][i] = reader.GetDouble(2);
                        }
                    }
                }
            }

            double sum = 0.0;
            for (int s = 0; s < shells; s++)
            {
                sum += occupancy[s];
            }

            if (!(sum > 0.0))
            {
                return;
            }

            atom.shellCum = new double[shells];
            atom.shellBindKev = new double[shells];
            atom.profCum = new double[shells][];
            double running = 0.0;
            for (int s = 0; s < shells; s++)
            {
                running += occupancy[s] / sum;
                atom.shellCum[s] = running;
                atom.shellBindKev[s] = binding[s];

                double[] cum = new double[momentumGrid.Length];
                for (int i = 1; i < momentumGrid.Length; i++)
                {
                    cum[i] = cum[i - 1]
                        + 0.5 * (j[s][i - 1] + j[s][i]) * (momentumGrid[i] - momentumGrid[i - 1]);
                }

                double top = cum[cum.Length - 1];
                if (top > 0.0)
                {
                    for (int i = 0; i < cum.Length; i++)
                    {
                        cum[i] /= top;
                    }
                }

                atom.profCum[s] = cum;
            }

            atom.shellCum[shells - 1] = 1.0;
        }
    }
}
