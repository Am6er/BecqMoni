# -*- coding: utf-8 -*-
"""П47 13.09.2026 (A310) — правка FsaAnalyzer.cs: контекст шума, FitResult.Model/Variance,
FitHuber с весами по модели (Пирсон + Хубер одним IRLS), ModelVariance, пределы от дисперсии оценщика.
Однократно; каждая замена проверяется на единственность."""
import io
import sys

p = 'BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs'
s = io.open(p, encoding='utf-8', newline='').read()
NL = '\r\n'


def rep(old, new, count=1):
    global s
    old = old.replace('\n', NL)
    new = new.replace('\n', NL)
    assert s.count(old) == count, (s.count(old), old[:80])
    s = s.replace(old, new)


# C. поле контекста шума
rep("        List<int> continuumKnots;\n",
    """        List<int> continuumKnots;

        /// <summary>
        /// (`A310`) Слагаемые дисперсии, НЕ зависящие от модели, — для весов по
        /// модели (<see cref="ModelWeights"/>): `Subtracted` — что вычтено из
        /// сырого отсчёта, чтобы получить `y` (континуум SNIP и фон), то есть
        /// `μ̂_сырой = модель + Subtracted`; `Extra` — всё, что в дисперсии
        /// сверх пуассоновского члена (погрешность континуума ξ, шум вычтенного
        /// фона, составной шум S43). Ставится в <c>Analyze</c> там же, где
        /// строятся веса решателя, и читается только <c>FitHuber</c>; null —
        /// веса по данным.
        /// </summary>
        sealed class NoiseTerms
        {
            public double[] Subtracted;
            public double[] Extra;
        }

        NoiseTerms noiseTerms;
""")

# D. в Analyze — после baseWeights
rep("""            double[] baseWeights = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                baseWeights[i] = 1.0 / variance[i];
            }
""",
    """            double[] baseWeights = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                baseWeights[i] = 1.0 / variance[i];
            }

            // (`A310`) Веса по модели: запоминается, чем `y` отличается от
            // сырого отсчёта и что в дисперсии сверх пуассона, — `FitHuber`
            // собирает из этого `max(μ̂, 1) + Extra` на каждом проходе. Считается
            // ЗДЕСЬ, после всех слагаемых дисперсии, чтобы плечо ВЫКЛ осталось
            // ровно прежним `max(N, 1) + Extra`.
            this.noiseTerms = null;
            if (this.ModelWeights)
            {
                NoiseTerms terms = new NoiseTerms
                {
                    Subtracted = new double[channels],
                    Extra = new double[channels]
                };
                for (int i = 0; i < channels; i++)
                {
                    terms.Subtracted[i] = raw[i] - y[i];
                    terms.Extra[i] = variance[i] - Math.Max(raw[i], 1.0);
                }

                this.noiseTerms = terms;
            }
""")

# E. FitResult: Model, Variance
rep("""            public double[] Weights;

            /// <summary>Матрица нормальных уравнений всех колонок фита.</summary>""",
    """            public double[] Weights;

            /// <summary>
            /// (`A310`) Дисперсия каналов, от которой построены <see cref="Weights"/>
            /// финального прохода: при весах по данным — та же, что подана в
            /// <c>FitHuber</c>; при весах по модели — `max(μ̂, 1) + Extra` модели
            /// предпоследнего прохода. Пределы `S9` берут её как дисперсию ТОГО
            /// оценщика, что дал амплитуды.
            /// </summary>
            public double[] Variance;

            /// <summary>Модель фита в каналах полосы (сумма колонок с амплитудами), в шкале `y`.</summary>
            public double[] Model;

            /// <summary>Матрица нормальных уравнений всех колонок фита.</summary>""")

# F. FitOnce: Model = model
rep("""                Weights = weights,
                Gram = gram,""",
    """                Weights = weights,
                Model = model,
                Gram = gram,""")

