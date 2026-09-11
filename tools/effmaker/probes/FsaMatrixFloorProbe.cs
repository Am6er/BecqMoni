using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace FsaMatrixFloorProbe
{
    /// <summary>
    /// ⛔ НОЖ НИЖЕ ПЕРВОГО УЗЛА МАТРИЦЫ (`A49`) — СЦЕНА, КОТОРОЙ У КОРПУСА НЕТ
    /// (`A51`).
    ///
    /// Решением Amber 02.09.2026 линия, лежащая ниже первого узла матрицы
    /// отклика, в образ не идёт: отклика на неё у матрицы нет, а
    /// <c>ResponseMatrix.Accumulate</c> за нижним краем сетки РАСТЯГИВАЕТ
    /// строку первого узла, то есть отдаёт линии чужую эффективность. Правка
    /// подтверждена ОДНОЙ сценой — `Th232(WT-20)` с матрицей от 20 кэВ.
    ///
    /// ⛔ ЗАЧЕМ ЭТА ПРОБА (`A51`). Корпус нож НЕ ПРОВЕРЯЕТ и проверить не
    /// может: у всех 44 его матриц первый узел один и тот же и лежит ниже любой
    /// библиотечной линии, поэтому на 121 спектре нож не срабатывал НИ РАЗУ.
    /// Ветка кода есть, а доказательства, что она нужна и верна, нет. Сцена
    /// здесь искусственная и своя: матрица с поднятым нижним краем сетки,
    /// библиотека с двумя линиями под ним и спектр с честным порогом АЦП.
    ///
    /// Четыре раздела:
    ///
    ///   1. НОЖ СРАБАТЫВАЕТ И НАЗВАН: счётчик
    ///      <see cref="FsaAnalyzer.MatrixFloorDroppedLines"/> и заверение
    ///      <see cref="FsaAnalyzer.BandNote"/>.
    ///   2. ЦЕНА ВЫКЛЮЧЕННОГО НОЖА — то самое, ради чего он поставлен: с
    ///      подпороговой линией связанный образ обязан положить часть площади
    ///      НИЖЕ ПОРОГА АЦП, где у спектра ровно ноль, и взвешенный НМНК
    ///      выбирает нуль для всей колонки. Ряд пропадает целиком.
    ///   3. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ — ТА ЖЕ библиотека на матрице с КОРПУСНЫМ
    ///      нижним узлом: нож молчит, счётчик ноль, и оба плеча рычага дают
    ///      разбор БИТ В БИТ один и тот же. Это и есть числовое подтверждение
    ///      посылки `A51` — корпусной сеткой правка не меряется вовсе.
    ///   4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ РЫЧАГА: он обязан менять ТОЛЬКО этот нож.
    ///      На сцене без подпороговых линий плечи снова совпадают бит в бит.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        /// <summary>Шкала: 1 кэВ на канал, 2048 каналов.</summary>
        const int Channels = 2048;

        /// <summary>
        /// Порог АЦП сцены, кэВ. Ниже него у спектра РОВНО НОЛЬ — и это не
        /// упрощение, а существо разбора `A49`: связанный образ обязан класть
        /// туда часть своей площади, пока в нём жива подпороговая линия.
        /// </summary>
        const int AdcFloor = 25;

        /// <summary>Заданная площадь ряда, отсчётов.</summary>
        const double ChainArea = 200000.0;

        /// <summary>Нижний узел «поднятой» матрицы, кэВ — как у `Th232(WT-20)`.</summary>
        const double RaisedFloorKev = 20.0;

        /// <summary>
        /// Нижний узел КОРПУСНОЙ матрицы, кэВ. Число взято из посылки `A51`:
        /// «у всех 44 корпусных матриц первый узел 5.00 кэВ».
        /// </summary>
        const double CorpusFloorKev = 5.0;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ Культура ЦЕЛИКОМ инвариантная (`T245`).
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            foreach (string a in args)
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }

            Console.WriteLine("=== НОЖ НИЖЕ ПЕРВОГО УЗЛА МАТРИЦЫ (`A49`/`A51`) ===");
            Console.WriteLine("  сцена: матрица от {0:F1} кэВ, порог АЦП {1} кэВ,"
                              + " в ряду две линии под нижним узлом (15.78 и 16.20 кэВ)",
                              RaisedFloorKev, AdcFloor);

            Sections();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void Sections()
        {
            ResponseMatrix raised = Matrix(RaisedFloorKev);
            ResponseMatrix corpus = Matrix(CorpusFloorKev);

            // Спектр строится ПО МАТРИЦЕ и только по тем линиям, отклик на
            // которые у неё есть. Подпороговые линии не дают в спектр ничего —
            // ни у прибора (они ниже порога АЦП), ни у модели (ниже сетки).
            int[] spectrum = Spectrum(raised);

            Console.WriteLine();
            Console.WriteLine("=== 1. НОЖ СРАБАТЫВАЕТ И НАЗВАН ===");
            Arm on = Run(spectrum, raised, true, "поднятая матрица, нож включён");
            Same("нож снял обе подпороговые линии", 2, on.Dropped);
            Same("заверение полосы называет нижний узел", true,
                 on.Note.IndexOf("ниже первого узла матрицы", StringComparison.Ordinal) >= 0);
            Same("ряд дожил до разбора", true, on.ChainArea > 0.0);
            Console.WriteLine("  нож включён: ряд {0:F0} отсчётов ({1:F1} % заданного), z {2:F2}, χ²/ndf {3:F3}",
                              on.ChainArea, 100.0 * on.ChainArea / ChainArea, on.ChainZ, on.Chi2Ndf);
            Console.WriteLine("  ⚠ до сотни доля не дотягивает и не обязана: часть комптоновского"
                              + " континуума образа законно берёт сплайн — здесь важно НЕ РАВЕНСТВО"
                              + " заданному, а сравнение плеч между собой");
            Console.WriteLine("  заверение: {0}", on.Note);

            Console.WriteLine();
            Console.WriteLine("=== 2. ЦЕНА ВЫКЛЮЧЕННОГО НОЖА ===");
            Arm off = Run(spectrum, raised, false, "поднятая матрица, нож ВЫКЛЮЧЕН");
            Same("с выключенным ножом не снято ни одной линии", 0, off.Dropped);
            Console.WriteLine("  нож выключён: ряд {0:F0} отсчётов ({1:F1} % заданного), z {2:F2}, χ²/ndf {3:F3}",
                              off.ChainArea, 100.0 * off.ChainArea / ChainArea, off.ChainZ, off.Chi2Ndf);

            // ⛔ ВОТ ЦЕНА, И ОНА НАЗВАНА ЧИСЛОМ. Ряд, объясняющий спектр
            // целиком, при выключенном ноже получает РОВНО НОЛЬ: пиковое окно
            // подпороговой линии накрывает область ниже порога АЦП, где у
            // спектра нуль, и связанный образ обязан положить туда часть
            // площади при любой ненулевой амплитуде.
            Same("без ножа ряд получает РОВНО НОЛЬ", 0.0, Math.Round(off.ChainArea));
            Same("и невязка от этого хуже", true, off.Chi2Ndf > on.Chi2Ndf);
            Console.WriteLine("  цена ножа: χ²/ndf {0:F3} против {1:F3} — в {2:F1} раза",
                              off.Chi2Ndf, on.Chi2Ndf,
                              on.Chi2Ndf > 0.0 ? off.Chi2Ndf / on.Chi2Ndf : double.NaN);

            Console.WriteLine();
            Console.WriteLine("=== 3. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: КОРПУСНАЯ СЕТКА ({0:F2} кэВ) ===",
                              CorpusFloorKev);
            int[] corpusSpectrum = Spectrum(corpus);
            Arm cOn = Run(corpusSpectrum, corpus, true, "корпусная матрица, нож включён");
            Arm cOff = Run(corpusSpectrum, corpus, false, "корпусная матрица, нож ВЫКЛЮЧЕН");
            Same("на корпусной сетке нож не срабатывает вовсе", 0, cOn.Dropped);
            Same("и заверение о нижнем узле не печатается", false,
                 cOn.Note.IndexOf("ниже первого узла матрицы", StringComparison.Ordinal) >= 0);

            // ⛔ ЭТО И ЕСТЬ ПОСЫЛКА `A51`, ПРОВЕРЕННАЯ СЧЁТОМ: на корпусной
            // сетке рычаг не меняет НИ ОДНОГО БИТА, то есть корпусом правка
            // `A49` не меряется — ни в плюс, ни в минус.
            Same("оба плеча рычага дают один и тот же разбор", true, Bitwise(cOn, cOff));
            Console.WriteLine("  корпусная сетка: ряд {0:F0} ({1:F1} % истины), строк состава {2},"
                              + " χ²/ndf {3:F3}; плечи рычага совпали бит в бит",
                              cOn.ChainArea, 100.0 * cOn.ChainArea / ChainArea, cOn.Parts, cOn.Chi2Ndf);

            // ⚠ И ЗАОДНО ИЗМЕРЕНА ГРАНИЦА ПРАВКИ `A49`, о которой строка не
            // говорила: ряд умирает и на корпусной сетке — там линии 15.78 и
            // 16.20 кэВ ВЫШЕ нижнего узла, ножу их не видно, а пиковое окно
            // по-прежнему достаёт под порог АЦП. Значит губит не «линия ниже
            // узла матрицы», а «линия, чьё окно достаёт туда, где у спектра
            // ноль», и нижний узел матрицы ловит лишь часть таких случаев.
            // В корпусе этого не видно по другой причине: библиотеку там
            // строит `FsaSampleLibrary` и режет полом полосы (`S98`), то есть
            // такие линии до анализатора не доходят вовсе.
            Same("на корпусной сетке ряд гибнет ПО ДРУГОЙ причине (не по ножу)",
                 0.0, Math.Round(cOn.ChainArea));

            Console.WriteLine();
            Console.WriteLine("=== 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ РЫЧАГА: БЕЗ ПОДПОРОГОВЫХ ЛИНИЙ ===");
            Arm hOn = Run(spectrum, raised, true, "поднятая матрица, библиотека без низа", true);
            Arm hOff = Run(spectrum, raised, false, null, true);
            Same("резать нечего — нож молчит", 0, hOn.Dropped);
            Same("и рычаг не меняет ничего", true, Bitwise(hOn, hOff));
            Same("ряд при этом жив", true, hOn.ChainArea > 0.0);
            Console.WriteLine("  та же поднятая матрица, но линий под узлом нет:"
                              + " ряд {0:F0} ({1:F1} % истины), χ²/ndf {2:F3} — плечи совпали",
                              hOn.ChainArea, 100.0 * hOn.ChainArea / ChainArea, hOn.Chi2Ndf);

            // ⛔ И ГЛАВНОЕ ЧИСЛО ПРОБЫ: разбор с включённым ножом обязан
            // СОВПАСТЬ с разбором библиотеки, из которой подпороговые линии
            // убраны рукой. Нож делает ровно это и ничего сверх — иначе
            // «правка помогает» значило бы «правка что-то ещё меняет».
            // ⚠ Сравниваются ЧИСЛА, а не заверение: у плеча с ножом в
            // `BandNote` стоит лишняя строка о выброшенных линиях — она и
            // обязана там стоять.
            Same("нож = ручное снятие тех же линий, числа сошлись", true, Numbers(on, hOn));
        }

        // ------------------------------------------------------------------
        // Один разбор
        // ------------------------------------------------------------------

        sealed class Arm
        {
            public double ChainArea;
            public double ChainZ;
            public double Chi2Ndf;
            public int Dropped;
            public int Parts;
            public string Note = "";
        }

        static Arm Run(int[] counts, ResponseMatrix matrix, bool cut, string report,
                       bool highOnly = false)
        {
            var spectrum = new EnergySpectrum(1.0, Channels)
            {
                EnergyCalibration = new PolynomialEnergyCalibration
                {
                    Coefficients = new[] { 0.0, 1.0 }
                },
                LiveTime = 1000.0
            };
            Array.Copy(counts, spectrum.Spectrum, Channels);

            var analyzer = new FsaAnalyzer
            {
                MinEnergy = AdcFloor,
                MaxEnergy = 1800.0,
                CascadeSumming = false,
                CascadeSumPeaks = false,
                Backscatter = false,
                PileUp = false,
                // Гейт геометрии (`A277`) снят: сцена искусственная, геометрии
                // у неё нет вовсе, а вопрос пробы — нож полосы.
                RequireGeometry = false,
                ResponseMatrix = matrix,
                MatrixFloorCut = cut
            };

            if (report != null)
            {
                FsaTuningReport.Print(analyzer, report);
            }

            FsaResult result = analyzer.Analyze(spectrum, null, Fwhm(),
                                                new List<FsaComponent> { Chain(highOnly) }, null);

            var arm = new Arm
            {
                Dropped = analyzer.MatrixFloorDroppedLines,
                Note = analyzer.BandNote ?? ""
            };

            if (result != null)
            {
                arm.Chi2Ndf = result.Chi2Ndf;
                if (report != null && result.Components != null)
                {
                    // Состав печатается целиком: «ряд получил ноль» и «ряд
                    // зовётся иначе, чем я ищу» иначе неразличимы.
                    var names = new List<string>();
                    foreach (FsaComponentResult c in result.Components)
                    {
                        names.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0} {1:F1} %", c.Name, c.SharePercent));
                    }

                    Console.WriteLine("  состав [{0}]: {1}", report,
                                      names.Count == 0 ? "(пусто)" : string.Join(", ", names.ToArray()));
                }

                if (result.Components != null)
                {
                    // ⛔ СУММА ПО ВСЕМ СТРОКАМ СОСТАВА, а не поиск по имени
                    // колонки. Колонка ряда одна, но в состав она выходит
                    // ЧЛЕНАМИ: `AsDefinitions` и `FsaResult` подписывают строку
                    // тем, кто линию излучил (`S70`). Поиск по имени колонки
                    // «Ряд-испытание» не находил НИЧЕГО и врал «ряд получил
                    // ноль» там, где ряд брал 95 % спектра.
                    arm.Parts = result.Components.Count;
                    foreach (FsaComponentResult c in result.Components)
                    {
                        if (c == null || c.Curve == null)
                        {
                            continue;
                        }

                        foreach (double v in c.Curve)
                        {
                            arm.ChainArea += v;
                        }

                        if (c.Z > arm.ChainZ)
                        {
                            arm.ChainZ = c.Z;
                        }
                    }
                }
            }

            return arm;
        }

        /// <summary>
        /// Совпадение двух плеч ПО ЧИСЛАМ разбора. Округления нет нарочно:
        /// «бит в бит» здесь значит буквально равенство double, и любое, самое
        /// малое движение рычага будет названо.
        /// </summary>
        static bool Bitwise(Arm a, Arm b)
        {
            return Numbers(a, b) && a.Dropped == b.Dropped
                   && string.Equals(a.Note, b.Note, StringComparison.Ordinal);
        }

        /// <summary>Только числа разбора, без заверения и счётчика ножа.</summary>
        static bool Numbers(Arm a, Arm b)
        {
            return a.ChainArea == b.ChainArea && a.ChainZ == b.ChainZ
                   && a.Chi2Ndf == b.Chi2Ndf && a.Parts == b.Parts;
        }

        // ------------------------------------------------------------------
        // Сцена
        // ------------------------------------------------------------------

        const string ChainName = "Ряд-испытание";

        /// <summary>
        /// Ряд с пятью линиями. Две нижние взяты из разбора `A49` дословно:
        /// 15.784 кэВ с выходом 33.3 % (снятие её не меняло НИЧЕГО) и 16.2 кэВ
        /// с выходом 0.72 % (снятие её переворачивало разбор) — то есть режет
        /// не интенсивность, а положение.
        /// </summary>
        static FsaComponent Chain(bool highOnly)
        {
            var component = new FsaComponent(ChainName, FsaComponentKind.Chain);
            if (!highOnly)
            {
                component.Lines.Add(new FsaLine("Испыт-1", 15.784, 33.3));
                component.Lines.Add(new FsaLine("Испыт-1", 16.200, 0.72));
            }

            component.Lines.Add(new FsaLine("Испыт-2", 238.63, 43.6));
            component.Lines.Add(new FsaLine("Испыт-3", 583.19, 30.4));
            component.Lines.Add(new FsaLine("Испыт-4", 911.20, 25.8));
            component.Lines.Add(new FsaLine("Испыт-5", 1460.82, 10.6));
            return component;
        }

        /// <summary>
        /// Матрица отклика: сетка от <paramref name="floorKev"/> до 2000 кэВ,
        /// в каждом узле фотопик плюс плоский комптоновский континуум до своего
        /// края. Уширения в строках НЕТ — его накладывает сам анализатор по
        /// ПШПВ-калибровке, как и у настоящих матриц склада.
        /// </summary>
        static ResponseMatrix Matrix(double floorKev)
        {
            var energies = new List<double>();
            for (double e = floorKev; e < 2000.0; e *= 1.15)
            {
                energies.Add(Math.Round(e, 3));
            }

            energies.Add(2000.0);

            var rows = new float[energies.Count][];
            for (int i = 0; i < energies.Count; i++)
            {
                double energy = energies[i];
                float[] row = new float[Channels];

                // Полное поглощение — доля 0.4; остальное в континуум до
                // комптоновского края. Числа условны и одинаковы во всех
                // плечах: проба сравнивает плечи между собой, а не с опытом.
                int peak = (int)Math.Round(energy);
                if (peak >= 0 && peak < Channels)
                {
                    row[peak] += 0.4f;
                }

                double edge = energy * (1.0 - 1.0 / (1.0 + 2.0 * energy / 511.0));
                int edgeBin = (int)Math.Round(edge);
                if (edgeBin > 1)
                {
                    float each = (float)(0.6 / edgeBin);
                    for (int b = 1; b <= edgeBin && b < Channels; b++)
                    {
                        row[b] += each;
                    }
                }

                rows[i] = row;
            }

            return new ResponseMatrix
            {
                Energies = energies.ToArray(),
                BinKev = 1.0,
                Rows = rows
            };
        }

        /// <summary>
        /// Искусственный спектр: отклик матрицы на линии ряда ВЫШЕ её нижнего
        /// узла, уширенный ПШПВ, плюс небольшой ровный фон. Ниже порога АЦП —
        /// ровно ноль.
        /// </summary>
        static int[] Spectrum(ResponseMatrix matrix)
        {
            FwhmCalibration fwhm = Fwhm();
            double floor = matrix.Energies[0];
            double[] deposit = new double[Channels];
            double weight = 0.0;

            foreach (FsaLine line in Chain(false).Lines)
            {
                if (line.Energy < floor || line.Energy > 1800.0)
                {
                    continue;
                }

                double[] response = matrix.Evaluate(line.Energy, Channels);
                if (response == null)
                {
                    continue;
                }

                weight += line.Intensity;
                for (int i = 0; i < Channels && i < response.Length; i++)
                {
                    deposit[i] += line.Intensity * response[i];
                }
            }

            double[] broadened = Broaden(deposit, fwhm);
            double sum = 0.0;
            foreach (double v in broadened)
            {
                sum += v;
            }

            int[] counts = new int[Channels];
            for (int i = AdcFloor; i < Channels; i++)
            {
                double value = sum > 0.0 ? ChainArea * broadened[i] / sum : 0.0;
                counts[i] = (int)Math.Round(value + 30.0);
            }

            return counts;
        }

        /// <summary>
        /// Уширение спектра поглощённой энергии ПШПВ-калибровкой — тем же
        /// <see cref="PeakShapeModel"/>, каким анализатор строит столбцы.
        /// </summary>
        static double[] Broaden(double[] deposit, FwhmCalibration fwhm)
        {
            double[] value = new double[Channels];
            for (int b = 0; b < Channels; b++)
            {
                if (!(deposit[b] > 0.0))
                {
                    continue;
                }

                double width = fwhm.ChannelToFwhm(b);
                if (!(width > 0.0))
                {
                    value[b] += deposit[b];
                    continue;
                }

                int span = (int)Math.Ceiling(2.5 * width);
                double norm = 0.0;
                for (int i = -span; i <= span; i++)
                {
                    norm += PeakShapeModel.RelativeValue(i, width, fwhm);
                }

                if (!(norm > 0.0))
                {
                    value[b] += deposit[b];
                    continue;
                }

                for (int i = -span; i <= span; i++)
                {
                    int at = b + i;
                    if (at < 0 || at >= Channels)
                    {
                        continue;
                    }

                    value[at] += deposit[b] * PeakShapeModel.RelativeValue(i, width, fwhm) / norm;
                }
            }

            return value;
        }

        /// <summary>ПШПВ² = 2.7·ch: 6.6 кэВ на 16.2 и 42 кэВ на 662.</summary>
        static FwhmCalibration Fwhm()
        {
            return new SimpleSqrtFwhmCalibration
            {
                Coefficients = new[] { 0.0, 2.7 }
            };
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-58} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(CultureInfo.InvariantCulture, " вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
