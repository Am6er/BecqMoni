using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace FsaRefitZClassProbe
{
    /// <summary>
    /// (`A275`) ОТСЕВ ПО ЗНАЧИМОСТИ НЕ ИМЕЕТ ПРАВА ВЫНЕСТИ ВЕСЬ ОБЪЯВЛЕННЫЙ
    /// СОСТАВ — замер на сцене, собранной в памяти.
    ///
    /// Откуда. У `AS80_Charoite` (малая база, непонятная часть) разбор
    /// возвращал ДВА компонента, и оба — приборные образы: `Xray-Pb` 20.6 % и
    /// `Xray-Ba` 1.1 %, при объявленных K-40 и Ra-226. Виноват был не образ:
    /// отсев по значимости (<see cref="FsaAnalyzer.RefitZ"/> = 3) судит
    /// нуклиды и мешающие образы ОДНИМ списком, `Xray-Pb` с z = 15.14 порог
    /// проходил, объявленный `Ra-226` с z = 2.26 — нет, и правило
    /// самоотключения («уцелевших нет — отсева не было») не срабатывало
    /// именно потому, что уцелел образ. Континуум потом разносился по двум
    /// уцелевшим, отчего доля образа и вырастала до 20.6 % при СОБСТВЕННЫХ
    /// 1.0 % отсчётов спектра.
    ///
    /// ⛔ Сцена здесь СИНТЕТИЧЕСКАЯ и своя, а не корпусная, нарочно: правило
    /// про класс компонентов, а не про свинец, чароит или AS80x80. Проба
    /// ничего не читает с диска и годится там, где корпуса нет вовсе.
    ///
    /// Три плеча, и рычаг у всех один — ЧТО ЛЕЖИТ В БИБЛИОТЕКЕ:
    ///
    ///   «состав + образ»  — слабый нуклид (z ниже порога) рядом со значимым
    ///                       мешающим образом. Нуклид ОБЯЗАН уцелеть, а
    ///                       <see cref="FsaAnalyzer.RefitZNuclidesRescued"/>
    ///                       быть больше нуля;
    ///   «состав без образа» — тот же слабый нуклид один. Уцелеть он обязан и
    ///                       здесь, но СТАРЫМ правилом (`keep` пуст), то есть
    ///                       возвращённых быть не должно вовсе;
    ///   «сильный состав»  — значимый нуклид рядом с тем же образом.
    ///                       ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: правило обязано МОЛЧАТЬ
    ///                       (возвращённых 0), иначе оно срабатывает всюду и
    ///                       не меряет ничего.
    ///
    /// ⚠ Проба судит СОСТАВ И СЧЁТЧИК, а не невязку. Свободный компонент без
    /// верхней границы всегда уменьшает Σχ², и приговор по невязке здесь
    /// подтвердил бы любую правку (цена ошибки измерена на ~~`S141`~~).
    ///
    ///   fsarefitzclassprobe
    /// </summary>
    static class Program
    {
        /// <summary>Каналов сцены; шкала 1 кэВ на канал, так что канал = кэВ.</summary>
        const int Channels = 512;

        /// <summary>Где стоит линия мешающего образа, кэВ.</summary>
        const double NuisanceKev = 75.0;

        /// <summary>Где стоят линии нуклида, кэВ.</summary>
        static readonly double[] NuclideKev = { 300.0, 600.0 };

        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ Культура ЦЕЛИКОМ инвариантная, а не клон системной с подменённым
            //    разделителем (`T245`): клон чинил ПЕЧАТЬ и оставлял РАЗБОР
            //    системным — обе стороны чинятся вместе (приказ Amber 05.09.2026).
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
            // отражение её не видит, и снятая позже она уже могла быть уведена.
            FsaTuningReport.Snapshot();

            foreach (string a in args)
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }

            Console.WriteLine("=== ОТСЕВ ПО ЗНАЧИМОСТИ И КЛАСС КОМПОНЕНТА (`A275`) ===");
            Console.WriteLine("сцена: {0} каналов, 1.00 кэВ на канал; образ на {1} кэВ, нуклид на {2} кэВ",
                              Channels.ToString(CultureInfo.InvariantCulture),
                              NuisanceKev.ToString("F1", CultureInfo.InvariantCulture),
                              string.Join(" и ", NuclideKev[0].ToString("F1", CultureInfo.InvariantCulture),
                                                 NuclideKev[1].ToString("F1", CultureInfo.InvariantCulture)));
            Console.WriteLine();

            Arm("состав + образ", true, true);
            Arm("состав без образа", false, true);
            Arm("сильный состав + образ", true, false);

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "СОШЛОСЬ: отсев не оставляет разбор без единого нуклида и молчит там, где состав значим"
                : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Одно плечо: собрать сцену, разобрать её и назвать, что уцелело.
        /// </summary>
        /// <param name="withNuisance">класть ли в библиотеку мешающий образ</param>
        /// <param name="weakNuclide">нуклид слабый (z ниже порога) или значимый</param>
        static void Arm(string what, bool withNuisance, bool weakNuclide)
        {
            // Высота нуклидных пиков — единственное, чем плечи «слабый» и
            // «сильный» отличаются друг от друга. Спектр один и тот же по
            // построению: подложка, большой пик образа, пики нуклида.
            double nuclidePeak = weakNuclide ? 12.0 : 4000.0;
            EnergySpectrum spectrum = Scene(nuclidePeak);

            var library = new List<FsaComponent>();
            FsaComponent nuclide = new FsaComponent("nuclide", FsaComponentKind.Single);
            foreach (double kev in NuclideKev)
            {
                nuclide.Lines.Add(new FsaLine("nuclide", kev, 100.0));
            }

            library.Add(nuclide);
            if (withNuisance)
            {
                FsaComponent image = new FsaComponent("image", FsaComponentKind.Nuisance);
                image.Lines.Add(new FsaLine("image", NuisanceKev, 100.0));
                library.Add(image);
            }

            var analyzer = new FsaAnalyzer
            {
                // Порог отсева НЕ ТРОГАЕТСЯ — судится поставочное умолчание.
                CascadeSumming = false,
                CascadeSumPeaks = false,
                Backscatter = false,
                PileUp = false,
                PartialResidualGate = false
            };
            FsaTuningReport.Print(analyzer);

            FsaResult result = analyzer.Analyze(spectrum, null, Fwhm(), library, null);

            bool nuclideAlive = false;
            bool imageAlive = false;
            double nuclideZ = double.NaN;
            double imageZ = double.NaN;
            if (result != null && result.Components != null)
            {
                foreach (FsaComponentResult component in result.Components)
                {
                    if (component.Kind == FsaComponentKind.Nuisance)
                    {
                        imageAlive = true;
                        imageZ = component.Z;
                    }
                    else
                    {
                        nuclideAlive = true;
                        nuclideZ = component.Z;
                    }
                }
            }

            Console.WriteLine("--- плечо «{0}» ---", what);
            Console.WriteLine("  порог отсева {0}, судимых {1} (нуклидных {2}), прошли {3}, возвращено {4}",
                              Num(analyzer.RefitZUsed), analyzer.RefitZJudged.ToString(CultureInfo.InvariantCulture),
                              analyzer.RefitZNuclidesJudged.ToString(CultureInfo.InvariantCulture),
                              analyzer.RefitZKept.ToString(CultureInfo.InvariantCulture),
                              analyzer.RefitZNuclidesRescued.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  исход отсева: {0}", analyzer.RefitZState);
            Console.WriteLine("  в разборе: нуклид {0} (z {1}), образ {2} (z {3})",
                              nuclideAlive ? "ЕСТЬ" : "НЕТ", Num(nuclideZ),
                              imageAlive ? "ЕСТЬ" : "НЕТ", Num(imageZ));

            Same("нуклид уцелел", true, nuclideAlive);
            if (withNuisance && weakNuclide)
            {
                // Плечо ради которого проба и написана: без правки здесь
                // оставался один образ.
                Same("образ уцелел вместе с ним", true, imageAlive);
                Same("правило сработало (возвращённых больше нуля)", true,
                     analyzer.RefitZNuclidesRescued > 0);
                Same("слабость нуклида НЕ выдумана: z ниже порога", true,
                     !double.IsNaN(nuclideZ) && nuclideZ < analyzer.RefitZUsed);
            }
            else
            {
                // ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ обоих родов: без образа правилу
                // нечего спасать (старое самоотключение), при значимом
                // составе — незачем.
                Same("правило молчит (возвращённых нет)", 0, analyzer.RefitZNuclidesRescued);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Сцена: ровная подложка, большой пик мешающего образа и два пика
        /// нуклида заданной высоты. Формы гауссовы, ширина — та же калибровка
        /// ПШПВ, которой потом идёт разбор: пик, набранный не той шириной,
        /// мерил бы рассогласование форм, а не отсев.
        /// </summary>
        static EnergySpectrum Scene(double nuclidePeak)
        {
            var spectrum = new EnergySpectrum(1.0, Channels)
            {
                EnergyCalibration = new PolynomialEnergyCalibration
                {
                    Coefficients = new[] { 0.0, 1.0 }
                },
                LiveTime = 1000.0,
                MeasurementTime = 1000.0
            };

            FwhmCalibration fwhm = Fwhm();
            for (int ch = 0; ch < Channels; ch++)
            {
                double value = 200.0;
                value += Gauss(ch, NuisanceKev, fwhm, 20000.0);
                foreach (double kev in NuclideKev)
                {
                    value += Gauss(ch, kev, fwhm, nuclidePeak);
                }

                spectrum.Spectrum[ch] = (int)Math.Round(value);
            }

            return spectrum;
        }

        /// <summary>Гауссиана высоты <paramref name="peak"/> с ПШПВ калибровки.</summary>
        static double Gauss(int channel, double centre, FwhmCalibration fwhm, double peak)
        {
            double width = fwhm.ChannelToFwhm(centre);
            if (!(width > 0.0))
            {
                return 0.0;
            }

            double sigma = width / 2.354820045;
            double t = (channel - centre) / sigma;
            return peak * Math.Exp(-0.5 * t * t);
        }

        /// <summary>ПШПВ² = 4 + 0.6·канал: около 7 каналов на 75 и 19 на 600.</summary>
        static FwhmCalibration Fwhm()
        {
            return new SimpleSqrtFwhmCalibration
            {
                Coefficients = new[] { 4.0, 0.6 }
            };
        }

        static string Num(double value)
        {
            return double.IsNaN(value)
                ? "нет"
                : value.ToString("F2", CultureInfo.InvariantCulture);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-48} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(CultureInfo.InvariantCulture,
                                                      " вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
