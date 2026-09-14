# -*- coding: utf-8 -*-
# П14 11.09.2026: квадратичная привязка шкалы как ЗАМЕР (умолчание степень 1 — не менять)
import io, sys, os
sys.stdout.reconfigure(encoding='utf-8')
os.chdir(r'C:\Users\moroz\bqp14')
def patch(p, pairs):
    s=io.open(p,encoding='utf-8-sig',newline='').read()
    crlf = '\r\n' in s
    for old,new in pairs:
        if crlf:
            old=old.replace('\n','\r\n'); new=new.replace('\n','\r\n')
        assert s.count(old)==1, (p, old[:80], s.count(old))
        s=s.replace(old,new)
    io.open(p,'w',encoding='utf-8-sig',newline='').write(s)

FA='BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs'
patch(FA, [
# --- поле квадратичного члена
('''        int scaleAnchorsUsed;

        string anchorNote;
''',
'''        int scaleAnchorsUsed;

        string anchorNote;

        /// <summary>
        /// (П14 11.09.2026, ЗАМЕР) Квадратичный член шкалы, каналы⁻¹:
        /// p″ = g·p + o + q·p². Нуль, пока привязка степени 2
        /// (<see cref="AnchorPolyDegree"/>) его не подобрала. Поле, а не
        /// параметр: усиление и ноль едут по всем построителям параметрами,
        /// и тянуть третий сквозь сорок сигнатур ради замера нельзя; читается
        /// ТОЛЬКО в <see cref="DriftPosition"/>, сбрасывается в начале
        /// <see cref="Analyze"/>.
        /// </summary>
        double driftQuad;
'''),
# --- свойства
('''        public int AnchorOffsetMinAnchors { get; set; }
''',
'''        public int AnchorOffsetMinAnchors { get; set; }

        /// <summary>
        /// (П14 11.09.2026, ЗАМЕР — умолчание 1, ⛔ НЕ МЕНЯТЬ без решения
        /// Amber «сначала замер, умолчание не менять») Степень многочлена
        /// привязки: 1 — прямая (усиление и ноль, `AMBER17`); 2 — ещё и
        /// квадратичный член, подбираемый тем же взвешенным МНК при числе
        /// принятых опор не меньше <see cref="AnchorPolyMinAnchors"/>, плече
        /// опор ≥ 2× и выигрыше χ² опор не меньше
        /// <see cref="AnchorPolyMinDeltaChi2"/>; иначе — как 1. 0 — опоры
        /// собираются и печатаются, шкала НЕ двигается (ряд «модель без
        /// привязки» для замера нелинейности). Рычаг — `--anchor-poly=`.
        /// </summary>
        public int AnchorPolyDegree { get; set; }

        /// <summary>
        /// (П14) Меньше стольких принятых опор — квадратичный член не
        /// подбирается. При трёх опорах парабола — интерполяция: ошибка любой
        /// опоры уходит в шкалу целиком. Развёртка 3/4/5 — `--anchor-poly-min=`.
        /// </summary>
        public int AnchorPolyMinAnchors { get; set; }

        /// <summary>
        /// (П14) Квадратичный член принимается, только если снижает χ² опор не
        /// меньше чем на столько против лучшей прямой — то же двухсигмовое
        /// правило, что у нуля. 0 — принимать всегда при достаточном числе
        /// опор. Рычаг — `--anchor-poly-dchi=`.
        /// </summary>
        public double AnchorPolyMinDeltaChi2 { get; set; }
'''),
# --- умолчания
('''            this.AnchorOffsetMinAnchors = 2;
''',
'''            this.AnchorOffsetMinAnchors = 2;
            this.AnchorPolyDegree = 1;
            this.AnchorPolyMinAnchors = 4;
            this.AnchorPolyMinDeltaChi2 = 4.0;
'''),
# --- DriftPosition
('''        double DriftPosition(double position, double gain, double offset)
        {
            return gain * position + offset;
        }''',
'''        double DriftPosition(double position, double gain, double offset)
        {
            // (П14) квадратичный член — поле, см. `driftQuad`; умолчанием нуль.
            return gain * position + offset + this.driftQuad * position * position;
        }'''),
# --- сброс в начале Analyze
('''            this.depositChannels = null;
            this.depositChannelsCalibration = null;
            this.kernelBank = null;
            this.deposits.Clear();''',
'''            this.depositChannels = null;
            this.depositChannelsCalibration = null;
            this.kernelBank = null;
            this.deposits.Clear();
            this.driftQuad = 0.0;'''),
# --- сигнатура CollectScaleAnchors
('''                                                 double[] y, double[] variance, double channelsPerKev,
                                                 out double a, out double b, out int used, out string note)
        {
            a = 1.0;
            b = 0.0;
            used = 0;''',
'''                                                 double[] y, double[] variance, double channelsPerKev,
                                                 out double a, out double b, out double c,
                                                 out int used, out string note)
        {
            a = 1.0;
            b = 0.0;
            c = 0.0;
            used = 0;'''),
# --- МНК: квадратичная ветка после выбора нуля
('''                        if (chi1 - chi2 >= 4.0)
                        {
                            a = a2;
                            b = b2;
                            how = "усиление и ноль";
                        }
                    }
                }

                if (fits.Count <= 2)
                {
                    break;
                }

                AnchorFit worst = null;
                double worstPull = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double pull = Math.Abs(f.Y - a * f.X - b) * Math.Sqrt(f.Weight);''',
'''                        if (chi1 - chi2 >= 4.0)
                        {
                            a = a2;
                            b = b2;
                            how = "усиление и ноль";
                        }
                    }
                }

                // (П14, ЗАМЕР) Квадратичный член — тем же взвешенным МНК, при
                // плече, при числе опор не меньше порога и только за выигрыш
                // χ² опор против лучшей прямой (то же правило, что у нуля).
                c = 0.0;
                if (this.AnchorPolyDegree >= 2 && lever
                    && fits.Count >= Math.Max(3, this.AnchorPolyMinAnchors))
                {
                    double chiLine = 0.0;
                    foreach (AnchorFit f in fits)
                    {
                        double r = f.Y - a * f.X - b;
                        chiLine += f.Weight * r * r;
                    }

                    // Нормальные уравнения 3×3 по базису {1, x, x²}, x в
                    // долях верхнего канала — иначе матрица вырождена по
                    // обусловленности (x² ~ 10⁶).
                    double xs = xMax;
                    double[,] m = new double[3, 3];
                    double[] v = new double[3];
                    foreach (AnchorFit f in fits)
                    {
                        double x = f.X / xs;
                        double[] basis = { 1.0, x, x * x };
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
                        double c3 = sol[2] / (xs * xs);
                        double chi3 = 0.0;
                        foreach (AnchorFit f in fits)
                        {
                            double r = f.Y - a3 * f.X - b3 - c3 * f.X * f.X;
                            chi3 += f.Weight * r * r;
                        }

                        if (chiLine - chi3 >= this.AnchorPolyMinDeltaChi2)
                        {
                            a = a3;
                            b = b3;
                            c = c3;
                            how = "усиление, ноль и квадрат";
                        }
                    }
                }

                if (fits.Count <= 2)
                {
                    break;
                }

                AnchorFit worst = null;
                double worstPull = 0.0;
                foreach (AnchorFit f in fits)
                {
                    double pull = Math.Abs(f.Y - a * f.X - b - c * f.X * f.X) * Math.Sqrt(f.Weight);'''),
# --- note с квадратом
('''            used = fits.Count;
            note = string.Format(CultureInfo.InvariantCulture,
                                 "опор {0} из {1} (промахов {6}), {2}: a {3:F5}, b {4:F3} кан. ({5:F2} кэВ)",
                                 used, anchors.Count, how, a, b, b / channelsPerKev, outliers);
            return anchors;
        }''',
'''            used = fits.Count;
            note = string.Format(CultureInfo.InvariantCulture,
                                 "опор {0} из {1} (промахов {6}), {2}: a {3:F5}, b {4:F3} кан. ({5:F2} кэВ), c {7:E3} кан⁻¹",
                                 used, anchors.Count, how, a, b, b / channelsPerKev, outliers, c);
            return anchors;
        }

        /// <summary>(П14) Решение 3×3 методом Гаусса с выбором ведущего; null при вырождении.</summary>
        static double[] Solve3(double[,] m, double[] v)
        {
            double[,] a = (double[,])m.Clone();
            double[] b = (double[])v.Clone();
            for (int col = 0; col < 3; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 3; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col]))
                    {
                        piv = r;
                    }
                }

                if (!(Math.Abs(a[piv, col]) > 1e-300))
                {
                    return null;
                }

                if (piv != col)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        double t = a[col, k];
                        a[col, k] = a[piv, k];
                        a[piv, k] = t;
                    }

                    double tb = b[col];
                    b[col] = b[piv];
                    b[piv] = tb;
                }

                for (int r = 0; r < 3; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }

                    double f = a[r, col] / a[col, col];
                    for (int k = col; k < 3; k++)
                    {
                        a[r, k] -= f * a[col, k];
                    }

                    b[r] -= f * b[col];
                }
            }

            double[] x = new double[3];
            for (int i = 0; i < 3; i++)
            {
                x[i] = b[i] / a[i, i];
                if (double.IsNaN(x[i]) || double.IsInfinity(x[i]))
                {
                    return null;
                }
            }

            return x;
        }'''),
# --- цикл привязки в Analyze
('''                    for (int pass = 0; pass < passes; pass++)
                    {
                        double a, b;
                        int used;
                        string note;
                        List<FsaScaleAnchor> anchors = this.CollectScaleAnchors(
                            best, calibration, fwhmCalibration, bestGain, bestOffset,
                            chLo, chHi, channels, y, variance, channelsPerKev,
                            out a, out b, out used, out note);
                        this.scaleAnchors = anchors;
                        this.anchorNote = note;
                        if (used == 0)
                        {
                            break;
                        }

                        // Новая шкала поверх прежней: p'' = a·(g·p + o) + b.
                        double gain = a * bestGain;
                        double offset = a * bestOffset + b;

                        // Сошлось — дальше не двигаемся: сдвиг меньше двадцатой
                        // канала на всём диапазоне полосы.
                        bool converged = Math.Abs(b) < 0.05
                                         && Math.Abs(a - 1.0) * Math.Max(1, chHi) < 0.05;''',
'''                    for (int pass = 0; pass < passes; pass++)
                    {
                        double a, b, c;
                        int used;
                        string note;
                        List<FsaScaleAnchor> anchors = this.CollectScaleAnchors(
                            best, calibration, fwhmCalibration, bestGain, bestOffset,
                            chLo, chHi, channels, y, variance, channelsPerKev,
                            out a, out b, out c, out used, out note);
                        this.scaleAnchors = anchors;
                        this.anchorNote = note;
                        if (used == 0)
                        {
                            break;
                        }

                        // (П14, ЗАМЕР) Степень 0 — опоры собраны и напечатаны,
                        // шкала не двигается: ряд «модель без привязки».
                        if (this.AnchorPolyDegree <= 0)
                        {
                            this.anchorNote = note + "; степень 0 — шкала не тронута (замер)";
                            break;
                        }

                        // Новая шкала поверх прежней: p'' = a·p' + b + c·p'²,
                        // p' = g·p + o + q·p²; члены c·q отброшены (оба ~1e-5).
                        double gain = a * bestGain + 2.0 * c * bestGain * bestOffset;
                        double offset = a * bestOffset + b + c * bestOffset * bestOffset;
                        double quad = a * this.driftQuad + c * bestGain * bestGain;

                        // Сошлось — дальше не двигаемся: сдвиг меньше двадцатой
                        // канала на всём диапазоне полосы.
                        bool converged = Math.Abs(b) < 0.05
                                         && Math.Abs(a - 1.0) * Math.Max(1, chHi) < 0.05
                                         && Math.Abs(c) * (double)chHi * chHi < 0.05;'''),
('''                        FitResult moved = FitHuber(library, fixedColumns, calibration, fwhmCalibration,
                                                   efficiency, gain, offset, chLo, chHi, channels,
                                                   y, variance, baseWeights, reportWeights, null);
                        if (moved == null)
                        {
                            this.anchorNote = note + "; фит на новой шкале не удался, шкала прежняя";
                            break;
                        }

                        best = moved;
                        bestGain = gain;
                        bestOffset = offset;
                        movedBy = used;''',
'''                        double quadBefore = this.driftQuad;
                        this.driftQuad = quad;
                        FitResult moved = FitHuber(library, fixedColumns, calibration, fwhmCalibration,
                                                   efficiency, gain, offset, chLo, chHi, channels,
                                                   y, variance, baseWeights, reportWeights, null);
                        if (moved == null)
                        {
                            this.driftQuad = quadBefore;
                            this.anchorNote = note + "; фит на новой шкале не удался, шкала прежняя";
                            break;
                        }

                        best = moved;
                        bestGain = gain;
                        bestOffset = offset;
                        movedBy = used;'''),
('''                        if (pass + 1 == passes)
                        {
                            this.scaleAnchors = this.CollectScaleAnchors(
                                best, calibration, fwhmCalibration, bestGain, bestOffset,
                                chLo, chHi, channels, y, variance, channelsPerKev,
                                out a, out b, out used, out note);
                            break;
                        }''',
'''                        if (pass + 1 == passes)
                        {
                            this.scaleAnchors = this.CollectScaleAnchors(
                                best, calibration, fwhmCalibration, bestGain, bestOffset,
                                chLo, chHi, channels, y, variance, channelsPerKev,
                                out a, out b, out c, out used, out note);
                            break;
                        }'''),
('''                        this.anchorNote = string.Format(CultureInfo.InvariantCulture,
                            "опор {0}; усиление {1:F5}, ноль {2:F2} кэВ ({3:F3} кан.)",
                            movedBy, bestGain, bestOffset / channelsPerKev, bestOffset);''',
'''                        this.anchorNote = string.Format(CultureInfo.InvariantCulture,
                            "опор {0}; усиление {1:F5}, ноль {2:F2} кэВ ({3:F3} кан.); квадрат {4:E3} кан⁻¹, стрелка {5:F2} кэВ",
                            movedBy, bestGain, bestOffset / channelsPerKev, bestOffset,
                            this.driftQuad, this.driftQuad * chHi * (double)chHi / 4.0 / channelsPerKev);'''),
# --- BuildResult
('''                ScaleAnchorsUsed = this.scaleAnchorsUsed,
                AnchorNote = this.anchorNote,''',
'''                ScaleAnchorsUsed = this.scaleAnchorsUsed,
                AnchorNote = this.anchorNote,
                AnchorQuadPerChannel = this.driftQuad,
                AnchorSagittaKev = this.driftQuad * chHi * (double)chHi / 4.0 / channelsPerKev,
                AnchorPolyDegree = this.AnchorPolyDegree,'''),
])

