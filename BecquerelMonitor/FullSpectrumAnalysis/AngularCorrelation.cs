using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Угловая корреляция двух гамма одного каскада (TODO N5).
    ///
    /// ЗАЧЕМ. Каскадное суммирование считает вероятность зарегистрировать оба
    /// кванта произведением эффективностей — то есть предполагает, что второй
    /// квант летит куда попало независимо от первого. Это неверно: направление
    /// второго связано с направлением первого через спин промежуточного
    /// уровня, и связь тем сильнее, чем выше его спин. У ЛСРМ это учтено
    /// (`GammaGammaCorr`), и их же числа показывают цену: Cs-134, линия
    /// 1365.2 — CF 0.807 с корреляциями против 0.854 без, то есть почти 6 %.
    ///
    /// ЧТО СЧИТАЕТСЯ. Классическое разложение по полиномам Лежандра:
    ///
    ///     W(θ) = 1 + A₂₂·P₂(cos θ) + A₄₄·P₄(cos θ),   A_kk = A_k(1)·A_k(2)
    ///
    /// где для каждого перехода
    ///
    ///     A_k = [F_k(L,L,j',j) + 2δ·F_k(L,L',j',j) + δ²·F_k(L',L',j',j)] / (1+δ²)
    ///
    /// j — спин ПРОМЕЖУТОЧНОГО уровня (он общий у обоих переходов), j' — спин
    /// другого конца перехода, δ — коэффициент смешивания. Коэффициент
    ///
    ///     F_k(L,L',j',j) = (−1)^(j'−j−1)·√((2L+1)(2L'+1)(2j+1)(2k+1))
    ///                      · (L L' k; 1 −1 0) · {L L' k; j j j'}
    ///
    /// — три-джей и шесть-джей символы Вигнера, считаются здесь же.
    ///
    /// ОТКУДА ДАННЫЕ. Спины уровней и мультипольности с коэффициентами
    /// смешивания — таблицы `g4_level` и `g4_gamma` (схемы уровней Geant4,
    /// `database/scheme.md`, §5г). До 08.08.2026 их не было: в `ensdf_gammas`
    /// мультипольность стояла у трети переходов.
    ///
    /// ЧЕГО ЭТО ЕЩЁ НЕ ДЕЛАЕТ. Здесь только ЯДЕРНАЯ половина — сама функция
    /// W(θ). Чтобы она вошла в CF, её надо усреднить по телесному углу
    /// детектора (коэффициенты ослабления Q_k), а это уже геометрия, которой
    /// у суммирователя нет (та же преграда, что у S20). Строка в TODO.
    /// </summary>
    public sealed class AngularCorrelation
    {
        /// <summary>Коэффициенты разложения; A₀₀ ≡ 1 и не хранится.</summary>
        public sealed class Coefficients
        {
            public double A22;
            public double A44;

            /// <summary>Изотропна ли корреляция (оба коэффициента нулевые).</summary>
            public bool IsIsotropic
            {
                get { return Math.Abs(this.A22) < 1e-12 && Math.Abs(this.A44) < 1e-12; }
            }

            /// <summary>W(θ) — плотность вероятности угла между квантами, ⟨W⟩ = 1.</summary>
            public double At(double cosTheta)
            {
                double c2 = cosTheta * cosTheta;
                double p2 = 0.5 * (3.0 * c2 - 1.0);
                double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
                return 1.0 + this.A22 * p2 + this.A44 * p4;
            }

            /// <summary>Наибольшее значение W на отрезке — для отбора при розыгрыше.</summary>
            public double Maximum()
            {
                // Полином четвёртой степени по cos θ: хватает грубой сетки,
                // точный максимум здесь не нужен — нужна верхняя граница.
                double best = 0.0;
                for (int i = 0; i <= 200; i++)
                {
                    double value = this.At(-1.0 + 2.0 * i / 200.0);
                    if (value > best)
                    {
                        best = value;
                    }
                }

                return best * 1.001;
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                                     "A22 = {0:F4}, A44 = {1:F4}", this.A22, this.A44);
            }
        }

        /// <summary>Изотропная корреляция — когда данных не хватило.</summary>
        public static readonly Coefficients Isotropic = new Coefficients();

        // ------------------------------------------------------------------
        // Ядерная часть
        // ------------------------------------------------------------------

        /// <summary>
        /// Коэффициенты каскада j1 → j → j2. Спины передаются как ЕСТЬ (могут
        /// быть полуцелыми), мультипольности — кодом Geant4, δ — коэффициенты
        /// смешивания соответствующих переходов.
        ///
        /// Порядок спинов важен: <paramref name="jStart"/> — уровень, С
        /// которого идёт первый квант, <paramref name="jMiddle"/> — общий
        /// промежуточный, <paramref name="jEnd"/> — куда приходит второй.
        /// </summary>
        public static Coefficients For(double jStart, double jMiddle, double jEnd,
                                       int multipolarity1, double mixing1,
                                       int multipolarity2, double mixing2)
        {
            int l1, l1Prime, l2, l2Prime;
            if (!Multipoles(multipolarity1, out l1, out l1Prime)
                || !Multipoles(multipolarity2, out l2, out l2Prime))
            {
                return Isotropic;
            }

            // Смешивание имеет смысл только у смешанного перехода: у чистого
            // второй мультиполь не существует, и ненулевая δ из базы к нему
            // не относится.
            if (l1Prime == l1) mixing1 = 0.0;
            if (l2Prime == l2) mixing2 = 0.0;

            Coefficients result = new Coefficients();
            result.A22 = Ak(2, l1, l1Prime, mixing1, jStart, jMiddle)
                         * Ak(2, l2, l2Prime, mixing2, jEnd, jMiddle);
            result.A44 = Ak(4, l1, l1Prime, mixing1, jStart, jMiddle)
                         * Ak(4, l2, l2Prime, mixing2, jEnd, jMiddle);
            return result;
        }

        /// <summary>
        /// Порядки мультиполей из кода Geant4: 1…7 = E0,E1,M1,E2,M2,E3,M3,
        /// смесь — 100·Nx+Ny. false — код неизвестен или это E0 (монополь
        /// гамма-квантом не излучается вовсе).
        /// </summary>
        static bool Multipoles(int code, out int l, out int lPrime)
        {
            l = 0;
            lPrime = 0;
            if (code <= 0)
            {
                return false;
            }

            int first = code >= 100 ? code / 100 : code;
            int second = code >= 100 ? code % 100 : first;
            l = OrderOf(first);
            lPrime = OrderOf(second);
            return l > 0 && lPrime > 0;
        }

        /// <summary>Порядок мультиполя по коду: E1 и M1 → 1, E2 и M2 → 2, …</summary>
        static int OrderOf(int code)
        {
            switch (code)
            {
                case 2: return 1;    // E1
                case 3: return 1;    // M1
                case 4: return 2;    // E2
                case 5: return 2;    // M2
                case 6: return 3;    // E3
                case 7: return 3;    // M3
                default: return 0;   // E0 (кодом 1) и всё незнакомое
            }
        }

        /// <summary>A_k одного перехода со смешиванием.</summary>
        static double Ak(int k, int l, int lPrime, double delta, double jOther, double jMiddle)
        {
            double pure = F(k, l, l, jOther, jMiddle);
            if (Math.Abs(delta) < 1e-12 && l == lPrime)
            {
                return pure;
            }

            double cross = F(k, l, lPrime, jOther, jMiddle);
            double high = F(k, lPrime, lPrime, jOther, jMiddle);
            return (pure + 2.0 * delta * cross + delta * delta * high)
                   / (1.0 + delta * delta);
        }

        /// <summary>
        /// F_k(L, L', j', j) — коэффициент Ферентца — Розенцвейга. j —
        /// промежуточный уровень (он входит в шесть-джей дважды).
        /// </summary>
        public static double F(int k, int l, int lPrime, double jOther, double jMiddle)
        {
            if (k == 0)
            {
                return l == lPrime ? 1.0 : 0.0;
            }

            double three = ThreeJ(2 * l, 2 * lPrime, 2 * k, 2, -2, 0);
            if (Math.Abs(three) < 1e-300)
            {
                return 0.0;
            }

            double six = SixJ(2 * l, 2 * lPrime, 2 * k,
                              Twice(jMiddle), Twice(jMiddle), Twice(jOther));
            if (Math.Abs(six) < 1e-300)
            {
                return 0.0;
            }

            double sign = IsOdd(Twice(jOther) - Twice(jMiddle) - 2) ? -1.0 : 1.0;
            double norm = Math.Sqrt((2.0 * l + 1.0) * (2.0 * lPrime + 1.0)
                                    * (2.0 * jMiddle + 1.0) * (2.0 * k + 1.0));
            return sign * norm * three * six;
        }

        static int Twice(double j)
        {
            return (int)Math.Round(2.0 * j);
        }

        /// <summary>Нечётен ли (2j)/2 — знак (−1)^j для целого j из удвоенного.</summary>
        static bool IsOdd(int twiceJ)
        {
            int j = twiceJ / 2;
            return (twiceJ % 2 == 0) && (j % 2 != 0);
        }

        // ------------------------------------------------------------------
        // Символы Вигнера. Всё в УДВОЕННЫХ моментах: полуцелые спины иначе
        // теряются на сравнениях с нулём.
        // ------------------------------------------------------------------

        static readonly double[] LogFactorial = BuildLogFactorial(256);

        static double[] BuildLogFactorial(int n)
        {
            double[] table = new double[n];
            double sum = 0.0;
            table[0] = 0.0;
            for (int i = 1; i < n; i++)
            {
                sum += Math.Log(i);
                table[i] = sum;
            }

            return table;
        }

        static double LogFact(int n)
        {
            return n < 0 || n >= LogFactorial.Length ? double.NaN : LogFactorial[n];
        }

        /// <summary>Треугольный множитель Δ(a,b,c) в логарифме; NaN — треугольник не складывается.</summary>
        static double LogDelta(int a2, int b2, int c2)
        {
            int p = (a2 + b2 - c2) / 2;
            int q = (a2 - b2 + c2) / 2;
            int r = (-a2 + b2 + c2) / 2;
            int s = (a2 + b2 + c2) / 2 + 1;
            if (p < 0 || q < 0 || r < 0 || (a2 + b2 + c2) % 2 != 0)
            {
                return double.NaN;
            }

            return 0.5 * (LogFact(p) + LogFact(q) + LogFact(r) - LogFact(s));
        }

        /// <summary>Три-джей символ Вигнера; аргументы удвоены.</summary>
        public static double ThreeJ(int j1, int j2, int j3, int m1, int m2, int m3)
        {
            if (m1 + m2 + m3 != 0)
            {
                return 0.0;
            }

            if (Math.Abs(m1) > j1 || Math.Abs(m2) > j2 || Math.Abs(m3) > j3)
            {
                return 0.0;
            }

            if ((j1 + m1) % 2 != 0 || (j2 + m2) % 2 != 0 || (j3 + m3) % 2 != 0)
            {
                return 0.0;
            }

            double logDelta = LogDelta(j1, j2, j3);
            if (double.IsNaN(logDelta))
            {
                return 0.0;
            }

            double logPrefix = logDelta + 0.5 * (
                LogFact((j1 + m1) / 2) + LogFact((j1 - m1) / 2)
                + LogFact((j2 + m2) / 2) + LogFact((j2 - m2) / 2)
                + LogFact((j3 + m3) / 2) + LogFact((j3 - m3) / 2));

            // Границы суммирования — там, где все факториалы неотрицательны.
            int lo = Math.Max(0, Math.Max((j2 - j3 - m1) / 2, (j1 - j3 + m2) / 2));
            int hi = Math.Min((j1 + j2 - j3) / 2,
                              Math.Min((j1 - m1) / 2, (j2 + m2) / 2));
            double sum = 0.0;
            for (int t = lo; t <= hi; t++)
            {
                double logTerm = LogFact(t)
                                 + LogFact((j1 + j2 - j3) / 2 - t)
                                 + LogFact((j1 - m1) / 2 - t)
                                 + LogFact((j2 + m2) / 2 - t)
                                 + LogFact((j3 - j2 + m1) / 2 + t)
                                 + LogFact((j3 - j1 - m2) / 2 + t);
                if (double.IsNaN(logTerm))
                {
                    continue;
                }

                double term = Math.Exp(logPrefix - logTerm);
                sum += (t % 2 == 0) ? term : -term;
            }

            int phase = (j1 - j2 - m3) / 2;
            return (phase % 2 == 0 ? 1.0 : -1.0) * sum;
        }

        /// <summary>Шесть-джей символ Вигнера по формуле Рака; аргументы удвоены.</summary>
        public static double SixJ(int j1, int j2, int j3, int j4, int j5, int j6)
        {
            double d1 = LogDelta(j1, j2, j3);
            double d2 = LogDelta(j1, j5, j6);
            double d3 = LogDelta(j4, j2, j6);
            double d4 = LogDelta(j4, j5, j3);
            if (double.IsNaN(d1) || double.IsNaN(d2) || double.IsNaN(d3) || double.IsNaN(d4))
            {
                return 0.0;
            }

            int[] lower =
            {
                (j1 + j2 + j3) / 2, (j1 + j5 + j6) / 2,
                (j4 + j2 + j6) / 2, (j4 + j5 + j3) / 2
            };
            int[] upper =
            {
                (j1 + j2 + j4 + j5) / 2, (j2 + j3 + j5 + j6) / 2, (j3 + j1 + j6 + j4) / 2
            };

            int lo = Math.Max(Math.Max(lower[0], lower[1]), Math.Max(lower[2], lower[3]));
            int hi = Math.Min(upper[0], Math.Min(upper[1], upper[2]));
            double logPrefix = d1 + d2 + d3 + d4;
            double sum = 0.0;
            for (int t = lo; t <= hi; t++)
            {
                double logTerm = LogFact(t - lower[0]) + LogFact(t - lower[1])
                                 + LogFact(t - lower[2]) + LogFact(t - lower[3])
                                 + LogFact(upper[0] - t) + LogFact(upper[1] - t)
                                 + LogFact(upper[2] - t);
                if (double.IsNaN(logTerm))
                {
                    continue;
                }

                double term = Math.Exp(logPrefix + LogFact(t + 1) - logTerm);
                sum += (t % 2 == 0) ? term : -term;
            }

            return sum;
        }

        // ------------------------------------------------------------------
        // Данные: схемы уровней из nucdb
        // ------------------------------------------------------------------

        /// <summary>Один переход схемы, как он нужен корреляции.</summary>
        public sealed class Transition
        {
            public int FromSeq;
            public int ToSeq;
            public double EnergyKev;
            public int Multipolarity;
            public double Mixing;
        }

        /// <summary>Схема уровней одного нуклида: спины и переходы.</summary>
        public sealed class Scheme
        {
            public int Z;
            public int A;

            /// <summary>Спин-чётность уровня по его номеру; NaN — неизвестен.</summary>
            public Dictionary<int, double> Jpi = new Dictionary<int, double>();

            public List<Transition> Transitions = new List<Transition>();

            /// <summary>Переход, ближайший по энергии в допуске; null — нет такого.</summary>
            public Transition Find(double energyKev, double toleranceKev)
            {
                Transition best = null;
                double bestGap = toleranceKev;
                foreach (Transition t in this.Transitions)
                {
                    double gap = Math.Abs(t.EnergyKev - energyKev);
                    if (gap <= bestGap)
                    {
                        bestGap = gap;
                        best = t;
                    }
                }

                return best;
            }

            /// <summary>
            /// Коэффициенты каскада «переход a, затем переход b». Каскадом они
            /// являются только если конец первого совпал с началом второго;
            /// иначе это не каскад, и корреляции между ними нет.
            /// </summary>
            public Coefficients Cascade(Transition first, Transition second)
            {
                if (first == null || second == null || first.ToSeq != second.FromSeq)
                {
                    return Isotropic;
                }

                double jStart, jMiddle, jEnd;
                if (!this.Jpi.TryGetValue(first.FromSeq, out jStart)
                    || !this.Jpi.TryGetValue(first.ToSeq, out jMiddle)
                    || !this.Jpi.TryGetValue(second.ToSeq, out jEnd))
                {
                    return Isotropic;      // спина нет — считать нечем
                }

                return For(Math.Abs(jStart), Math.Abs(jMiddle), Math.Abs(jEnd),
                           first.Multipolarity, first.Mixing,
                           second.Multipolarity, second.Mixing);
            }
        }

        static readonly object Gate = new object();
        static readonly Dictionary<int, Scheme> Cache = new Dictionary<int, Scheme>();

        /// <summary>
        /// Схема нуклида из `g4_level`/`g4_gamma`; null — таблиц нет или
        /// нуклида в них нет. Кэшируется, включая отрицательный ответ.
        /// </summary>
        public static Scheme SchemeOf(int z, int a)
        {
            int key = z * 1000 + a;
            lock (Gate)
            {
                Scheme found;
                if (Cache.TryGetValue(key, out found))
                {
                    return found;
                }

                Scheme loaded = Load(z, a);
                Cache[key] = loaded;
                return loaded;
            }
        }

        // Схемы уровней лежат в `schemedb.sqlite` — своём файле с 08.08.2026
        // (`tools/nucdb/split_db.py`): `g4_level`/`g4_gamma` весят 27 МБ и
        // меняются только при смене версии поставки PhotonEvaporation.
        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schemedb.sqlite");
        }

        static Scheme Load(int z, int a)
        {
            string path = DatabasePath();
            if (!File.Exists(path))
            {
                return null;
            }

            Scheme scheme = new Scheme { Z = z, A = a };
            try
            {
                using (SqliteConnection connection = new SqliteConnection(
                    "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "select seq, jpi from g4_level where z=" + z + " and a=" + a
                            + " and jpi is not null";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                scheme.Jpi[reader.GetInt32(0)] = reader.GetDouble(1);
                            }
                        }

                        command.CommandText =
                            "select from_seq, to_seq, energy_ev, multipolarity, mixing_ratio"
                            + " from g4_gamma where z=" + z + " and a=" + a;
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                scheme.Transitions.Add(new Transition
                                {
                                    FromSeq = reader.GetInt32(0),
                                    ToSeq = reader.GetInt32(1),
                                    EnergyKev = reader.GetInt64(2) / 1000.0,
                                    Multipolarity = reader.GetInt32(3),
                                    Mixing = reader.GetDouble(4)
                                });
                            }
                        }
                    }
                }
            }
            catch (SqliteException)
            {
                return null;               // таблиц нет — база старее импорта
            }

            return scheme.Transitions.Count > 0 ? scheme : null;
        }
    }
}
