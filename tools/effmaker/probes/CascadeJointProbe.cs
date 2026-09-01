using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Text;

namespace CascadeJointProbe
{
    /// <summary>
    /// Совместная эффективность пары квантов каскада: во сколько раз истинная
    /// вероятность поглотить ОБА больше произведения средних (`S19`, `S50`).
    ///
    /// Формула сумм-пика перемножает средние по объёму эффективности, а точка
    /// распада у двух квантов ОДНА — их шансы связаны. Проба меряет поправку
    ///
    ///     κ(E₁,E₂) = ⟨ε₁ε₂⟩ / (⟨ε₁⟩·⟨ε₂⟩)
    ///
    /// тем же переносом, которым считается матрица.
    ///
    ///   cascadejointprobe --geometry=X.in --pairs=201.83:306.78[,88.34:201.83] [--n=400000]
    ///
    /// Сверка: Geant4 на сцене `ASN16_lu_side` даёт для (201.83, 306.78) 1.26,
    /// а на точечном источнике 1.00 — вторая точка и есть контроль, что мерится
    /// именно протяжённость, а не что-то ещё.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, pairs = "201.83:306.78";
            int histories = 400000;
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--pairs=", StringComparison.Ordinal)) pairs = a.Substring(8);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (geometryPath == null)
            {
                Console.Error.WriteLine("нужен --geometry=<файл.in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            if (geometry == null)
            {
                Console.Error.WriteLine("геометрия не прочиталась: " + geometryPath);
                return 2;
            }

            Console.WriteLine("геометрия: {0}", geometryPath);
            Console.WriteLine("историй на точку: {0}", histories);
            Console.WriteLine();
            Console.WriteLine("  {0,10} {1,10} {2,10} {3,10}", "E1, кэВ", "E2, кэВ", "κ", "±%");
            foreach (string item in pairs.Split(','))
            {
                string[] two = item.Split(':');
                if (two.Length != 2)
                {
                    Console.Error.WriteLine("--pairs=<E1>:<E2>[,<E1>:<E2>...]");
                    return 2;
                }

                double e1 = double.Parse(two[0], CultureInfo.InvariantCulture);
                double e2 = double.Parse(two[1], CultureInfo.InvariantCulture);
                var simulator = new EfficiencySimulator(geometry) { Histories = histories };
                double error;
                double kappa = simulator.JointPeakFactor(e1, e2, out error);
                Console.WriteLine("  {0,10:F2} {1,10:F2} {2,10:F4} {3,10:F2}", e1, e2, kappa, error);
            }

            return 0;
        }
    }
}
