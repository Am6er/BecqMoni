# -*- coding: utf-8 -*-
# П16 11.09.2026: поверх патча П14 (anchor-poly.patch) в копии C:\Users\moroz\bqp16 —
# третья координата привязки шкалы: s(E) = E·r(E)/r(E0) − E (свет), коэффициент β.
# Замены точные; каждая обязана лечь ровно один раз, иначе — отказ.
import io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\bqp16'

def patch(rel, pairs):
    p = os.path.join(ROOT, rel)
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        s = f.read()
    for old, new in pairs:
        n = s.count(old)
        if n != 1:
            raise SystemExit('%s: образец встречается %d раз(а):\n%s' % (rel, n, old[:200]))
        s = s.replace(old, new)
    with io.open(p, 'w', encoding='utf-8', newline='') as f:
        f.write(s)
    print('%s: %d замен' % (rel, len(pairs)))

NL = '\r\n' if io.open(os.path.join(ROOT, 'BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs'), encoding='utf-8-sig', newline='').read(4000).count('\r\n') else '\n'
def L(text):
    return text.replace('\n', NL)

# ---------------------------------------------------------------- FsaAnalyzer.cs
A = 'BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs'
pairs = []

# 1. поля
pairs.append((L('''        double driftQuad;
'''), L('''        double driftQuad;

        /// <summary>
        /// (П16 11.09.2026, ЗАМЕР) Коэффициент β световой координаты шкалы:
        /// p″ = g·p + o + q·p² + β·S(p), где S(p) — сдвиг положения в каналах,
        /// который даёт кривая света r(E) при β = 1
        /// (<see cref="lightShiftChannels"/>): s(E) = E·r(E)/r(E₀) − E.
        /// Нуль, пока <see cref="AnchorLightCurve"/> пуст. Поле по тем же
        /// доводам, что <see cref="driftQuad"/>; читается ТОЛЬКО в
        /// <see cref="DriftPosition"/>, сбрасывается в начале <see cref="Analyze"/>.
        /// </summary>
        double driftLight;

        /// <summary>(П16) S(p) по каналам спектра при β = 1, каналы; null — координата выключена.</summary>
        double[] lightShiftChannels;

        /// <summary>(П16) max |S(p)| по полосе, каналы — для условия сходимости.</summary>
        double lightShiftMax;
''')))

# 2. свойства
pairs.append((L('''        public bool AnchorPolyMonotonic { get; set; }
'''), L('''        public bool AnchorPolyMonotonic { get; set; }

        /// <summary>
        /// (П16 11.09.2026, ЗАМЕР — умолчание пусто, ⛔ НЕ МЕНЯТЬ без решения
        /// Amber «Параболу не включать; замер r(E)») Кривая относительного
        /// света на фотон r(E) для световой координаты привязки: пусто —
        /// координата выключена (шкала `AMBER17`); "model" — собственная
        /// фотонная кривая модели (`LightScaleProbe`, G1S_point5, 200k
        /// историй, П14); "lit" — Ходюк 2010/2012 дословно в 9…100 кэВ и
        /// форма модели, растянутая к 111.2 % на 100 кэВ, выше. Таблицы —
        /// <see cref="FsaLightScale"/>. Рычаг — `--anchor-light=`.
        /// </summary>
        public string AnchorLightCurve { get; set; }

        /// <summary>
        /// (П16) β: 1 — пик модели стоит там, где его ставит свет; 0 —
        /// как без координаты. При <see cref="AnchorLightBetaFree"/> — запасное
        /// значение, когда подбор невозможен (опор меньше порога или нет плеча).
        /// Рычаг — `--anchor-beta=`.
        /// </summary>
        public double AnchorLightBeta { get; set; }

        /// <summary>(П16) β подбирается тем же взвешенным МНК (базис {1, x, S}) при плече и опорах ≥ <see cref="AnchorLightMinAnchors"/>.</summary>
        public bool AnchorLightBetaFree { get; set; }

        /// <summary>(П16) Меньше стольких опор — β не подбирается, берётся <see cref="AnchorLightBeta"/>.</summary>
        public int AnchorLightMinAnchors { get; set; }

        /// <summary>(П16) Подобранный β принимается за выигрыш χ² опор не меньше этого против прямой при запасном β; 0 — всегда.</summary>
        public double AnchorLightMinDeltaChi2 { get; set; }

        /// <summary>(П16) E₀ — энергия, на которой s(E₀) = 0 (калибровочная линия прибора); 661.657.</summary>
        public double AnchorLightReferenceKev { get; set; }
''')))

