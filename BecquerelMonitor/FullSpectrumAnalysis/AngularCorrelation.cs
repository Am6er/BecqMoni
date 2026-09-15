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
    /// где для перехода, СНИМАЮЩЕГО промежуточный уровень (второго в каскаде),
    ///
    ///     A_k(2) = [F_k(L,L,j',j) + 2δ·F_k(L,L',j',j) + δ²·F_k(L',L',j',j)] / (1+δ²),
    ///
    /// а для перехода, ЗАСЕЛЯЮЩЕГО его (первого), интерференционный член несёт
    /// множитель (−1)^(L+L′):
    ///
    ///     A_k(1) = [F_k(L,L,j',j) + (−1)^(L+L′)·2δ·F_k(L,L',j',j) + δ²·F_k(L',L',j',j)] / (1+δ²).
    ///
    /// ⛔ Так — в соглашении Кране–Стеффена (Krane, Steffen, Phys. Rev. C 2,
    /// 724 (1970); Krane, Steffen, Wheeler, Nucl. Data Tables 11, 351 (1973)),
    /// в котором даны δ ENSDF и PhotonEvaporation, то есть таблицы `g4_gamma`.
    /// Явно, с той же оговоркой «differ by a phase factor (−1)^{L+L′}» —
    /// ур. (9) против (12) в arXiv:2110.00619 (Monte Carlo γ-корреляций по
    /// статистическим тензорам). До 15.09.2026 (П86, `AMBER42`) оба перехода
    /// считались одной формулой с +δ — на чистых каскадах (δ = 0) это
    /// незаметно, а на смешанных давало A₂₂ с неверным знаком
    /// интерференции: Cs-134 563+605 −0.168 против +0.026 ± 0.004 у Geant4
    /// (П85, десять каскадов прямой меркой `g4cf angcorr`; читатель —
    /// `AngularProbe` раздел 4; журнал
    /// `handover/handover-2026-09-15-p86-amber42-delta-sign-rev25.md`).
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

            // ⛔ (П86, 15.09.2026, `AMBER42`) Знак δ ЗАСЕЛЯЮЩЕГО перехода. В
            // соглашении Кране–Стеффена (δ ENSDF = δ PhotonEvaporation = δ
            // таблицы `g4_gamma`) интерференционный член первого перехода
            // каскада несёт множитель (−1)^(L+L′) — у всех смешанных
            // переходов базы L′ = L ± 1, то есть просто −δ₁; у второго
            // перехода знак прямой. До этого дня оба считались с +δ, и на
            // смешанных каскадах A₂₂ выходил с неверным знаком
            // интерференции: Cs-134 563+605 −0.168 против +0.026 ± 0.004 у
            // Geant4 (П85 §5.3, десять каскадов; А₄₄ ∝ δ² и знака не видит —
            // потому сходился). Чистые каскады (δ = 0) правкой не тронуты.
            // Читатель — `AngularProbe` раздел 4 (`--old-sign` — контроль).
            double delta1 = ((l1 + l1Prime) % 2 != 0) ? -mixing1 : mixing1;

            Coefficients result = new Coefficients();
            result.A22 = Ak(2, l1, l1Prime, delta1, jStart, jMiddle)
                         * Ak(2, l2, l2Prime, mixing2, jEnd, jMiddle);
            result.A44 = Ak(4, l1, l1Prime, delta1, jStart, jMiddle)
                         * Ak(4, l2, l2Prime, mixing2, jEnd, jMiddle);
            return result;
        }

        /// <summary>
        /// Порядки мультиполей из кода Geant4: 1…9 = E0,E1,M1,E2,M2,E3,M3,E4,M4
        /// (`scheme.md` §5г), смесь — 100·Nx+Ny. false — код неизвестен или
        /// это E0 (монополь гамма-квантом не излучается вовсе).
        ///
        /// ⚠ E4/M4 (коды 8, 9) добавлены 13.09.2026 (П49): прежде переход
        /// такого порядка считался изотропным молча, а у Bi-207 линия 1063.7
        /// (13/2⁺ → 5/2⁻) — M4, и её пара с 569.7 — самый сильный каскад
        /// нуклида. Члены выше k = 4 (у L = 4 есть A₆₆) по-прежнему не
        /// считаются — разложение хранит два коэффициента.
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
                case 8: return 4;    // E4
                case 9: return 4;    // M4
                default: return 0;   // E0 (кодом 1), E5+ и всё незнакомое
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

            /// <summary>
            /// Переход, отвечающий линии распада; null — нет такого.
            ///
            /// ⛔ «БЛИЖАЙШИЙ ПО ЭНЕРГИИ» — НЕВЕРНОЕ ПРАВИЛО, опровергнуто
            /// измерением (`D31`) и исправлено 23.08.2026 (`W26`). У Hf-176 на
            /// линию 306.780 кэВ три кандидата, и ближе всех оказывается
            /// САМОЗВАНЕЦ с уровня 3467.40 кэВ и нулевой интенсивностью
            /// (306.900, промах 0.120), а не настоящий переход 3→2 с уровня
            /// 596.82 (306.640, промах 0.140). Уровень 3467 кэВ β-распад
            /// Lu-176 (Q = 1194 кэВ) населить не может в принципе.
            ///
            /// Правило поэтому такое же, как у
            /// <c>CascadeAtomicData.MatchTransition</c>, и держится оно одно на
            /// двоих нарочно: переходы с нулевой относительной интенсивностью
            /// не грузятся вовсе (<see cref="Load"/>), среди оставшихся берётся
            /// переход с САМОГО НИЖНЕГО уровня, и лишь при равенстве уровней —
            /// ближайший по энергии.
            ///
            /// ⚠ Допуск здесь по-прежнему задаётся вызывающим, а не константой
            /// класса: у <c>CascadeAtomicData</c> он свой (0.6 кэВ), и сводить
            /// их в одно число без измерения нельзя — разные поставки энергий.
            /// </summary>
            public Transition Find(double energyKev, double toleranceKev)
            {
                Transition best = null;
                double bestGap = 0.0;
                foreach (Transition t in this.Transitions)
                {
                    double gap = Math.Abs(t.EnergyKev - energyKev);
                    if (gap > toleranceKev)
                    {
                        continue;
                    }

                    if (best == null
                        || t.FromSeq < best.FromSeq
                        || (t.FromSeq == best.FromSeq && gap < bestGap))
                    {
                        best = t;
                        bestGap = gap;
                    }
                }

                return best;
            }

            /// <summary>
            /// Переход по ПАРЕ УРОВНЕЙ (`N14`): номера уровней у
            /// <see cref="CascadeAtomicData.Transition"/> — из той же таблицы
            /// `g4_gamma`, и сопоставление по ним не знает ни допуска по
            /// энергии, ни самозванца. null — такого перехода в схеме нет
            /// (в том числе отсеянного нулевой интенсивностью).
            /// </summary>
            public Transition FindSeq(int fromSeq, int toSeq)
            {
                foreach (Transition t in this.Transitions)
                {
                    if (t.FromSeq == fromSeq && t.ToSeq == toSeq)
                    {
                        return t;
                    }
                }

                return null;
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
                            "select seq, jpi from g4_level where z="
                            + z.ToString(CultureInfo.InvariantCulture)
                            + " and a=" + a.ToString(CultureInfo.InvariantCulture)
                            + " and jpi is not null";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                scheme.Jpi[reader.GetInt32(0)] = reader.GetDouble(1);
                            }
                        }

                        command.CommandText =
                            "select from_seq, to_seq, energy_ev, multipolarity, mixing_ratio,"
                            + " intensity_ppm"
                            + " from g4_gamma where z=" + z.ToString(CultureInfo.InvariantCulture)
                            + " and a=" + a.ToString(CultureInfo.InvariantCulture);
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // Переход, которого не испускают, кандидатом
                                // быть не может (`W26`, `D31`): в `g4_gamma`
                                // рядом с настоящими лежат переходы с нулевой
                                // относительной интенсивностью, и по энергии
                                // они бывают БЛИЖЕ настоящего.
                                if (reader.IsDBNull(5) || reader.GetDouble(5) <= 0.0)
                                {
                                    continue;
                                }

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

        // ------------------------------------------------------------------
        // Пара линий распада → коэффициенты (`N14`)
        // ------------------------------------------------------------------

        /// <summary>
        /// Допуск сопоставления линии распада строке <c>GammaIntensity</c> —
        /// тот же, что у сумматора для линии компонента (0.3 кэВ): линия
        /// таблицы совпадений и линия <c>decay_radiations</c> — одна поставка,
        /// но записаны с разным округлением.
        /// </summary>
        const double LineMatchKev = 0.3;

        /// <summary>
        /// Коэффициенты A₂₂, A₄₄ для пары линий ОДНОГО РАСПАДА по ключу
        /// нуклида — тем же путём, каким сумматор берёт переходы для рентгена
        /// и гейта по времени (<see cref="CascadeAtomicData"/>): линия →
        /// строка выходов → переход схемы своей ВЕТВИ (Z, A дочернего) →
        /// спины и мультипольности из <see cref="SchemeOf"/>.
        ///
        /// Изотропно (нули), когда: атомных данных нет; линии не сопоставлен
        /// переход; линии из РАЗНЫХ ветвей (у Eu-152 схема Sm-152 и схема
        /// Gd-152 — разные события); переходы не смежны (конец одного —
        /// не начало другого); спина или мультипольности нет.
        ///
        /// ⚠ ПРИБЛИЖЕНИЕ НАЗВАНО: пара, между переходами которой лежит
        /// НЕНАБЛЮДАЕМЫЙ промежуточный переход, считается изотропной, а у неё
        /// корреляция лишь ослаблена множителями U_k промежуточного. Это
        /// занижает поправку, а не завышает.
        /// </summary>
        public static Coefficients ForPair(string nuclideKey, double firstKev, double secondKev)
        {
            if (string.IsNullOrEmpty(nuclideKey))
            {
                return Isotropic;
            }

            CascadeAtomicData atomic = CascadeAtomicData.Of(nuclideKey);
            if (atomic == null || atomic.Branches == null)
            {
                return Isotropic;
            }

            CascadeAtomicData.Transition first = TransitionOf(atomic, firstKev);
            CascadeAtomicData.Transition second = TransitionOf(atomic, secondKev);
            if (first == null || second == null
                || first.BranchIndex < 0 || first.BranchIndex != second.BranchIndex
                || first.BranchIndex >= atomic.Branches.Count)
            {
                return Isotropic;
            }

            CascadeAtomicData.Branch branch = atomic.Branches[first.BranchIndex];
            Scheme scheme = SchemeOf(branch.Z, branch.A);
            if (scheme == null)
            {
                return Isotropic;
            }

            Transition a = scheme.FindSeq(first.FromSeq, first.ToSeq);
            Transition b = scheme.FindSeq(second.FromSeq, second.ToSeq);
            if (a == null || b == null)
            {
                return Isotropic;
            }

            Coefficients w = scheme.Cascade(a, b);
            if (w.IsIsotropic)
            {
                w = scheme.Cascade(b, a);
            }

            return w;
        }

        /// <summary>
        /// Переход, стоящий у СИЛЬНЕЙШЕЙ строки выходов в допуске
        /// <see cref="LineMatchKev"/>; null — строки нет или переход ей не
        /// сопоставлен.
        /// </summary>
        static CascadeAtomicData.Transition TransitionOf(CascadeAtomicData atomic, double energyKev)
        {
            CascadeAtomicData.GammaLine best = null;
            foreach (CascadeAtomicData.GammaLine row in atomic.GammaIntensity)
            {
                if (row == null || row.Transition == null
                    || Math.Abs(row.EnergyKev - energyKev) > LineMatchKev)
                {
                    continue;
                }

                if (best == null || row.IntensityPct > best.IntensityPct)
                {
                    best = row;
                }
            }

            return best != null ? best.Transition : null;
        }
    }

    /// <summary>
    /// Коэффициенты ослабления угловой корреляции Q_k(E) сцены (`N14`) —
    /// ГЕОМЕТРИЧЕСКАЯ половина, которой у сумматора нет: у него только матрица.
    ///
    /// ЧТО ЭТО. Моменты угловой эффективности пика по Лежандру:
    ///
    ///     Q_k(E) = ∫ ε_p(E, θ)·P_k(cos θ) dΩ / ∫ ε_p(E, θ) dΩ,   k = 2, 4,
    ///
    /// где θ — угол вылета кванта к оси «точка распада → центр кристалла»,
    /// а для протяжённой пробы среднее берётся и по точкам розыгрыша.
    /// Вероятность поглотить в пике ОБА кванта каскада с корреляцией
    /// W(θ₁₂) = 1 + Σ A_kk·P_k(cos θ₁₂) по теореме сложения полиномов Лежандра
    /// (осевая симметрия, член m = 0):
    ///
    ///     ε_пары = ε_p(1)·ε_p(2)·(1 + A₂₂·Q₂(1)·Q₂(2) + A₄₄·Q₄(1)·Q₄(2)).
    ///
    /// ПРЕДЕЛЫ, по которым таблица проверяется: точечный источник далеко от
    /// кристалла (малый телесный угол) — Q_k → 1, корреляция входит целиком;
    /// геометрия 4π (поле, маринелли) — ε(θ) почти постоянна, Q_k → 0, и
    /// изотропное произведение верно само по себе.
    ///
    /// ⚠ ПРИБЛИЖЕНИЯ НАЗВАНЫ: (а) члены m ≠ 0 теоремы сложения у точки вне
    /// оси отброшены — ось берётся на центр кристалла, где они наименьшие;
    /// (б) среднее по точкам берётся ПОРОЗНЬ для двух энергий (Q_k(1)·Q_k(2)
    /// вместо ⟨q_k(r,1)·q_k(r,2)⟩) — связь точек двух квантов уже несёт κ.
    ///
    /// ОТКУДА. Считается пробой `AngularQkProbe` тем же переносом, что и
    /// матрица (<c>EfficiencySimulator</c>), и лежит САЙДКАРОМ рядом с
    /// матрицей: текстовый файл `*.qk` в каталоге склада матриц, малый и
    /// пригодный для git. Ключ соответствия — ОТПЕЧАТОК ГЕОМЕТРИИ
    /// (<see cref="FingerprintOf"/>), а не клеймо матрицы: Q_k — свойство
    /// формы сцены, и смена физики склада его не обесценивает (порядок
    /// 0.1 %), тогда как клеймо меняется каждым единым счётом.
    /// </summary>
    public sealed class AngularAttenuation
    {
        public const int Format = 1;
        public const string Extension = ".qk";

        public string Scene;
        public string GeometrySha;
        public string MatrixStamp;
        public int Histories;
        public int Seed;
        public string Built;

        public double[] Energies;
        public double[] Q2;
        public double[] Q4;
        public double[] Q2Err;
        public double[] Q4Err;
        public double[] PeakEff;

        /// <summary>
        /// Те же моменты для ПОЛНОЙ эффективности (квант задел кристалл) —
        /// ими считается вынос из пика: партнёр уносит событие, куда бы он ни
        /// попал, и его угловое распределение — распределение ε_T, а не ε_p.
        /// Пусто (файл старого вида) — вынос берёт моменты пика.
        /// </summary>
        public double[] Q2T;
        public double[] Q4T;
        public double[] Q2TErr;
        public double[] Q4TErr;
        public double[] TotalEff;

        /// <summary>Откуда загружена; пусто — посчитана в этом процессе.</summary>
        public string FilePath;

        public int Count
        {
            get { return this.Energies != null ? this.Energies.Length : 0; }
        }

        /// <summary>
        /// Отпечаток геометрии — SHA-256 машинного текста сцены
        /// (<c>GeometryWriter.Render</c>), без версии физики и настроек.
        /// </summary>
        public static string FingerprintOf(EfficiencyMaker.GeometryModel geometry)
        {
            if (geometry == null)
            {
                return "";
            }

            string text;
            try
            {
                text = EfficiencyMaker.GeometryWriter.Render(geometry);
            }
            catch (Exception)
            {
                text = geometry.Describe();
            }

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var hex = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        /// <summary>
        /// Q_k при энергии — линейная интерполяция по ln E между узлами,
        /// за краями — крайний узел. k = 2 или 4; иное — ноль (член выпадает).
        /// </summary>
        public double Q(int k, double energyKev)
        {
            return this.Interpolate(k == 2 ? this.Q2 : (k == 4 ? this.Q4 : null), energyKev);
        }

        /// <summary>Момент ПОЛНОЙ эффективности; без своих столбцов — момент пика.</summary>
        public double QT(int k, double energyKev)
        {
            double[] values = k == 2 ? this.Q2T : (k == 4 ? this.Q4T : null);
            if (values == null || values.Length != this.Count)
            {
                return this.Q(k, energyKev);
            }

            return this.Interpolate(values, energyKev);
        }

        double Interpolate(double[] values, double energyKev)
        {
            if (values == null || this.Energies == null || this.Energies.Length == 0
                || values.Length != this.Energies.Length || !(energyKev > 0.0))
            {
                return 0.0;
            }

            int n = this.Energies.Length;
            if (energyKev <= this.Energies[0])
            {
                return values[0];
            }

            if (energyKev >= this.Energies[n - 1])
            {
                return values[n - 1];
            }

            int i = 1;
            while (i < n - 1 && this.Energies[i] < energyKev)
            {
                i++;
            }

            double x0 = Math.Log(this.Energies[i - 1]);
            double x1 = Math.Log(this.Energies[i]);
            double t = x1 > x0 ? (Math.Log(energyKev) - x0) / (x1 - x0) : 0.0;
            return values[i - 1] + (values[i] - values[i - 1]) * t;
        }

        public void Save(string path)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# BecqMoni angular attenuation Q_k (N14)\n");
            sb.Append("format=").Append(Format.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("scene=").Append(this.Scene ?? "").Append('\n');
            sb.Append("geomsha=").Append(this.GeometrySha ?? "").Append('\n');
            sb.Append("matrix=").Append(this.MatrixStamp ?? "").Append('\n');
            sb.Append("histories=").Append(this.Histories.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("seed=").Append(this.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("built=").Append(this.Built ?? "").Append('\n');
            bool total = this.Q2T != null && this.Q4T != null && this.Q2T.Length == this.Count;
            sb.Append(total
                          ? "# E_keV Q2 Q4 dQ2 dQ4 eps_peak Q2T Q4T dQ2T dQ4T eps_total\n"
                          : "# E_keV Q2 Q4 dQ2 dQ4 eps_peak\n");
            for (int i = 0; i < this.Count; i++)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                                        "{0:R} {1:F6} {2:F6} {3:F6} {4:F6} {5:G6}",
                                        this.Energies[i], this.Q2[i], this.Q4[i],
                                        this.Q2Err != null ? this.Q2Err[i] : 0.0,
                                        this.Q4Err != null ? this.Q4Err[i] : 0.0,
                                        this.PeakEff != null ? this.PeakEff[i] : 0.0));
                if (total)
                {
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                                            " {0:F6} {1:F6} {2:F6} {3:F6} {4:G6}",
                                            this.Q2T[i], this.Q4T[i],
                                            this.Q2TErr != null ? this.Q2TErr[i] : 0.0,
                                            this.Q4TErr != null ? this.Q4TErr[i] : 0.0,
                                            this.TotalEff != null ? this.TotalEff[i] : 0.0));
                }

                sb.Append('\n');
            }

            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>Разбор файла; null — файла нет, формат чужой или таблица пуста.</summary>
        public static AngularAttenuation Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var table = new AngularAttenuation { FilePath = path };
            var e = new List<double>();
            var q2 = new List<double>();
            var q4 = new List<double>();
            var d2 = new List<double>();
            var d4 = new List<double>();
            var eff = new List<double>();
            var q2t = new List<double>();
            var q4t = new List<double>();
            var d2t = new List<double>();
            var d4t = new List<double>();
            var efft = new List<double>();
            bool formatSeen = false;
            foreach (string raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && !char.IsDigit(line[0]))
                {
                    string key = line.Substring(0, eq);
                    string value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "format":
                            int format;
                            formatSeen = int.TryParse(value, NumberStyles.Integer,
                                                      CultureInfo.InvariantCulture, out format)
                                         && format == Format;
                            break;
                        case "scene": table.Scene = value; break;
                        case "geomsha": table.GeometrySha = value; break;
                        case "matrix": table.MatrixStamp = value; break;
                        case "histories":
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                         out table.Histories);
                            break;
                        case "seed":
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                         out table.Seed);
                            break;
                        case "built": table.Built = value; break;
                    }

                    continue;
                }

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    continue;
                }

                double energy, a, b;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out energy)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                    || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                {
                    continue;
                }

                double x;
                e.Add(energy);
                q2.Add(a);
                q4.Add(b);
                d2.Add(parts.Length > 3 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                d4.Add(parts.Length > 4 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                eff.Add(parts.Length > 5 && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                if (parts.Length > 10)
                {
                    q2t.Add(double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                    q4t.Add(double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                    d2t.Add(double.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                    d4t.Add(double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                    efft.Add(double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : 0.0);
                }
            }

            if (!formatSeen || e.Count == 0)
            {
                return null;
            }

            table.Energies = e.ToArray();
            table.Q2 = q2.ToArray();
            table.Q4 = q4.ToArray();
            table.Q2Err = d2.ToArray();
            table.Q4Err = d4.ToArray();
            table.PeakEff = eff.ToArray();
            if (q2t.Count == e.Count)
            {
                table.Q2T = q2t.ToArray();
                table.Q4T = q4t.ToArray();
                table.Q2TErr = d2t.ToArray();
                table.Q4TErr = d4t.ToArray();
                table.TotalEff = efft.ToArray();
            }

            return table;
        }

        static readonly object Gate = new object();
        static readonly Dictionary<string, KeyValuePair<DateTime, AngularAttenuation>> Cache =
            new Dictionary<string, KeyValuePair<DateTime, AngularAttenuation>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Таблица ЭТОЙ геометрии среди `*.qk` каталога — по отпечатку; null —
        /// каталога нет или ни один файл не подошёл. Файлы кэшируются по
        /// времени записи: сайдкаров десятки, спектров сотни.
        /// </summary>
        public static AngularAttenuation Find(string directory, EfficiencyMaker.GeometryModel geometry)
        {
            if (string.IsNullOrEmpty(directory) || geometry == null || !Directory.Exists(directory))
            {
                return null;
            }

            string sha = FingerprintOf(geometry);
            if (sha.Length == 0)
            {
                return null;
            }

            foreach (string file in Directory.GetFiles(directory, "*" + Extension))
            {
                AngularAttenuation table = Cached(file);
                if (table != null && string.Equals(table.GeometrySha, sha, StringComparison.OrdinalIgnoreCase))
                {
                    return table;
                }
            }

            return null;
        }

        static AngularAttenuation Cached(string file)
        {
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            lock (Gate)
            {
                KeyValuePair<DateTime, AngularAttenuation> have;
                if (Cache.TryGetValue(file, out have) && have.Key == stamp)
                {
                    return have.Value;
                }

                AngularAttenuation loaded = Load(file);
                Cache[file] = new KeyValuePair<DateTime, AngularAttenuation>(stamp, loaded);
                return loaded;
            }
        }
    }
}
