using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BoundScatterProbe
{
    /// <summary>
    /// Поверка рассеяния на СВЯЗАННОМ электроне (N11, физика 7): функция
    /// S(x,Z), доплеровское размытие по профилям Комптона и когерентный угол
    /// по F²(x,Z).
    ///
    ///     boundscatterprobe [--geometry=X.in] [--n=200000]
    ///                       [--energies=60,200,662,1461]
    ///
    /// Три раздела, и первый — главный.
    ///
    /// 1. **Сечение из S(x,Z) против XCOM.** Интеграл ∫(dσ_KN/dΩ)·S(x,Z)dΩ
    ///    обязан совпасть с некогерентным сечением XCOM на тот же атом. Это
    ///    сверка ДВУХ НЕЗАВИСИМЫХ поставок (EPDL97 против XCOM) и
    ///    одновременно единственная проверка, что аргумент x взят в тех
    ///    единицах, в каких он лежит в базе: ошибись в множителе — и
    ///    отношение уедет в разы, а не на проценты. Розыгрыш угла отбором по
    ///    S/Z принимает ровно эту долю от Клейна — Нишины, поэтому колонка
    ///    «принято» меряет то же самое уже самим кодом розыгрыша.
    ///
    /// 2. **Доплер.** При рассеянии строго назад печатается среднее и
    ///    среднеквадратичное отклонение рассеянной энергии от свободной
    ///    формулы. Среднее обязано остаться на свободном значении (импульс
    ///    электрона симметричен), а ширина — это и есть размытие
    ///    комптоновского края, которое до сих пор в модели отсутствовало.
    ///
    /// 3. **Когерентное.** Средний косинус угла: с ростом энергии рассеяние
    ///    обязано прижиматься вперёд (⟨cos⟩ → 1), потому что форм-фактор
    ///    обрезает большие переданные импульсы.
    ///
    /// Без --geometry берётся CsI плотности 4.51 — вещество кристаллов
    /// корпуса; ключ нужен, только чтобы посмотреть на чужой кристалл.
    /// </summary>
    static class Program
    {
        const double ElectronMassKev = 510.99895;
        const double ClassicalRadiusCm = 2.8179403262e-13;
        const double Avogadro = 6.02214076e23;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            int samples = 200000;
            var energies = new List<double> { 30, 60, 100, 200, 356, 661.657, 1332.5, 2614.5 };

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
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

            GeometryModel geometry;
            if (geometryPath != null)
            {
                if (!File.Exists(geometryPath))
                {
                    Console.Error.WriteLine("нет файла геометрии: " + geometryPath);
                    return 2;
                }

                geometry = GeometryModel.Load(geometryPath);
            }
            else
            {
                geometry = null;
            }

            GeometryMaterial crystal = geometry != null ? geometry.Crystal : CesiumIodide();
            Console.WriteLine("кристалл: {0}, плотность {1:F3} г/см³",
                crystal.Name == "" ? "(без имени)" : crystal.Name, crystal.Density);
            Console.WriteLine();

            SectionCrossSection(crystal, energies);
            SectionDoppler(crystal, energies, samples);
            SectionRayleigh(crystal, energies, samples);
            return 0;
        }

        /// <summary>CsI: доли массовые, плотность как у сцинтилляторов корпуса.</summary>
        static GeometryMaterial CesiumIodide()
        {
            const double MassCs = 132.90545;
            const double MassI = 126.90447;
            double total = MassCs + MassI;
            GeometryMaterial m = new GeometryMaterial { Name = "CsI", Density = 4.51 };
            m.Fractions[55] = MassCs / total;
            m.Fractions[53] = MassI / total;
            return m;
        }

        // ------------------------------------------------------------------
        // 1. Сечение из S(x,Z) против XCOM
        // ------------------------------------------------------------------

        static void SectionCrossSection(GeometryMaterial material, List<double> energies)
        {
            Console.WriteLine("1. Некогерентное сечение: ∫KN·S(x,Z)dΩ из EPDL97 против XCOM");
            Console.WriteLine("   (разные поставки данных; расхождение больше нескольких % —");
            Console.WriteLine("    либо единицы x, либо интерполяция)");
            Console.WriteLine();
            Console.WriteLine("     Z    E, кэВ    σ_EPDL, барн   σ_XCOM, барн   отн.    принято, %");

            foreach (KeyValuePair<int, double> pair in material.Fractions)
            {
                int z = pair.Key;
                ScatteringData.Atom atom = ScatteringData.Of(z);
                if (atom == null)
                {
                    Console.WriteLine("    {0,3}   — угловых данных нет", z);
                    continue;
                }

                foreach (double e in energies)
                {
                    double epdl = IntegrateBound(atom, e);
                    double xcom = XcomIncoherentBarn(z, e);
                    double free = KleinNishinaTotal(e) * z;
                    Console.WriteLine("    {0,3}  {1,8:F1}    {2,12:F5}   {3,12:F5}   {4,6:F3}   {5,7:F2}",
                        z, e, epdl * 1e24, xcom, xcom > 0.0 ? epdl * 1e24 / xcom : 0.0,
                        free > 0.0 ? 100.0 * epdl / free : 0.0);
                }
            }

            Console.WriteLine();
        }

        /// <summary>∫ (dσ_KN/dΩ)·S(x,Z) dΩ, см²/атом. Симпсон по cosθ.</summary>
        static double IntegrateBound(ScatteringData.Atom atom, double energyKev)
        {
            const int N = 4000;                 // чётное
            double a = energyKev / ElectronMassKev;
            double k = ScatteringData.InverseCmPerKev * energyKev;
            double h = 2.0 / N;
            double sum = 0.0;
            for (int i = 0; i <= N; i++)
            {
                double cos = -1.0 + i * h;
                if (cos > 1.0) cos = 1.0;
                double eps = 1.0 / (1.0 + a * (1.0 - cos));
                double sin2 = 1.0 - cos * cos;
                double kn = 0.5 * ClassicalRadiusCm * ClassicalRadiusCm
                            * eps * eps * (eps + 1.0 / eps - sin2);
                double x = k * Math.Sqrt(Math.Max(0.0, 0.5 * (1.0 - cos)));
                double f = kn * atom.ScatteringFunction(x);
                double w = (i == 0 || i == N) ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);
                sum += w * f;
            }

            return 2.0 * Math.PI * sum * h / 3.0;
        }

        static double XcomIncoherentBarn(int z, double energyKev)
        {
            double mass;
            if (!MaterialDatabase.AtomicMass.TryGetValue(z, out mass) || !(mass > 0.0))
            {
                return 0.0;
            }

            // см²/г → см²/атом → барн
            double perGram = PartialCrossSections.MassCrossSection(
                z, energyKev, PhotonProcess.Incoherent);
            return perGram * mass / Avogadro * 1e24;
        }

        static double KleinNishinaTotal(double energyKev)
        {
            return EfficiencySimulator.KleinNishinaTotal(energyKev);
        }

        // ------------------------------------------------------------------
        // 2. Доплеровское размытие
        // ------------------------------------------------------------------

        static void SectionDoppler(GeometryMaterial material, List<double> energies, int samples)
        {
            Console.WriteLine("2. Доплер: рассеяние строго назад, {0} розыгрышей на точку", samples);
            Console.WriteLine();
            Console.WriteLine("   Распределение ТЯЖЕЛОХВОСТОЕ, и среднеквадратичное о нём врёт:");
            Console.WriteLine("   ядро набирают внешние оболочки (импульс ~1 а.е., это ~1.7 кэВ на");
            Console.WriteLine("   662 кэВ), а хвост — K и L, где импульс доходит до 10–20 а.е. Поэтому");
            Console.WriteLine("   ширина печатается квантилями: «ядро» — полуразность 25 и 75 %");
            Console.WriteLine("   (для гауссианы это 0.6745σ), «хвост» — от 5 до 95 %.");
            Console.WriteLine();
            Console.WriteLine("    E, кэВ   свободная   медиана    сдвиг    ядро±, кэВ   хвост 5–95 %, кэВ");

            var sim = new EfficiencySimulator(null)
            {
                BoundCompton = true,
                DopplerBroadening = true,
            };

            foreach (double e in energies)
            {
                sim.ResetStream((ulong)Math.Round(e * 1024.0) + 12345UL);
                double freeBack = e / (1.0 + 2.0 * e / ElectronMassKev);
                List<double> values = new List<double>(samples);
                for (int i = 0; i < samples; i++)
                {
                    // угол зажат на 180°, чтобы мерить ТОЛЬКО доплер
                    double back = sim.DopplerAt(material, e, -1.0);
                    if (back > 0.0)
                    {
                        values.Add(back);
                    }
                }

                if (values.Count == 0)
                {
                    Console.WriteLine("   {0,7:F1}   нет розыгрышей", e);
                    continue;
                }

                values.Sort();
                double median = Quantile(values, 0.5);
                double core = 0.5 * (Quantile(values, 0.75) - Quantile(values, 0.25));
                Console.WriteLine("   {0,7:F1}   {1,9:F3} {2,9:F3} {3,8:F3}    {4,9:F3}    {5,8:F2} … {6,8:F2}",
                    e, freeBack, median, median - freeBack, core,
                    Quantile(values, 0.05), Quantile(values, 0.95));
            }

            Console.WriteLine();
        }

        /// <summary>Квантиль отсортированного набора, линейно между соседями.</summary>
        static double Quantile(List<double> sorted, double q)
        {
            int n = sorted.Count;
            double pos = q * (n - 1);
            int lo = (int)Math.Floor(pos);
            int hi = Math.Min(n - 1, lo + 1);
            return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
        }

        // ------------------------------------------------------------------
        // 3. Когерентное рассеяние
        // ------------------------------------------------------------------

        static void SectionRayleigh(GeometryMaterial material, List<double> energies, int samples)
        {
            Console.WriteLine("3. Когерентное: угол по F²(x,Z), {0} розыгрышей на точку", samples);
            Console.WriteLine("   с ростом энергии обязано прижиматься вперёд");
            Console.WriteLine();
            Console.WriteLine("    E, кэВ    ⟨cosθ⟩    ⟨θ⟩, °   доля θ<10°, %   доля θ>90°, %");

            var sim = new EfficiencySimulator(null) { RayleighScatter = true };
            foreach (double e in energies)
            {
                sim.ResetStream((ulong)Math.Round(e * 4096.0) + 999UL);
                double sumCos = 0.0, sumAngle = 0.0;
                int narrow = 0, wide = 0;
                for (int i = 0; i < samples; i++)
                {
                    double cos = sim.RayleighCosine(material, e);
                    sumCos += cos;
                    double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI;
                    sumAngle += theta;
                    if (theta < 10.0) narrow++;
                    if (theta > 90.0) wide++;
                }

                Console.WriteLine("   {0,7:F1}   {1,8:F5}  {2,7:F2}   {3,12:F2}   {4,12:F2}",
                    e, sumCos / samples, sumAngle / samples,
                    100.0 * narrow / samples, 100.0 * wide / samples);
            }

            Console.WriteLine();
        }
    }
}