# 3. DriftPosition
pairs.append((L('''            // (П14) квадратичный член — поле, см. `driftQuad`; умолчанием нуль.
            return gain * position + offset + this.driftQuad * position * position;
        }
'''), L('''            // (П14) квадратичный член — поле, см. `driftQuad`; умолчанием нуль.
            // (П16) световая координата — поле `driftLight` × таблица S(p).
            return gain * position + offset + this.driftQuad * position * position
                   + this.driftLight * this.LightShift(position);
        }

        /// <summary>(П16) S(p), каналы: линейная интерполяция таблицы <see cref="lightShiftChannels"/>; 0 без таблицы.</summary>
        double LightShift(double position)
        {
            double[] table = this.lightShiftChannels;
            if (table == null || table.Length == 0 || !Finite(position))
            {
                return 0.0;
            }

            if (position <= 0.0)
            {
                return table[0];
            }

            int last = table.Length - 1;
            if (position >= last)
            {
                return table[last];
            }

            int lo = (int)position;
            double frac = position - lo;
            return table[lo] + (table[lo + 1] - table[lo]) * frac;
        }
''')))

# 4. сигнатура CollectScaleAnchors
pairs.append((L('''                                                 out double a, out double b, out double c,
                                                 out int used, out string note)
        {
            a = 1.0;
            b = 0.0;
            c = 0.0;
'''), L('''                                                 out double a, out double b, out double c,
                                                 out double beta, out int used, out string note)
        {
            a = 1.0;
            b = 0.0;
            c = 0.0;
            beta = 0.0;
''')))

# 5. AnchorFit: S и YEff
pairs.append((L('''                        fits.Add(new AnchorFit
                        {
                            Anchor = anchor, X = centreModel, Y = centreData, Weight = 1.0 / varCentre
                        });
'''), L('''                        fits.Add(new AnchorFit
                        {
                            Anchor = anchor, X = centreModel, Y = centreData, Weight = 1.0 / varCentre,
                            // (П16) световая координата опоры — S на модельном центре
                            S = this.LightShift(centreModel)
                        });
''')))

# 6. начало МНК: закреплённая добавка β и YEff
pairs.append((L('''            string how = "усиление";
            string polyRefused = null;
            int outliers = 0;
            while (true)
            {
                double swxx = 0.0, swxy = 0.0, sw = 0.0, swx = 0.0, swy = 0.0;
                double xMin = double.MaxValue, xMax = double.MinValue;
                foreach (AnchorFit f in fits)
                {
                    sw += f.Weight;
                    swx += f.Weight * f.X;
                    swy += f.Weight * f.Y;
                    swxx += f.Weight * f.X * f.X;
                    swxy += f.Weight * f.X * f.Y;
'''), L('''            string how = "усиление";
            string polyRefused = null;
            string lightRefused = null;
            int outliers = 0;

            // (П16, ЗАМЕР) Световая координата. Добавка β к уже стоящему
            // `driftLight`: при закреплённом β — ровно столько, чтобы итог был
            // равен заданному; при свободном — то же как запасной ход, а
            // подбор ниже, 3×3 по базису {1, x, S}. Прямая при закреплённом β
            // считается по Y' = Y − β·S — та же прямая `AMBER17`, только
            // свет вычтен из измерения.
            bool lightOn = this.lightShiftChannels != null;
            double betaFixedAdd = lightOn ? this.AnchorLightBeta - this.driftLight : 0.0;
            foreach (AnchorFit f in fits)
            {
                f.YEff = f.Y - betaFixedAdd * f.S;
            }

            while (true)
            {
                beta = betaFixedAdd;
                double swxx = 0.0, swxy = 0.0, sw = 0.0, swx = 0.0, swy = 0.0;
                double xMin = double.MaxValue, xMax = double.MinValue;
                foreach (AnchorFit f in fits)
                {
                    sw += f.Weight;
                    swx += f.Weight * f.X;
                    swy += f.Weight * f.YEff;
                    swxx += f.Weight * f.X * f.X;
                    swxy += f.Weight * f.X * f.YEff;
''')))

pairs.append((L('''                double a1 = swxy / swxx;
                double chi1 = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double r = f.Y - a1 * f.X;
                    chi1 += f.Weight * r * r;
                }
'''), L('''                double a1 = swxy / swxx;
                double chi1 = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double r = f.YEff - a1 * f.X;
                    chi1 += f.Weight * r * r;
                }
''')))

