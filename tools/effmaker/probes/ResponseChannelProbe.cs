using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ResponseChannelProbe
{
    /// <summary>
    /// Каналы отклика: сумма по каналам обязана СОВПАСТЬ с обычным откликом
    /// побитово, а сами каналы — стоять там, где велит кинематика.
    ///
    /// Первая проверка главная и звучит скучно, но она единственная, что ловит
    /// потерянную или посчитанную дважды историю: раскладка по каналам не
    /// тянет ни одного случайного числа, значит розыгрыш от неё не меняется, и
    /// два прогона обязаны дать одни и те же числа до последнего бита. Любое
    /// расхождение — это история, попавшая не в свой канал или ни в один.
    ///
    /// Дальше — где каналы обязаны быть:
    ///
    /// * **вылет 511** живёт только выше порога рождения пар (1022 кэВ) и
    ///   ставит пики на E−511 и E−1022. Ниже порога канал обязан быть ПУСТ;
    /// * **вылет K-рентгена** ставит пик на 28–33 кэВ ниже линии (Kα иода и
    ///   цезия) и заметен внизу шкалы, где фотопоглощение преобладает;
    /// * **комптон** обрывается на краю E/(1+2E/511) и не имеет права заходить
    ///   выше него сколько-нибудь заметно.
    ///
    ///     responsechannelprobe --geometry=X.in [--e=662,2614] [--n=200000] [--bin=2]
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            double[] energies = { 662.0, 2614.0 };
            int histories = 200000;
            double binKev = 2.0;

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--e=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(4).Split(',');
                    energies = new double[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        energies[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
                    }
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("{0} историй, бин {1:F2} кэВ", histories, binKev);
            int bad = 0;

            foreach (double energy in energies)
            {
                Console.WriteLine();
                Console.WriteLine("=== {0:F0} кэВ ===", energy);

                double err1, err2;
                double[] plain = Make(geometry, histories).Response(energy, binKev, out err1);
                double[][] channels = Make(geometry, histories).ResponseByChannel(energy, binKev, out err2);

                // --- 1. Сумма каналов против обычного отклика ----------------
                int mismatch = 0;
                double worst = 0.0;
                for (int b = 0; b < plain.Length; b++)
                {
                    double sum = 0.0;
                    foreach (double[] channel in channels)
                    {
                        sum += channel[b];
                    }

                    double diff = Math.Abs(sum - plain[b]);
                    if (diff > worst)
                    {
                        worst = diff;
                    }

                    // Допуск — на сложение в разном порядке, не на физику.
                    if (diff > 1e-15 + 1e-9 * Math.Abs(plain[b]))
                    {
                        mismatch++;
                    }
                }

                bad += Report(mismatch == 0, "сумма каналов равна отклику: {0} бинов, худшее расхождение {1:E2}",
                              plain.Length, worst);

                double total = Sum(plain);
                double peak = Sum(channels[(int)EfficiencySimulator.ResponseChannel.Peak]);
                double compton = Sum(channels[(int)EfficiencySimulator.ResponseChannel.Compton]);
                double annih = Sum(channels[(int)EfficiencySimulator.ResponseChannel.Escape511]);
                double xray = Sum(channels[(int)EfficiencySimulator.ResponseChannel.EscapeXray]);
                Console.WriteLine("доли: пик {0:P2}, комптон {1:P2}, вылет 511 {2:P2}, вылет рентгена {3:P2}",
                                  peak / total, compton / total, annih / total, xray / total);

                // --- 2. Пик целиком в своём канале --------------------------
                int peakBin = EfficiencySimulator.PeakBin(energy, binKev);
                double peakElsewhere = 0.0;
                for (int c = 0; c < channels.Length; c++)
                {
                    if (c != (int)EfficiencySimulator.ResponseChannel.Peak)
                    {
                        peakElsewhere += channels[c][peakBin];
                    }
                }

                // Не ноль: в бин пика зажимается и то, что вылетело на доли
                // бина. Но это должны быть КРОХИ против самого пика.
                double inPeakBin = channels[(int)EfficiencySimulator.ResponseChannel.Peak][peakBin];
                bad += Report(peakElsewhere < 0.02 * inPeakBin,
                              "бин пика принадлежит каналу пика: чужого {0:P2}",
                              inPeakBin > 0.0 ? peakElsewhere / inPeakBin : 0.0);

                // --- 3. Вылет 511 только выше порога рождения пар ------------
                if (energy < 1022.0)
                {
                    bad += Report(annih <= 0.0, "ниже порога пар канал 511 пуст: {0:E3}", annih);
                }
                else
                {
                    bad += Report(annih > 0.0, "выше порога пар канал 511 не пуст: {0:P2} отклика", annih / total);
                    foreach (double shift in new[] { 511.0, 1022.0 })
                    {
                        int at = EfficiencySimulator.PeakBin(energy - shift, binKev);
                        double local = Window(channels[(int)EfficiencySimulator.ResponseChannel.Escape511], at - 1, at + 1);
                        double around = Window(channels[(int)EfficiencySimulator.ResponseChannel.Escape511], at - 12, at + 12);
                        // Пик вылета обязан ВЫСТУПАТЬ над своей окрестностью:
                        // три бина из двадцати пяти держат заметно больше трёх
                        // двадцать пятых, иначе это не пик, а ровное плато.
                        bool stands = around > 0.0 && local / around > 3.0 * 3.0 / 25.0;
                        bad += Report(stands, "пик вылета на {0:F0} кэВ выступает: {1:P1} от окрестности",
                                      energy - shift, around > 0.0 ? local / around : 0.0);
                    }
                }

                // --- 4. Комптон не заходит выше края ------------------------
                // Край — энергия, оставшаяся ЭЛЕКТРОНУ при рассеянии на 180°,
                // то есть E минус энергия рассеянного кванта. 662 -> 478,
                // 2614 -> 2381.
                double edge = energy - energy / (1.0 + 2.0 * energy / 511.0);
                int edgeBin = EfficiencySimulator.PeakBin(edge, binKev);
                double[] comptonRow = channels[(int)EfficiencySimulator.ResponseChannel.Compton];
                double aboveEdge = Window(comptonRow, edgeBin + 8, comptonRow.Length - 1);
                if (energy >= 200.0)
                {
                    // Не ноль: многократное рассеяние законно заводит выше края.
                    // Но основная масса обязана лежать ПОД ним.
                    bad += Report(aboveEdge < 0.35 * compton,
                                  "комптон в основном ниже края {0:F0} кэВ: выше него {1:P1}",
                                  edge, compton > 0.0 ? aboveEdge / compton : 0.0);
                }
                else
                {
                    // Ниже 200 кэВ правило неприменимо, и это не поблажка.
                    // Край там прижат к нулю (при 59 кэВ он равен 11), сам
                    // комптон — единицы процентов отклика, а канал набирается
                    // НЕДОБОРОМ от рассеяния на пути к кристаллу: такой квант
                    // приносит свою энергию целиком, и никакого края у этого
                    // распределения нет. Требовать его — значит требовать от
                    // модели того, чего в ней нет по построению.
                    Console.WriteLine("--   край {0:F0} кэВ прижат к нулю, правило не применяется (комптона {1:P1})",
                                      edge, compton / total);
                }

                // --- 5. Вылет рентгена стоит на 28-33 кэВ ниже линии --------
                if (xray > 0.0)
                {
                    int at = EfficiencySimulator.PeakBin(energy - 30.6, binKev);
                    double[] xrayRow = channels[(int)EfficiencySimulator.ResponseChannel.EscapeXray];
                    double local = Window(xrayRow, at - 3, at + 3);
                    bad += Report(local > 0.25 * xray,
                                  "вылет рентгена собран у {0:F0} кэВ: {1:P1} канала",
                                  energy - 30.6, local / xray);
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static EfficiencySimulator Make(GeometryModel geometry, int histories)
        {
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = histories,
                PeakHalfWidthKev = 0.0
            };

            // Зерно одно и то же: два прогона обязаны разыграть ОДНИ И ТЕ ЖЕ
            // истории, иначе первая проверка меряла бы статистику.
            sim.ResetStream((ulong)sim.Seed);
            return sim;
        }

        static double Sum(double[] values)
        {
            double total = 0.0;
            foreach (double v in values)
            {
                total += v;
            }

            return total;
        }

        static double Window(double[] values, int from, int to)
        {
            double total = 0.0;
            for (int i = Math.Max(0, from); i <= to && i < values.Length; i++)
            {
                total += values[i];
            }

            return total;
        }

        static int Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("{0} {1}", ok ? "ok  " : "ПЛОХО", string.Format(CultureInfo.InvariantCulture, format, args));
            return ok ? 0 : 1;
        }
    }
}
