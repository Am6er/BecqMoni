using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LineAuditScaleProbe
{
    /// <summary>
    /// (`S179`, П126 22.09.2026) ДИСПЕРСИЯ ВЫЧТЕННОГО ФОНА В СВЕРКЕ ЛИНИЙ —
    /// ПРОТИВ ФОРМУЛЫ НА СИНТЕТИКЕ. <see cref="FsaLineAudit"/> получает
    /// собранный РУКАМИ <see cref="FsaResult"/> (модель, континуум, вычтенный
    /// фон s·N_фона, образ компонента) и спектр с линейной шкалой, так что
    /// окно линии, суммы под ним и оба живых времени известны точно, и σ с
    /// порогом решения считаются рядом по формуле:
    ///
    ///   σ² = Σ data + s·Σ bg,   L_C = k·√(Σ cont + Σ bg + s·Σ bg),   s = T/T_фона.
    ///
    /// Плечи: s = 1 (фон той же длительности — σ побитово равна прежней
    /// `√(data + bg)`, порог — Карри «парных наблюдений» 2.33·√B при C = 0);
    /// s = 0.01 (фон в сто раз длиннее — вклад фона в σ² падает в сто раз);
    /// отказ — разбор, взявший фон, без спектра фона обязан кончиться
    /// <see cref="ArgumentException"/>, а не молчаливой единицей; ряд —
    /// компонент вида <see cref="FsaComponentKind.Chain"/> с почленным
    /// результатом (два члена, `ChainRoot` = имя ряда) обязан дать чистоту 1
    /// и «свой» образ суммой членов, а компонент, которого в результате нет, —
    /// ни одной строки.
    ///
    ///   lineauditscaleprobe          — код 0: все плечи сошлись; 1 — расхождение.
    /// </summary>
    static class Program
    {
        const double Tol = 1e-9;

        static int failures;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine("=== S179: дисперсия вычтенного фона в FsaLineAudit против формулы ===");
            Console.WriteLine("{0,-28} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10}",
                              "плечо", "σ_audit", "σ_формула", "L_C audit", "L_C форм.", "чистота", "Z");

            Scenario("s = 1 (T_фона = T)", liveTime: 1000.0, backgroundLive: 1000.0);
            Scenario("s = 0.01 (T_фона = 100·T)", liveTime: 1000.0, backgroundLive: 100000.0);
            Scenario("s = 0.033 (G1S: 1800/54000)", liveTime: 1800.25, backgroundLive: 54000.038);
            Refusal();
            Chain();

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "СОШЛОСЬ: все плечи по формуле, отказ без спектра фона назван, ряд собран из членов."
                : string.Format(CultureInfo.InvariantCulture, "РАСХОЖДЕНИЙ: {0}", failures));
            return failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------

        sealed class Stand
        {
            public EnergySpectrum Spectrum;
            public EnergySpectrum Background;
            public FsaResult Result;
            public FwhmCalibration Fwhm;
            public List<FsaComponent> Library;
            public int Lo, Hi;
            public double SumData, SumBg, SumCont, SumModelNet;
        }

        /// <summary>
        /// Стенд: 256 каналов, шкала 1 кэВ/канал, ПШПВ 4 канала везде, линия на
        /// 100 кэВ → окно ±1 ПШПВ = каналы 96…104. Данные — континуум 50 +
        /// гаусс площади 2000 + фон; вычтенный фон — s·N_фона при N_фона = 4000
        /// отсчётов/канал (плоский), то есть bg = s·4000: при s = 1 фон под
        /// окном вдвое больше самого пика, при s = 0.01 — сорок на канал.
        /// </summary>
        static Stand Build(double liveTime, double backgroundLive, bool chain)
        {
            const int channels = 256;
            const double fwhm = 4.0;
            const double center = 100.0;
            double scale = liveTime / backgroundLive;
            double sigmaCh = fwhm / 2.354820045;

            var calibration = new PolynomialEnergyCalibration();
            calibration.PolynomialOrder = 1;
            calibration.Coefficients = new[] { 0.0, 1.0 };

            var fwhmCal = new SimpleSqrtFwhmCalibration();
            fwhmCal.Coefficients = new[] { fwhm * fwhm, 0.0 };

            var own = new double[channels];
            var ownB = new double[channels];
            var cont = new double[channels];
            var bg = new double[channels];
            var model = new double[channels];
            var data = new int[channels];
            var bgCounts = new int[channels];
            for (int i = 0; i < channels; i++)
            {
                double g = 2000.0 / (sigmaCh * Math.Sqrt(2.0 * Math.PI))
                           * Math.Exp(-0.5 * Math.Pow((i - center) / sigmaCh, 2.0));
                // у ряда образ делится между двумя членами 3:1 — сумма та же
                own[i] = chain ? 0.75 * g : g;
                ownB[i] = chain ? 0.25 * g : 0.0;
                cont[i] = 50.0;
                bgCounts[i] = 4000;
                bg[i] = scale * bgCounts[i];
                model[i] = cont[i] + g;
                data[i] = (int)Math.Round(model[i] + bg[i] + 3.0 * Math.Sin(i));
            }

            var spectrum = new EnergySpectrum(1.0, channels);
            spectrum.Spectrum = data;
            spectrum.EnergyCalibration = calibration;
            spectrum.LiveTime = liveTime;
            spectrum.MeasurementTime = liveTime;

            var background = new EnergySpectrum(1.0, channels);
            background.Spectrum = bgCounts;
            background.EnergyCalibration = calibration;
            background.LiveTime = backgroundLive;
            background.MeasurementTime = backgroundLive;

            var result = new FsaResult();
            result.Model = model;
            result.Continuum = cont;
            result.Background = bg;
            result.BackgroundUsed = true;
            result.Gain = 1.0;
            result.OffsetChannels = 0.0;
            result.FirstChannel = 0;
            result.LastChannel = channels - 1;
            result.LiveTime = liveTime;

            string name = chain ? "Ряд-X" : "Одиночный-X";
            var component = new FsaComponent(name, chain ? FsaComponentKind.Chain : FsaComponentKind.Single);
            component.Lines.Add(new FsaLine(chain ? "Член-A" : name, center, 100.0));
            if (chain)
            {
                result.Components.Add(new FsaComponentResult { Name = "Член-A", Curve = own, ChainRoot = name });
                result.Components.Add(new FsaComponentResult { Name = "Член-B", Curve = ownB, ChainRoot = name });
            }
            else
            {
                result.Components.Add(new FsaComponentResult { Name = name, Curve = own });
            }

            var stand = new Stand
            {
                Spectrum = spectrum,
                Background = background,
                Result = result,
                Fwhm = fwhmCal,
                Library = new List<FsaComponent> { component },
                Lo = (int)Math.Floor(center - fwhm),
                Hi = (int)Math.Ceiling(center + fwhm),
            };
            for (int i = stand.Lo; i <= stand.Hi; i++)
            {
                stand.SumData += data[i];
                stand.SumBg += bg[i];
                stand.SumCont += cont[i];
                stand.SumModelNet += model[i] - cont[i];
            }

            return stand;
        }

        static void Scenario(string title, double liveTime, double backgroundLive)
        {
            Stand stand = Build(liveTime, backgroundLive, chain: false);
            double s = liveTime / backgroundLive;
            List<FsaLineAudit.LineCheck> checks = FsaLineAudit.Run(stand.Spectrum, stand.Result, stand.Fwhm,
                                                                    stand.Library, stand.Background);
            if (checks.Count != 1)
            {
                Fail(title, "строк " + checks.Count.ToString(CultureInfo.InvariantCulture) + ", ждали 1");
                return;
            }

            FsaLineAudit.LineCheck c = checks[0];
            double sigma = Math.Sqrt(stand.SumData + s * stand.SumBg);
            double threshold = FsaLineAudit.DecisionK * Math.Sqrt(stand.SumCont + stand.SumBg + s * stand.SumBg);
            double sigmaOld = Math.Sqrt(stand.SumData + stand.SumBg);
            Console.WriteLine("{0,-28} {1,10:F4} {2,10:F4} {3,10:F4} {4,10:F4} {5,10:F3} {6,10:F3}",
                              title, c.Sigma, sigma, c.DecisionThreshold, threshold, c.Purity, c.Z);
            Check(title, "σ", c.Sigma, sigma);
            Check(title, "L_C", c.DecisionThreshold, threshold);
            Check(title, "измерено", c.Measured, stand.SumData - stand.SumBg - stand.SumCont);
            Check(title, "ожидание", c.Expected, stand.SumModelNet);
            if (Math.Abs(s - 1.0) < 1e-12)
            {
                // прежняя формула `√(data + bg)` при s = 1 — та же величина побитово
                if (c.Sigma != sigmaOld)
                {
                    Fail(title, "при s = 1 σ обязана быть побитово прежней √(data + bg): " +
                                c.Sigma.ToString("R", CultureInfo.InvariantCulture) + " против " +
                                sigmaOld.ToString("R", CultureInfo.InvariantCulture));
                }
                else
                {
                    Console.WriteLine("    s = 1: σ побитово равна прежней √(Σdata + Σbg) = {0:R}; порог прежний был бы {1:F4} (без члена фона)",
                                      sigmaOld, FsaLineAudit.DecisionK * Math.Sqrt(stand.SumCont + stand.SumBg));
                }
            }
            else
            {
                Console.WriteLine("    прежняя σ = √(Σdata + Σbg) = {0:F4}; вклад фона в σ² прежний {1:F1}, верный {2:F1} (×{3:F3})",
                                  sigmaOld, stand.SumBg, s * stand.SumBg, s);
            }
        }

        static void Refusal()
        {
            Stand stand = Build(1000.0, 100000.0, chain: false);
            try
            {
                FsaLineAudit.Run(stand.Spectrum, stand.Result, stand.Fwhm, stand.Library, null);
                Fail("отказ", "разбор с фоном без спектра фона ПРОШЁЛ молча — масштаб подменён единицей");
            }
            catch (ArgumentException e)
            {
                Console.WriteLine("{0,-28} ОТКАЗ НАЗВАН: {1}", "без спектра фона",
                                  e.Message.Split('\n')[0]);
            }

            // фон не вычитался — спектр фона не нужен, строка есть, σ = √Σdata
            Stand plain = Build(1000.0, 1000.0, chain: false);
            plain.Result.Background = null;
            plain.Result.BackgroundUsed = false;
            List<FsaLineAudit.LineCheck> checks = FsaLineAudit.Run(plain.Spectrum, plain.Result, plain.Fwhm,
                                                                    plain.Library, null);
            if (checks.Count != 1)
            {
                Fail("без фона", "строк " + checks.Count.ToString(CultureInfo.InvariantCulture));
                return;
            }

            Check("без фона", "σ", checks[0].Sigma, Math.Sqrt(plain.SumData));
            Check("без фона", "L_C", checks[0].DecisionThreshold, FsaLineAudit.DecisionK * Math.Sqrt(plain.SumCont));
            Console.WriteLine("{0,-28} {1,10:F4} {2,10:F4} {3,10:F4} {4,10:F4}", "фон не вычитался",
                              checks[0].Sigma, Math.Sqrt(plain.SumData),
                              checks[0].DecisionThreshold, FsaLineAudit.DecisionK * Math.Sqrt(plain.SumCont));
        }

        static void Chain()
        {
            Stand stand = Build(1000.0, 100000.0, chain: true);
            List<FsaLineAudit.LineCheck> checks = FsaLineAudit.Run(stand.Spectrum, stand.Result, stand.Fwhm,
                                                                    stand.Library, stand.Background);
            if (checks.Count != 1)
            {
                Fail("ряд", "строк " + checks.Count.ToString(CultureInfo.InvariantCulture) + ", ждали 1");
                return;
            }

            Console.WriteLine("{0,-28} чистота {1:F3}, компонент «{2}», ожидание {3:F1} (модель без подложки {4:F1})",
                              "ряд из двух членов", checks[0].Purity, checks[0].Component,
                              checks[0].Expected, stand.SumModelNet);
            Check("ряд", "чистота (образ = сумма членов)", checks[0].Purity, 1.0);
            if (!checks[0].Obligatory)
            {
                Fail("ряд", "линия площади 2000 над подложкой 450 не признана обязательной");
            }

            // компонента, которого в результате нет, — ни строки
            var absent = new List<FsaComponent> { new FsaComponent("Нет-такого", FsaComponentKind.Chain) };
            absent[0].Lines.Add(new FsaLine("Нет-такого", 100.0, 100.0));
            int rows = FsaLineAudit.Run(stand.Spectrum, stand.Result, stand.Fwhm, absent, stand.Background).Count;
            if (rows != 0)
            {
                Fail("ряд", "компонент без результата дал строк: " + rows.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                Console.WriteLine("{0,-28} строк 0 — пропущен, как и должно", "компонент вне результата");
            }
        }

        static void Check(string title, string what, double got, double want)
        {
            if (Math.Abs(got - want) > Tol * Math.Max(1.0, Math.Abs(want)))
            {
                Fail(title, string.Format(CultureInfo.InvariantCulture, "{0}: {1:R} против формулы {2:R}", what, got, want));
            }
        }

        static void Fail(string title, string why)
        {
            failures++;
            Console.WriteLine("  ⛔ {0}: {1}", title, why);
        }
    }
}