pairs.append((L('''                        double chi2 = 0.0;
                        foreach (AnchorFit f in fits)
                        {
                            double r = f.Y - a2 * f.X - b2;
                            chi2 += f.Weight * r * r;
                        }
'''), L('''                        double chi2 = 0.0;
                        foreach (AnchorFit f in fits)
                        {
                            double r = f.YEff - a2 * f.X - b2;
                            chi2 += f.Weight * r * r;
                        }
''')))

# П14-квадрат: те же Y → YEff (степень 1 умолчанием, блок не включается, но пусть согласован)
pairs.append((L('''                    double chiLine = 0.0;
                    foreach (AnchorFit f in fits)
                    {
                        double r = f.Y - a * f.X - b;
                        chiLine += f.Weight * r * r;
                    }
'''), L('''                    double chiLine = 0.0;
                    foreach (AnchorFit f in fits)
                    {
                        double r = f.YEff - a * f.X - b;
                        chiLine += f.Weight * r * r;
                    }
''')))
pairs.append((L('''                            v[i] += f.Weight * basis[i] * f.Y;
'''), L('''                            v[i] += f.Weight * basis[i] * f.YEff;
''')))
pairs.append((L('''                        double chi3 = 0.0;
                        foreach (AnchorFit f in fits)
                        {
                            double r = f.Y - a3 * f.X - b3 - c3 * f.X * f.X;
                            chi3 += f.Weight * r * r;
                        }
'''), L('''                        double chi3 = 0.0;
                        foreach (AnchorFit f in fits)
                        {
                            double r = f.YEff - a3 * f.X - b3 - c3 * f.X * f.X;
                            chi3 += f.Weight * r * r;
                        }
''')))

# 7. свободный β — после блока квадрата, перед отсевом
pairs.append((L('''                if (fits.Count <= 2)
                {
                    break;
                }

                AnchorFit worst = null;
                double worstPull = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double pull = Math.Abs(f.Y - a * f.X - b - c * f.X * f.X) * Math.Sqrt(f.Weight);
'''), L('''                // (П16, ЗАМЕР) Свободный β — тем же взвешенным МНК по базису
                // {1, x, S} (x в долях верхнего канала, S в долях своего
                // максимума), при плече и опорах не меньше порога; принимается
                // за выигрыш χ² опор против прямой при запасном β.
                lightRefused = null;
                if (lightOn && this.AnchorLightBetaFree && lever
                    && fits.Count >= Math.Max(3, this.AnchorLightMinAnchors))
                {
                    double chiFixed = 0.0;
                    double sMax = 0.0;
                    foreach (AnchorFit f in fits)
                    {
                        double r = f.Y - a * f.X - b - beta * f.S;
                        chiFixed += f.Weight * r * r;
                        sMax = Math.Max(sMax, Math.Abs(f.S));
                    }

                    if (sMax > 1.0E-9)
                    {
                        double xs = xMax;
                        double[,] m = new double[3, 3];
                        double[] v = new double[3];
                        foreach (AnchorFit f in fits)
                        {
                            double[] basis = { 1.0, f.X / xs, f.S / sMax };
                            for (int i = 0; i < 3; i++)
                            {
                                v[i] += f.Weight * basis[i] * f.Y;
                                for (int j = 0; j < 3; j++)
                                {
                                    m[i, j] += f.Weight * basis[i] * basis[j];
                                }
                            }
                        }

                        double[] sol = Solve3(m, v);
                        if (sol != null)
                        {
                            double b3 = sol[0];
                            double a3 = sol[1] / xs;
                            double beta3 = sol[2] / sMax;
                            double chi3 = 0.0;
                            foreach (AnchorFit f in fits)
                            {
                                double r = f.Y - a3 * f.X - b3 - beta3 * f.S;
                                chi3 += f.Weight * r * r;
                            }

                            if (chiFixed - chi3 >= this.AnchorLightMinDeltaChi2)
                            {
                                a = a3;
                                b = b3;
                                beta = beta3;
                                how = "усиление, ноль и β";
                            }
                            else
                            {
                                lightRefused = string.Format(CultureInfo.InvariantCulture,
                                    "β отвергнут: Δχ² опор {0:F2} < порога", chiFixed - chi3);
                            }
                        }
                        else
                        {
                            lightRefused = "β отвергнут: вырожденный МНК";
                        }
                    }
                    else
                    {
                        lightRefused = "β отвергнут: S = 0 у всех опор";
                    }
                }
                else if (lightOn && this.AnchorLightBetaFree)
                {
                    lightRefused = lever ? "β не подбирался: опор меньше порога" : "β не подбирался: нет плеча";
                }

                if (fits.Count <= 2)
                {
                    break;
                }

                AnchorFit worst = null;
                double worstPull = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double pull = Math.Abs(f.Y - a * f.X - b - c * f.X * f.X - beta * f.S) * Math.Sqrt(f.Weight);
''')))

