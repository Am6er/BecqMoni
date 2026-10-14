// Проба полосы П313 (15.10.2026): СТАБИЛИЗАТОР ПИКА НА СИНТЕТИЧЕСКОМ СПЕКТРЕ С ИЗВЕСТНЫМ УХОДОМ.
//
// НАСТОЯЩИЙ `PeakStabilizer.Stabilize(ResultData)` — тот, что `MainForm.UpdateCalibrationPeak`
// зовёт раз в секунду при включённой галке стабилизатора (`DCEnergyCalibrationView`). Шкала
// прибора — парабола E(ch) = −5 + 1.45·ch + 5e-6·ch² на 2048 каналах; опора — Cs-137 661.66 кэВ,
// допуск 5 %. Спектр — гауссов пик (ПШПВ 7 % на 662) + полка комптона + спадающий фон,
// пуассоновы отсчёты; усиление ушло на δ: линия энергии E стоит на канале ch_dev(E)·(1+δ).
//
// Мерка: энергия, которую НОВАЯ шкала спектра даёт каналу истинной вершины линии, минус
// 661.66 кэВ; по 200 зёрнам на каждую статистику — медиана |ошибки|, 90-й процентиль,
// наибольшая; «тронута» — доля прогонов, где шкала спектра изменилась.
//
// Разделы:
//   K  контроли: пустой спектр — шкала не тронута; повторный вызов на том же спектре —
//      шкала побитово та же (идемпотентность, заявленная `AMBER110`); уход +3 % при 10⁶
//      отсчётов — снят до канала;
//   N  статистика: 30 … 10⁶ отсчётов спектра при уходе +3 % — как скачет шкала в первые
//      секунды набора (стабилизатор зовётся с первой секунды, мерка — что видит человек);
//   D  уход 0, ±2, ±4, ±6 % при 10⁶ отсчётов — уход за допуск окна (5 %);
//   B  наклонный фон под линией (1460.8 кэВ, фон в 2 раза выше вершины на краю окна) —
//      смещение оценки «максимум сглаженного спектра»;
//   P  опорной линии в спектре НЕТ (проба без Cs-137, галка стабилизатора включена) —
//      куда уезжает шкала;
//   Q  МОДЕЛЬ ПРЕДЛОЖЕНИЯ к вопросу Amber (код приложения НЕ меняется): опора принимается,
//      только если максимум сглаженного окна не на краю и стоит над прямой между краями
//      окна не меньше чем на 3σ (σ — пуассонова, по 11 каналам сглаживания); иначе шкала не
//      трогается. Те же спектры, что N и P: доля принятых и ошибка шкалы.
//
// Коды: 0 — контроли раздела K выполнены; 3 — контроль K не выполнен (пустой спектр тронул
// шкалу, повторный вызов её сдвинул, или уход при богатой статистике не снят до 1.5 канала);
// 5 — обрыв. Разделы N, D, B — числа для журнала, кода не меняют.
//
//   P313StabilizerProbe.exe            (из каталога, где лежит BecquerelMonitor.exe)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BecquerelMonitor;