FR='BecquerelMonitor/FullSpectrumAnalysis/FsaResult.cs'
patch(FR, [
('''        public double AnchorOffsetKev { get; set; }
''',
'''        public double AnchorOffsetKev { get; set; }

        /// <summary>(П14, ЗАМЕР) Квадратичный член шкалы, кан⁻¹: p″ = g·p + o + q·p². Нуль при степени 1.</summary>
        public double AnchorQuadPerChannel { get; set; }

        /// <summary>(П14) Стрелка параболы над хордой полосы, кэВ: q·chHi²/4 в кэВ.</summary>
        public double AnchorSagittaKev { get; set; }

        /// <summary>(П14) Степень многочлена привязки, с которой считалось.</summary>
        public int AnchorPolyDegree { get; set; }
'''),
])

CP='tools/effmaker/probes/CorpusFsaProbe.cs'
patch(CP, [
('''                if (a.StartsWith("--anchor-minfwhm=", StringComparison.Ordinal))
                {
                    o.AnchorMinFwhm = double.Parse(a.Substring(17), CultureInfo.InvariantCulture);
                    continue;
                }''',
'''                if (a.StartsWith("--anchor-minfwhm=", StringComparison.Ordinal))
                {
                    o.AnchorMinFwhm = double.Parse(a.Substring(17), CultureInfo.InvariantCulture);
                    continue;
                }
                // (П14 11.09.2026, ЗАМЕР) степень многочлена привязки (0 — собрать
                // опоры, шкалу не двигать; 1 — прямая, умолчание; 2 — с квадратом),
                // порог числа опор для квадрата и порог выигрыша χ² опор.
                if (a.StartsWith("--anchor-poly=", StringComparison.Ordinal))
                {
                    o.AnchorPoly = int.Parse(a.Substring(14), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--anchor-poly-min=", StringComparison.Ordinal))
                {
                    o.AnchorPolyMin = int.Parse(a.Substring(18), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--anchor-poly-dchi=", StringComparison.Ordinal))
                {
                    o.AnchorPolyDchi = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                    continue;
                }'''),
('''            if (o.AnchorMinFwhm >= 0.0)
            {
                analyzer.AnchorMinFwhmChannels = o.AnchorMinFwhm;
            }

            return analyzer;''',
'''            if (o.AnchorMinFwhm >= 0.0)
            {
                analyzer.AnchorMinFwhmChannels = o.AnchorMinFwhm;
            }

            if (o.AnchorPoly >= 0)
            {
                analyzer.AnchorPolyDegree = o.AnchorPoly;
            }

            if (o.AnchorPolyMin > 0)
            {
                analyzer.AnchorPolyMinAnchors = o.AnchorPolyMin;
            }

            if (o.AnchorPolyDchi >= 0.0)
            {
                analyzer.AnchorPolyMinDeltaChi2 = o.AnchorPolyDchi;
            }

            return analyzer;'''),
('''            public double AnchorMinFwhm = -1.0;
''',
'''            public double AnchorMinFwhm = -1.0;
            public int AnchorPoly = -1;         // (П14) степень; -1 — умолчание анализатора
            public int AnchorPolyMin = -1;
            public double AnchorPolyDchi = -1.0;
'''),
('''                row.AnchorsUsed = result.ScaleAnchorsUsed;
                row.AnchorOffsetKev = result.AnchorOffsetKev;
                row.AnchorNote = result.AnchorNote ?? "";''',
'''                row.AnchorsUsed = result.ScaleAnchorsUsed;
                row.AnchorOffsetKev = result.AnchorOffsetKev;
                row.AnchorNote = result.AnchorNote ?? "";
                row.AnchorQuad = result.AnchorQuadPerChannel;
                row.AnchorSagKev = result.AnchorSagittaKev;'''),
('''            public int AnchorsUsed;
''',
'''            public int AnchorsUsed;
            public double AnchorQuad;      // (П14) квадратичный член, кан⁻¹
            public double AnchorSagKev;    // (П14) стрелка над хордой полосы, кэВ
'''),
('''                                   + "anchors_used,anchor_offset_kev,anchor_note");''',
'''                                   + "anchors_used,anchor_offset_kev,anchor_note,"
                                   // (П14, ЗАМЕР) квадратичный член привязки и стрелка
                                   + "anchor_quad,anchor_sag_kev");'''),
('''                            r.AnchorsUsed.ToString(CultureInfo.InvariantCulture),
                            F(r.AnchorOffsetKev, "F3"),
                            Csv(r.AnchorNote)));''',
'''                            r.AnchorsUsed.ToString(CultureInfo.InvariantCulture),
                            F(r.AnchorOffsetKev, "F3"),
                            Csv(r.AnchorNote),
                            r.AnchorQuad.ToString("E4", CultureInfo.InvariantCulture),
                            F(r.AnchorSagKev, "F3")));'''),
])
print('ok')