# 8. заметка
pairs.append((L('''            if (polyRefused != null)
            {
                note += "; " + polyRefused;
            }

            return anchors;
        }
'''), L('''            if (polyRefused != null)
            {
                note += "; " + polyRefused;
            }

            if (lightOn)
            {
                note += string.Format(CultureInfo.InvariantCulture,
                                      "; свет {0}: β+ {1:F3} (итого {2:F3})",
                                      this.AnchorLightCurve, beta, this.driftLight + beta);
                if (lightRefused != null)
                {
                    note += "; " + lightRefused;
                }
            }

            return anchors;
        }
''')))

# 9. AnchorFit — поля
pairs.append((L('''        sealed class AnchorFit
        {
            public FsaScaleAnchor Anchor;
            public double X;
            public double Y;
            public double Weight;
        }
'''), L('''        sealed class AnchorFit
        {
            public FsaScaleAnchor Anchor;
            public double X;
            public double Y;
            public double Weight;

            /// <summary>(П16) Световая координата S(X), каналы, при β = 1.</summary>
            public double S;

            /// <summary>(П16) Y − β_закр·S — измерение с вычтенным закреплённым светом.</summary>
            public double YEff;
        }
''')))

# 10. умолчания конструктора
pairs.append((L('''            this.AnchorPolyMonotonic = true;
'''), L('''            this.AnchorPolyMonotonic = true;
            // (П16) световая координата ВЫКЛЮЧЕНА умолчанием — решение Amber 11.09.2026 «замер»
            this.AnchorLightCurve = null;
            this.AnchorLightBeta = 1.0;
            this.AnchorLightBetaFree = false;
            this.AnchorLightMinAnchors = 3;
            this.AnchorLightMinDeltaChi2 = 0.0;
            this.AnchorLightReferenceKev = 661.657;
''')))

# 11. сброс и таблица S(p)
pairs.append((L('''            this.deposits.Clear();
            this.driftQuad = 0.0;
'''), L('''            this.deposits.Clear();
            this.driftQuad = 0.0;
            this.driftLight = 0.0;
            this.lightShiftChannels = null;
            this.lightShiftMax = 0.0;
''')))
pairs.append((L('''            bool narrowFit = this.Band == FsaBandMode.FitToLibrary;
'''), L('''
            // (П16, ЗАМЕР) Таблица световой координаты S(p) по каналам при
            // β = 1: канал i → энергия E → E + s(E) → канал; s(E) из кривой
            // <see cref="AnchorLightCurve"/>. Считается один раз на разбор.
            if (!string.IsNullOrEmpty(this.AnchorLightCurve))
            {
                double[] table = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    double e = calibration.ChannelToEnergy(i);
                    double s = FsaLightScale.ShiftKev(this.AnchorLightCurve, e, this.AnchorLightReferenceKev);
                    double p = Finite(s) && e + s > 0.0 ? EnergyToChannelSafe(calibration, e + s, channels) : double.NaN;
                    double d = Finite(p) ? p - i : 0.0;
                    table[i] = d;
                    this.lightShiftMax = Math.Max(this.lightShiftMax, Math.Abs(d));
                }

                this.lightShiftChannels = table;
            }

            bool narrowFit = this.Band == FsaBandMode.FitToLibrary;
''')))

