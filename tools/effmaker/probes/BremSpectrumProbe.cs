using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BremSpectrumProbe
{
    /// <summary>
    /// Поверка спектра тормозного из сечений Зельцера — Бергера (M3, физика 8).
    ///
    ///     bremspectrumprobe [--material=CsI] [--n=200000]
    ///                       [--energies=100,662,1332,2614]
    ///
    /// Главная сверка — **баланс энергии против ESTAR**. Таблица толстой
    /// мишени строится из СЕЧЕНИЙ (`seltzer_berger`) и пробега (`ElectronData`,
    /// ESTAR), а полная излучённая энергия у ESTAR лежит отдельной колонкой —
    /// радиационным выходом Y(T). Это два разных числа из двух разных мест
    /// одной поставки, и сойтись они обязаны: интеграл ∫k·(dN/dk)dk выше
    /// 5 кэВ плюс отброшенный низ (он мал — при dN/dk ~ 1/k энергия ниже k₀
    /// составляет долю k₀/T) должен дать Y(T)·T.
    ///
    /// Второй раздел — форма: где кванты по сравнению с прежним приближением
    /// Крамерса dN/dk = C/k. Именно форма решает, вылетит квант или сядет на
    /// месте, и именно она до сих пор была угадана, а не посчитана.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string materialName = "CsI";
            int samples = 200000;
            var energies = new List<double> { 100, 300, 661.657, 1332.5, 2614.5 };

            foreach (string a in args)
            {
                if (a.StartsWith("--material=", StringComparison.Ordinal)) materialName = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) samples = int.Parse(a.Substring(4));
                else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string part in a.Substring(11).Split(','))
                    {
                        energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            ElectronData.Material electron = ElectronData.ByName(materialName);
            if (electron == null)
            {
                Console.Error.WriteLine("нет вещества в ElectronData: " + materialName);
                Console.Error.WriteLine("есть: CsI, NaI, BGO, LaBr3, CeBr3, SrI2, CdTe, CZT, GSO, Ge, Al, PTFE, Water");
                return 2;
            }

            GeometryMaterial material = Compound(materialName);
            if (material == null)
            {
                Console.Error.WriteLine("состав для " + materialName + " в пробе не прописан");
                return 2;
            }

            ThickTargetBrem table = ThickTargetBrem.For(material, electron, 5.0);
            if (table == null)
            {
                Console.Error.WriteLine("таблица не построилась — нет сечений или пробега");
                return 1;
            }

            Console.WriteLine("вещество: {0}, отсечка {1:F1} кэВ", materialName, table.MinKev);
            Console.WriteLine();
            Console.WriteLine("1. Баланс энергии: интеграл Зельцера — Бергера против выхода ESTAR");
            Console.WriteLine();
            Console.WriteLine("   Уровень спектра подтянут к ESTAR (форма — от Зельцера — Бергера);");
            Console.WriteLine("   колонка «подтяжка» и есть размер невязки: единица — сошлось само.");
            Console.WriteLine();
            Console.WriteLine("    T, кэВ   Y(T)·T, кэВ   ∫k dN выше 5 кэВ   подтяжка   ⟨N⟩    ⟨k⟩, кэВ");

            foreach (double t in energies)
            {
                double estar = ElectronData.YieldOf(electron, t) * t;
                double above = table.Radiated(t);
                double n = table.Photons(t);
                Console.WriteLine("   {0,7:F1}   {1,11:F4}   {2,15:F4}   {3,8:F4}   {4,6:F4}   {5,8:F2}",
                    t, estar, above, table.Anchor(t), n, n > 0.0 ? above / n : 0.0);
            }

            Console.WriteLine();
            Console.WriteLine("2. Форма: где кванты. Доли розыгрышей по декадам, {0} на точку", samples);
            Console.WriteLine("   (в скобках — то же у приближения Крамерса dN/dk = C/k)");
            Console.WriteLine();
            Console.WriteLine("    T, кэВ    5–10 кэВ     10–50      50–200     200–1000    выше 1000");

            var rnd = new Random(20260808);
            foreach (double t in energies)
            {
                double[] bins = new double[5];
                double[] kramers = new double[5];
                for (int i = 0; i < samples; i++)
                {
                    bins[BinOf(table.SampleKev(t, rnd.NextDouble()))] += 1.0;
                    double k = 5.0 * Math.Pow(t / 5.0, rnd.NextDouble());
                    kramers[BinOf(k)] += 1.0;
                }

                Console.Write("   {0,7:F1} ", t);
                for (int b = 0; b < 5; b++)
                {
                    Console.Write("  {0,5:F1} ({1,5:F1})",
                        100.0 * bins[b] / samples, 100.0 * kramers[b] / samples);
                }

                Console.WriteLine();
            }

            Console.WriteLine();
            return 0;
        }

        static int BinOf(double kKev)
        {
            if (kKev < 10.0) return 0;
            if (kKev < 50.0) return 1;
            if (kKev < 200.0) return 2;
            if (kKev < 1000.0) return 3;
            return 4;
        }

        /// <summary>
        /// Состав кристаллов — только те, что нужны пробе. Массовые доли из
        /// атомных весов базы: состав таблице нужен, чтобы взвесить Z².
        /// </summary>
        static GeometryMaterial Compound(string name)
        {
            switch (name)
            {
                case "CsI": return Make("CsI", 4.51, new[] { 55, 53 }, new[] { 1, 1 });
                case "NaI": return Make("NaI", 3.67, new[] { 11, 53 }, new[] { 1, 1 });
                case "BGO": return Make("BGO", 7.13, new[] { 83, 32, 8 }, new[] { 4, 3, 12 });
                case "LaBr3": return Make("LaBr3", 5.08, new[] { 57, 35 }, new[] { 1, 3 });
                case "CeBr3": return Make("CeBr3", 5.18, new[] { 58, 35 }, new[] { 1, 3 });
                case "SrI2": return Make("SrI2", 4.55, new[] { 38, 53 }, new[] { 1, 2 });
                case "Ge": return Make("Ge", 5.323, new[] { 32 }, new[] { 1 });
                case "Al": return Make("Al", 2.699, new[] { 13 }, new[] { 1 });
                case "Water": return Make("Water", 1.0, new[] { 1, 8 }, new[] { 2, 1 });
                default: return null;
            }
        }

        static GeometryMaterial Make(string name, double density, int[] z, int[] atoms)
        {
            GeometryMaterial m = new GeometryMaterial { Name = name, Density = density };
            double total = 0.0;
            double[] mass = new double[z.Length];
            for (int i = 0; i < z.Length; i++)
            {
                double a;
                if (!MaterialDatabase.AtomicMass.TryGetValue(z[i], out a))
                {
                    return null;
                }

                mass[i] = a * atoms[i];
                total += mass[i];
            }

            for (int i = 0; i < z.Length; i++)
            {
                m.Fractions[z[i]] = mass[i] / total;
            }

            return m;
        }
    }
}