static class P313StabilizerProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const int N = 2048;
    static readonly double[] DevCoef = { -5.0, 1.45, 5e-6 };

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Console.WriteLine("приложение: " + typeof(PeakStabilizer).Assembly.Location);
            int k = SectionK();
            SectionN();
            SectionD();
            SectionB();
            SectionP();
            SectionQ();
            if (k != 0)
            {
                Console.WriteLine("⛔ ИТОГ: контроль раздела K не выполнен (код 3)");
                return 3;
            }
            Console.WriteLine("ИТОГ: контроли раздела K выполнены (код 0)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ ОБРЫВ: " + ex);
            return 5;
        }
    }

    static PolynomialEnergyCalibration DevCal()
    {
        PolynomialEnergyCalibration c = new PolynomialEnergyCalibration();
        c.PolynomialOrder = 2;
        c.Coefficients = (double[])DevCoef.Clone();
        return c;
    }

    static double DevE(double ch) { return DevCoef[0] + DevCoef[1] * ch + DevCoef[2] * ch * ch; }

    static double DevCh(double e)
    {
        double a = DevCoef[2], b = DevCoef[1], c = DevCoef[0] - e;
        return (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
    }

    static ResultData Make(int[] spectrum, double target, decimal errPct)
    {
        DeviceConfigInfo dev = new DeviceConfigInfo();
        dev.NumberOfChannels = N;
        dev.EnergyCalibration = DevCal();
        dev.StabilizerConfig = new StabilizerConfig();
        dev.StabilizerConfig.TargetPeaks = new List<TargetPeak>
        {
            new TargetPeak { Nuclide = "ref", Energy = (decimal)target, Error = errPct }
        };
        ResultData rd = new ResultData();
        rd.DeviceConfig = dev;
        EnergySpectrum es = new EnergySpectrum(1.0, N);
        es.Spectrum = spectrum;
        es.EnergyCalibration = DevCal();
        rd.EnergySpectrum = es;
        return rd;
    }

    /// <summary>Модель спектра одной линии в КАНАЛАХ прибора с уходом δ: ожидание на канал.</summary>
    static double[] Model(double e, double delta, double peakFrac, double bgSlopeScale)
    {
        double[] mu = new double[N];
        double chPeak = DevCh(e) * (1 + delta);
        double fwhmKeV = 0.07 * 661.66 * Math.Sqrt(e / 661.66);
        double sigmaCh = fwhmKeV / 2.3548 / (DevCoef[1] + 2 * DevCoef[2] * DevCh(e)) * (1 + delta);
        double eEdge = e * (1 - 1 / (1 + 2 * e / 511.0));
        double chEdge = DevCh(eEdge) * (1 + delta);
        double sumPeak = 0, sumCont = 0;
        double[] pk = new double[N], ct = new double[N];
        for (int i = 0; i < N; i++)
        {
            double x = (i - chPeak) / sigmaCh;   // координата канала = его номер (центроид по номеру)
            pk[i] = Math.Exp(-0.5 * x * x);
            double shelf = 0.5 * (1 - Erf((i - chEdge) / (sigmaCh * 1.414)));
            double bg = Math.Exp(-(i + 0.5) / (N * bgSlopeScale));
            ct[i] = 0.6 * shelf + bg;
            sumPeak += pk[i];
            sumCont += ct[i];
        }
        for (int i = 0; i < N; i++)
        {
            mu[i] = peakFrac * pk[i] / sumPeak + (1 - peakFrac) * ct[i] / sumCont;
        }
        return mu;
    }

    static double Erf(double x)
    {
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return x >= 0 ? y : -y;
    }

    static int[] Sample(double[] mu, double total, Random rnd)
    {
        int[] s = new int[N];
        for (int i = 0; i < N; i++)
        {
            s[i] = Poisson(mu[i] * total, rnd);
        }
        return s;
    }

    static int Poisson(double lam, Random rnd)
    {
        if (lam <= 0) return 0;
        if (lam > 50)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            double g = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            return Math.Max(0, (int)Math.Round(lam + Math.Sqrt(lam) * g));
        }
        double L = Math.Exp(-lam), p = 1.0;
        int k = 0;
        do { k++; p *= rnd.NextDouble(); } while (p > L);
        return k - 1;
    }

    /// <summary>Ошибка новой шкалы на канале истинной вершины, кэВ; NaN — шкала не тронута.</summary>
    static double Err(ResultData rd, double e, double delta, out bool touched)
    {
        PolynomialEnergyCalibration before = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
        double[] c0 = (double[])before.Coefficients.Clone();
        new PeakStabilizer().Stabilize(rd);
        PolynomialEnergyCalibration after = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
        touched = !after.Coefficients.SequenceEqual(c0);
        double chTrue = DevCh(e) * (1 + delta);
        return after.ChannelToEnergy(chTrue) - e;
    }

    static string G(double v, int d = 2) { return v.ToString("F" + d.ToString(Inv), Inv); }

    static int SectionK()
    {
        Console.WriteLine();
        Console.WriteLine("── K: контроли");
        int bad = 0;
        // пустой спектр
        {
            ResultData rd = Make(new int[N], 661.66, 5m);
            double e = Err(rd, 661.66, 0.0, out bool touched);
            Console.WriteLine("  пустой спектр: шкала " + (touched ? "ТРОНУТА ⛔" : "не тронута") + ", опор " + rd.CalibrationPeaks.Count.ToString(Inv));
            if (touched) bad = 3;
        }
        // уход +3 %, 10^6 отсчётов, и повторный вызов
        {
            Random rnd = new Random(5);
            int[] s = Sample(Model(661.66, 0.03, 0.3, 0.3), 1e6, rnd);
            ResultData rd = Make(s, 661.66, 5m);
            double before = DevE(DevCh(661.66) * 1.03) - 661.66;
            double e1 = Err(rd, 661.66, 0.03, out bool t1);
            double[] c1 = (double[])((PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration).Coefficients.Clone();
            PolynomialEnergyCalibration cal1 = (PolynomialEnergyCalibration)((PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration).Clone();
            double e2 = Err(rd, 661.66, 0.03, out bool t2);
            PolynomialEnergyCalibration cal2 = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            double maxShift = 0;
            for (int ch = 1; ch < N; ch++) maxShift = Math.Max(maxShift, Math.Abs(cal2.ChannelToEnergy(ch) - cal1.ChannelToEnergy(ch)));
            // повторные вызовы — как такт окна раз в секунду: час набора
            for (int it = 0; it < 3600; it++) new PeakStabilizer().Stabilize(rd);
            PolynomialEnergyCalibration cal3 = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            double hourShift = 0;
            for (int ch = 1; ch < N; ch++) hourShift = Math.Max(hourShift, Math.Abs(cal3.ChannelToEnergy(ch) - cal1.ChannelToEnergy(ch)));
            double keVperCh = DevCoef[1] + 2 * DevCoef[2] * DevCh(661.66);
            Console.WriteLine(string.Format(Inv, "  уход +3 %, 10⁶ отсчётов: шкала прибора на вершине ошибается на {0} кэВ; после стабилизатора {1} кэВ ({2} кан.)",
                G(before), G(e1), G(e1 / keVperCh)));
            Console.WriteLine(string.Format(Inv, "  повторный вызов: коэффициенты {0}, наибольший сдвиг шкалы по 1…{1} кан. {2} кэВ; после 3600 вызовов (час такта) {3} кэВ",
                t2 ? "изменились в последних знаках" : "побитово те же", N - 1, maxShift.ToString("E2", Inv), hourShift.ToString("E2", Inv)));
            if (maxShift > 1e-6 || hourShift > 1e-6) { Console.WriteLine("  ⛔ повторный вызов сдвигает шкалу больше 1e-6 кэВ"); bad = 3; }
            if (Math.Abs(e1 / keVperCh) > 1.5) { Console.WriteLine("  ⛔ уход не снят до 1.5 канала"); bad = 3; }
        }
        return bad;
    }

    static void Distribution(string label, double e, double delta, double total, int seeds, double bgScale, double peakFrac, decimal errPct)
    {
        double[] mu = Model(e, delta, peakFrac, bgScale);
        double keVperCh = DevCoef[1] + 2 * DevCoef[2] * DevCh(e);
        List<double> errs = new List<double>();
        int touchedN = 0;
        double inWindow = 0;
        int lo = (int)Math.Floor(DevCh(e * (1 - (double)errPct / 100))), hi = (int)Math.Ceiling(DevCh(e * (1 + (double)errPct / 100)));
        for (int i = lo; i <= hi; i++) inWindow += mu[i] * total;
        for (int sd = 0; sd < seeds; sd++)
        {
            Random rnd = new Random(1000 + sd);
            ResultData rd = Make(Sample(mu, total, rnd), e, errPct);
            double err = Err(rd, e, delta, out bool touched);
            if (touched) touchedN++;
            errs.Add(err);
        }
        double[] a = errs.Select(Math.Abs).OrderBy(v => v).ToArray();
        double med = a[a.Length / 2], p90 = a[(int)(a.Length * 0.9)], max = a[a.Length - 1];
        double mean = errs.Average();
        Console.WriteLine(string.Format(Inv, "  {0,-22} | в окне {1,9} | тронута {2,3}/{3} | ср. {4,8} | |ош.| медиана {5,7} / 90 % {6,7} / макс {7,7} кэВ | медиана {8} % E",
            label, G(inWindow, 0), touchedN, seeds, G(mean), G(med), G(p90), G(max), G(100 * med / e, 2)));
    }

    static void SectionN()
    {
        Console.WriteLine();
        Console.WriteLine("── N: статистика (Cs-137 661.66 кэВ, уход +3 %, допуск 5 %, 200 зёрен; ошибка новой шкалы на истинной вершине)");
        Console.WriteLine("  (до стабилизатора шкала прибора ошибается на " + G(DevE(DevCh(661.66) * 1.03) - 661.66) + " кэВ)");
        foreach (double total in new[] { 30.0, 100.0, 300.0, 1e3, 3e3, 1e4, 1e5, 1e6 })
        {
            Distribution("отсчётов " + total.ToString("G6", Inv), 661.66, 0.03, total, 200, 0.3, 0.3, 5m);
        }
    }

    static void SectionD()
    {
        Console.WriteLine();
        Console.WriteLine("── D: уход усиления при 10⁶ отсчётов (Cs-137, допуск 5 %, 50 зёрен)");
        foreach (double d in new[] { -0.06, -0.04, -0.02, 0.0, 0.02, 0.04, 0.06 })
        {
            Distribution("уход " + (d * 100).ToString("+0;-0;0", Inv) + " %", 661.66, d, 1e6, 50, 0.3, 0.3, 5m);
        }
    }

    static void SectionB()
    {
        Console.WriteLine();
        Console.WriteLine("── B: наклонный фон (K-40 1460.8 кэВ, доля пика 2 %, крутой спад фона; уход 0; 50 зёрен)");
        foreach (double total in new[] { 1e4, 1e5, 1e6, 1e7 })
        {
            Distribution("отсчётов " + total.ToString("G6", Inv), 1460.8, 0.0, total, 50, 0.08, 0.02, 5m);
        }
    }

    static void SectionP()
    {
        Console.WriteLine();
        Console.WriteLine("── P: опорной линии в спектре НЕТ (опора Cs-137 661.66, в спектре только сплошное; уход 0; 50 зёрен)");
        foreach (double total in new[] { 1e3, 1e4, 1e5, 1e6 })
        {
            Distribution("отсчётов " + total.ToString("G6", Inv), 661.66, 0.0, total, 50, 0.3, 0.0, 5m);
        }
    }

    /// <summary>Модель предложения: тот же поиск, что у PeakStabilizer (SMA 11, окно от шкалы
    /// прибора), плюс проверка значимости. Возвращает канал опоры или −1.</summary>
    static int ProposedFind(int[] spec, double e, double errPct)
    {
        int w = 11;
        double[] sm = new double[N];
        for (int i = 0; i < N; i++)
        {
            double sum = 0;
            for (int j = i - w / 2; j < i - w / 2 + w; j++) sum += spec[Math.Min(N - 1, Math.Max(0, j))];
            sm[i] = sum / w;
        }
        int L = Math.Max(0, (int)Math.Floor(DevCh(e * (1 - errPct / 100)))), R = Math.Min(N - 1, (int)Math.Ceiling(DevCh(e * (1 + errPct / 100))));
        int c = -1;
        double mx = 0;
        for (int i = L; i <= R; i++) if (sm[i] > mx) { mx = sm[i]; c = i; }
        if (c <= L || c >= R) return -1;
        double b = sm[L] + (sm[R] - sm[L]) * (c - L) / (double)(R - L);
        double sigma = Math.Sqrt((sm[c] + b) / w);
        return mx - b > 3 * sigma ? c : -1;
    }

    static void ProposedDistribution(string label, double e, double delta, double total, int seeds, double bgScale, double peakFrac)
    {
        double[] mu = Model(e, delta, peakFrac, bgScale);
        double chTrue = DevCh(e) * (1 + delta);
        double expected = DevCh(e);
        int accepted = 0;
        List<double> errs = new List<double>();
        for (int sd = 0; sd < seeds; sd++)
        {
            Random rnd = new Random(1000 + sd);
            int[] spec = Sample(mu, total, rnd);
            int c = ProposedFind(spec, e, 5.0);
            double err;
            if (c > 0)
            {
                accepted++;
                // одна опора: шкала прибора, прочитанная в канале b·ch, b = expected / c (AMBER110)
                double bb = expected / c;
                err = DevE(bb * chTrue) - e;
            }
            else
            {
                err = DevE(chTrue) - e;   // шкала не тронута
            }
            errs.Add(err);
        }
        double[] a = errs.Select(Math.Abs).OrderBy(v => v).ToArray();
        Console.WriteLine(string.Format(Inv, "  {0,-22} | принято {1,3}/{2} | |ош.| медиана {3,7} / 90 % {4,7} / макс {5,7} кэВ",
            label, accepted, seeds, G(a[a.Length / 2]), G(a[(int)(a.Length * 0.9)]), G(a[a.Length - 1])));
    }

    static void SectionQ()
    {
        Console.WriteLine();
        Console.WriteLine("── Q: МОДЕЛЬ ПРЕДЛОЖЕНИЯ (значимость 3σ над прямой краёв окна, не край) — код приложения не тронут");
        Console.WriteLine("  линия есть, уход +3 % (как N; без стабилизатора ошибка " + G(DevE(DevCh(661.66) * 1.03) - 661.66) + " кэВ), 200 зёрен:");
        foreach (double total in new[] { 30.0, 100.0, 300.0, 1e3, 3e3, 1e4, 1e5, 1e6 })
        {
            ProposedDistribution("отсчётов " + total.ToString("G6", Inv), 661.66, 0.03, total, 200, 0.3, 0.3);
        }
        Console.WriteLine("  линии нет (как P; шкала прибора верна, ошибка 0), 50 зёрен:");
        foreach (double total in new[] { 1e3, 1e4, 1e5, 1e6 })
        {
            ProposedDistribution("отсчётов " + total.ToString("G6", Inv), 661.66, 0.0, total, 50, 0.3, 0.0);
        }
        Console.WriteLine("  уход за допуск ±6 %, 10⁶ отсчётов, 50 зёрен:");
        foreach (double d in new[] { -0.06, 0.06 })
        {
            ProposedDistribution("уход " + (d * 100).ToString("+0;-0;0", Inv) + " %", 661.66, d, 1e6, 50, 0.3, 0.3);
        }
    }
}