# G. FitHuber loop
rep("""            double[] weights = (double[])baseWeights.Clone();
            FitResult best = null;
            int passes = this.HuberM > 0.0 ? 3 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                best = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                               gain, offset, chLo, chHi, channels, y, weights, subset);
                if (best == null || pass + 1 == passes)
                {
                    break;
                }

                for (int i = chLo; i <= chHi; i++)
                {
                    double sigma = Math.Sqrt(variance[i]);
                    double residual = Math.Abs(best.Residual[i]);
                    double m = this.HuberM * sigma;
                    weights[i] = residual > m ? (1.0 / variance[i]) * (m / residual) : 1.0 / variance[i];
                }
            }
""",
    """            double[] weights = (double[])baseWeights.Clone();
            // Дисперсия, от которой берутся вес `1/σ²` и порог Хубера `m·σ`
            // ТЕКУЩЕГО перевзвешивания: по данным — поданная `variance`; по
            // модели (`A310`) — пересобирается от модели каждого прохода.
            double[] scale = variance;
            bool pearson = this.ModelWeights && this.noiseTerms != null;
            FitResult best = null;
            int passes = this.HuberM > 0.0 ? 3 : 1;
            if (pearson && passes < 2)
            {
                // Без Хубера веса по модели всё равно требуют второго прохода:
                // первый — по данным, затравкой.
                passes = 2;
            }

            for (int pass = 0; pass < passes; pass++)
            {
                best = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                               gain, offset, chLo, chHi, channels, y, weights, subset);
                if (best == null || pass + 1 == passes)
                {
                    break;
                }

                if (pearson)
                {
                    scale = this.ModelVariance(best, variance, chLo, chHi);
                }

                for (int i = chLo; i <= chHi; i++)
                {
                    double w = 1.0 / scale[i];
                    if (this.HuberM > 0.0)
                    {
                        double sigma = Math.Sqrt(scale[i]);
                        double residual = Math.Abs(best.Residual[i]);
                        double m = this.HuberM * sigma;
                        if (residual > m)
                        {
                            w *= m / residual;
                        }
                    }

                    weights[i] = w;
                }
            }

            if (best != null)
            {
                best.Variance = scale;
            }
""")

# H. helper ModelVariance — перед ReportNdf
rep("""        static double ReportNdf(FitResult fit, double[] report, int chLo, int chHi)
        {""",
    """        /// <summary>
        /// (`A310`) Дисперсия каналов ОТ МОДЕЛИ прохода: `max(μ̂, 1) + Extra`,
        /// μ̂ = модель фита + вычтенное (континуум SNIP, фон) — ожидание сырого
        /// отсчёта. Вне полосы фита остаётся дисперсия по данным: там ни веса,
        /// ни пределы не читаются. Массив — НОВЫЙ на каждый проход: поданная
        /// `variance` живёт у вызывающего и другим фитам нужна нетронутой.
        /// </summary>
        double[] ModelVariance(FitResult fit, double[] variance, int chLo, int chHi)
        {
            double[] v = (double[])variance.Clone();
            NoiseTerms terms = this.noiseTerms;
            if (fit == null || fit.Model == null || terms == null)
            {
                return v;
            }

            for (int i = chLo; i <= chHi; i++)
            {
                double mu = fit.Model[i] + terms.Subtracted[i];
                v[i] = Math.Max(mu, 1.0) + terms.Extra[i];
            }

            return v;
        }

        static double ReportNdf(FitResult fit, double[] report, int chLo, int chHi)
        {""")

# I. ComputeCharacteristicLimits: дисперсия оценщика
rep("""            List<int> A = fit.ActiveIndices;
            double[,] H = fit.ActiveInverse;
            double[] W = fit.Weights;""",
    """            List<int> A = fit.ActiveIndices;
            double[,] H = fit.ActiveInverse;
            double[] W = fit.Weights;
            // (`A310`) Дисперсия ТОГО оценщика, что дал амплитуды: при весах по
            // модели — от модели финального фита, и нулевая гипотеза ниже
            // читается `μ̂ − a·φ`; при весах по данным — поданная, как прежде.
            double[] V = fit.Variance ?? variance;""")
rep("""                    double v0 = variance[i] - amplitude * phi[i];""",
    """                    double v0 = V[i] - amplitude * phi[i];""")

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
