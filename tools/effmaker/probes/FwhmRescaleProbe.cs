using BecquerelMonitor;
using System;
using System.Globalization;
using System.Text;

namespace FwhmRescaleProbe
{
    /// <summary>
    /// Кривая ПШПВ под ДРУГОЕ ЧИСЛО КАНАЛОВ (`S54`) — читатель правки, которой
    /// иначе неоткуда взяться: вызов ровно один и он интерактивный
    /// (`MainForm`, меню смены числа каналов), корпус этот путь не проходит.
    ///
    /// Что проверяется:
    ///
    /// 1. **Тождество пересчёта.** И канал, и ширина меряются в каналах, значит
    ///    у пересчитанной кривой обязано выполняться F'(ch/mul) = F(ch)/mul в
    ///    любой точке шкалы. Проверяется на всех трёх формах и в обе стороны —
    ///    и при укрупнении канала, и при дроблении.
    /// 2. **Кривая БЕЗ опорных точек пересчитывается.** Ровно здесь стоял
    ///    дефект: подгонка по пустому списку не проходила, её ответ
    ///    отбрасывался, и наружу МОЛЧА уходила кривая прежнего масштаба.
    ///    Признак — F'(ch/mul) = F(ch), а не F(ch)/mul.
    /// 3. **Кривая С точками по-прежнему переподгоняется** — старый путь не
    ///    сломан: точки переносятся и по ним фитируется заново.
    ///
    ///     fwhmrescaleprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // Коэффициенты взяты правдоподобными, а не круглыми: круглые
            // прощают ошибку в степени mul, потому что mul у них сокращается.
            var sqrt = new SqrtFwhmCalibration();
            sqrt.Coefficients = new[] { 4.7, 0.031, 1.9E-06 };

            var simple = new SimpleSqrtFwhmCalibration();
            simple.Coefficients = new[] { 5.3, 0.027 };

            var power = new PowerFwhmCalibration();
            power.Coefficients = new[] { 0.42, 0.569 };   // ASN16, V2

            // 8192 → 1024 (канал вчетверо ШИРЕ, mul = 8) и обратно.
            foreach (int[] pair in new[] { new[] { 8192, 1024 }, new[] { 1024, 4096 },
                                           new[] { 4096, 3000 } })
            {
                CheckIdentity("SqrtFwhm", sqrt, pair[0], pair[1]);
                CheckIdentity("SimpleSqrt", simple, pair[0], pair[1]);
                CheckIdentity("Power", power, pair[0], pair[1]);
            }

            // Тот же дефект, названный своими словами: пересчёт обязан ИЗМЕНИТЬ
            // кривую. Если ширина в НОВОМ канале вышла равна ширине в старом —
            // масштаб не применился, а это ровно то, что молчало.
            CheckMoved("SqrtFwhm", sqrt, 8192, 1024);
            CheckMoved("SimpleSqrt", simple, 8192, 1024);
            CheckMoved("Power", power, 8192, 1024);

            // Старый путь: с точками подгонка идёт как прежде.
            var withPeaks = new PowerFwhmCalibration();
            withPeaks.Coefficients = new[] { 0.42, 0.569 };
            for (int ch = 400; ch <= 6800; ch += 1600)
            {
                withPeaks.CalibrationPeaks.Add(new CalibrationPeak
                {
                    Channel = ch,
                    Energy = ch * 0.36,
                    FWHM = withPeaks.ChannelToFwhm(ch)
                });
            }

            FwhmCalibration fitted = withPeaks.RecalcWithNewChannelNum(8192, 1024);
            bool peaksMoved = fitted.CalibrationPeaks.Count == withPeaks.CalibrationPeaks.Count
                              && fitted.CalibrationPeaks[0].Channel == 50
                              && !fitted.NotCalibrated();
            Report(peaksMoved, "с точками: {0} точек перенесены (первая канал {1}), кривая подогнана",
                   fitted.CalibrationPeaks.Count, fitted.CalibrationPeaks[0].Channel);

            // И у подогнанной кривой тождество обязано держаться тоже — точки
            // легли на неё же.
            CheckIdentityOf("Power+точки", withPeaks, fitted, 8192, 1024, 0.02);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void CheckIdentity(string name, FwhmCalibration curve, int oldNum, int newNum)
        {
            FwhmCalibration moved = curve.RecalcWithNewChannelNum(oldNum, newNum);
            CheckIdentityOf(name, curve, moved, oldNum, newNum, 1E-09);
        }

        static void CheckIdentityOf(string name, FwhmCalibration curve, FwhmCalibration moved,
                                    int oldNum, int newNum, double tolerance)
        {
            double mul = (double)oldNum / newNum;
            double worst = 0.0;
            double atChannel = 0.0;
            for (int ch = oldNum / 64; ch < oldNum; ch += oldNum / 64)
            {
                double expected = curve.ChannelToFwhm(ch) / mul;
                double got = moved.ChannelToFwhm(ch / mul);
                double diff = expected > 0.0 ? Math.Abs(got - expected) / expected : Math.Abs(got);
                if (diff > worst)
                {
                    worst = diff;
                    atChannel = ch;
                }
            }

            Report(worst < tolerance, "{0,-12} {1,5} → {2,-5} F'(ch/mul) = F(ch)/mul: худшее {3:E2} на канале {4:F0}",
                   name, oldNum, newNum, worst, atChannel);
        }

        static void CheckMoved(string name, FwhmCalibration curve, int oldNum, int newNum)
        {
            double mul = (double)oldNum / newNum;
            FwhmCalibration moved = curve.RecalcWithNewChannelNum(oldNum, newNum);
            double before = curve.ChannelToFwhm(oldNum / 2);
            double after = moved.ChannelToFwhm(oldNum / 2 / mul);
            bool changed = Math.Abs(after - before) > 1E-06;
            Report(changed, "{0,-12} масштаб применился: было {1:F3} кан., стало {2:F3} кан. (mul {3:F1})",
                   name, before, after, mul);
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine((ok ? "[СОШЛОСЬ] " : "[ПРОВАЛ  ] ") + string.Format(format, args));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
