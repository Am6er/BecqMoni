using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MineProbe
{
    /// <summary>
    /// Проверка двух ловушек разбора `.in` ВПРЫСКОМ: обе портят результат молча,
    /// поэтому убедиться, что защита работает, можно только подсунув
    /// испорченный файл и посмотрев, что получилось.
    ///
    /// 1. Слой с толщиной, но без плотности. Области сцены вложены и ищутся по
    ///    порядку, поэтому пропавший слой не исчезает, а ЗАМЕЩАЕТСЯ слоем
    ///    снаружи: у отражателя на месте ПТФЭ оказывался алюминий корпуса, и
    ///    «убранный» отражатель давал МИНУС к эффективности вместо плюса.
    ///    Признак починки — смена знака.
    ///
    /// 2. `FractionType = ATOM`. Атомные доли, прочитанные как массовые, дают
    ///    правдоподобный, но неверный состав. Признак починки — пересчёт
    ///    атомных долей в те же массовые, что стоят в исходном файле.
    ///
    ///   mineprobe &lt;файл .in&gt;
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("mineprobe <файл .in>");
                return 1;
            }

            string source = args[0];
            string temp = Path.Combine(Path.GetTempPath(), "mineprobe");
            Directory.CreateDirectory(temp);
            int bad = 0;
            bad += CheckMissingDensity(source, temp) ? 0 : 1;
            bad += CheckAtomFractions(source, temp) ? 0 : 1;
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ОБЕ ЛОВУШКИ ЗАКРЫТЫ" : string.Format("НЕ ЗАКРЫТО: {0}", bad));
            return bad == 0 ? 0 : 2;
        }

        // ------------------------------------------------------------------

        static bool CheckMissingDensity(string source, string temp)
        {
            Console.WriteLine("=== 1. слой без плотности");
            GeometryModel baseline = GeometryModel.Load(source);
            string broken = Path.Combine(temp, "no_reflector_density.in");
            File.WriteAllText(broken,
                Replace(File.ReadAllText(source, Encoding.GetEncoding(1251)),
                        "DS_RoCrystalReflector", "0"),
                Encoding.GetEncoding(1251));

            GeometryModel hurt = GeometryModel.Load(broken);
            bool warned = hurt.Warnings.Count > 0;
            Console.WriteLine("    предупреждений: {0}", hurt.Warnings.Count);
            foreach (string w in hurt.Warnings)
            {
                Console.WriteLine("      {0}", w);
            }

            double a = Efficiency(baseline, 50.0);
            double b = Efficiency(hurt, 50.0);
            double delta = (b / a - 1.0) * 100.0;
            Console.WriteLine("    50 кэВ: {0:E4} -> {1:E4}  ({2:+0.0;-0.0} %)", a, b, delta);

            // Отражатель поглощает, значит без него эффективность обязана
            // ВЫРАСТИ. Минус означал бы, что его место занял корпус.
            bool ok = warned && delta > 0.0;
            Console.WriteLine(ok
                ? "    закрыто: слой стал пустотой, о пропаже сказано"
                : "    НЕ ЗАКРЫТО: " + (warned ? "знак не тот — слой чем-то замещён" : "молчит"));
            return ok;
        }

        // ------------------------------------------------------------------

        static bool CheckAtomFractions(string source, string temp)
        {
            Console.WriteLine("=== 2. FractionType = ATOM");
            GeometryModel baseline = GeometryModel.Load(source);
            string text = File.ReadAllText(source, Encoding.GetEncoding(1251));

            // Иодид цезия: атомные доли ровно 0.5/0.5, массовые в файле —
            // 0.488451/0.511549. Подменяем и то и другое.
            text = Replace(text, "DS_FractionsCrystal[0]", "0.5");
            text = Replace(text, "DS_FractionsCrystal[1]", "0.5");
            text = Replace(text, "DS_FractionTypeCrystal", "ATOM");
            string atom = Path.Combine(temp, "atom_fractions.in");
            File.WriteAllText(atom, text, Encoding.GetEncoding(1251));

            GeometryModel converted = GeometryModel.Load(atom);
            Console.WriteLine("    предупреждений: {0}", converted.Warnings.Count);
            foreach (string w in converted.Warnings)
            {
                Console.WriteLine("      {0}", w);
            }

            bool ok = converted.Warnings.Count > 0;
            foreach (KeyValuePair<int, double> pair in baseline.Crystal.Fractions)
            {
                double now;
                converted.Crystal.Fractions.TryGetValue(pair.Key, out now);
                double diff = Math.Abs(now - pair.Value);
                Console.WriteLine("    Z={0,3}: в файле массовая {1:F6}, из атомных получилось {2:F6}  (расх. {3:F6})",
                                  pair.Key, pair.Value, now, diff);
                ok = ok && diff < 5e-4;
            }

            Console.WriteLine(ok
                ? "    закрыто: атомные доли пересчитаны в те же массовые"
                : "    НЕ ЗАКРЫТО");
            return ok;
        }

        // ------------------------------------------------------------------

        /// <summary>Подменить значение ключа, сохранив остальной файл как есть.</summary>
        static string Replace(string text, string key, string value)
        {
            string pattern = "(?m)^" + Regex.Escape(key) + @"\s*=.*$";
            string replacement = key + " = " + value;
            if (!Regex.IsMatch(text, pattern))
            {
                throw new InvalidOperationException("ключ не найден: " + key);
            }

            return Regex.Replace(text, pattern, replacement);
        }

        static double Efficiency(GeometryModel g, double energy)
        {
            EfficiencySimulator sim = new EfficiencySimulator(g) { Histories = 120000 };
            double error;
            return sim.Efficiency(energy, out error);
        }
    }
}
