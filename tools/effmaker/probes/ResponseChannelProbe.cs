using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
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
    /// * **вылет рентгена разведён на K и L** (`AMBER16` п. 1, решение Amber
    ///   11.09.2026 «Развести K и L отдельными каналами»), и это проверяется
    ///   ДВУМЯ ПЛЕЧАМИ с одним зерном: без ключа и с ключом. Ниже K-края
    ///   кристалла канал K обязан быть ПУСТ, а L — нет; без ключа пуст обязан
    ///   быть L; и сумма двух каналов обязана совпасть с прежним единым
    ///   каналом — иначе разведение потеряло или удвоило истории;
    /// * **комптон** обрывается на краю E/(1+2E/511) и не имеет права заходить
    ///   выше него сколько-нибудь заметно.
    ///
    /// ⛔ ПИК ВЫЛЕТА ИЩЕТСЯ В СВЕТОВОЙ ШКАЛЕ И СУДИТСЯ В ИСТОРИЯХ (`AMBER15`,
    /// находка П7, закрыта 12.09.2026 полосой П3). До этого дня «пик
    /// выступает» значило «три бина из двадцати пяти вокруг `E − 1022` держат
    /// больше 36 %», и число это было снято на `G1S`. На `AS80_point0`,
    /// 2614 кэВ, это давало 18.7 % при 200 000 историй, 30.4 при 400 000, 24.4
    /// при 800 000 и 39.1 при 400 000 на 2614.5 — числа без опоры.
    ///
    /// Причина — ПИК СТОИТ НЕ ТАМ, ГДЕ ЭНЕРГИЯ. Строка отклика переложена по
    /// координате СВЕТА (`RemapLightScale`, физика F11/П19): пик DE у обеих
    /// сцен стоит в бине 794 (`AS80`: 203.5 историй), а `E − 1022` = 1592 кэВ —
    /// это бин 796; окно ±1 бин по энергии держало 46.5 + 4.9 + 2.1 — ХВОСТ
    /// пика, и «18.7 %» было долей хвоста, а её колебания по n и по 2614/2614.5
    /// — шумом хвоста (по вложенным кускам по 200 000, зерно одно: 18.7 / 42 /
    /// 18 — то есть σ около десяти пунктов). Пик SE стоит в бинах 1052–1053 при
    /// энергетическом 1052 — сдвиг меньше бина, оттого SE «проходил». Разница
    /// сцен (`AS80` в среднем ≈ 29 %, `G1S` ≈ 45 % по старой мере) — тоже доля
    /// хвоста, а не «плато у крупного кристалла»: с окном на пике обе дают
    /// 90.9 и 94.6 %. А при 800 000 историй ОДНА история в чужом канале давала
    /// «100 % от окрестности» — проверка «в чужом канале пика нет» так же
    /// была слепа к счёту событий.
    ///
    /// Теперь оба утверждения — статистические и про пик, найденный В СВЕТОВОЙ
    /// ШКАЛЕ: центр окна — максимум канала в ±12 бинах от энергетического
    /// места (сдвиг печатается — это и есть положение пика по свету), окно —
    /// 25 бинов вокруг него, статистика — k событий в трёх бинах у максимума
    /// из N событий окна. Вопрос тот же, что был записан словами («иначе это
    /// не пик, а ровное плато»): пик ВЫСТУПАЕТ, если k невероятно при ровном
    /// плато — хвост P(S ≥ k | плато) ниже 1.35e-3 (односторонние 3σ), где
    /// нуль разыгрывается С ТЕМ ЖЕ поиском максимума (<see cref="NullTail"/>;
    /// бином не годится — выбор максимума сдвигает долю плато вверх); в ЧУЖОМ
    /// канале пика НЕТ, если тот же хвост НЕ ниже. Порога в процентах,
    /// зависящего от сцены, нет. Событий для приговора должно хватать: окно DE
    /// добирается историями (×2 до ×16 от `--n`, то же зерно — старшая выборка
    /// содержит младшую) до <see cref="MinWindowHistories"/>, а если и после
    /// этого их меньше — контроль ПРОВАЛИВАЕТСЯ вслух, а не молчит (`T219`).
    /// Счёт событий — из долей: континуум считается аналоговой веткой с
    /// единичными весами, доля × n у Σ окна — целое с точностью до перелива
    /// света через два края окна (< 2); больший остаток — отказ.
    ///
    ///     responsechannelprobe --geometry=X.in [--e=662,2614] [--n=200000] [--bin=2]
    ///                          [--spoil=merge|flat]
    ///
    /// Ключи порчи — положительный контроль приговора. `--spoil=merge`: канал
    /// DE ссыпается в канал SE (прежняя слитая раскладка) — проба ОБЯЗАНА
    /// отказать: канал DE пуст, пик DE не выступает (судить нечем), пик DE
    /// найден в ЧУЖОМ канале. `--spoil=flat`: канал DE в ±12 бинах от пика
    /// разравнивается в плато той же площади — «пик DE выступает» ОБЯЗАН
    /// отказать, остальное — стоять.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        /// <summary>
        /// Сколько событий в окне 25 бинов нужно, чтобы приговор «выступает /
        /// плато» имел силу: при 100 событиях σ доли у пика в 36 % — 4.8
        /// пункта, и плато (12 %) от него отстоит на пять σ.
        /// </summary>
        const int MinWindowHistories = 100;

        /// <summary>Односторонние 3σ нормального закона — порог хвоста.</summary>
        const double ThreeSigmaTail = 1.35E-3;

        static bool spoilMerge;
        static bool spoilFlat;

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
                else if (a == "--spoil=merge") spoilMerge = true;
                else if (a == "--spoil=flat") spoilFlat = true;
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

                double err1, err2, err3;
                double[] plain = Make(geometry, histories, false).Response(energy, binKev, out err1);
                double[][] channels = Make(geometry, histories, false)
                                          .ResponseByChannel(energy, binKev, out err2);
                // ⛔ (`AMBER16` п. 1) ВТОРОЕ ПЛЕЧО — то же зерно, тот же розыгрыш,
                // отличается ТОЛЬКО разведением K/L. Иначе «в канале L что-то
                // есть» ничего не доказывало бы: это могла быть другая выборка.
                double[][] split = Make(geometry, histories, true)
                                       .ResponseByChannel(energy, binKev, out err3);
                if (spoilMerge)
                {
                    // Порча — ОБОИМ плечам одинаково, чтобы её видели только
                    // проверки пиков вылета, а не сверка плеч по прочим каналам.
                    Merge(channels);
                    Merge(split);
                    Console.WriteLine("⚠ ПОРЧА --spoil=merge: канал DE ссыпан в канал SE");
                }

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
                double xray = Sum(channels[(int)EfficiencySimulator.ResponseChannel.EscapeXrayK]);
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
                    //
                    // ⛔ (П3, 12.09.2026) ПРИГОВОР — В ИСТОРИЯХ, см. шапку. Окно
                    // DE у крупного кристалла при штатных 200 000 историй
                    // держит десятки событий, и доля в нём — шум с σ около
                    // десяти пунктов; поэтому сначала ДОБОР историй тем же
                    // зерном до `MinWindowHistories` в окне DE (оно беднее SE),
                    // потом точный биномиальный хвост против ровного плато.
                    var where = new[]
                    {
                        new { Shift = 511.0, Own = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation,
                              Other = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble, Name = "SE" },
                        new { Shift = 1022.0, Own = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble,
                              Other = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation, Name = "DE" }
                    };
                    double[][] escapeArm = channels;
                    int escapeHistories = histories;
                    int deAt = EfficiencySimulator.PeakBin(energy - 1022.0, binKev);
                    int deChannel = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble;
                    for (int mult = 2; mult <= 16; mult *= 2)
                    {
                        double deWindow = Window(escapeArm[deChannel], deAt - 12, deAt + 12) * escapeHistories;
                        if (deWindow >= MinWindowHistories)
                        {
                            break;
                        }

                        double topUpErr;
                        escapeHistories = histories * mult;
                        escapeArm = Make(geometry, escapeHistories, false)
                                        .ResponseByChannel(energy, binKev, out topUpErr);
                        if (spoilMerge)
                        {
                            Merge(escapeArm);
                        }

                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                          "--   окно DE бедно ({0:F0} событий при {1} историях) — добор до {2} историй тем же зерном",
                                          deWindow, escapeHistories / mult, escapeHistories));
                    }

                    foreach (var w in where)
                    {
                        int at = EfficiencySimulator.PeakBin(energy - w.Shift, binKev);
                        // ⛔ ПИК ИЩЕТСЯ, А НЕ БЕРЁТСЯ ПО ЭНЕРГИИ (П3, 12.09.2026).
                        // В строке отклика бины переложены по координате
                        // СВЕТА (`RemapLightScale`, физика F11/П19), и пик DE у
                        // `AS80_point0` стоит в бине 794 при `E − 1022` в 796:
                        // −4 кэВ. Окно ±1 бин по энергии ловило ХВОСТ, и вся
                        // находка П7 (18.7 % против 36 %) была шумом доли хвоста.
                        // Центр — максимум в ±12 бинах от энергетического места;
                        // сдвиг печатается: это и есть положение пика по свету.
                        int center, local, around;
                        if (spoilFlat && w.Own == deChannel)
                        {
                            // Порча «плато»: пик DE разравнивается по своему
                            // окну — площадь та же, выступа нет. Второму плечу
                            // — то же, чтобы сверку плеч «побитово» порча не
                            // тревожила: её предмет — только приговор о пике.
                            Flatten(escapeArm[deChannel], at);
                            if (ReferenceEquals(escapeArm, channels))
                            {
                                Flatten(split[deChannel], at);
                            }

                            Console.WriteLine("⚠ ПОРЧА --spoil=flat: канал DE в окне пика разровнен в плато");
                        }

                        if (!PeakStat(escapeArm[w.Own], at, escapeHistories, out center, out local, out around))
                        {
                            bad += Report(false, "пик {0}: Σ окна × n не целое — веса историй не единичны, счёт событий недостоверен",
                                          w.Name);
                            continue;
                        }

                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                          "--   окно {0}: по энергии {1:F0} кэВ (бин {2}), максимум в бине {3} — сдвиг по свету {4:+0.0;-0.0;0.0} кэВ; "
                                          + "историй по бинам: {5}",
                                          w.Name, energy - w.Shift, at, center, (center - at) * binKev,
                                          WindowText(escapeArm[w.Own], center, escapeHistories)));

                        // Пик вылета обязан ВЫСТУПАТЬ над своей окрестностью:
                        // k из N событий окна в трёх бинах вокруг максимума
                        // невероятны при ровном плато. Нуль — розыгрышем, с тем
                        // же поиском максимума (`NullTail`).
                        if (around < MinWindowHistories)
                        {
                            bad += Report(false, "пик {0} на {1:F0} кэВ: в окне {2} событий из {3} нужных даже после добора — "
                                          + "судить НЕЧЕМ; возьмите больше историй (--n=)",
                                          w.Name, energy - w.Shift, around, MinWindowHistories);
                        }
                        else
                        {
                            double tail = NullTail(around, local);
                            bad += Report(tail < ThreeSigmaTail,
                                          "пик {0} выступает в своём канале: {1} из {2} событий окна в трёх бинах у максимума "
                                          + "({3:F1} %), P(≥ k | плато) = {4:E2} < {5:E2}",
                                          w.Name, local, around, 100.0 * local / around, tail, ThreeSigmaTail);
                        }

                        int alienCenter, alien, alienAround;
                        if (!PeakStat(escapeArm[w.Other], at, escapeHistories, out alienCenter, out alien, out alienAround))
                        {
                            bad += Report(false, "пик {0} в чужом канале: Σ окна × n не целое — веса историй не единичны", w.Name);
                            continue;
                        }

                        // В ЧУЖОМ канале пика НЕТ, если то, что там лежит,
                        // совместимо с ровным плато. Одна история в центре окна
                        // (`G1S_point5`, 800 000 историй, 2103 кэВ) — это 100 %
                        // «от окрестности» и не пик.
                        double alienTail = alienAround > 0 ? NullTail(alienAround, alien) : 1.0;
                        bad += Report(alienTail >= ThreeSigmaTail,
                                      "пика {0} в ЧУЖОМ канале нет: {1} из {2} событий окна в трёх бинах у максимума (бин {3}), "
                                      + "P(≥ k | плато) = {4:E2} (пик — ниже {5:E2})",
                                      w.Name, alien, alienAround, alienCenter, alienTail, ThreeSigmaTail);
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
                // ⛔ (`AMBER16` п. 1) ПРАВИЛО ПРИМЕНЯЕТСЯ ТОЛЬКО ВЫШЕ K-КРАЯ, и
                // это не поблажка, а исправление посылки. Число 30.6 — это Kα
                // иода и цезия, то есть правило с самого начала говорило о
                // K-серии; ниже K-края в канале лежит L-вылет (2.68…5.02 кэВ), и
                // «пик на E−30.6» требует его там, где недобор физически равен
                // единицам кэВ. На 32.194 кэВ правило указывало на 1.6 кэВ и
                // честно проваливалось — ровно ту беду и назвала Amber
                // 11.09.2026: имя канала обещало K, содержимое было L. С
                // разведением утверждение о K-пике снова верно — но только там,
                // где K-серия открыта.
                double kEdge = LowestKEdge(geometry);
                if (xray > 0.0 && (!(kEdge > 0.0) || energy > kEdge))
                {
                    int at = EfficiencySimulator.PeakBin(energy - 30.6, binKev);
                    double[] xrayRow = channels[(int)EfficiencySimulator.ResponseChannel.EscapeXrayK];
                    double local = Window(xrayRow, at - 3, at + 3);
                    bad += Report(local > 0.25 * xray,
                                  "вылет рентгена собран у {0:F0} кэВ: {1:F1} % канала",
                                  energy - 30.6, 100.0 * local / xray);
                }
                else if (xray > 0.0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                      "--   ниже K-края {0:F2} кэВ пика K-вылета быть не может: "
                                      + "в канале L-серия, её приёмка ниже", kEdge));
                }

                // --- 6. РАЗВЕДЕНИЕ K И L (`AMBER16` п. 1) -------------------
                bad += SplitCheck(geometry, energy, binKev, channels, split, total);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// ⛔ (`AMBER16` п. 1, решение Amber 11.09.2026 «Развести K и L
        /// отдельными каналами») Приёмка разведения — ТРИ утверждения, и ни одно
        /// не заменяет двух других.
        ///
        /// 1. **Без ключа канал L пуст РОВНО.** Иначе разведение работало бы
        ///    всегда, а склад из 44 матриц, посчитанный до 11.09.2026, оказался
        ///    бы чужим молча — ровно беда `T114`.
        /// 2. **Ничего не потеряно и не удвоено:** K+L с ключом равны прежнему
        ///    единому каналу, а остальные четыре канала не шевельнулись. Это и
        ///    есть проверка, что ключ ПЕРЕКЛАДЫВАЕТ истории, а не считает другую
        ///    физику.
        /// 3. **ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПО КИНЕМАТИКЕ:** ниже самого низкого
        ///    K-края кристалла канал K обязан быть ПУСТ, а L — нет. У NaI это
        ///    K-край иода 33.17 кэВ, у CsI — 33.17 (иод) и 35.99 (цезий); при
        ///    32.194 кэВ оба закрыты, а L-края 4.56 и 5.01 открыты. Без этого
        ///    пункта «в канале L что-то есть» не отличалось бы от «метка
        ///    поставлена куда попало».
        ///
        /// ⚠ Край берётся ИЗ БАЗЫ ВЕЩЕСТВА по составу кристалла, а не числом в
        /// исходнике: копия числа рядом с данными разошлась бы с ними молча.
        /// </summary>
        static int SplitCheck(GeometryModel geometry, double energy, double binKev,
                              double[][] plainArm, double[][] splitArm, double total)
        {
            int k = (int)EfficiencySimulator.ResponseChannel.EscapeXrayK;
            int l = (int)EfficiencySimulator.ResponseChannel.EscapeXrayL;
            double wasK = Sum(plainArm[k]);
            double wasL = Sum(plainArm[l]);
            double nowK = Sum(splitArm[k]);
            double nowL = Sum(splitArm[l]);
            int bad = 0;

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                              "вылет рентгена: без ключа K {0:E4} L {1:E4}; "
                              + "с ключом K {2:E4} L {3:E4} (доля L в статье {4:F2} %)",
                              wasK, wasL, nowK, nowL,
                              nowK + nowL > 0.0 ? 100.0 * nowL / (nowK + nowL) : 0.0));

            bad += Report(wasL == 0.0, "БЕЗ ключа канал L пуст РОВНО: {0:E3}", wasL);

            // Сумма — не побитово: истории те же, но складываются по другим
            // группам, и последний бит законно расходится. Мера относительная.
            double sumWas = wasK, sumNow = nowK + nowL;
            bool kept = Math.Abs(sumNow - sumWas) <= 1e-12 * Math.Max(1e-300, sumWas);
            bad += Report(kept, "K+L с ключом равны прежнему каналу: {0:E6} против {1:E6}",
                          sumNow, sumWas);

            int moved = 0;
            for (int c = 0; c < plainArm.Length; c++)
            {
                if (c == k || c == l)
                {
                    continue;
                }

                for (int b = 0; b < plainArm[c].Length; b++)
                {
                    if (plainArm[c][b] != splitArm[c][b])
                    {
                        moved++;
                    }
                }
            }

            bad += Report(moved == 0,
                          "прочие каналы ПОБИТОВО те же: разошлось бинов {0}", moved);

            double edge = LowestKEdge(geometry);
            if (edge > 0.0 && energy < edge && !(nowK + nowL > 0.0))
            {
                // ⛔ НЕ «ok» и не молчание: контроль, которому нечего судить,
                // обязан сказать это вслух и провалиться (`T219`: «молчит» и
                // «говорит невнятно» — разные беды, и обе хуже отказа). Так
                // бывает у толстой объёмной пробы ниже её собственных краёв —
                // `ASN16_lu_side` при 32.194 кэВ за 400 000 историй не дал ни
                // одной с вылетом рентгена: оксид лютеция ниже своего K-края
                // 63.3 кэВ поглощает почти всё сам.
                bad += Report(false,
                              "ниже K-края {0:F2} кэВ судить НЕЧЕГО: за прогон ни одной истории "
                              + "с вылетом рентгена — мало историй (--n=) либо отклик сцены на этой "
                              + "энергии ничтожен; возьмите точечный источник или больше историй",
                              edge);
            }
            else if (edge > 0.0 && energy < edge)
            {
                bad += Report(nowK == 0.0,
                              "ниже K-края {0:F2} кэВ канал K ПУСТ РОВНО: {1:E3}", edge, nowK);
                bad += Report(nowL > 0.0,
                              "ниже K-края {0:F2} кэВ канал L НЕ пуст: {1:E3} ({2:F4} % отклика)",
                              edge, nowL, total > 0.0 ? 100.0 * nowL / total : 0.0);

                // ⛔ И КУДА ИМЕННО он лёг: «канал не пуст» одно ничего не
                // говорит о кинематике, а именно она тут и проверяется.
                //
                // Граница выводится, а не подбирается. L-квант не может унести
                // больше своего L-края (у иода 5.19 кэВ), а в канал рентгена
                // история попадает только когда рентген унёс НЕ МЕНЬШЕ остальных
                // статей вместе (`PickChannel`: `lossXray >= rest`), то есть
                // весь вылет не больше удвоенного L-края — 10.4 кэВ у иода.
                // Значит ВЕСЬ канал обязан лежать выше депозита E − 2·L-край.
                // Запас 0.1 % оставлен на округление бина, а не на физику.
                //
                // ⚠ Через K-край эту границу ставить НЕЛЬЗЯ: ниже него E − K-край
                // отрицателен, порог сползает к нулю, и утверждение становится
                // пустым — «всё лежит выше нуля» верно всегда.
                double lEdge = HighestLEdge(geometry);
                if (lEdge > 0.0)
                {
                    double floorKev = Math.Max(0.0, energy - 2.0 * lEdge);
                    int floorBin = EfficiencySimulator.PeakBin(floorKev, binKev);
                    double[] lRow = splitArm[l];
                    double near = Window(lRow, floorBin, lRow.Length - 1);
                    bad += Report(near >= 0.999 * nowL,
                                  "L-вылет лёг ВЫШЕ депозита {0:F2} кэВ (E − 2·L-край {1:F2}): "
                                  + "{2:F2} % канала",
                                  floorKev, lEdge, nowL > 0.0 ? 100.0 * near / nowL : 0.0);
                }
            }
            else if (edge > 0.0)
            {
                // Выше края работают обе серии, и требовать пустоты нельзя ни от
                // одной. Печатается для чтения глазами, приговора тут нет.
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                  "--   выше K-края {0:F2} кэВ обе серии открыты, "
                                  + "правило пустоты не применяется", edge));
            }

            return bad;
        }

        /// <summary>
        /// Самый НИЗКИЙ K-край среди элементов кристалла, кэВ; 0 — данных нет.
        /// Ниже него K-серия не открыта ни у одного элемента, и канал K обязан
        /// быть пуст.
        /// </summary>
        static double LowestKEdge(GeometryModel geometry)
        {
            double lowest = 0.0;
            if (geometry == null || geometry.Crystal == null)
            {
                return 0.0;
            }

            foreach (KeyValuePair<int, double> pair in geometry.Crystal.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(pair.Key);
                if (f == null || !(f.KEdgeKev > 0.0))
                {
                    continue;
                }

                if (lowest <= 0.0 || f.KEdgeKev < lowest)
                {
                    lowest = f.KEdgeKev;
                }
            }

            return lowest;
        }

        /// <summary>
        /// Самый ВЫСОКИЙ L-край среди элементов кристалла, кэВ; 0 — данных нет.
        /// Больше него L-квант унести не может ни у одного элемента.
        /// </summary>
        static double HighestLEdge(GeometryModel geometry)
        {
            double highest = 0.0;
            if (geometry == null || geometry.Crystal == null)
            {
                return 0.0;
            }

            foreach (KeyValuePair<int, double> pair in geometry.Crystal.Fractions)
            {
                if (!(pair.Value > 0.0))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(pair.Key);
                if (f == null || !f.HasL || f.LEdgeKev == null)
                {
                    continue;
                }

                foreach (double e in f.LEdgeKev)
                {
                    if (e > highest)
                    {
                        highest = e;
                    }
                }
            }

            return highest;
        }

        static EfficiencySimulator Make(GeometryModel geometry, int histories, bool splitXray)
        {
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = histories,
                PeakHalfWidthKev = 0.0,
                // (`AMBER16` п. 1) Единственное, чем плечи различаются.
                SplitXrayShells = splitXray
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

        /// <summary>
        /// События окна В ИСТОРИЯХ: доля × n у центральных трёх бинов
        /// (`at−1…at+1`) и у всего окна (`at−12…at+12`). Континуум считается
        /// аналоговой веткой с единичными весами (конус выключен), но ПОСЛЕ
        /// счёта бины перекладываются по координате света
        /// (`RemapLightScale`, физика F11): вес бина делится между двумя
        /// соседними, и целым остаётся лишь Σ по окну — с точностью до
        /// перелива через ДВА края окна, то есть меньше двух историй. Больший
        /// остаток — веса не единичны, и метод отказывает.
        /// </summary>
        static bool Count(double[] row, int at, int histories, out int local, out int around)
        {
            double l = Window(row, at - 1, at + 1) * histories;
            double a = Window(row, at - 12, at + 12) * histories;
            local = (int)Math.Round(l);
            around = (int)Math.Round(a);
            return Math.Abs(a - around) < 2.0;
        }

        /// <summary>
        /// Пик в окне: максимум ищется в ±12 бинах от энергетического места
        /// `at` (в световой шкале пик стоит не там), окно из 25 бинов
        /// центрируется на нём, `local` — три бина у максимума, `around` — всё
        /// окно; оба в историях. Ложь при нецелой Σ окна — см. <see cref="Count"/>.
        /// </summary>
        static bool PeakStat(double[] row, int at, int histories,
                             out int center, out int local, out int around)
        {
            center = at;
            double top = -1.0;
            for (int b = Math.Max(0, at - 12); b <= at + 12 && b < row.Length; b++)
            {
                if (row[b] > top)
                {
                    top = row[b];
                    center = b;
                }
            }

            return Count(row, center, histories, out local, out around);
        }

        /// <summary>
        /// Нуль «ровное плато» С ТЕМ ЖЕ ПОИСКОМ МАКСИМУМА: N событий
        /// рассыпаются равномерно по 25 бинам, максимум берётся среди тех, у
        /// кого есть оба соседа, статистика — сумма трёх бинов у максимума.
        /// Хвост P(S ≥ k) — долей розыгрышей. Бином здесь не годится: выбор
        /// максимума сдвигает долю «плато» с 12 % вверх, и тем сильнее, чем
        /// меньше N. Зерно фиксировано — приговор воспроизводим побитово.
        /// </summary>
        static double NullTail(int n, int k)
        {
            if (k <= 0)
            {
                return 1.0;
            }

            if (n <= 0)
            {
                return 0.0;
            }

            const int Trials = 20000;
            var rng = new Random(20260912);
            int[] bins = new int[25];
            int hits = 0;
            for (int t = 0; t < Trials; t++)
            {
                Array.Clear(bins, 0, bins.Length);
                for (int i = 0; i < n; i++)
                {
                    bins[rng.Next(25)]++;
                }

                int best = -1, at = 1;
                for (int b = 1; b < 24; b++)
                {
                    if (bins[b] > best)
                    {
                        best = bins[b];
                        at = b;
                    }
                }

                if (bins[at - 1] + bins[at] + bins[at + 1] >= k)
                {
                    hits++;
                }
            }

            return (double)hits / Trials;
        }

        /// <summary>Окно канала в историях, бин за бином — для чтения глазами.</summary>
        static string WindowText(double[] row, int at, int histories)
        {
            var sb = new StringBuilder();
            for (int b = Math.Max(0, at - 12); b <= at + 12 && b < row.Length; b++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                if (b == at - 1 || b == at + 2)
                {
                    sb.Append('|');
                }

                sb.Append((row[b] * histories).ToString("F1", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Ключ порчи `--spoil=flat`: ±12 бинов вокруг максимума в ±12 бинах
        /// от `at` заменяются средним — плато той же площади.
        /// </summary>
        static void Flatten(double[] row, int at)
        {
            int center = at;
            double top = -1.0;
            for (int b = Math.Max(0, at - 12); b <= at + 12 && b < row.Length; b++)
            {
                if (row[b] > top)
                {
                    top = row[b];
                    center = b;
                }
            }

            int lo = Math.Max(0, center - 12), hi = Math.Min(row.Length - 1, center + 12);
            double mean = Window(row, lo, hi) / (hi - lo + 1);
            for (int b = lo; b <= hi; b++)
            {
                row[b] = mean;
            }

            // Крошечный выступ в центре, чтобы поиск максимума нашёл ЭТО же
            // окно, а не его край: иначе половина окна ушла бы в неразровненное.
            row[center] = mean * (1.0 + 1.0E-9);
        }

        /// <summary>
        /// Ключ порчи `--spoil=merge`: канал DE ссыпается в канал SE — прежняя
        /// слитая раскладка, которую проба обязана отличить.
        /// </summary>
        static void Merge(double[][] channels)
        {
            int se = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation;
            int de = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble;
            for (int b = 0; b < channels[de].Length; b++)
            {
                channels[se][b] += channels[de][b];
                channels[de][b] = 0.0;
            }
        }

        static int Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("{0} {1}", ok ? "ok  " : "ПЛОХО", string.Format(CultureInfo.InvariantCulture, format, args));
            return ok ? 0 : 1;
        }
    }
}