# 12. цикл привязки в Analyze
pairs.append((L('''                        double a, b, c;
                        int used;
                        string note;
                        List<FsaScaleAnchor> anchors = this.CollectScaleAnchors(
                            best, calibration, fwhmCalibration, bestGain, bestOffset,
                            chLo, chHi, channels, y, variance, channelsPerKev,
                            out a, out b, out c, out used, out note);
'''), L('''                        double a, b, c, beta;
                        int used;
                        string note;
                        List<FsaScaleAnchor> anchors = this.CollectScaleAnchors(
                            best, calibration, fwhmCalibration, bestGain, bestOffset,
                            chLo, chHi, channels, y, variance, channelsPerKev,
                            out a, out b, out c, out beta, out used, out note);
''')))
pairs.append((L('''                        double quad = a * this.driftQuad + c * bestGain * bestGain;

                        // Сошлось — дальше не двигаемся: сдвиг меньше двадцатой
                        // канала на всём диапазоне полосы.
                        bool converged = Math.Abs(b) < 0.05
                                         && Math.Abs(a - 1.0) * Math.Max(1, chHi) < 0.05
                                         && Math.Abs(c) * (double)chHi * chHi < 0.05;
'''), L('''                        double quad = a * this.driftQuad + c * bestGain * bestGain;
                        // (П16) световая координата: p'' = a·p' + b + β⁺·S(p'), S(p') ≈ S(p)
                        double light = a * this.driftLight + beta;

                        // Сошлось — дальше не двигаемся: сдвиг меньше двадцатой
                        // канала на всём диапазоне полосы.
                        bool converged = Math.Abs(b) < 0.05
                                         && Math.Abs(a - 1.0) * Math.Max(1, chHi) < 0.05
                                         && Math.Abs(c) * (double)chHi * chHi < 0.05
                                         && Math.Abs(beta) * this.lightShiftMax < 0.05;
''')))
pairs.append((L('''                        double quadBefore = this.driftQuad;
                        this.driftQuad = quad;
                        FitResult moved = FitHuber(library, fixedColumns, calibration, fwhmCalibration,
                                                   efficiency, gain, offset, chLo, chHi, channels,
                                                   y, variance, baseWeights, reportWeights, null);
                        if (moved == null)
                        {
                            this.driftQuad = quadBefore;
'''), L('''                        double quadBefore = this.driftQuad;
                        double lightBefore = this.driftLight;
                        this.driftQuad = quad;
                        this.driftLight = light;
                        FitResult moved = FitHuber(library, fixedColumns, calibration, fwhmCalibration,
                                                   efficiency, gain, offset, chLo, chHi, channels,
                                                   y, variance, baseWeights, reportWeights, null);
                        if (moved == null)
                        {
                            this.driftQuad = quadBefore;
                            this.driftLight = lightBefore;
''')))
pairs.append((L('''                            this.scaleAnchors = this.CollectScaleAnchors(
                                best, calibration, fwhmCalibration, bestGain, bestOffset,
                                chLo, chHi, channels, y, variance, channelsPerKev,
                                out a, out b, out c, out used, out note);
                            break;
'''), L('''                            this.scaleAnchors = this.CollectScaleAnchors(
                                best, calibration, fwhmCalibration, bestGain, bestOffset,
                                chLo, chHi, channels, y, variance, channelsPerKev,
                                out a, out b, out c, out beta, out used, out note);
                            this.anchorNote = note;
                            break;
''')))
pairs.append((L('''                            "опор {0}; усиление {1:F5}, ноль {2:F2} кэВ ({3:F3} кан.); квадрат {4:E3} кан⁻¹, стрелка {5:F2} кэВ",
                            movedBy, bestGain, bestOffset / channelsPerKev, bestOffset,
                            this.driftQuad, this.driftQuad * chHi * (double)chHi / 4.0 / channelsPerKev);
'''), L('''                            "опор {0}; усиление {1:F5}, ноль {2:F2} кэВ ({3:F3} кан.); квадрат {4:E3} кан⁻¹, стрелка {5:F2} кэВ; свет {6}: β {7:F3}; {8}",
                            movedBy, bestGain, bestOffset / channelsPerKev, bestOffset,
                            this.driftQuad, this.driftQuad * chHi * (double)chHi / 4.0 / channelsPerKev,
                            this.AnchorLightCurve ?? "выкл", this.driftLight, this.anchorNote);
''')))

# 13. результат
pairs.append((L('''                AnchorPolyDegree = this.AnchorPolyDegree,
'''), L('''                AnchorPolyDegree = this.AnchorPolyDegree,
                AnchorLightCurve = this.AnchorLightCurve ?? "",
                AnchorLightBeta = this.driftLight,
                AnchorLightFree = this.AnchorLightBetaFree,
''')))

