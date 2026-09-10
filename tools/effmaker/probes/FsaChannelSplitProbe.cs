using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaChannelSplitProbe
{
    /// <summary>
    /// (`S3`, первый этап; `T176`; `S167`) ЧЕТЫРЕ КАНАЛА ОТКЛИКА, ПРОВЕДЁННЫЕ
    /// ЧЕРЕЗ ВЕСЬ РАЗБОР, И ТОЖДЕСТВО «Σ КАНАЛОВ = ЛЕНТА» В КАЖДОЙ ТОЧКЕ.
    ///
    /// Постановка Amber 10.09.2026. Разделение каналов нужно не ради картинки:
    /// каскадная поправка `CF` правит ТОЛЬКО канал полного поглощения, а по
    /// суммарной матрице её пришлось бы одинаково растянуть и на пик, и на весь
    /// комптоновский хвост. Отсюда и остальное: видно, из какого канала пришла
    /// ошибка физики; вылет 511 и вылет рентгена проверяются порознь; двойной
    /// счёт при добавлении сумм-пиков и сумм-континуума становится видимым.
    ///
    /// ⛔ ЧТО ИМЕННО ЗДЕСЬ ДОКАЗЫВАЕТСЯ — шесть точек тождества, названные в
    /// постановке:
    ///
    ///   1. на входе кэша (ножа ещё не было)   — плечо `--floor=0`;
    ///   2. после подпорогового ножа           — умолчание порога доверия;
    ///   3. после уширения                     — то же, что 4, других
    ///      наблюдаемых у уширенной ленты нет;
    ///   4. в `FsaComponentResult`             — Σ `ChannelCurves` = `Curve`;
    ///   5. после свёртки «прочих»             — слои `BuildStackedLayers`;
    ///   6. после родительской группировки     — `BuildParentLayers`.
    ///
    /// В точках 5 и 6 подложка РАЗНЕСЕНА по слоям, а в каналы она не идёт
    /// (решение Amber 10.09.2026, «Подложка вне каналов»), поэтому тождество
    /// там читается как Σ каналов = `Curve` − `ContinuumCurve`.
    ///
    /// Точки 1 и 2 живут внутри разбора и наружу не выходят — они читаются
    /// ОТРАЖЕНИЕМ из кэша гистограмм (`FsaAnalyzer.deposits`). Это не обход
    /// приличий: складывать в приложение диагностику, которую читает одна
    /// проба, дороже, чем прочитать поле.
    ///
    /// ⛔ ЗАЩИТА ОТ ПОВТОРЕНИЯ `S37`: каждый подслой обязан быть неотрицателен
    /// и поканально не выше своей ленты. Проверяется у всех четырёх каналов и у
    /// подслоя сумм-пиков.
    ///
    /// `T176`: на спектре с матрицей, сумм-пиками и ПОЛНЫМ РЯДОМ проверяется,
    /// что родительская строка равна сумме дочерних — по ленте, по сумм-пикам
    /// и по каждому каналу. Прежде это было доказано на `ASN16_Th232` при
    /// НУЛЕ сумм-пиков, то есть половина кода проверялась на пустоте.
    ///
    /// `S167` (`--band=lo-hi`): центр тяжести полосы у ИЗМЕРЕНИЯ и у МОДЕЛИ
    /// порознь, а модель — ещё и по частям: полное поглощение, сумм-пик,
    /// сумм-континуум и остальные каналы. Ровно то, чего не хватало, чтобы
    /// сказать, откуда взялись оставшиеся ~8 кэВ смещения.
    ///
    ///   fsachannelsplitprobe --spectrum=X.xml --chain=Th-232 [--nuclides=40K]
    ///                        [--band=470-560] [--floor=0] [--dump=out.csv]
    ///
    /// Запускать из рабочего каталога корпуса (`mk_appwd.ps1`).
    /// </summary>
    static class Program
    {
        static int bad;

        /// <summary>
        /// Допуск тождества. Числа — суммы сотен тысяч слагаемых порядка 1e5
        /// отсчётов, и порядок сложения у ленты и у каналов разный: лента
        /// копится по группам бинов, каналы — по каналам внутри группы. Значит
        /// совпадение ожидается машинное, а не побитовое, и мера здесь
        /// ОТНОСИТЕЛЬНАЯ, от величины самой ленты.
        /// </summary>
        const double Tol = 1.0E-9;

        static readonly string[] ChannelNames =
        {
            "полное поглощение", "комптон", "вылет 511", "вылет рентгена"
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это
            // статика, отражение её не видит, и снятая позже она уже могла
            // быть уведена.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, dumpPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            double bandLo = 0.0, bandHi = 0.0;
            double floorKev = -1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.Add(a.Substring(8));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(11).Split(','));
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--floor=", StringComparison.Ordinal))
                    floorKev = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--band=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(7).Split('-');
                    if (parts.Length != 2)
                    {
                        Console.Error.WriteLine("--band=<нижняя>-<верхняя>, кэВ");
                        return 2;
                    }

                    bandLo = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    bandHi = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (chains.Count == 0 && nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужен состав: --chain=Th-232 и/или --nuclides=176LU");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр  : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор  : {0}", ProbeDeviceConfig.Attach(rd));

            ResponseMatrix matrix = null;
            string material = null;
            if (rd.Efficiency != null && rd.Efficiency.HasGeometry && rd.Efficiency.UseResponseMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrix loaded = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal, out fileFormat);
                if (loaded != null && loaded.IsValidFor(rd.Efficiency.Geometry))
                {
                    matrix = loaded;
                    material = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
                }
                else if (loaded == null)
                {
                    Console.WriteLine("матрица : НЕТ — {0}, формат файла {1}", refusal, fileFormat);
                }
                else
                {
                    Console.WriteLine("матрица : файл есть, но клеймо не сошлось");
                }
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВХОДА. Без матрицы каналов нет вовсе, и
            // все тождества ниже прошли бы на пустоте — «0 = 0» верно всегда.
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ без матрицы каналов нет — проверять нечего, опыт негоден");
                return 1;
            }

            if (!matrix.HasChannels || matrix.ChannelRows.Length != EfficiencySimulator.ResponseChannelCount)
            {
                Console.Error.WriteLine("⛔ у матрицы {0} каналов вместо {1}",
                                        matrix.HasChannels ? matrix.ChannelRows.Length : 0,
                                        EfficiencySimulator.ResponseChannelCount);
                return 1;
            }

            Console.WriteLine("матрица : есть, {0}, каналов {1}, бин {2} кэВ",
                              material, matrix.ChannelRows.Length,
                              matrix.BinKev.ToString("F3", CultureInfo.InvariantCulture));

            FsaSampleSpec spec = SpecOf(rd, chains, nuclides);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            List<NuclideDefinition> definitions = FsaSampleLibrary.AsDefinitions(library);
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, null, definitions);

            // ⚠ В ФИТ ИДЁТ БИБЛИОТЕКА ОБРАЗЦА, А НЕ СОБРАННАЯ ПО ПОДПИСЯМ ПИКОВ.
            // `FsaLibrary.BuildFromPeaks` строит компоненты из того, что подписал
            // финдер, и члены ряда выходят оттуда ОДИНОЧНЫМИ, без
            // `DecayChainRoot`. На этом первый прогон `T176` и споткнулся:
            // `G1S16_Th232_Denta100` дал Tl-208 / Ac-228 / Pb-212 / Bi-212 четырьмя
            // самостоятельными строками и НИ ОДНОЙ родительской — то есть
            // проверять было нечего. Корпусная проба кормит фит именно
            // `library`, а пики ей нужны для подписей и отчёта.
            List<FsaComponent> byPeaks = library;
            Console.WriteLine("состав  : пиков {0}, образов {1} (--lib=sample)", peaks.Count, byPeaks.Count);
            if (byPeaks.Count == 0)
            {
                Console.Error.WriteLine("⛔ библиотека пуста");
                return 1;
            }

            // ---------------------------------------------------------------
            // Плечо, на котором меряется всё: каскад целиком, как в приложении.
            // ---------------------------------------------------------------
            FsaAnalyzer analyzer;
            FsaResult result = Run(rd, byPeaks, matrix, material, true, true, floorKev,
                                   "полный каскад", out analyzer);
            if (result == null)
            {
                return 1;
            }

            int builtSumPeaks = CountSumPeaks(matrix, material, byPeaks);
            Console.WriteLine("сумм-пиков построено: {0}", builtSumPeaks);

            // ---------------------------------------------------------------
            // Точки 1 и 2: гистограмма поглощения в кэше.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== точка {0}: гистограмма в кэше (порог доверия {1}) ===",
                              floorKev == 0.0 ? "1, ножа не было" : "2, после ножа",
                              floorKev < 0.0 ? "по умолчанию" : floorKev.ToString("F1", CultureInfo.InvariantCulture));
            CheckDeposits(analyzer);

            // ---------------------------------------------------------------
            // Точки 3 и 4: уширенная лента компонента.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== точки 3-4: FsaComponentResult (после уширения) ===");
            int withChannels = 0;
            foreach (FsaComponentResult component in result.Components)
            {
                if (component.ChannelCurves == null)
                {
                    continue;
                }

                withChannels++;
                CheckIdentity("компонент " + component.Name, component.Curve, null,
                              component.ChannelCurves, component.SumPeakCurve);
            }

            Same("компонентов с раскладкой по каналам больше нуля", true, withChannels > 0);
            Console.WriteLine("компонентов {0}, из них с каналами {1}", result.Components.Count, withChannels);

            // ---------------------------------------------------------------
            // Точка 5: слои со свёрнутым «прочим» и разнесённой подложкой.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== точка 5: слои после свёртки «прочих» ===");
            List<FsaStackLayer> layers = result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers);
            CheckLayers(layers);

            // ---------------------------------------------------------------
            // Точка 6 и `T176`: родительская группировка.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== точка 6 и T176: родительская группировка ===");
            List<FsaStackLayer> parents = FsaPresentationBuilder.BuildParentLayers(
                result, FsaResult.DefaultMaxNamedLayers);
            CheckLayers(parents);
            CheckParents(result, parents, builtSumPeaks);

            // ---------------------------------------------------------------
            // Каскадный `CF` меняет ТОЛЬКО канал пика.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== CF правит только канал полного поглощения ===");
            FsaAnalyzer noCascadeAnalyzer, cfOnlyAnalyzer;
            FsaResult noCascade = Run(rd, byPeaks, matrix, material, false, false, floorKev,
                                      "без каскада", out noCascadeAnalyzer);
            FsaResult cfOnly = Run(rd, byPeaks, matrix, material, true, false, floorKev,
                                   "CF без сумм-пиков", out cfOnlyAnalyzer);
            if (noCascade != null && cfOnly != null)
            {
                CompareChannels(noCascade, cfOnly);
            }

            // ⚠ ТО ЖЕ СРАВНЕНИЕ ДО УШИРЕНИЯ — по гистограммам из кэша.
            // Оно разводит две разные вещи: тронул ли `CF` САМ вклад
            // непиковых каналов (тут обязан быть СТРОГИЙ НОЛЬ: накопление
            // каналов независимо) — или их сдвинуло УШИРЕНИЕ, у которого
            // порог отсечки (`top·1e-5`) берётся от МАКСИМУМА ПОЛНОЙ
            // гистограммы, а его `CF` меняет. Без этого развода второе
            // читалось бы как «`CF` протёк в комптон».
            if (noCascadeAnalyzer != null && cfOnlyAnalyzer != null)
            {
                CompareDeposits(noCascadeAnalyzer, cfOnlyAnalyzer);
            }

            // ---------------------------------------------------------------
            // `S167`: полоса.
            // ---------------------------------------------------------------
            if (bandHi > bandLo)
            {
                Console.WriteLine();
                Console.WriteLine("=== S167: полоса {0}…{1} кэВ ===",
                                  bandLo.ToString("F1", CultureInfo.InvariantCulture),
                                  bandHi.ToString("F1", CultureInfo.InvariantCulture));
                Band(rd, result, bandLo, bandHi);
            }

            // ---------------------------------------------------------------
            // Контрольные числа для сверки «до/после»: их сравнивают со сборкой
            // прежнего кода, и по ним называется цена перехода на сумму каналов.
            // ---------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== контрольные числа (сверка до/после) ===");
            Control(result, dumpPath);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: всё сошлось" : "ИТОГ: расхождений " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // Проверки
        // ==================================================================

        /// <summary>
        /// Гистограммы поглощения из кэша разбора — ОТРАЖЕНИЕМ. Проверяется
        /// то же тождество, что и у лент, но на бинах энергии: до уширения.
        /// </summary>
        static void CheckDeposits(FsaAnalyzer analyzer)
        {
            FieldInfo field = typeof(FsaAnalyzer).GetField(
                "deposits", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                Console.Error.WriteLine("⛔ поля кэша `deposits` нет — проба смотрит не туда");
                bad++;
                return;
            }

            var cache = field.GetValue(analyzer) as System.Collections.IDictionary;
            if (cache == null || cache.Count == 0)
            {
                Console.Error.WriteLine("⛔ кэш гистограмм пуст — мерить нечего");
                bad++;
                return;
            }

            int checkedCount = 0, withChannels = 0;
            double worst = 0.0;
            string worstName = null;
            foreach (System.Collections.DictionaryEntry entry in cache)
            {
                object deposit = entry.Value;
                if (deposit == null)
                {
                    continue;
                }

                var component = entry.Key as FsaComponent;
                string name = component != null ? component.Name : "?";
                double[] values = FieldOf<double[]>(deposit, "Values");
                double[][] channels = FieldOf<double[][]>(deposit, "Channels");
                checkedCount++;
                if (channels == null)
                {
                    continue;
                }

                withChannels++;
                double relative = MaxRelativeGap(values, channels, null);
                if (relative > worst)
                {
                    worst = relative;
                    worstName = name;
                }
            }

            Console.WriteLine("гистограмм в кэше {0}, с каналами {1}", checkedCount, withChannels);
            Same("гистограмм с раскладкой по каналам больше нуля", true, withChannels > 0);
            Console.WriteLine("худшее относительное расхождение Σ каналов и ленты: {0} ({1})",
                              worst.ToString("E3", CultureInfo.InvariantCulture), worstName ?? "-");
            Same("Σ каналов = гистограмма", true, worst <= Tol);
        }

        /// <summary>Тождество и неотрицательность на одной ленте.</summary>
        static void CheckIdentity(string title, double[] curve, double[] spread,
                                  double[][] channels, double[] sumPeaks)
        {
            double relative = MaxRelativeGap(curve, channels, spread);
            if (relative > Tol)
            {
                Console.Error.WriteLine("⛔ {0}: Σ каналов ≠ лента, худшее относительное {1}",
                                        title, relative.ToString("E3", CultureInfo.InvariantCulture));
                bad++;
            }

            // ⛔ S37: подслой не может быть выше своей ленты и не может быть
            // отрицательным. Мера — та же лента за вычетом подложки: каналы её
            // не получают.
            double negative = 0.0, over = 0.0;
            string overName = null, negativeName = null;
            for (int c = 0; c < channels.Length; c++)
            {
                double[] row = channels[c];
                if (row == null)
                {
                    continue;
                }

                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] < negative)
                    {
                        negative = row[i];
                        negativeName = ChannelNames[c];
                    }

                    double own = Own(curve, spread, i);
                    double excess = row[i] - own;
                    if (excess > over)
                    {
                        over = excess;
                        overName = ChannelNames[c];
                    }
                }
            }

            if (sumPeaks != null)
            {
                for (int i = 0; i < sumPeaks.Length; i++)
                {
                    if (sumPeaks[i] < negative)
                    {
                        negative = sumPeaks[i];
                        negativeName = "сумм-пики";
                    }

                    double excess = sumPeaks[i] - Own(curve, spread, i);
                    if (excess > over)
                    {
                        over = excess;
                        overName = "сумм-пики";
                    }
                }
            }

            if (negative < -Tol)
            {
                Console.Error.WriteLine("⛔ {0}: отрицательный подслой «{2}», худшее {1}",
                                        title, negative.ToString("E3", CultureInfo.InvariantCulture),
                                        negativeName ?? "?");
                bad++;
            }

            if (over > Tol * Math.Max(1.0, Max(curve)))
            {
                Console.Error.WriteLine("⛔ {0}: подслой «{2}» ВЫШЕ ленты на {1} отсчётов",
                                        title, over.ToString("E3", CultureInfo.InvariantCulture),
                                        overName ?? "?");
                bad++;
            }
        }

        static void CheckLayers(List<FsaStackLayer> layers)
        {
            int withChannels = 0;
            foreach (FsaStackLayer layer in layers)
            {
                if (layer.ChannelCurves == null)
                {
                    continue;
                }

                withChannels++;
                CheckIdentity("слой " + layer.Name, layer.Curve, layer.ContinuumCurve,
                              layer.ChannelCurves, layer.SumPeakCurve);
            }

            Console.WriteLine("слоёв {0}, из них с каналами {1}", layers.Count, withChannels);
            Same("слоёв с раскладкой по каналам больше нуля", true, withChannels > 0);
        }

        /// <summary>
        /// (`T176`) Родитель = Σ дочерних: по ленте, по сумм-пикам и по каждому
        /// каналу. Дочерние берутся из результата по
        /// <see cref="FsaComponentResult.DecayChainRoot"/> — тем же признаком,
        /// каким их собирает построитель.
        /// </summary>
        static void CheckParents(FsaResult result, List<FsaStackLayer> parents, int builtSumPeaks)
        {
            List<FsaStackLayer> full = result.BuildStackedLayers(int.MaxValue);
            var children = new Dictionary<string, List<FsaStackLayer>>(StringComparer.Ordinal);
            foreach (FsaStackLayer layer in full)
            {
                if (layer.Kind == FsaComponentKind.Nuisance || string.IsNullOrEmpty(layer.DecayChainRoot))
                {
                    continue;
                }

                List<FsaStackLayer> bag;
                if (!children.TryGetValue(layer.DecayChainRoot, out bag))
                {
                    bag = new List<FsaStackLayer>();
                    children[layer.DecayChainRoot] = bag;
                }

                bag.Add(layer);
            }

            int tested = 0, sumPeakTested = 0;
            foreach (FsaStackLayer parent in parents)
            {
                List<FsaStackLayer> bag;
                if (string.IsNullOrEmpty(parent.DecayChainRoot)
                    || !children.TryGetValue(parent.DecayChainRoot, out bag) || bag.Count < 2)
                {
                    continue;
                }

                tested++;
                int channels = parent.Curve.Length;
                double[] curve = new double[channels];
                double[] sums = null;
                double[][] byChannel = null;
                foreach (FsaStackLayer child in bag)
                {
                    for (int i = 0; i < channels && i < child.Curve.Length; i++)
                    {
                        curve[i] += child.Curve[i];
                    }

                    if (child.SumPeakCurve != null)
                    {
                        if (sums == null)
                        {
                            sums = new double[channels];
                        }

                        for (int i = 0; i < channels && i < child.SumPeakCurve.Length; i++)
                        {
                            sums[i] += child.SumPeakCurve[i];
                        }
                    }

                    if (child.ChannelCurves == null)
                    {
                        continue;
                    }

                    if (byChannel == null)
                    {
                        byChannel = new double[child.ChannelCurves.Length][];
                        for (int c = 0; c < byChannel.Length; c++)
                        {
                            byChannel[c] = new double[channels];
                        }
                    }

                    for (int c = 0; c < child.ChannelCurves.Length && c < byChannel.Length; c++)
                    {
                        double[] row = child.ChannelCurves[c];
                        if (row == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < channels && i < row.Length; i++)
                        {
                            byChannel[c][i] += row[i];
                        }
                    }
                }

                Console.WriteLine("родитель {0}: дочерних {1}, лента {2} отсчётов",
                                  parent.Name, bag.Count,
                                  Sum(parent.Curve).ToString("F3", CultureInfo.InvariantCulture));
                Gap("  лента: родитель = Σ дочерних", parent.Curve, curve);
                if (sums != null)
                {
                    sumPeakTested++;
                    Gap("  сумм-пики: родитель = Σ дочерних", parent.SumPeakCurve, sums);
                }

                if (byChannel != null && parent.ChannelCurves != null)
                {
                    for (int c = 0; c < byChannel.Length && c < parent.ChannelCurves.Length; c++)
                    {
                        Gap("  канал «" + ChannelNames[c] + "»: родитель = Σ дочерних",
                            parent.ChannelCurves[c], byChannel[c]);
                    }
                }
            }

            // ⚠ СЦЕНА БЕЗ РЯДА — НЕ РАСХОЖДЕНИЕ, А ПРОПУСК. На `ASN16_Lu176` ряда
            // нет вовсе, и требовать там родительскую строку значило бы держать сторожа
            // красным там, где стеречь нечего, — а всегда красного сторожа
            // перестают читать. Сама `T176` проверяется на ториевой сцене,
            // и там пропуска не бывает.
            if (tested == 0)
            {
                Console.WriteLine("ряда с дочерними строками на этой сцене НЕТ — T176 здесь не проверяется (пропуск)");
                return;
            }

            // ⛔ Половина T176 была доказана НА НУЛЕ сумм-пиков — именно этого
            // и не хватало. Если сумм-пики построены, а в родителе их нет,
            // проверка снова меряет пустоту, и молчать об этом нельзя.
            if (builtSumPeaks > 0)
            {
                Same("T176: родитель с сумм-пиками проверен (а не на нуле)", true, sumPeakTested > 0);
            }
        }

        /// <summary>
        /// Каскадный `CF` включается ТОЛЬКО в канале пика: три остальных канала
        /// обязаны совпасть с плечом без каскада побитово.
        /// </summary>
        static void CompareChannels(FsaResult without, FsaResult with)
        {
            double withRate = with.LiveTime > 0.0 ? with.LiveTime : 1.0;
            var byName = new Dictionary<string, FsaComponentResult>(StringComparer.Ordinal);
            foreach (FsaComponentResult c in without.Components)
            {
                byName[c.Name] = c;
            }

            int compared = 0, peakMoved = 0;
            double worstOther = 0.0;
            string worstName = null;
            foreach (FsaComponentResult c in with.Components)
            {
                FsaComponentResult other;
                if (!byName.TryGetValue(c.Name, out other) || c.ChannelCurves == null
                    || other.ChannelCurves == null)
                {
                    continue;
                }

                // ⚠ Амплитуды у плеч СВОИ: фит другой, и лента целиком другая.
                // Поэтому сравнивается канал В ЕДИНИЦАХ ОБРАЗА — делённый на
                // амплитуду компонента. Доля канала в ленте для этого НЕ
                // ГОДИТСЯ: `CF` уменьшает пиковую строку, отчего доля комптона
                // растёт сама собой, и мерилась бы не «тронут ли канал», а
                // ровно то, что и должно было измениться (первая правка этой
                // пробы на том и споткнулась: 1.7E-2 на комптоне Lu-176).
                compared++;
                double peakGap = AmplitudeGap(c, other, withRate, (int)EfficiencySimulator.ResponseChannel.Peak);
                if (peakGap > 1.0E-9)
                {
                    peakMoved++;
                }

                for (int ch = 1; ch < c.ChannelCurves.Length; ch++)
                {
                    double gap = AmplitudeGap(c, other, withRate, ch);
                    if (gap > worstOther)
                    {
                        worstOther = gap;
                        worstName = c.Name + "/" + ChannelNames[ch];
                    }
                }
            }

            Console.WriteLine("сравнено компонентов {0}, канал пика тронут у {1}", compared, peakMoved);
            Console.WriteLine("худшее расхождение ОСТАЛЬНЫХ каналов: {0} ({1})",
                              worstOther.ToString("E3", CultureInfo.InvariantCulture), worstName ?? "-");
            Same("CF тронул канал полного поглощения", true, peakMoved > 0);

            // ⚠ ПОСЛЕ УШИРЕНИЯ СТРОГОГО НУЛЯ ЗДЕСЬ НЕ БУДЕТ, и это не протечка
            // физики: порог отсечки уширения — `top·1e-5` от МАКСИМУМА полной
            // гистограммы, а `CF` этот максимум меняет, отчего у самой границы
            // порога пара бинов входит в свёртку или выпадает из неё. Измерено
            // 10.09.2026 на `G1S16_Th232_Denta100`: после уширения 6.287E-4,
            // ДО уширения — строгий ноль (см. `CompareDeposits` ниже, оно и
            // есть критерий). Держать здесь критерием «ровно ноль» значило бы
            // объявить дефектом свойство общего порога — того самого, который
            // и делает каналы частями ОДНОЙ ленты.
            Same("комптон и вылеты не тронуты заметно (≤ 1e-3, общий порог отсечки)",
                 true, worstOther <= 1.0E-3);
        }

        /// <summary>
        /// Расхождение канала В ЕДИНИЦАХ ОБРАЗА: площадь канала, делённая на
        /// амплитуду компонента (она же `CountRate` × живое время), отнесённая
        /// к той же величине у второго плеча.
        /// </summary>
        /// <summary>
        /// Каналы ГИСТОГРАММ двух плеч — до всякого уширения и без
        /// всякой амплитуды. Здесь утверждение «`CF` правит только канал
        /// полного поглощения» проверяется в чистом виде.
        /// </summary>
        static void CompareDeposits(FsaAnalyzer without, FsaAnalyzer with)
        {
            Dictionary<string, double[][]> a = DepositChannels(without);
            Dictionary<string, double[][]> b = DepositChannels(with);
            int compared = 0, peakMoved = 0;
            double worstOther = 0.0, worstPeak = 0.0;
            string worstName = null;
            foreach (var pair in a)
            {
                double[][] second;
                if (!b.TryGetValue(pair.Key, out second))
                {
                    continue;
                }

                compared++;
                for (int c = 0; c < pair.Value.Length && c < second.Length; c++)
                {
                    double one = Sum(pair.Value[c]), two = Sum(second[c]);
                    double scale = Math.Max(Math.Abs(one), Math.Abs(two));
                    double gap = scale > 0.0 ? Math.Abs(one - two) / scale : 0.0;
                    if (c == (int)EfficiencySimulator.ResponseChannel.Peak)
                    {
                        if (gap > 0.0)
                        {
                            peakMoved++;
                        }

                        if (gap > worstPeak)
                        {
                            worstPeak = gap;
                        }

                        continue;
                    }

                    if (gap > worstOther)
                    {
                        worstOther = gap;
                        worstName = pair.Key + "/" + ChannelNames[c];
                    }
                }
            }

            Console.WriteLine("гистограммы: сверено {0}, канал пика тронут у {1} (худшее {2})",
                              compared, peakMoved, worstPeak.ToString("E3", CultureInfo.InvariantCulture));
            Console.WriteLine("гистограммы: худшее расхождение ОСТАЛЬНЫХ каналов: {0} ({1})",
                              worstOther.ToString("E3", CultureInfo.InvariantCulture), worstName ?? "-");
            Same("до уширения: сверено больше нуля гистограмм", true, compared > 0);
            Same("до уширения: `CF` тронул канал полного поглощения", true, peakMoved > 0);
            Same("до уширения: комптон и вылеты не тронуты ВОВСЕ", 0.0, worstOther);
        }

        static Dictionary<string, double[][]> DepositChannels(FsaAnalyzer analyzer)
        {
            var found = new Dictionary<string, double[][]>(StringComparer.Ordinal);
            FieldInfo field = typeof(FsaAnalyzer).GetField(
                "deposits", BindingFlags.NonPublic | BindingFlags.Instance);
            var cache = field != null ? field.GetValue(analyzer) as System.Collections.IDictionary : null;
            if (cache == null)
            {
                return found;
            }

            foreach (System.Collections.DictionaryEntry entry in cache)
            {
                var component = entry.Key as FsaComponent;
                double[][] channels = entry.Value != null
                    ? FieldOf<double[][]>(entry.Value, "Channels") : null;
                if (component != null && channels != null)
                {
                    found[component.Name] = channels;
                }
            }

            return found;
        }

        static double AmplitudeGap(FsaComponentResult a, FsaComponentResult b, double liveTime, int channel)
        {
            double ampA = a.CountRate * liveTime, ampB = b.CountRate * liveTime;
            if (!(ampA > 0.0) || !(ampB > 0.0))
            {
                return 0.0;
            }

            double perA = Sum(a.ChannelCurves[channel]) / ampA;
            double perB = Sum(b.ChannelCurves[channel]) / ampB;
            double scale = Math.Max(Math.Abs(perA), Math.Abs(perB));
            return scale > 0.0 ? Math.Abs(perA - perB) / scale : 0.0;
        }

        /// <summary>
        /// (`S167`) Центр тяжести полосы порознь у измерения и у модели, и
        /// разложение модельной полосы по частям.
        /// </summary>
        static void Band(ResultData rd, FsaResult result, double lo, double hi)
        {
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int[] raw = rd.EnergySpectrum.Spectrum;
            int channels = result.Model.Length;

            double measuredArea = 0.0, measuredMoment = 0.0;
            double modelArea = 0.0, modelMoment = 0.0;
            double netArea = 0.0, netMoment = 0.0;
            double[] channelArea = new double[EfficiencySimulator.ResponseChannelCount];
            double[] channelMoment = new double[EfficiencySimulator.ResponseChannelCount];
            double sumPeakArea = 0.0, sumPeakMoment = 0.0;
            double continuumArea = 0.0, continuumMoment = 0.0;

            for (int i = 0; i < channels; i++)
            {
                double energy = calibration.ChannelToEnergy(i);
                if (energy < lo || energy > hi)
                {
                    continue;
                }

                double measured = i < raw.Length ? raw[i] : 0.0;
                double background = result.Background != null && i < result.Background.Length
                    ? result.Background[i] : 0.0;
                measuredArea += measured;
                measuredMoment += measured * energy;
                double net = measured - background;
                netArea += net;
                netMoment += net * energy;
                modelArea += result.Model[i];
                modelMoment += result.Model[i] * energy;
                continuumArea += result.Continuum[i];
                continuumMoment += result.Continuum[i] * energy;

                foreach (FsaComponentResult c in result.Components)
                {
                    if (c.ChannelCurves != null)
                    {
                        for (int ch = 0; ch < c.ChannelCurves.Length; ch++)
                        {
                            double[] row = c.ChannelCurves[ch];
                            if (row != null && i < row.Length)
                            {
                                channelArea[ch] += row[i];
                                channelMoment[ch] += row[i] * energy;
                            }
                        }
                    }

                    if (c.SumPeakCurve != null && i < c.SumPeakCurve.Length)
                    {
                        sumPeakArea += c.SumPeakCurve[i];
                        sumPeakMoment += c.SumPeakCurve[i] * energy;
                    }
                }
            }

            // ⛔ ЦЕНТР ТЯЖЕСТИ И МАКСИМУМ — РАЗНЫЕ ВЕЛИЧИНЫ, и наблюдение
            // Amber 09.09.2026 (`S167`) было про МАКСИМУМ: «широкий максимум
            // спектра при 519.48». Мерить в ответ один центр тяжести значило бы
            // отвечать не на тот вопрос. Максимум измерения берётся по
            // сглаженному счёту (скользящее среднее по ПШПВ/2), иначе на
            // 100 тысячах отсчётов он гуляет по шуму.
            //
            // ⚠ ПОЛОЖЕНИЕ МАКСИМУМА ЗАВИСИТ ОТ ШИРИНЫ СГЛАЖИВАНИЯ, и это не придирка:
            // у полосы 470…560 своей вершины почти нет — она плечо, и чем шире окно,
            // тем сильнее «максимум» съезжает к центру массы. Поэтому меряется тремя
            // ширинами сразу, и цитировать одно число без ширины нельзя.
            foreach (double part in new[] { 0.0, 0.25, 0.5 })
            {
                Peak("измерение", raw, calibration, result.Background, channels, lo, hi, rd, part);
                Peak("модель", result.Model, calibration, null, channels, lo, hi, rd, part);
            }

            Centre("измерение (как есть)", measuredArea, measuredMoment);
            Centre("измерение за вычетом фона", netArea, netMoment);
            Centre("модель целиком", modelArea, modelMoment);
            Centre("  подложка сплайна", continuumArea, continuumMoment);
            for (int ch = 0; ch < channelArea.Length; ch++)
            {
                Centre("  канал «" + ChannelNames[ch] + "»", channelArea[ch], channelMoment[ch]);
            }

            // ⚠ Сумм-пик СИДИТ ВНУТРИ канала полного поглощения, а сумм-континуум
            // — внутри остальных: это части ленты, а не пятый канал. Поэтому их
            // числа печатаются отдельной строкой и в сумму каналов НЕ
            // добавляются — иначе то же самое посчиталось бы дважды.
            Centre("  из них сумм-пики (внутри канала пика)", sumPeakArea, sumPeakMoment);
        }

        /// <summary>
        /// Положение максимума в полосе. Кривая сглаживается скользящим средним
        /// шириной в половину ПШПВ на середине полосы: у измерения шум иначе
        /// назначает максимумом случайный канал, а сравнивать надо ту же
        /// величину, что видит глаз на экране.
        /// </summary>
        static void Peak(string title, System.Collections.IList curve, EnergyCalibration calibration,
                         double[] background, int channels, double lo, double hi, ResultData rd,
                         double fwhmPart)
        {
            int width = 0;
            if (fwhmPart > 0.0 && rd.FwhmCalibration != null)
            {
                double middle = EnergyToChannel(calibration, 0.5 * (lo + hi), channels);
                double fwhm = middle >= 0.0 ? rd.FwhmCalibration.ChannelToFwhm(middle) : 0.0;
                if (fwhm > 2.0)
                {
                    width = (int)(fwhm * fwhmPart);
                }
            }

            double best = double.NegativeInfinity, bestEnergy = double.NaN;
            for (int i = 0; i < channels; i++)
            {
                double energy = calibration.ChannelToEnergy(i);
                if (energy < lo || energy > hi)
                {
                    continue;
                }

                double sum = 0.0;
                int taken = 0;
                for (int k = i - width; k <= i + width; k++)
                {
                    if (k < 0 || k >= channels || k >= curve.Count)
                    {
                        continue;
                    }

                    double v = Convert.ToDouble(curve[k], CultureInfo.InvariantCulture);
                    if (background != null && k < background.Length)
                    {
                        v -= background[k];
                    }

                    sum += v;
                    taken++;
                }

                if (taken == 0)
                {
                    continue;
                }

                double mean = sum / taken;
                if (mean > best)
                {
                    best = mean;
                    bestEnergy = energy;
                }
            }

            Console.WriteLine("BAND\tМАКСИМУМ {0}\tсглажено ±{1} кан. ({2} ПШПВ)\tположение {3} кэВ",
                              title, width,
                              fwhmPart.ToString("F2", CultureInfo.InvariantCulture),
                              double.IsNaN(bestEnergy)
                                  ? "-" : bestEnergy.ToString("F2", CultureInfo.InvariantCulture));
        }

        static double EnergyToChannel(EnergyCalibration calibration, double energyKev, int channels)
        {
            for (int i = 0; i < channels - 1; i++)
            {
                if (calibration.ChannelToEnergy(i) <= energyKev && calibration.ChannelToEnergy(i + 1) > energyKev)
                {
                    return i;
                }
            }

            return -1.0;
        }

        static void Centre(string title, double area, double moment)
        {
            Console.WriteLine("BAND\t{0}\tплощадь {1}\tцентр тяжести {2} кэВ",
                              title,
                              area.ToString("F1", CultureInfo.InvariantCulture),
                              area > 0.0
                                  ? (moment / area).ToString("F2", CultureInfo.InvariantCulture)
                                  : "-");
        }

        /// <summary>Числа, по которым сверяют прогон со сборкой прежнего кода.</summary>
        static void Control(FsaResult result, string dumpPath)
        {
            Console.WriteLine("CONTROL\tchi2/ndf\t{0}", result.Chi2Ndf.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("CONTROL\tневязка\t{0}", result.ModelResidual.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("CONTROL\tмодель\t{0}", Sum(result.Model).ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("CONTROL\tподложка\t{0}", Sum(result.Continuum).ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("CONTROL\tсостав\t{0}", result.Components.Count);

            var lines = new List<string>();
            lines.Add("component;kind;chainRoot;decayRoot;curve;rate;z;share");
            foreach (FsaComponentResult c in result.Components)
            {
                string row = string.Format(CultureInfo.InvariantCulture, "{0};{5};{6};{7};{1};{2};{3};{4}",
                                           c.Name,
                                           Sum(c.Curve).ToString("R", CultureInfo.InvariantCulture),
                                           c.CountRate.ToString("R", CultureInfo.InvariantCulture),
                                           c.Z.ToString("R", CultureInfo.InvariantCulture),
                                           c.SharePercent.ToString("R", CultureInfo.InvariantCulture),
                                           c.Kind, c.ChainRoot ?? "-", c.DecayChainRoot ?? "-");
                lines.Add(row);
                Console.WriteLine("CONTROL\t{0}", row);
            }

            if (dumpPath != null)
            {
                File.WriteAllLines(dumpPath, lines, new UTF8Encoding(true));
                Console.WriteLine("записано: {0}", dumpPath);
            }
        }

        // ==================================================================
        // Мелочь
        // ==================================================================

        static double Own(double[] curve, double[] spread, int i)
        {
            double own = i < curve.Length ? curve[i] : 0.0;
            if (spread != null && i < spread.Length)
            {
                own -= spread[i];
            }

            return own;
        }

        static double MaxRelativeGap(double[] curve, double[][] channels, double[] spread)
        {
            double scale = Math.Max(1.0E-12, Max(curve));
            double worst = 0.0;
            for (int i = 0; i < curve.Length; i++)
            {
                double sum = 0.0;
                for (int c = 0; c < channels.Length; c++)
                {
                    double[] row = channels[c];
                    if (row != null && i < row.Length)
                    {
                        sum += row[i];
                    }
                }

                double gap = Math.Abs(sum - Own(curve, spread, i)) / scale;
                if (gap > worst)
                {
                    worst = gap;
                }
            }

            return worst;
        }

        static void Gap(string title, double[] left, double[] right)
        {
            if (left == null || right == null)
            {
                Console.Error.WriteLine("⛔ {0}: одной из сторон НЕТ ({1} / {2})",
                                        title, left == null ? "null" : "есть", right == null ? "null" : "есть");
                bad++;
                return;
            }

            double scale = Math.Max(1.0E-12, Max(left));
            double worst = 0.0;
            for (int i = 0; i < left.Length && i < right.Length; i++)
            {
                double gap = Math.Abs(left[i] - right[i]) / scale;
                if (gap > worst)
                {
                    worst = gap;
                }
            }

            Console.WriteLine("{0}: худшее относительное {1}",
                              title, worst.ToString("E3", CultureInfo.InvariantCulture));
            if (worst > Tol)
            {
                Console.Error.WriteLine("⛔ {0}: РАСХОЖДЕНИЕ", title);
                bad++;
            }
        }

        static void Same(string title, object expected, object actual)
        {
            bool ok = Equals(expected, actual);
            Console.WriteLine("{0}: {1}", title, ok ? "да" : "НЕТ (" + actual + " вместо " + expected + ")");
            if (!ok)
            {
                bad++;
            }
        }

        static T FieldOf<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(target) as T : null;
        }

        static double Sum(double[] curve)
        {
            if (curve == null)
            {
                return 0.0;
            }

            double s = 0.0;
            foreach (double v in curve)
            {
                s += v;
            }

            return s;
        }

        static double Max(double[] curve)
        {
            double m = 0.0;
            if (curve == null)
            {
                return m;
            }

            foreach (double v in curve)
            {
                if (v > m)
                {
                    m = v;
                }
            }

            return m;
        }

        static int CountSumPeaks(ResponseMatrix matrix, string material, List<FsaComponent> library)
        {
            FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, material);
            if (summer == null)
            {
                return 0;
            }

            int n = 0;
            foreach (FsaComponent component in library)
            {
                FsaCascadeSummer.Correction correction = summer.For(component);
                if (correction != null && correction.SumPeaks != null)
                {
                    n += correction.SumPeaks.Count;
                }
            }

            return n;
        }

        static FsaResult Run(ResultData rd, List<FsaComponent> library, ResponseMatrix matrix,
                             string material, bool cascadeSumming, bool cascadeSumPeaks,
                             double floorKev, string title, out FsaAnalyzer analyzer)
        {
            analyzer = new FsaAnalyzer
            {
                ResponseMatrix = matrix,
                ScintillatorMaterial = material
            };

            // ⛔ НАСТРОЙКИ — ТЕ ЖЕ, ЧТО У ПРИЛОЖЕНИЯ, и накладываются ПЕРВЫМИ.
            // Без этого связка равновесия остаётся в состоянии конструктора,
            // ряд не собирается в родительскую строку, и `T176` проверять
            // становится нечего — ровно это и вышло на первом прогоне
            // `G1S16_Th232_Denta100`: 4 компонента и НИ ОДНОЙ родительской
            // строки. Каскадные ключи плеча ставятся ПОСЛЕ — они и есть предмет
            // опыта.
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            options.ApplyTo(analyzer);
            analyzer.CascadeSumming = cascadeSumming;
            analyzer.CascadeSumPeaks = cascadeSumPeaks;

            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            if (floorKev >= 0.0)
            {
                analyzer.ResponseContinuumTrustFloorKev = floorKev;
            }

            // (`T243`) ЧЕМ СЧИТАЛИ ЭТО ПЛЕЧО — ДО СЧЁТА И ВСЛУХ. Анализатор у
            // каждого плеча СВОЙ, и состояние прошлого разбора в отчёт не
            // попадает. Здесь это важно вдвойне: плечи отличаются
            // ИМЕННО настройками каскада и порогом доверия.
            FsaTuningReport.Print(analyzer, title);

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось: {0}", title);
                bad++;
                return null;
            }

            Console.WriteLine("плечо «{0}»: chi2/ndf {1}, невязка {2} %, каскад применён: {3}",
                              title,
                              result.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture),
                              (result.ModelResidual * 100.0).ToString("F2", CultureInfo.InvariantCulture),
                              result.CascadeSummingUsed ? "да" : "нет");
            return result;
        }

        static FsaSampleSpec SpecOf(ResultData rd, List<string> chains, List<string> nuclides)
        {
            var spec = new FsaSampleSpec
            {
                Efficiency = FsaEfficiency.FromConfig(rd.Efficiency),
                AdcFloorKev = FsaBand.AdcFloorOf(rd.EnergySpectrum)
            };
            foreach (string label in chains)
            {
                spec.Chains.Add(new FsaSampleChain(NucidOf(label)));
            }

            foreach (string nucid in nuclides)
            {
                spec.Nuclides.Add(NucidOf(nucid));
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig
                && peakConfig.Max_Range > peakConfig.Min_Range)
            {
                spec.MinEnergyKev = peakConfig.Min_Range;
                spec.MaxEnergyKev = peakConfig.Max_Range;
            }

            GeometryModel geometry = rd.Efficiency != null && rd.Efficiency.HasGeometry
                ? rd.Efficiency.Geometry : null;
            if (geometry != null)
            {
                FsaSampleLibrary.DescribeCrystal(spec, geometry.Crystal, 0.01,
                    EfficiencySimulator.ScintillatorNameOf(geometry));
            }

            return spec;
        }

        /// <summary>«Th-232» → «232TH»: nucid, как его зовёт nucdb.</summary>
        static string NucidOf(string label)
        {
            int dash = label.IndexOf('-');
            if (dash < 0)
            {
                return label.ToUpperInvariant();
            }

            return label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant();
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
