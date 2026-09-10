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
    /// * **вылет аннигиляции** живёт только выше порога рождения пар
    ///   (1022 кэВ). Каналов у него ДВА (`AMBER15`, 10.09.2026): ОДИНОЧНЫЙ
    ///   ставит пик на E−511, ДВОЙНОЙ — на E−1022, и каждый пик обязан стоять
    ///   В СВОЁМ канале, а не в общем. Ниже порога пусты ОБА;
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
                // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
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
                double annihSingle = Sum(channels[(int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation]);
                double annihDouble = Sum(channels[(int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble]);
                double annih = annihSingle + annihDouble;
                double xray = Sum(channels[(int)EfficiencySimulator.ResponseChannel.EscapeXray]);
                // ⛔ `P` не применяется (`T247`): выше 1000 % он ставит
                //    разделитель разрядов, а группировки разрядов нет вовсе
                //    (решение Amber 05.09.2026). Процент — множителем и текстом.
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                  "доли: пик {0:F2} %, комптон {1:F2} %, вылет SE {2:F2} %, "
                                  + "вылет DE {3:F2} %, вылет рентгена {4:F2} %",
                                  100.0 * peak / total, 100.0 * compton / total,
                                  100.0 * annihSingle / total, 100.0 * annihDouble / total,
                                  100.0 * xray / total));
                // Абсолютные доли отклика на историю — ими и меряется
                // разведение (`AMBER15`): было одно число, стало два.
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                  "каналы вылета аннигиляции: одиночный {0:E4}, двойной {1:E4}, "
                                  + "их сумма {2:E4} (прежний общий канал)",
                                  annihSingle, annihDouble, annih));

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
                              "бин пика принадлежит каналу пика: чужого {0:F2} %",
                              inPeakBin > 0.0 ? 100.0 * peakElsewhere / inPeakBin : 0.0);

                // --- 3. Вылет 511 только выше порога рождения пар ------------
                if (energy < 1022.0)
                {
                    // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ разведения: ниже порога пар
                    // рождения пар нет вовсе, значит ПУСТЫ ОБА канала. Проверка
                    // каждого порознь — не мелочь: сумма их обнулилась бы и при
                    // ошибке, где двойной канал набрал ровно столько, сколько
                    // потерял одиночный.
                    bad += Report(annihSingle <= 0.0, "ниже порога пар канал SE пуст: {0:E3}", annihSingle);
                    bad += Report(annihDouble <= 0.0, "ниже порога пар канал DE пуст: {0:E3}", annihDouble);
                }
                else
                {
                    bad += Report(annihSingle > 0.0, "выше порога пар канал SE не пуст: {0:F2} % отклика", 100.0 * annihSingle / total);
                    bad += Report(annihDouble > 0.0, "выше порога пар канал DE не пуст: {0:F2} % отклика", 100.0 * annihDouble / total);
                    // ⛔ (`AMBER15`) Каждый пик — В СВОЁМ канале. Прежде оба
                    // искались в одном, и проба проходила бы ровно так же, если
                    // бы SE и DE лежали вперемешку: это она и не различала.
                    // Теперь у каждого свой канал, и вторым числом печатается,
                    // сколько того же пика осталось в ЧУЖОМ канале.
                    var where = new[]
                    {
                        new { Shift = 511.0, Own = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation,
                              Other = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble, Name = "SE" },
                        new { Shift = 1022.0, Own = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble,
                              Other = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation, Name = "DE" }
                    };
                    foreach (var w in where)
                    {
                        int at = EfficiencySimulator.PeakBin(energy - w.Shift, binKev);
                        double local = Window(channels[w.Own], at - 1, at + 1);
                        double around = Window(channels[w.Own], at - 12, at + 12);
                        // Пик вылета обязан ВЫСТУПАТЬ над своей окрестностью:
                        // три бина из двадцати пяти держат заметно больше трёх
                        // двадцать пятых, иначе это не пик, а ровное плато.
                        bool stands = around > 0.0 && local / around > 3.0 * 3.0 / 25.0;
                        bad += Report(stands, "пик {0} на {1:F0} кэВ выступает в своём канале: {2:F1} % от окрестности",
                                      w.Name, energy - w.Shift, around > 0.0 ? 100.0 * local / around : 0.0);

                        double alien = Window(channels[w.Other], at - 1, at + 1);
                        double alienAround = Window(channels[w.Other], at - 12, at + 12);
                        bool quiet = !(alienAround > 0.0) || alien / alienAround <= 3.0 * 3.0 / 25.0;
                        bad += Report(quiet, "пика {0} в ЧУЖОМ канале нет: {1:F1} % от окрестности (было бы {2:F1} % при слиянии)",
                                      w.Name, alienAround > 0.0 ? 100.0 * alien / alienAround : 0.0,
                                      100.0 * 3.0 / 25.0);
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
                                  "комптон в основном ниже края {0:F0} кэВ: выше него {1:F1} %",
                                  edge, compton > 0.0 ? 100.0 * aboveEdge / compton : 0.0);
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
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                      "--   край {0:F0} кэВ прижат к нулю, правило не применяется (комптона {1:F1} %)",
                                      edge, 100.0 * compton / total));
                }

                // --- 5. Вылет рентгена стоит на 28-33 кэВ ниже линии --------
                if (xray > 0.0)
                {
                    int at = EfficiencySimulator.PeakBin(energy - 30.6, binKev);
                    double[] xrayRow = channels[(int)EfficiencySimulator.ResponseChannel.EscapeXray];
                    double local = Window(xrayRow, at - 3, at + 3);
                    bad += Report(local > 0.25 * xray,
                                  "вылет рентгена собран у {0:F0} кэВ: {1:F1} % канала",
                                  energy - 30.6, 100.0 * local / xray);
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