# 14. статический класс кривых — в конец файла
pairs.append((L('''            return (int)Math.Round(channel);
        }
    }
}
'''), L('''            return (int)Math.Round(channel);
        }
    }

    /// <summary>
    /// (П16 11.09.2026, ЗАМЕР) Кривые относительного света на фотон r(E)
    /// (свет пика полного поглощения на кэВ линии) для световой координаты
    /// привязки шкалы. Интерполяция линейна по ln E, вне таблицы — крайние
    /// значения. Сдвиг положения пика в шкале, откалиброванной по E₀:
    /// s(E) = E·r(E)/r(E₀) − E.
    ///
    /// "model" — собственная фотонная кривая модели: `LightScaleProbe
    /// --geometry=G1S_point5.in --n=200000`, П14 11.09.2026
    /// (`handover/p14-anchor-poly/lightscale_g1s_point5.txt`); нормировка —
    /// свет ЭЛЕКТРОНА 662 кэВ = 1, потому 662 даёт 1.0091 (свет фотона делится
    /// между несколькими электронами меньшей энергии). Для замера — таблицей;
    /// в приложении её место — кривая своей геометрии из матрицы.
    ///
    /// "lit" — Ходюк, Родный, Доренбос 2010 (JAP 107, 113513; arXiv:1102.3799),
    /// ДОСЛОВНО из текста, нормировка 662 кэВ = 1: 9 кэВ 111.5 %, 20 кэВ
    /// 117.2 %, минимум K-провала 114.1 % на 34.5 кэВ, 50 кэВ 115.8 %, 100 кэВ
    /// 111.2 % (между 50 и 100 — линейно, по тексту); 10 кэВ 112 % — Ходюк—
    /// Доренбос 2012 (arXiv:1204.4350), табл. I. Точки 30 (1.170), 33 (1.160),
    /// 45 (1.158) — достройка по словам «drop in the range 30–45 keV». Выше
    /// 100 кэВ у Ходюка чисел нет (синхротрон 9…100 кэВ): взята форма модели,
    /// растянутая множителем k = 0.112/(r_model(100)/r_model(662) − 1) = 2.857,
    /// чтобы на 100 кэВ сойтись с 111.2 % — то есть «модель занижает величину,
    /// но не форму». Обе достройки названы, потому что от них зависит всё
    /// выше 100 кэВ.
    /// </summary>
    internal static class FsaLightScale
    {
        static readonly double[][] Model =
        {
            new[] { 22.0, 1.1321 }, new[] { 31.0, 1.1020 }, new[] { 35.0, 1.1062 }, new[] { 40.0, 1.1042 },
            new[] { 53.0, 1.0887 }, new[] { 59.5, 1.0747 }, new[] { 81.0, 1.0601 }, new[] { 88.0, 1.0550 },
            new[] { 122.0, 1.0388 }, new[] { 166.0, 1.0285 }, new[] { 245.0, 1.0206 }, new[] { 344.0, 1.0161 },
            new[] { 356.0, 1.0157 }, new[] { 511.0, 1.0116 }, new[] { 662.0, 1.0091 }, new[] { 835.0, 1.0073 },
            new[] { 964.0, 1.0062 }, new[] { 1173.0, 1.0051 }, new[] { 1332.0, 1.0044 }, new[] { 1408.0, 1.0042 },
            new[] { 2614.0, 1.0021 },
        };

        static readonly double[][] KhodyukLow =
        {
            new[] { 9.0, 1.115 }, new[] { 10.0, 1.12 }, new[] { 20.0, 1.172 }, new[] { 30.0, 1.170 },
            new[] { 33.0, 1.160 }, new[] { 34.5, 1.141 }, new[] { 45.0, 1.158 }, new[] { 50.0, 1.158 },
            new[] { 100.0, 1.112 },
        };

        const double ModelAt662 = 1.0091;

        static double Interp(double[][] table, double e)
        {
            if (!(e > 0.0) || e <= table[0][0])
            {
                return table[0][1];
            }

            int last = table.Length - 1;
            if (e >= table[last][0])
            {
                return table[last][1];
            }

            for (int i = 1; i <= last; i++)
            {
                if (e <= table[i][0])
                {
                    double e0 = table[i - 1][0], r0 = table[i - 1][1];
                    double e1 = table[i][0], r1 = table[i][1];
                    double t = (Math.Log(e) - Math.Log(e0)) / (Math.Log(e1) - Math.Log(e0));
                    return r0 + (r1 - r0) * t;
                }
            }

            return table[last][1];
        }

        /// <summary>r(E) кривой по имени; NaN — имя неизвестно.</summary>
        public static double Relative(string curve, double e)
        {
            if (curve == "model")
            {
                return Interp(Model, e);
            }

            if (curve == "lit")
            {
                if (e <= 100.0)
                {
                    return Interp(KhodyukLow, e);
                }

                double k = 0.112 / (Interp(Model, 100.0) / ModelAt662 - 1.0);
                return 1.0 + k * (Interp(Model, e) / ModelAt662 - 1.0);
            }

            return double.NaN;
        }

        /// <summary>s(E) = E·r(E)/r(E₀) − E, кэВ; NaN при неизвестной кривой.</summary>
        public static double ShiftKev(string curve, double e, double e0)
        {
            double r = Relative(curve, e);
            double r0 = Relative(curve, e0);
            if (double.IsNaN(r) || double.IsNaN(r0) || !(r0 > 0.0))
            {
                return double.NaN;
            }

            return e * r / r0 - e;
        }

        /// <summary>
        /// (П16) S в каналах для положения <paramref name="position"/> по
        /// калибровке спектра — тот же счёт, что у таблицы анализатора; для
        /// <see cref="FsaLineAudit"/>.
        /// </summary>
        public static double ShiftChannels(string curve, EnergyCalibration calibration, double position, int channels, double e0)
        {
            if (string.IsNullOrEmpty(curve) || calibration == null)
            {
                return 0.0;
            }

            double e = calibration.ChannelToEnergy(position);
            double s = ShiftKev(curve, e, e0);
            if (double.IsNaN(s) || !(e + s > 0.0))
            {
                return 0.0;
            }

            try
            {
                double p = calibration.EnergyToChannel(e + s, maxChannels: channels);
                return double.IsNaN(p) || double.IsInfinity(p) ? 0.0 : p - position;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }
    }
}
''')))

