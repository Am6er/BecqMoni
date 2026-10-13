using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FsaTotalReadersProbe
{
    /// <summary>
    /// ДВА ЧИТАТЕЛЯ ПОЛНОГО ОСЛАБЛЕНИЯ В FSA — на прежней прямой по сумме
    /// (`Element.Total`) или на сумме каналов (`PartialCrossSections.MassTotal`,
    /// ~~`AMBER74`~~)? (П149, 24.09.2026, строка `AMBER80`, часть читателей.)
    ///
    /// Читатели: знаменатель веса K-вылета (`FsaSampleLibrary.CrystalMix
    /// .Attenuation` — и ослабление своего рентгена, и полное на энергии
    /// родителя) и доля рождения пар `FsaLibrary.PairShare` (порог отбора
    /// родителей SE/DE и их порядок на пути по умолчанию).
    ///
    /// Проба печатает на кристаллах CsI, NaI, LaBr₃ (массовые доли — из
    /// формулы, атомные массы IUPAC):
    ///   * Σw·μ по прямой и по сумме каналов и их отношение;
    ///   * `PairShare` так, как его считает ПРИЛОЖЕНИЕ (закрытый метод зовётся
    ///     отражением — это и есть мерка правки), и то же по обеим формулам;
    ///   * отношение веса K-вылета «сумма каналов / прямая» для родителей на
    ///     сетке энергий — через закрытый `EscapeFraction` приложения.
    ///
    ///     fsatotalreadersprobe [--check]
    ///
    /// `--check` — код 1, если `PairShare` или ослабление смеси вылета
    /// (`CrystalMix.Attenuation`, отражением) у приложения НЕ равны счёту по
    /// сумме каналов (то есть читатель ещё на прямой). Имён нуклидов нет.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            bool check = false;
            foreach (string a in args)
            {
                if (a == "--check") check = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            var crystals = new List<KeyValuePair<string, Dictionary<int, double>>>
            {
                Crystal("CsI", new[] { 55, 53 }, new[] { 132.90545196, 126.90447 }, new[] { 1, 1 }),
                Crystal("NaI", new[] { 11, 53 }, new[] { 22.98976928, 126.90447 }, new[] { 1, 1 }),
                Crystal("LaBr3", new[] { 57, 35 }, new[] { 138.90547, 79.904 }, new[] { 1, 3 })
            };

            MethodInfo pairShare = typeof(FsaLibrary).GetMethod("PairShare",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo escapeFraction = typeof(FsaSampleLibrary).GetMethod("EscapeFraction",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (pairShare == null || escapeFraction == null)
            {
                Console.Error.WriteLine("закрытые методы не найдены отражением: PairShare {0}, EscapeFraction {1}",
                                        pairShare != null, escapeFraction != null);
                return 2;
            }

            int stale = 0;
            double[] energies = { 28.6, 31.0, 33.0, 100.0, 300.0, 661.657, 1100.0, 1173.2, 1332.5, 1500.0, 1764.5, 2000.0, 2614.5, 3000.0 };
            foreach (KeyValuePair<string, Dictionary<int, double>> crystal in crystals)
            {
                Console.WriteLine();
                Console.WriteLine("=== {0}: {1} ===", crystal.Key, Fractions(crystal.Value));
                object mix = CrystalMixOf(crystal.Value);
                MethodInfo attenuation = mix != null ? mix.GetType().GetMethod("Attenuation") : null;
                Console.WriteLine("E, кэВ\tμ прямая\tμ каналы\tканалы/прямая−1, %\tμ вылета приложения\tприложение =\tPairShare прямая\tPairShare каналы\tсдвиг, %\tPairShare приложения\tприложение =");
                foreach (double e in energies)
                {
                    double chord = Mix(crystal.Value, e, false);
                    double channels = Mix(crystal.Value, e, true);
                    double pair = Pair(crystal.Value, e);
                    double oldShare = chord > 0.0 ? pair / chord : double.NaN;
                    double newShare = channels > 0.0 ? pair / channels : double.NaN;
                    double app = (double)pairShare.Invoke(null, new object[] { crystal.Value, e });
                    string which = e < 1022.0 ? "—"
                        : Same(app, newShare) ? "каналы" : Same(app, oldShare) ? "ПРЯМАЯ" : "?";
                    if (e >= 1022.0 && which != "каналы") stale++;
                    double mu = attenuation != null ? (double)attenuation.Invoke(mix, new object[] { e }) : double.NaN;
                    string muWhich = Near(mu, channels) ? "каналы" : Near(mu, chord) ? "ПРЯМАЯ" : "?";
                    if (Math.Abs(channels / chord - 1.0) > 1e-9 && muWhich != "каналы") stale++;
                    Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}",
                                      F(e, "F1"), F(chord, "G6"), F(channels, "G6"),
                                      F(100.0 * (channels / chord - 1.0), "F3"), F(mu, "G6"), muWhich,
                                      F(oldShare, "G5"), F(newShare, "G5"),
                                      F(100.0 * (newShare / oldShare - 1.0), "F3"), F(app, "G5"), which);
                }

                // Вес K-вылета: photo/total · EscapeFraction(μ(Kα), μ(E)). Меняется
                // только ослабление, поэтому отношение «каналы / прямая» по двум
                // ослаблениям и двум вызовам EscapeFraction приложения.
                Console.WriteLine("вес K-вылета, каналы/прямая − 1, % (по элементам кристалла, Kα элемента):");
                foreach (KeyValuePair<int, double> member in crystal.Value)
                {
                    MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(member.Key);
                    if (fluorescence == null) continue;
                    double kAlpha = fluorescence.LineKev[0];
                    var row = new StringBuilder();
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (double parent in new[] { 40.0, 60.0, 80.0, 122.0, 200.0, 356.0, 511.0, 661.657, 1000.0, 1460.8, 2614.5 })
                    {
                        if (parent <= fluorescence.KEdgeKev) continue;
                        double xOld = Mix(crystal.Value, kAlpha, false), xNew = Mix(crystal.Value, kAlpha, true);
                        double tOld = Mix(crystal.Value, parent, false), tNew = Mix(crystal.Value, parent, true);
                        double fOld = (double)escapeFraction.Invoke(null, new object[] { xOld, tOld });
                        double fNew = (double)escapeFraction.Invoke(null, new object[] { xNew, tNew });
                        double ratio = (fNew / tNew) / (fOld / tOld) - 1.0;
                        lo = Math.Min(lo, ratio);
                        hi = Math.Max(hi, ratio);
                        row.AppendFormat(CultureInfo.InvariantCulture, " {0}:{1:F3}", parent, 100.0 * ratio);
                    }

                    Console.WriteLine("  Z={0} Kα {1} кэВ:{2}  (от {3:F3} до {4:F3} %)", member.Key,
                                      F(kAlpha, "F3"), row, 100.0 * lo, 100.0 * hi);
                }
            }

            Console.WriteLine();
            Console.WriteLine(stale == 0 ? "ОБА ЧИТАТЕЛЯ ПРИЛОЖЕНИЯ — НА СУММЕ КАНАЛОВ"
                                         : "читатели приложения НЕ на сумме каналов: " + stale.ToString(CultureInfo.InvariantCulture) + " точек");
            return check && stale > 0 ? 1 : 0;
        }

        static KeyValuePair<string, Dictionary<int, double>> Crystal(string name, int[] z, double[] mass, int[] count)
        {
            double sum = 0.0;
            for (int i = 0; i < z.Length; i++) sum += mass[i] * count[i];
            var map = new Dictionary<int, double>();
            for (int i = 0; i < z.Length; i++) map[z[i]] = mass[i] * count[i] / sum;
            return new KeyValuePair<string, Dictionary<int, double>>(name, map);
        }

        /// <summary>Σw·μ смеси: прямой по `Element.Total` либо суммой каналов.</summary>
        static double Mix(Dictionary<int, double> fractions, double kev, bool channels)
        {
            double mu = 0.0;
            foreach (KeyValuePair<int, double> f in fractions)
            {
                MaterialDatabase.Element element;
                int lo, hi;
                if (!MaterialDatabase.TryGet(f.Key, out element)
                    || !MaterialDatabase.Bracket(element.EnergyKev, kev, out lo, out hi))
                {
                    return double.NaN;
                }

                mu += f.Value * (channels
                    ? PartialCrossSections.MassTotal(element, lo, hi, kev, Math.Log(kev))
                    : MaterialDatabase.Interpolate(element.EnergyKev, element.LogEnergyKev,
                                                   element.Total, element.LogTotal, lo, hi, kev, Math.Log(kev)));
            }

            return mu;
        }

        /// <summary>Σw·σ_пар пороговой формой — как в `FsaLibrary.PairShare`.</summary>
        static double Pair(Dictionary<int, double> fractions, double kev)
        {
            double pair = 0.0;
            foreach (KeyValuePair<int, double> f in fractions)
            {
                MaterialDatabase.Element element;
                int lo, hi;
                if (!MaterialDatabase.TryGet(f.Key, out element)
                    || !MaterialDatabase.Bracket(element.EnergyKev, kev, out lo, out hi))
                {
                    return double.NaN;
                }

                pair += f.Value * PartialCrossSections.MassCrossSection(element, lo, hi, kev, Math.Log(kev),
                                                                         PhotonProcess.PairProduction, true);
            }

            return pair;
        }

        /// <summary>
        /// Смесь кристалла ТЕМ классом, каким её держит приложение
        /// (`FsaSampleLibrary.CrystalMix`, закрыт — отражением): её
        /// `Attenuation` и есть знаменатель веса K-вылета.
        /// </summary>
        static object CrystalMixOf(Dictionary<int, double> fractions)
        {
            Type mixType = typeof(FsaSampleLibrary).GetNestedType("CrystalMix", BindingFlags.NonPublic);
            Type partType = mixType != null ? mixType.GetNestedType("Part", BindingFlags.NonPublic | BindingFlags.Public) : null;
            if (mixType == null || partType == null) return null;
            object mix = Activator.CreateInstance(mixType, true);
            object parts = mixType.GetField("Parts").GetValue(mix);
            MethodInfo add = parts.GetType().GetMethod("Add");
            foreach (KeyValuePair<int, double> f in fractions)
            {
                MaterialDatabase.Element element;
                if (!MaterialDatabase.TryGet(f.Key, out element)) return null;
                object part = Activator.CreateInstance(partType, true);
                partType.GetField("Fraction").SetValue(part, f.Value);
                partType.GetField("Element").SetValue(part, element);
                add.Invoke(parts, new[] { part });
            }

            return mix;
        }

        static bool Near(double a, double b)
        {
            return !double.IsNaN(a) && !double.IsNaN(b) && Math.Abs(a - b) <= 1e-9 * Math.Abs(b);
        }

        static bool Same(double a, double b)
        {
            return !double.IsNaN(a) && !double.IsNaN(b) && Math.Abs(a - b) <= 1e-12 * Math.Max(1e-300, Math.Abs(b));
        }

        static string Fractions(Dictionary<int, double> map)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, double> f in map)
                parts.Add("Z=" + f.Key.ToString(CultureInfo.InvariantCulture) + " " + f.Value.ToString("F4", CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
