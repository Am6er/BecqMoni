using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace DoseBinCentreProbe
{
    /// <summary>
    /// `AMBER71`: по какой энергии доза судит БИН отклика.
    ///
    /// `DoseRateManager.ResponseFractions` относила бин `b` к энергии
    /// `(b + 0.5)·шаг`, тогда как бин поглощённой энергии в складе — это
    /// `round(E/шаг)` (<c>EfficiencySimulator.PeakBin</c>), то есть центр бина
    /// `b` равен `b·шаг`. При шаге склада 2 кэВ это сдвиг НА ЦЕЛЫЙ кэВ, и на
    /// каждой границе диапазона дозы один бин континуума меняет владельца. Тот
    /// же дефект в разборе закрыт ~~`S15`~~ 07.08.2026.
    ///
    /// Мерка — доли отклика САМИ, а не доза через них: `ResponseFractions`
    /// зовётся отражением с сеткой, которую строит та же `BuildGrid`, что и
    /// расчёт. Так число видно прямо, без умножения на отсчёты.
    ///
    ///   §1 РЕАЛЬНАЯ МАТРИЦА: своя доля первых диапазонов при низе шкалы
    ///      11.0 / 11.5 / 12.0 / 10.0 кэВ — числа ревизии.
    ///   §2 ПУТЬ ПРИЛОЖЕНИЯ: мощность дозы и покрытие на спектре корпуса.
    ///   §3 ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, ответ известен руками: строка-дельта в
    ///      одном бине; диапазон, накрывающий шкалу ЦЕЛИКОМ, обязан дать
    ///      1.000, а диапазон, куда истинный центр бина попадает, а сдвинутый
    ///      на полбина — уже нет, обязан дать 1.000 после правки и 0.000 до.
    ///   §4 КОНТРОЛЬ НЕИЗМЕННОСТИ: широкий диапазон вдали от краёв — доля
    ///      печатается круговым видом `R` и сравнивается прогонами.
    ///
    ///     dosebincentreprobe [--dir=&lt;корпус&gt;] [--mx=&lt;каталог .rmx&gt;]
    ///                        [--spectrum=AS80_Cs137_0cm] [--scene=AS80_point0]
    ///
    /// ⚠ Склад матриц лежит ВНЕ дерева и посчитан прежним поколением физики;
    /// клеймо такой матрицы с нынешним кодом не сходится, и проба его
    /// ПЕРЕСЧИТЫВАЕТ (`ComputeStamp` той же геометрии). На измеряемую величину
    /// это не влияет: доля диапазона — соглашение о центре бина, а не физика.
    /// Сказано вслух нарочно — числа §1 и §2 цитировать как дозу нельзя.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static string matrixDir;
        static string spectrumName = "AS80_Cs137_0cm";
        static string sceneName = "AS80_point0";

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ (`T247`) Инвариантная культура ЦЕЛИКОМ: точка и в печати, и в разборе.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--mx=", StringComparison.Ordinal)) matrixDir = a.Substring(5);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumName = a.Substring(11);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) sceneName = a.Substring(8);
            }

            Console.WriteLine("AMBER71: центр бина отклика в долях диапазонов дозы");
            Console.WriteLine();

            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", spectrumName + ".xml"));
            if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
            {
                Console.WriteLine("ОСНАСТКА: нет спектра корпуса или у него нет кривой с геометрией: " + spectrumName);
                return 2;
            }

            string mxPath = Path.Combine(matrixDir ?? Path.Combine(corpusDir, "geometries"), sceneName + ".rmx");
            ResponseMatrix matrix = LoadMatrix(mxPath, data.Efficiency.Geometry);
            if (matrix == null)
            {
                Console.WriteLine("ОСНАСТКА: нет годной матрицы " + mxPath);
                return 2;
            }

            RealRanges(data, matrix);
            LowScan(data, matrix);
            Application(data, matrix);
            Synthetic(data.Efficiency, matrix);

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // §1. Доли на реальной матрице
        // ==================================================================

        static void RealRanges(ResultData data, ResponseMatrix matrix)
        {
            Head("§1. СВОЯ ДОЛЯ ДИАПАЗОНА при разном низе шкалы (матрица " + sceneName + ")");
            Console.WriteLine("  шаг бина склада {0:F3} кэВ; узлов {1}; раскладка по каналам: {2}",
                              matrix.BinKev, matrix.Energies.Length, matrix.HasChannels ? "есть" : "нет");
            Console.WriteLine("  TransferByChannel у свежей матрицы: {0}", matrix.TransferByChannel);

            DoseRateInput input = DoseRateInput.Of(data.Efficiency, matrix);
            foreach (double low in new[] { 11.0, 11.5, 12.0, 10.0, 10.3 })
            {
                double[] grid = DoseRateEstimator.BuildGrid(low, 3000.0);
                DoseRateRange[] ranges = RangesOf(grid);
                double[][] f = Fractions(input, ranges);
                Console.WriteLine();
                Console.WriteLine("  низ шкалы {0:F1} кэВ, диапазонов {1}:", low, ranges.Length);
                for (int k = 0; k < Math.Min(4, ranges.Length); k++)
                {
                    // Своя доля — `fractions[k][k]`, то есть ПОЛНАЯ
                    // эффективность линии центра в свой же диапазон (на ней
                    // доза и делит). Рядом — та же величина, отнесённая ко
                    // ВСЕМУ, что линия дала по сетке: это и есть «какая часть
                    // отклика осталась дома», число, которым мерится сдвиг
                    // центра бина.
                    double sum = 0.0;
                    for (int i = 0; i < ranges.Length; i++)
                    {
                        sum += f[k][i];
                    }

                    Console.WriteLine("     диапазон {0}: {1,8:F3}…{2,8:F3} кэВ, центр {3,8:F3} —"
                                      + " своя доля {4:E4}, по сетке {5:E4}, дома {6:F6}",
                                      k, ranges[k].LowKev, ranges[k].HighKev, ranges[k].CenterKev,
                                      f[k][k], sum, sum > 0.0 ? f[k][k] / sum : 0.0);
                }
            }
        }

        // ==================================================================
        // §5. `AMBER82`: низ шкалы перебором
        // ==================================================================

        /// <summary>
        /// (`AMBER82`, П145 24.09.2026) Доля «дома» у диапазона 0 при низе
        /// шкалы 10.00…13.99 кэВ шагом 0.01. После `AMBER70` пик линии делится
        /// на два бина, и при судействе бина по центру нижний бин у низа
        /// шкалы 10.0…10.7 кэВ целиком уходил ниже `LowKev`: доля падала
        /// СКАЧКОМ с 1.000 до ~0.78 (поток диапазона ×1.28). По перекрытию
        /// бина с диапазоном она от положения низа зависит НЕПРЕРЫВНО — это и
        /// судится: наибольший скачок между соседними низами. Число «ниже
        /// 0.999» печатается для сверки с ревизией, но не судится: по
        /// перекрытию бин — отрезок шириной в шаг, и часть его законно лежит
        /// ниже шкалы.
        /// </summary>
        static void LowScan(ResultData data, ResponseMatrix matrix)
        {
            Head("§5. AMBER82: доля «дома» у диапазона 0, низ шкалы 10.00…13.99 кэВ шагом 0.01");
            DoseRateInput input = DoseRateInput.Of(data.Efficiency, matrix);
            Console.WriteLine("  (а) без раскладки каналов — бин против границ диапазона:");
            LowScanOne(input, matrix, 0, 0.0, true);
            // (б) Путь приложения: строка ложится на КАНАЛЫ спектра, каналы — в
            // диапазон по центру (`AMBER101`). Шкала линейная, `E(0)` = низ.
            Console.WriteLine("  (б) раскладка каналов 8192 × 0.3465 кэВ (как AS80), E(0) = низ:");
            LowScanOne(input, matrix, 8192, 0.3465, false);
            Console.WriteLine("  (в) раскладка каналов 1024 × 2.93 кэВ (как RC-103), E(0) = низ:");
            LowScanOne(input, matrix, 1024, 2.93, false);
            Console.WriteLine("  ⚠ (б) и (в) печатаются, но не судятся: там диапазон — целые каналы по центру, и");
            Console.WriteLine("    доля скачет на долю ширины канала, когда граница переходит через центр канала, —");
            Console.WriteLine("    ровно так же, как у самого спектра (согласие сторон мерит DoseGridProbe).");
        }

        static void LowScanOne(DoseRateInput input, ResponseMatrix matrix, int channels, double gain, bool judge)
        {
            int below = 0, total = 0;
            double worst = 2.0, worstLow = 0.0, jump = 0.0, jumpLow = 0.0, previous = double.NaN;
            for (int step = 0; step < 400; step++)
            {
                double low = 10.0 + 0.01 * step;
                double top = channels > 0 ? Math.Min(3000.0, low + gain * channels) : 3000.0;
                DoseRateRange[] ranges = RangesOf(DoseRateEstimator.BuildGrid(low, top));
                object layout = channels > 0 ? Layout(low, gain, channels, ranges) : null;
                double[][] f = Fractions(input, ranges, layout);
                // ⚠ Знаменатель — ВСЯ строка (полная эффективность), а не
                // сумма по сетке: бин, ушедший НИЖЕ `LowKev`, не попадает ни
                // в один диапазон, и отношение к сумме по сетке его не видит
                // (§1 печатает именно такое — там 1.000 при низе 10.3).
                double sum = DoseRateInput.FullEfficiency(matrix, ranges[0].CenterKev);
                double home = sum > 0.0 ? f[0][0] / sum : 0.0;
                total++;
                if (home < 0.999) below++;
                if (home < worst) { worst = home; worstLow = low; }
                if (!double.IsNaN(previous) && Math.Abs(home - previous) > jump)
                {
                    jump = Math.Abs(home - previous);
                    jumpLow = low;
                }

                previous = home;
                if (Math.Abs(low - 10.3) < 1e-9)
                {
                    Console.WriteLine("  низ 10.30 кэВ: диапазон 0 {0:F3}…{1:F3} кэВ, дома {2:F6}, поток ×{3:F4}",
                                      ranges[0].LowKev, ranges[0].HighKev, home, home > 0.0 ? 1.0 / home : 0.0);
                }
            }

            Console.WriteLine("  низов {0}; дома < 0.999 у {1} ({2:F2} %); худшее {3:F6} при низе {4:F2} кэВ",
                              total, below, 100.0 * below / total, worst, worstLow);
            Console.WriteLine("  наибольший скачок доли между соседними низами (0.01 кэВ): {0:F6} у низа {1:F2} кэВ",
                              jump, jumpLow);
            if (!judge)
            {
                return;
            }

            Ok(jump < 0.02, string.Format(CultureInfo.InvariantCulture,
                "доля «дома» непрерывна по низу шкалы: скачок {0:F4} < 0.02 (судейство по центру давало ~0.2)", jump));
        }

        // ==================================================================
        // §2. Путь приложения
        // ==================================================================

        static void Application(ResultData data, ResponseMatrix matrix)
        {
            Head("§2. ПУТЬ ПРИЛОЖЕНИЯ: DoseRateManager.Calculate на " + spectrumName);
            var manager = new DoseRateManager(Config());
            DoseRate dose = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, matrix));
            if (dose == null || !string.IsNullOrEmpty(dose.Refusal))
            {
                Console.WriteLine("  ОТКАЗ: " + (dose == null ? "(null)" : dose.Refusal));
                Bad("доза посчиталась");
                return;
            }

            Console.WriteLine("  доза {0:F6} мкЗв/ч, покрытие {1:F6}, диапазонов {2}",
                              dose.Rate, dose.Coverage, dose.Ranges.Count);
            Console.WriteLine("  ПОБИТОВО доза {0:R}, ошибка {1:R}", dose.Rate, dose.Error);
            for (int k = 0; k < Math.Min(6, dose.Ranges.Count); k++)
            {
                DoseRateRange r = dose.Ranges[k];
                Console.WriteLine("     {0,8:F3}…{1,8:F3} кэВ: своя доля {2:F6}, отсчётов {3:F0}, объяснено {4:F1}, мкЗв/ч {5:F6}{6}",
                                  r.LowKev, r.HighKev, r.OwnEfficiency, r.Counts, r.Explained, r.DoseRate,
                                  r.Skipped ? "  (вне покрытия)" : "");
            }

            Ok(dose.Rate > 0.0, "доза положительна");
        }

        // ==================================================================
        // §3, §4. Синтетика: ответ известен руками
        // ==================================================================

        static void Synthetic(EfficiencyConfigData curve, ResponseMatrix sample)
        {
            Head("§3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: строка-дельта, ответ руками");
            const double step = 2.0;
            const double node = 500.0;

            // ⚠ Строка берётся `Evaluate(центр диапазона)`, то есть растянутой
            // НА ЦЕНТР ТОГО ЖЕ диапазона: диапазон, поставленный «вокруг
            // узла», сдвига не покажет — дельта уедет вместе с ним. Поэтому
            // диапазон ставится так, чтобы его ЦЕНТР совпал с узлом 500.0
            // (растяжение ровно 1), а ВЕРХ пришёлся ровно на `(b + 0.5)·шаг`:
            // истинный центр бина 500.0 внутри, сдвинутый на полбина 501.0 —
            // уже у соседа. Ответ известен руками: 1.000 по соглашению склада,
            // 0.000 по прежнему.
            ResponseMatrix whole = Delta(curve.Geometry, sample, step, node, (int)(node / step));
            DoseRateInput wide = DoseRateInput.Of(curve, whole);
            DoseRateRange[] edge = { Range(499.0, 501.0), Range(501.0, 520.0) };
            double[][] f = Fractions(wide, edge);
            Console.WriteLine("  дельта в бине {0} ({1:F1} кэВ), диапазон 499…501 кэВ (центр = узел):"
                              + " своя доля {2:F6}", (int)(node / step), node, f[0][0]);
            Ok(Math.Abs(f[0][0] - 1.0) < 1e-9,
               "бин с центром 500.0 остаётся в диапазоне 499…501 (руками: 1.000)");

            // Диапазон 100…1000 кэВ накрывает ВСЮ строку: доля обязана быть
            // ровно 1.000 при любом соглашении о центре бина.
            DoseRateRange[] one = { Range(100.0, 1000.0) };
            double got = Fractions(wide, one)[0][0];
            Console.WriteLine("  тот же узел, диапазон 100…1000 кэВ: доля {0:F6}", got);
            Ok(Math.Abs(got - 1.0) < 1e-9, "диапазон, накрывающий шкалу целиком, даёт 1.000");

            Head("§4. КОНТРОЛЬ НЕИЗМЕННОСТИ: широкий диапазон вдали от краёв");
            // Диапазон вдесятеро шире шага склада и обоими краями далеко от
            // бина: полбина туда или сюда ничего не меняет.
            DoseRateRange[] far = { Range(400.0, 600.0) };
            double stable = Fractions(wide, far)[0][0];
            Console.WriteLine("  диапазон 400…600 кэВ: доля {0:F6}", stable);
            Console.WriteLine("  ПОБИТОВО доля {0:R}", stable);
            Ok(Math.Abs(stable - 1.0) < 1e-9, "доля широкого диапазона — 1.000, сдвиг полубина ей безразличен");
        }

        /// <summary>
        /// Матрица с ОДНИМ узлом и строкой-дельтой в названном бине. Клеймо
        /// считается для геометрии кривой — иначе вход откажет; сетка узлов из
        /// одной точки, и <c>Evaluate</c> отдаёт эту строку без смешения.
        /// </summary>
        static ResponseMatrix Delta(GeometryModel geometry, ResponseMatrix sample,
                                    double step, double nodeKev, int bin)
        {
            var row = new float[bin + 8];
            row[bin] = 1.0f;
            var m = new ResponseMatrix
            {
                BinKev = step,
                Energies = new[] { nodeKev },
                Rows = new[] { row },
                Histories = 1,
                Normalization = sample.Normalization,
                Options = sample.Options,
            };
            m.Stamp = ResponseMatrix.ComputeStamp(geometry, sample.Options);
            return m;
        }

        static DoseRateRange Range(double low, double high)
        {
            return new DoseRateRange
            {
                LowKev = low,
                HighKev = high,
                CenterKev = 0.5 * (low + high),
                Counts = 0.0,
                Attributed = 0.0,
            };
        }

        static DoseRateRange[] RangesOf(double[] grid)
        {
            var ranges = new DoseRateRange[grid.Length - 1];
            for (int k = 0; k < ranges.Length; k++)
            {
                ranges[k] = Range(grid[k], grid[k + 1]);
            }

            return ranges;
        }

        /// <summary>
        /// `DoseRateManager.ResponseFractions` — закрытая и статическая; проба
        /// зовёт её отражением НАРОЧНО: мерится именно она, а не доза через
        /// неё, и подставлять сетку руками иначе нечем.
        /// </summary>
        static double[][] Fractions(DoseRateInput input, DoseRateRange[] ranges)
        {
            return Fractions(input, ranges, null);
        }

        /// <summary>
        /// (`AMBER82`/`AMBER101`, П145) Раскладка каналов линейной шкалы
        /// `E(i) = low + gain·i` — закрытым `DoseRateManager.ChannelLayout.Of`,
        /// отражением: так §5 мерит тот путь, которым идёт приложение.
        /// </summary>
        static object Layout(double low, double gain, int channels, DoseRateRange[] ranges)
        {
            Type type = typeof(DoseRateManager).GetNestedType("ChannelLayout", BindingFlags.NonPublic);
            MethodInfo of = type == null ? null : type.GetMethod("Of", BindingFlags.Public | BindingFlags.Static);
            if (of == null)
            {
                throw new InvalidOperationException("в DoseRateManager нет ChannelLayout.Of — проба ослепла");
            }

            var calibration = new PolynomialEnergyCalibration();
            calibration.PolynomialOrder = 1;
            calibration.Coefficients = new double[] { low, gain };
            return of.Invoke(null, new object[] { calibration, channels, channels, null, ranges });
        }

        static double[][] Fractions(DoseRateInput input, DoseRateRange[] ranges, object layout)
        {
            MethodInfo m = typeof(DoseRateManager).GetMethod(
                "ResponseFractions", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null)
            {
                throw new InvalidOperationException(
                    "в DoseRateManager нет ResponseFractions — проба ослепла, а не сошлась");
            }

            try
            {
                return (double[][])m.Invoke(null, new object[] { input, ranges, layout });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        // ------------------------------------------------------------------

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }

        static void Bad(string what)
        {
            checks++;
            failed++;
            Console.WriteLine("  [НЕТ]  " + what);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        /// <summary>
        /// Матрица склада. Клеймо СВЕРЯЕТСЯ и, если не сходится (склад прежнего
        /// поколения физики), пересчитывается — с оглашением. Молча этого
        /// делать нельзя: «клеймо не сходится» у живого прогона значит отказ.
        /// </summary>
        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            Console.WriteLine("  матрица {0}: узлов {1}, {2:F1}…{3:F1} кэВ, историй {4}, каналов {5}",
                              Path.GetFileName(path), matrix.Energies.Length, matrix.Energies[0],
                              matrix.Energies[matrix.Energies.Length - 1], matrix.Histories,
                              matrix.HasChannels ? matrix.ChannelRows.Length : 0);
            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                Console.WriteLine("  ⚠ клеймо матрицы не сходится с геометрией кривой (склад прежнего");
                Console.WriteLine("    поколения физики) — ПЕРЕСЧИТАНО пробой; числа §1–§2 не доза, а");
                Console.WriteLine("    соглашение о центре бина.");
                matrix.Stamp = ResponseMatrix.ComputeStamp(geometry, matrix.Options);
            }

            return matrix;
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }
    }
}