patch(A, pairs)

# ---------------------------------------------------------------- FsaResult.cs
patch('BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs', [
    (L('''        /// <summary>(П14) Степень многочлена привязки, с которой считалось.</summary>
        public int AnchorPolyDegree { get; set; }
'''), L('''        /// <summary>(П14) Степень многочлена привязки, с которой считалось.</summary>
        public int AnchorPolyDegree { get; set; }

        /// <summary>(П16, ЗАМЕР) Кривая света световой координаты привязки ("model"/"lit"); пусто — выключена.</summary>
        public string AnchorLightCurve { get; set; }

        /// <summary>(П16) Итоговый β световой координаты (0 при выключенной).</summary>
        public double AnchorLightBeta { get; set; }

        /// <summary>(П16) β подбирался (true) или закреплён ключом (false).</summary>
        public bool AnchorLightFree { get; set; }
''')),
])

# ---------------------------------------------------------------- FsaLineAudit.cs
patch('BecquerelMonitor/FullSpectrumAnalysis/FsaLineAudit.cs', [
    (L('''                                               result.Gain, result.OffsetChannels,
                                               result.AnchorQuadPerChannel);
'''), L('''                                               result.Gain, result.OffsetChannels,
                                               result.AnchorQuadPerChannel,
                                               result.AnchorLightCurve, result.AnchorLightBeta);
''')),
    (L('''                                     double gain, double offset, double quad)
        {
'''), L('''                                     double gain, double offset, double quad,
                                     string lightCurve, double lightBeta)
        {
''')),
    (L('''                // (П14) квадратичный член привязки — тот же, что у DriftPosition.
                double p = gain * position + offset + quad * position * position;
'''), L('''                // (П14) квадратичный член привязки — тот же, что у DriftPosition.
                // (П16) световая координата — тот же счёт, что у таблицы анализатора.
                double p = gain * position + offset + quad * position * position
                           + (lightBeta != 0.0
                              ? lightBeta * FsaLightScale.ShiftChannels(lightCurve, calibration, position, channels, 661.657)
                              : 0.0);
''')),
])

