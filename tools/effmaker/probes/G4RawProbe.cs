using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace G4RawProbe
{
    /// <summary>
    /// СЫРОЙ отклик нашего переноса рядом со сценой для внешнего арбитра —
    /// одним заходом, без уширения и без фита.
    ///
    /// ЗАЧЕМ. `G4CompareProbe` сверяет с Geant4 уже УШИРЕННУЮ модель на шкале
    /// измеренного спектра, и в такой сверке слиты три вещи: перенос, форма
    /// пика и энергетическая калибровка. Для вопроса «какой физики у нас нет»
    /// нужен голый перенос: распределение ПОГЛОЩЁННОЙ энергии, бин в бин с
    /// `g4cf … hist`. Пик вылета, комптоновский край, обратное рассеяние и
    /// K-вылет в сырой гистограмме видны сами, а уширение их прячет.
    ///
    ///     g4rawprobe --geometry=X.in [--spectrum=X.xml] --energy=661.657
    ///                [--n=2000000] [--bin=1] [--seed=20260902]
    ///                [--out=raw.csv] [--scene=scene.txt]
    ///                [--bands=1-12,13-25,55-59] [--peakw]
    ///
    /// `--out=` — `keV,response` (доля на историю; ПОСЛЕДНИЙ бин — пик полного
    /// поглощения, см. <see cref="EfficiencySimulator.Response"/>).
    /// `--scene=` — `DumpScene()` в формате, который читает `g4cf scene`.
    ///
    /// ⛔ `--bands=` СЧИТАЕТ ПОЛОСЫ САМА ПРОБА (`T134`). До 10.09.2026 полосы
    /// каждый раз считались отдельным скриптом поверх `--out=`, у каждой сверки
    /// своим, — то есть числа полос разных заходов сравнивались на веру. Ключ
    /// берёт список `a-b` в кэВ через запятую и печатает по каждой полосе долю
    /// на историю и долю от полной суммы.
    ///
    /// ⚠ Полоса считается по КОНТИНУУМУ: бины `i` с энергией `i*bin` в `[a, b]`
    /// и `i &lt; N-1`. Последний бин — пик полного поглощения, его энергия в
    /// `--out=` записана как `(N-1)*bin` и в полосу НЕ входит, иначе полоса у
    /// верхнего края шкалы молча вобрала бы весь пик. Пик печатается отдельной
    /// строкой выше.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, spectrumPath = null;
            string outPath = null, scenePath = null;
            double energyKev = 661.657, binKev = 1.0;
            int histories = 2000000, seed = 20260902;
            // ⛔ ШКАЛА. Наш отклик по умолчанию переложен в шкалу СВЕТА
            // (`F11`): прибор меряет не энергию, а свет, и у сцинтиллятора
            // выход на килоэлектронвольт от энергии зависит. Geant4 отдаёт
            // ЭНЕРГОВЫДЕЛЕНИЕ и о свете не знает — значит сверять надо с
            // выключенной непропорциональностью, иначе узкие особенности
            // (пики вылета, 511) сравниваются со сдвигом.
            bool light = true;
            // `A68`: допуск пика из геометрии вместо нуля (`E34`).
            bool peakw = false;
            // Конус на габарит сцены в аналоговой ветви (`A57`) — ключ замера A/B.
            bool cone = false;
            // Когерентное своим каналом во взвешенной ветви (`N13`) — рычаг `A58`.
            bool rayl2 = false;
            double escT0 = -1.0;      // <0 — не трогать умолчание (`A63`)
            // Ключи АБЛЯЦИИ каналов утечки (`A63`): чем держится каждая полоса.
            bool xray = true, esc = true, brem = true;
            bool noLXray = false;                       // `A60`
            bool noKLCascade = false;                   // `A101`
            double escSlope = -1.0;
            double escSoft = -1.0, escSoftKev = -1.0;   // `A63`
            double escCurve = -1.0;                     // `A70`
            double[][] bands = null;                    // `T134`, --bands=
            foreach (string a in args)
            {
                if (a == "--no-light") { light = false; continue; }
                // ⛔ `A68`: допуск пика ИЗ ГЕОМЕТРИИ (`E34`, ключ склада
                // `--peakw=1`). Не удобство, а ПЛЕЧО встречной проверки ниже:
                // взвешенная ветвь зовёт пиком «недобрало не больше допуска»,
                // аналоговая — «округлилось в бин пика», и при нулевом допуске
                // это РАЗНЫЕ множества. С допуском из геометрии полуширина
                // ПШПВ перекрывает окно округления, определения сходятся, и
                // отношение ветвей начинает мерить перенос, а не разнобой
                // определений.
                if (a == "--peakw") { peakw = true; continue; }
                if (a == "--cone") { cone = true; continue; }
                if (a == "--rayl2") { rayl2 = true; continue; }
                if (a == "--no-xray") { xray = false; continue; }
                if (a == "--no-esc") { esc = false; continue; }
                if (a == "--no-brem") { brem = false; continue; }
                // `A60`, АБЛЯЦИЯ: снять вылет L-рентгена. Выключенный
                // ключ возвращает счёт физики 14 до последнего бита.
                if (a == "--no-lxray") { noLXray = true; continue; }
                // `A101`: атомный каскад K→L. Умолчанием ВКЛ (физика 16), как и
                // в расчёте матрицы, — иначе проба мерила бы не то, что склад.
                // Ключ выключает его для абляции.
                if (a == "--no-klcasc") { noKLCascade = true; continue; }
                if (a.StartsWith("--esc-soft=", StringComparison.Ordinal))
                {
                    escSoft = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-curve=", StringComparison.Ordinal))
                {
                    escCurve = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-soft-kev=", StringComparison.Ordinal))
                {
                    escSoftKev = double.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-slope=", StringComparison.Ordinal))
                {
                    escSlope = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-t0=", StringComparison.Ordinal))
                {
                    // `A63`: порог включения вылета электрона, кэВ. Умолчание 350.
                    //
                    // ⛔ «Ниже порога вылета нет ВООБЩЕ» — БОЛЬШЕ НЕ ПРАВДА, и
                    // здесь это стояло написанным (снято 10.09.2026, полоса П1).
                    // Порогово-линейная часть действительно даёт ноль ниже 350,
                    // но `A63` добавила к глубине вылета МЯГКОЕ слагаемое
                    // `ElectronEscapeSoftAmp·exp(−T/ElectronEscapeSoftKev)`,
                    // и при T = 26 кэВ (фотоэлектрон линии 59.5) оно равно
                    // 0.5·exp(−0.26) = 0.386 — то есть вылет там ЕСТЬ и он
                    // велик. Мерено на этой же пробе: `--no-esc` двигает
                    // отношение ветвей на 59.5 кэВ с 1.0097 до 1.0045.
                    escT0 = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal)) energyKev = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenePath = a.Substring(8);
                else if (a.StartsWith("--bands=", StringComparison.Ordinal))
                {
                    // `T134`: список полос `a-b` через запятую, границы в кэВ.
                    // ⛔ Плохой список — ОТКАЗ, а не пустой разбор: молча
                    // пропущенная полоса выглядит как «полос нет», и это ровно
                    // тот случай, из-за которого ключ и заводится.
                    if (!TryParseBands(a.Substring(8), out bands))
                    {
                        Console.Error.WriteLine("не разобрал --bands=, нужен список «a-b» через запятую, границы в кэВ: " + a.Substring(8));
                        return 2;
                    }

                    continue;
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            GeometryModel geometry = null;
            if (geometryPath != null)
            {
                if (!File.Exists(geometryPath))
                {
                    Console.Error.WriteLine("нет файла геометрии: " + geometryPath);
                    return 2;
                }

                geometry = GeometryModel.Load(geometryPath);
            }
            else if (spectrumPath != null)
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                ResultDataFile file;
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var stream = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    file = (ResultDataFile)serializer.Deserialize(stream);
                }

                ResultData rd = file.ResultDataList[0];
                if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
                {
                    Console.Error.WriteLine("у спектра нет кривой с геометрией");
                    return 2;
                }

                geometry = rd.Efficiency.Geometry;
            }
            else
            {
                Console.Error.WriteLine("нужен --geometry=<файл.in> или --spectrum=<файл.xml>");
                return 2;
            }

            var simulator = new EfficiencySimulator(geometry);
            simulator.Seed = seed;
            simulator.Histories = histories;
            simulator.LightNonproportionality = light;
            simulator.LXrayEscape = !noLXray;           // `A60`
            simulator.KLCascade = !noKLCascade;         // `A101`
            simulator.AnalogConeSampling = cone;
            simulator.RayleighToCrystal = rayl2;
            simulator.XrayEscape = xray;
            simulator.ElectronEscape = esc;
            simulator.Bremsstrahlung = brem;
            if (escSoft >= 0.0) { simulator.ElectronEscapeSoftAmp = escSoft; }
            if (escCurve > 0.0) { simulator.ElectronEscapeCurve = escCurve; }
            if (escSoftKev > 0.0) { simulator.ElectronEscapeSoftKev = escSoftKev; }
            if (escSlope >= 0.0)
            {
                simulator.ElectronEscapeSlope = escSlope;
            }

            if (escT0 >= 0.0)
            {
                simulator.ElectronEscapeT0Kev = escT0;
                Console.WriteLine("порог вылета электрона: {0} кэВ (умолчание 350)", escT0);
            }
            if (peakw)
            {
                simulator.PeakHalfWidthKev = geometry.PeakHalfWidthKev(energyKev);
            }

            Console.WriteLine("допуск пика: {0} кэВ ({1})",
                              simulator.PeakHalfWidthKev.ToString("F3", CultureInfo.InvariantCulture),
                              peakw ? "ИЗ ГЕОМЕТРИИ, --peakw (`E34`)" : "ноль, как у поставочного склада");
            Console.WriteLine("шкала: {0}", light ? "СВЕТ (как в матрице)" : "энерговыделение (как у Geant4)");
            Console.WriteLine("розыгрыш аналоговой: {0}", cone ? "КОНУС на габарит сцены (`A57`)" : "полная сфера");

            if (scenePath != null)
            {
                File.WriteAllText(scenePath, simulator.DumpScene(), new UTF8Encoding(false));
                Console.WriteLine("сцена: " + scenePath);
            }

            double error;
            double[] response = simulator.Response(energyKev, binKev, out error);
            if (response == null)
            {
                Console.Error.WriteLine("отклик не посчитался");
                return 1;
            }

            double sum = 0.0;
            for (int i = 0; i < response.Length; i++)
            {
                sum += response[i];
            }

            Console.WriteLine("E={0} кэВ, историй {1}, бин {2} кэВ, бинов {3}", energyKev, histories, binKev, response.Length);
            Console.WriteLine("пик {0:E6}, полная {1:E6}, ошибка взвешенной ветки {2:F2} %, шум континуума {3:F2} %",
                              response[response.Length - 1], sum, error, simulator.LastContinuumRelativeError);

            // ⛔ ВСТРЕЧНАЯ ПРОВЕРКА ДВУХ ВЕТВЕЙ. Пик берётся у ВЗВЕШЕННОЙ ветви,
            // континуум — у АНАЛОГОВОЙ, и это два независимых оценивателя одной
            // величины. Аналоговая своё попадание в пик выбрасывает
            // (`CountPeakBinDropped`), но СЧИТАЕТ, — значит её оценку пика можно
            // напечатать и сверить с взвешенной. Разошлись — расходятся ветви, а
            // не мы с арбитром.
            //
            // ⛔ ЧИТАТЬ ОТНОШЕНИЕ БЕЗ `--peakw` НЕЛЬЗЯ (`A68`, 10.09.2026).
            // Ветви зовут пиком РАЗНОЕ: взвешенная — «недобрало не больше
            // `PeakHalfWidthKev`» (`InPeak`), аналоговая — «округлилось в бин
            // пика» (`bin >= peak`, без оговорки). При нулевом допуске второе
            // МНОЖЕСТВО ШИРЕ первого, и часть отношения — разнобой
            // определений, а не физика. Мерить перенос — плечом `--peakw`, где
            // допуск перекрывает окно округления и определения совпадают.
            int n = Math.Max(1000, histories);
            double analogPeak = simulator.WeightPeakBinDropped / n;
            Console.WriteLine("пик аналоговой ветви {0:E6} ({1} историй из {2}), взвеш./аналог. = {3:F4}",
                              analogPeak, simulator.CountPeakBinDropped, n,
                              analogPeak > 0.0 ? response[response.Length - 1] / analogPeak : 0.0);

            // ⛔ ТРЕТИЙ ОЦЕНИВАТЕЛЬ ТОЙ ЖЕ ВЕЛИЧИНЫ — `TotalEfficiency` (`A56`).
            // Полную эффективность считают ДВА разных обхода: сумма отклика выше
            // и этот. Обход у него свой, и правка `A54` (пары вне кристалла) в
            // него не дошла — расхождение выше 1022 кэВ было ровно об этом.
            // Печатается рядом, чтобы следующее такое расхождение увидел кто
            // угодно, а не только тот, кто пошёл его искать.
            // ⛔ ПЕРЕПОЛНЕНИЕ ОЧЕРЕДЕЙ (`A65`): отброшенный квант — это тихо
            // заниженный континуум. Счётчики были заведены и НЕ ЧИТАЛИСЬ ни
            // одной пробой — печатаются здесь, чтобы отказ было видно.
            Console.WriteLine("отброшено переполнением: очередь квантов {0}, вылеты {1}",
                              simulator.CountPendingDropped, simulator.CountEscapeDropped);
            Console.WriteLine("комптонов в кристалле {0}, с вакансией {1}, ответили рентгеном {2} (`A61`)",
                              simulator.CountCrystalCompton, simulator.CountCrystalVacancy,
                              simulator.CountVacancyXray);
            Console.WriteLine("флуоресценция: K-квантов {0}, L-квантов {1} (`A60`)",
                              simulator.CountKXray, simulator.CountLXray);
            // `A101`: знаменатель рядом с числителем нарочно — без него
            // «мало каскадов» неотличимо от «мало Kα».
            Console.WriteLine("каскад K→L: вакансий на L {0}, из них ответили квантом {1} (`A101`)",
                              simulator.CountKLVacancy, simulator.CountKLCascade);

            double totalError;
            double totalSecond = simulator.TotalEfficiency(energyKev, out totalError);
            Console.WriteLine("полная вторым обходом (TotalEfficiency) {0:E6} ± {1:F2} %, отклик/обход = {2:F4}",
                              totalSecond, totalError, totalSecond > 0.0 ? sum / totalSecond : 0.0);

            // ⛔ ПОЛОСЫ СЧИТАЕТ САМА ПРОБА (`T134`). Прежде их считали скриптом
            // поверх `--out=` — у каждой сверки своим, и числа полос разных
            // заходов сравнивались на веру. Теперь определение полосы одно и
            // живёт рядом с расчётом.
            if (bands != null)
            {
                Console.WriteLine();
                Console.WriteLine("ПОЛОСЫ КОНТИНУУМА (бины i*{0} кэВ, i < {1}; бин пика НЕ входит)",
                                  binKev.ToString("F3", CultureInfo.InvariantCulture),
                                  response.Length - 1);
                // Континуум — всё, кроме последнего бина: он несёт пик полного
                // поглощения, а его энергия в `--out=` записана как (N-1)*bin.
                double continuum = sum - response[response.Length - 1];
                foreach (double[] bd in bands)
                {
                    double inBand = 0.0;
                    int binsInBand = 0;
                    for (int i = 0; i < response.Length - 1; i++)
                    {
                        double kev = i * binKev;
                        if (kev >= bd[0] && kev <= bd[1])
                        {
                            inBand += response[i];
                            binsInBand++;
                        }
                    }

                    // ⚠ Пустая полоса — не ноль отклика, а промах по шкале, и
                    // это разные беды. Число бинов печатается рядом, чтобы их
                    // было видно ОТДЕЛЬНО.
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,8:F2}...{1,8:F2} кэВ: {2:E6}  ({3:F3} % отклика, {4:F3} % континуума, бинов {5})",
                        bd[0], bd[1], inBand,
                        sum > 0.0 ? 100.0 * inBand / sum : 0.0,
                        continuum > 0.0 ? 100.0 * inBand / continuum : 0.0,
                        binsInBand));
                }
            }

            if (outPath != null)
            {
                using (var writer = new StreamWriter(outPath, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("keV,response");
                    for (int i = 0; i < response.Length; i++)
                    {
                        writer.WriteLine("{0},{1}",
                                         (i * binKev).ToString("F3", CultureInfo.InvariantCulture),
                                         response[i].ToString("E8", CultureInfo.InvariantCulture));
                    }
                }

                Console.WriteLine("отклик: " + outPath);
            }

            return 0;
        }

        /// <summary>
        /// Разбор `--bands=a-b,c-d,…` (`T134`). Границы в кэВ, разделитель
        /// дробной части — ТОЧКА, разбор явной инвариантной культурой
        /// (приказ Amber 05.09.2026), как и у всех остальных ключей пробы.
        /// ⛔ Возвращает false на ЛЮБОМ изъяне списка: пустой список, пара без
        /// дефиса, нечисло, отрицательная граница, перевёрнутая полоса.
        /// Молчаливый пропуск здесь неотличим от «полос нет».
        /// </summary>
        static bool TryParseBands(string spec, out double[][] bands)
        {
            bands = null;
            if (string.IsNullOrEmpty(spec))
            {
                return false;
            }

            string[] parts = spec.Split(',');
            var list = new double[parts.Length][];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                // ⛔ Длина проверяется ДО поиска: `IndexOf(знак, 1)` на пустой
                //    строке бросает, и хвостовая запятая («1-12,») валила пробу
                //    исключением вместо отказа с доводом. Поймано положительным
                //    контролем 10.09.2026 — шестым плохим входом из шести.
                if (p.Length < 3)
                {
                    return false;
                }

                // Дефис ищется ПОСЛЕ первого знака, иначе «-5-10» распалось бы
                // по своему же минусу; отрицательных границ у шкалы всё равно нет.
                int dash = p.IndexOf('-', 1);
                if (dash <= 0 || dash == p.Length - 1)
                {
                    return false;
                }

                double lo, hi;
                if (!double.TryParse(p.Substring(0, dash), NumberStyles.Float, CultureInfo.InvariantCulture, out lo)
                    || !double.TryParse(p.Substring(dash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out hi))
                {
                    return false;
                }

                if (lo < 0.0 || hi < lo)
                {
                    return false;
                }

                list[i] = new[] { lo, hi };
            }

            bands = list;
            return true;
        }
    }
}
