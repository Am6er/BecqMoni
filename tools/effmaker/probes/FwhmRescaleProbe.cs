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
    /// 1. **Тождество пересчёта.** И канал, и ширина меряются в каналах, а номер
    ///    канала — ЦЕНТР канала (`AMBER73`): новый канал j покрывает старые
    ///    [m·j, m·(j+1)) с центром x = m·j + s, s = (m − 1)/2. Значит у
    ///    пересчитанной кривой обязано выполняться F'((x − s)/m) = F(x)/m
    ///    (`AMBER171`, остаток, полоса fixfwhm 05.10.2026). Корневые формы
    ///    сдвигом замкнуты — тождество точное; степенная a·x^p сдвигом не
    ///    замкнута, у неё прежний чистый множитель F'(x/m) = F(x)/m — так и
    ///    проверяется. Положительный контроль: у корневой формы тождество БЕЗ
    ///    сдвига обязано НЕ держаться (иначе сдвиг не применился).
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
                CheckIdentity("SqrtFwhm", sqrt, pair[0], pair[1], true);
                CheckIdentity("SimpleSqrt", simple, pair[0], pair[1], true);
                CheckIdentity("Power", power, pair[0], pair[1], false);
            }

            // Положительный контроль сдвига: корневая кривая БЕЗ сдвига центра
            // обязана расходиться заметно (≈ (m − 1)/(4·x) по ширине) — иначе
            // тождество со сдвигом выше ничего не отличает.
            CheckShiftMatters("SqrtFwhm", sqrt, 8192, 1024);
            CheckShiftMatters("SimpleSqrt", simple, 8192, 1024);

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
            // Точки переносятся со сдвигом центра (`AMBER171`), значит и тождество — со сдвигом.
            CheckIdentityOf("Power+точки", withPeaks, fitted, 8192, 1024, 0.02, true);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void CheckIdentity(string name, FwhmCalibration curve, int oldNum, int newNum, bool shifted)
        {
            FwhmCalibration moved = curve.RecalcWithNewChannelNum(oldNum, newNum);
            CheckIdentityOf(name, curve, moved, oldNum, newNum, 1E-09, shifted);
        }

        /// <summary>Худшее относительное расхождение F'((x − s)/m) с F(x)/m по старой шкале.</summary>
        static double Worst(FwhmCalibration curve, FwhmCalibration moved, int oldNum, int newNum,
                            bool shifted, out double atChannel)
        {
            double mul = (double)oldNum / newNum;
            double shift = shifted ? (mul - 1.0) / 2.0 : 0.0;
            double worst = 0.0;
            atChannel = 0.0;
            for (int ch = oldNum / 64; ch < oldNum; ch += oldNum / 64)
            {
                double expected = curve.ChannelToFwhm(ch) / mul;
                double got = moved.ChannelToFwhm((ch - shift) / mul);
                double diff = expected > 0.0 ? Math.Abs(got - expected) / expected : Math.Abs(got);
                if (diff > worst)
                {
                    worst = diff;
                    atChannel = ch;
                }
            }
            return worst;
        }

        static void CheckIdentityOf(string name, FwhmCalibration curve, FwhmCalibration moved,
                                    int oldNum, int newNum, double tolerance, bool shifted)
        {
            double atChannel;
            double worst = Worst(curve, moved, oldNum, newNum, shifted, out atChannel);
            Report(worst < tolerance, "{0,-12} {1,5} → {2,-5} {3}: худшее {4:E2} на канале {5:F0}",
                   name, oldNum, newNum,
                   shifted ? "F'((ch−s)/mul) = F(ch)/mul" : "F'(ch/mul) = F(ch)/mul    ",
                   worst, atChannel);
        }

        static void CheckShiftMatters(string name, FwhmCalibration curve, int oldNum, int newNum)
        {
            FwhmCalibration moved = curve.RecalcWithNewChannelNum(oldNum, newNum);
            double atChannel;
            double worst = Worst(curve, moved, oldNum, newNum, false, out atChannel);
            Report(worst > 1E-04, "{0,-12} контроль: БЕЗ сдвига центра тождество расходится на {1:E2} (канал {2:F0}) — сдвиг применён",
                   name, worst, atChannel);
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