# ---------------------------------------------------------------- CorpusFsaProbe.cs
patch('tools/effmaker/probes/CorpusFsaProbe.cs', [
    (L('''                if (a.StartsWith("--anchor-poly-mono=", StringComparison.Ordinal))
                {
                    o.AnchorPolyMono = int.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }
'''), L('''                if (a.StartsWith("--anchor-poly-mono=", StringComparison.Ordinal))
                {
                    o.AnchorPolyMono = int.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }
                // (П16 11.09.2026, ЗАМЕР) световая координата привязки: кривая
                // (0 — выкл, model, lit), β (free — подбирать, число — закрепить),
                // порог опор для подбора и порог выигрыша χ² опор.
                if (a.StartsWith("--anchor-light=", StringComparison.Ordinal))
                {
                    string v = a.Substring(15);
                    o.AnchorLight = v == "0" || v == "off" ? "" : v;
                    continue;
                }
                if (a.StartsWith("--anchor-beta=", StringComparison.Ordinal))
                {
                    string v = a.Substring(14);
                    if (v == "free")
                    {
                        o.AnchorBetaFree = true;
                    }
                    else
                    {
                        o.AnchorBeta = double.Parse(v, CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                if (a.StartsWith("--anchor-beta-min=", StringComparison.Ordinal))
                {
                    o.AnchorBetaMin = int.Parse(a.Substring(18), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--anchor-beta-dchi=", StringComparison.Ordinal))
                {
                    o.AnchorBetaDchi = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }
''')),
    (L('''            if (o.AnchorPolyMono >= 0)
            {
                analyzer.AnchorPolyMonotonic = o.AnchorPolyMono != 0;
            }
'''), L('''            if (o.AnchorPolyMono >= 0)
            {
                analyzer.AnchorPolyMonotonic = o.AnchorPolyMono != 0;
            }

            if (o.AnchorLight != null)
            {
                analyzer.AnchorLightCurve = o.AnchorLight == "" ? null : o.AnchorLight;
            }

            if (o.AnchorBetaFree)
            {
                analyzer.AnchorLightBetaFree = true;
            }

            if (!double.IsNaN(o.AnchorBeta))
            {
                analyzer.AnchorLightBeta = o.AnchorBeta;
            }

            if (o.AnchorBetaMin > 0)
            {
                analyzer.AnchorLightMinAnchors = o.AnchorBetaMin;
            }

            if (o.AnchorBetaDchi >= 0.0)
            {
                analyzer.AnchorLightMinDeltaChi2 = o.AnchorBetaDchi;
            }
''')),
    (L('''                row.AnchorQuad = result.AnchorQuadPerChannel;
                row.AnchorSagKev = result.AnchorSagittaKev;
'''), L('''                row.AnchorQuad = result.AnchorQuadPerChannel;
                row.AnchorSagKev = result.AnchorSagittaKev;
                row.AnchorLight = result.AnchorLightCurve ?? "";
                row.AnchorBeta = result.AnchorLightBeta;
                row.AnchorBetaFree = result.AnchorLightFree;
''')),
    (L('''                                   + "anchor_quad,anchor_sag_kev");
'''), L('''                                   + "anchor_quad,anchor_sag_kev,"
                                   // (П16, ЗАМЕР) световая координата: кривая, β, подбирался ли
                                   + "anchor_light,anchor_beta,anchor_beta_free");
''')),
    (L('''                            r.AnchorQuad.ToString("E4", CultureInfo.InvariantCulture),
                            F(r.AnchorSagKev, "F3")));
'''), L('''                            r.AnchorQuad.ToString("E4", CultureInfo.InvariantCulture),
                            F(r.AnchorSagKev, "F3"),
                            Csv(r.AnchorLight ?? ""),
                            F(r.AnchorBeta, "F4"),
                            r.AnchorBetaFree ? "1" : "0"));
''')),
    (L('''            public int AnchorPolyMono = -1;      // (П14) 1/0 — заслон монотонности; -1 — умолчание
'''), L('''            public int AnchorPolyMono = -1;      // (П14) 1/0 — заслон монотонности; -1 — умолчание
            public string AnchorLight = null;    // (П16) кривая световой координаты; null — умолчание анализатора
            public double AnchorBeta = double.NaN; // (П16) закреплённый β / запасной при free
            public bool AnchorBetaFree = false;  // (П16) подбирать β
            public int AnchorBetaMin = -1;
            public double AnchorBetaDchi = -1.0;
''')),
    (L('''            public double AnchorQuad;      // (П14) квадратичный член, кан⁻¹
            public double AnchorSagKev;    // (П14) стрелка над хордой полосы, кэВ
'''), L('''            public double AnchorQuad;      // (П14) квадратичный член, кан⁻¹
            public double AnchorSagKev;    // (П14) стрелка над хордой полосы, кэВ
            public string AnchorLight;     // (П16) кривая световой координаты
            public double AnchorBeta;      // (П16) итоговый β
            public bool AnchorBetaFree;    // (П16) β подбирался
''')),
])
print('ГОТОВО')
