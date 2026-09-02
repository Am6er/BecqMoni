using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace FsaRobustnessProbe
{
    /// <summary>
    /// Положительные и отрицательные контроли защит FSA из S114/S115/S117/S118.
    ///
    /// S114: единственная линия ниже первого узла матрицы обязана отвергнуть
    /// разбор; та же линия НА узле должна строить результат. S115: нечисловые
    /// точки кривой не должны попасть в интерполятор, а двух годных точек
    /// достаточно для нормальной выборки. S117: нечисловая строка базы не
    /// должна стать линией образа. S118: бесконечная ПШПВ не должна увести
    /// разбор в бесконечный обход сетки континуума.
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            Console.WriteLine("=== надёжность FSA (S114/S115/S117/S118) ===");
            CheckEfficiencyInput();
            CheckMatrixFloor();
            CheckLibraryInput();
            CheckFwhmInput();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void CheckEfficiencyInput()
        {
            FsaEfficiency curve = FsaEfficiency.FromConfig(new EfficiencyConfigData
            {
                Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 10.0, Efficiency = 0.10, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = double.NaN, Efficiency = 0.20, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 20.0, Efficiency = 0.20, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 30.0, Efficiency = double.PositiveInfinity, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 40.0, Efficiency = 0.40, ErrorPercent = double.NaN }
                }
            });

            double efficiency, error;
            Same("две конечные точки дают кривую", true, curve != null);
            Same("кривая без NaN вычисляется", true,
                 curve != null && curve.TryEval(15.0, out efficiency, out error)
                 && Finite(efficiency) && Finite(error));

            FsaEfficiency rejected = FsaEfficiency.FromConfig(new EfficiencyConfigData
            {
                Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 10.0, Efficiency = 0.10, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 20.0, Efficiency = double.NaN, ErrorPercent = 2.0 }
                }
            });
            Same("одна конечная точка не образует кривую", null, rejected);
        }

        static void CheckMatrixFloor()
        {
            ResponseMatrix matrix = new ResponseMatrix
            {
                Energies = new[] { 20.0 },
                BinKev = 1.0,
                Rows = new[] { PeakRow(21, 20) }
            };

            Same("линия ниже первого узла отклоняет разбор", null,
                 Analyze(matrix, 10.0));
            Same("линия на первом узле остаётся допустимой", true,
                 Analyze(matrix, 20.0) != null);
        }

        static void CheckLibraryInput()
        {
            Peak peak = new Peak
            {
                Nuclide = new NuclideDefinition { Name = "Test-1" }
            };

            List<FsaComponent> nanIntensity = FsaLibrary.BuildFromPeaks(
                new[] { peak },
                new[]
                {
                    new NuclideDefinition
                    {
                        Name = "Test-1", Energy = 100.0, Intencity = double.NaN
                    }
                });
            Same("NaN-интенсивность не создаёт образ", 0, nanIntensity.Count);

            List<FsaComponent> infinityEnergy = FsaLibrary.BuildFromPeaks(
                new[] { peak },
                new[]
                {
                    new NuclideDefinition
                    {
                        Name = "Test-1", Energy = double.PositiveInfinity, Intencity = 1.0
                    }
                });
            Same("∞-энергия не создаёт образ", 0, infinityEnergy.Count);
        }

        static void CheckFwhmInput()
        {
            ResponseMatrix matrix = new ResponseMatrix
            {
                Energies = new[] { 20.0 },
                BinKev = 1.0,
                Rows = new[] { PeakRow(21, 20) }
            };

            bool completed;
            try
            {
                Analyze(matrix, 20.0, double.PositiveInfinity);
                completed = true;
            }
            catch
            {
                completed = false;
            }

            Same("∞-ПШПВ завершает разбор без исключения", true, completed);
        }

        static FsaResult Analyze(ResponseMatrix matrix, double energy, double fwhmSquare = 1.0)
        {
            EnergySpectrum spectrum = new EnergySpectrum(1.0, 128)
            {
                EnergyCalibration = new PolynomialEnergyCalibration
                {
                    Coefficients = new[] { 0.0, 1.0 }
                },
                LiveTime = 1.0
            };
            for (int i = 0; i < spectrum.Spectrum.Length; i++)
            {
                spectrum.Spectrum[i] = 1;
            }
            spectrum.Spectrum[(int)energy] = 100;

            SimpleSqrtFwhmCalibration fwhm = new SimpleSqrtFwhmCalibration
            {
                Coefficients = new[] { fwhmSquare, 0.0 }
            };
            FsaComponent component = new FsaComponent("test", FsaComponentKind.Single);
            component.Lines.Add(new FsaLine("test", energy, 100.0));

            FsaAnalyzer analyzer = new FsaAnalyzer
            {
                ResponseMatrix = matrix,
                CascadeSumming = false,
                CascadeSumPeaks = false,
                Backscatter = false,
                PileUp = false,
                PartialResidualGate = false,
                RefitZ = 0.0,
                HuberM = 0.0
            };
            return analyzer.Analyze(spectrum, null, fwhm,
                                    new List<FsaComponent> { component }, null);
        }

        static float[] PeakRow(int count, int bin)
        {
            float[] row = new float[count];
            row[bin] = 1.0f;
            return row;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-52} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
