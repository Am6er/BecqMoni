using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`AMBER121`, остаток; П169 28.09.2026) ТЕОРЕТИЧЕСКОЕ ОТНОШЕНИЕ ε/β⁺
    /// РАЗРЕШЁННОГО ПЕРЕХОДА. Решение Amber 28.09.2026 вопросником, дословно:
    /// «Теорией ε/β⁺ по Q (Рекомендую)».
    ///
    /// Зачем. Питание уровня в ENSDF местами дано только ПОЛНЫМ (TI = IB + IE,
    /// `ensdf_feedings.intensity_total`) — оценщик раздела на позитроны и захват
    /// не дал; в поставке таких записей с энергетически разрешённым β⁺ 501 (и
    /// 85 с неопределённой энергией перехода). Прежде читатель их не видел
    /// вовсе, и доля β⁺ набора (f_ENSDF, `AMBER100`) и поток через уровень
    /// (`S188`) шли без них. Раздел здесь — тем же способом, каким его считает
    /// программа LOGFT у оценщиков: у разрешённого перехода ядерный матричный
    /// элемент у β⁺ и захвата один и сокращается, и отношение — чистая
    /// кинематика и атомная физика:
    ///
    ///   ε/β⁺ = 2π² Σₓ ρₓ(R) qₓ² / f_β⁺,
    ///   f_β⁺ = ∫₁^{W₀} F(−Z_d, W) p W (W₀ − W)² dW,   W₀ = E₀ − 1,
    ///   qₓ = E₀ − Bₓ  (энергия нейтрино захвата с оболочки x = K, L1, M1),
    ///
    /// в единицах mₑc² и ħ/(mₑc); E₀ = Q + E(родителя) − E(уровня); F — функция
    /// Ферми с конечным радиусом R = 1.2·A^{1/3} фм; ρₓ — плотность электрона ns
    /// у ядра по водородоподобному решению Дирака с зарядом родителя,
    /// экранированным по Слейтеру (K 0.30, L1 4.15, M1 11.25); Bₓ — энергии
    /// связи дочернего атома (`matdb.eadl_binding`).
    ///
    /// Сверено: ²²Na → уровень 1274 — 0.108 (ENSDF IE/IB 9.502/90.5 = 0.105),
    /// ¹⁸F — 0.0326 (0.0324), ⁶⁴Cu → основное — 2.56 (2.47). Мерка на всех
    /// E-записях поставки, где раздел ЕСТЬ и у обоих чисел есть погрешность
    /// (родитель в основном состоянии, 4026 записей): доля β⁺ в пределах 1σ
    /// записи — 90.2 %, в пределах 2σ — 95.3 %; медиана отношения
    /// (ε/β⁺)теор / (IE/IB)ENSDF 1.052 (обменная и перекрывательная поправки
    /// не учтены). Худшие — запрещённые переходы (`40K`, `26AL`, `122SB`),
    /// которым эта формула не предназначена; мерка — `handover/p169/ecbeta_measure.py`.
    ///
    /// Имён нуклидов здесь нет — только Z, A, энергии.
    /// </summary>
    public static class AllowedCaptureRatio
    {
        const double Alpha = 1.0 / 137.035999;
        const double ElectronMassKev = 510.99895;
        const double ComptonLengthFm = 386.15926796;

        /// <summary>Оболочки ns: главное число, номер оболочки EADL, экранирование по Слейтеру.</summary>
        static readonly double[][] Shells =
        {
            new[] { 1.0, 1.0, 0.30 },
            new[] { 2.0, 3.0, 4.15 },
            new[] { 3.0, 8.0, 11.25 },
        };

        /// <summary>Номера оболочек EADL, которые нужны расчёту (K, L1, M1).</summary>
        public static readonly int[] ShellIds = { 1, 3, 8 };

        static readonly double[] Lanczos =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028,
            771.32342877765313, -176.61502916214059, 12.507343278686905,
            -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7
        };

        /// <summary>
        /// Доля β⁺ в полном питании уровня, β⁺ / (β⁺ + ε). NaN — посчитать нельзя
        /// (нет энергий связи, неразумные Z или A); 0 — β⁺ запрещён энергией.
        /// </summary>
        /// <param name="daughterZ">Z дочернего ядра.</param>
        /// <param name="mass">Массовое число.</param>
        /// <param name="transitionKev">E₀ = Q + E(родителя) − E(уровня), кэВ.</param>
        /// <param name="bindingKev">Энергии связи дочернего атома по номеру оболочки EADL, кэВ.</param>
        public static double BetaPlusShare(int daughterZ, int mass, double transitionKev,
                                           IDictionary<int, double> bindingKev)
        {
            if (daughterZ < 1 || daughterZ + 1 >= 137 || mass < 1 || bindingKev == null
                || double.IsNaN(transitionKev))
            {
                return double.NaN;
            }

            double e0 = transitionKev / ElectronMassKev;
            double w0 = e0 - 1.0;
            if (!(w0 > 1.0))
            {
                return 0.0;
            }

            double radius = 1.2 * Math.Pow(mass, 1.0 / 3.0) / ComptonLengthFm;
            double fb = BetaPlusIntegral(daughterZ, w0, radius);
            if (!(fb > 0.0))
            {
                return double.NaN;
            }

            double zParent = daughterZ + 1;
            double ec = 0.0;
            bool any = false;
            foreach (double[] shell in Shells)
            {
                double b;
                if (!bindingKev.TryGetValue((int)shell[1], out b))
                {
                    continue;
                }

                double q = e0 - b / ElectronMassKev;
                if (!(q > 0.0))
                {
                    continue;
                }

                any = true;
                ec += DensityAtNucleus(zParent - shell[2], shell[0], radius) * q * q;
            }

            if (!any)
            {
                return double.NaN;
            }

            double ratio = 2.0 * Math.PI * Math.PI * ec / fb;
            return 1.0 / (1.0 + ratio);
        }

        static double DensityAtNucleus(double zEff, double n, double radius)
        {
            double az = Alpha * zEff;
            double g = Math.Sqrt(1.0 - az * az);
            return az * az * az / (Math.PI * n * n * n) * (1.0 + g) / Gamma(2.0 * g + 1.0)
                   * Math.Pow(2.0 * az * radius / n, 2.0 * g - 2.0);
        }

        /// <summary>f_β⁺ по Симпсону; подстановка W = 1 + (W₀ − 1)t² снимает корень у порога.</summary>
        static double BetaPlusIntegral(int daughterZ, double w0, double radius)
        {
            const int n = 400;
            double h = 1.0 / n;
            double sum = 0.0;
            for (int i = 0; i <= n; i++)
            {
                double t = i * h;
                double w = 1.0 + (w0 - 1.0) * t * t;
                double p = Math.Sqrt(Math.Max(w * w - 1.0, 0.0));
                double value = p > 0.0
                    ? Fermi(-daughterZ, w, radius) * p * w * (w0 - w) * (w0 - w) * 2.0 * (w0 - 1.0) * t
                    : 0.0;
                double coefficient = i == 0 || i == n ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);
                sum += coefficient * value;
            }

            return sum * h / 3.0;
        }

        /// <summary>F(Z, W) с конечным радиусом; Z &lt; 0 — позитрон.</summary>
        static double Fermi(double zSigned, double w, double radius)
        {
            double p = Math.Sqrt(Math.Max(w * w - 1.0, 1e-300));
            double az = Alpha * zSigned;
            double g = Math.Sqrt(1.0 - az * az);
            double eta = az * w / p;
            double lg = LogGammaReal(g, eta);
            return 2.0 * (1.0 + g) * Math.Pow(2.0 * p * radius, 2.0 * g - 2.0)
                   * Math.Exp(Math.PI * eta + 2.0 * lg - 2.0 * LogGamma(2.0 * g + 1.0));
        }

        static double Gamma(double x)
        {
            return Math.Exp(LogGamma(x));
        }

        static double LogGamma(double x)
        {
            return LogGammaReal(x, 0.0);
        }

        /// <summary>Re ln Γ(x + iy), Ланцош (g = 7); x ≥ 0.5 во всех вызовах.</summary>
        static double LogGammaReal(double x, double y)
        {
            // z − 1
            double zr = x - 1.0, zi = y;
            double sr = Lanczos[0], si = 0.0;
            for (int k = 1; k < 9; k++)
            {
                double dr = zr + k, di = zi;
                double den = dr * dr + di * di;
                sr += Lanczos[k] * dr / den;
                si -= Lanczos[k] * di / den;
            }

            double tr = zr + 7.5, ti = zi;
            // ln t (комплексный)
            double lnTr = 0.5 * Math.Log(tr * tr + ti * ti), lnTi = Math.Atan2(ti, tr);
            // Re[(z + 0.5)·ln t − t + ln s]
            double re = (zr + 0.5) * lnTr - zi * lnTi - tr + 0.5 * Math.Log(sr * sr + si * si);
            return 0.5 * Math.Log(2.0 * Math.PI) + re;
        }
    }
}
