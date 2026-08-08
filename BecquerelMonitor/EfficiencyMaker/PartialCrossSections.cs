using System;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Канал взаимодействия гамма-кванта с веществом.</summary>
    public enum PhotonProcess
    {
        /// <summary>Когерентное (рэлеевское): направление меняет, энергию нет.</summary>
        Coherent,
        /// <summary>Некогерентное (комптоновское).</summary>
        Incoherent,
        Photoelectric,
        /// <summary>Рождение пары в поле ядра и в поле электрона вместе.</summary>
        PairProduction
    }

    /// <summary>
    /// Парциальные сечения по каналам взаимодействия, см2/г.
    ///
    /// Источник — NIST XCOM 3.1. Числа лежат в `matdb.sqlite`, берутся через
    /// <see cref="MaterialDatabase"/>.
    ///
    /// ЗАЧЕМ ОНИ НУЖНЫ ОТДЕЛЬНО. Полного ослабления для переноса мало.
    /// В сцинтилляторе канал поглощения — малая разность больших чисел: в CsI
    /// на 1332 кэВ комптон даёт 92.3 % ослабления, фотоэффект 5.2 %,
    /// когерентное 1.6 %, пары 0.9 %. Если считать фотоэффектом всё, кроме
    /// комптона, канал поглощения выходит 7.7 % вместо 5.2 % — завышен в
    /// полтора раза, и ровно во столько же завышается эффективность в пике.
    ///
    /// Пока таблица лежала в исходнике, в ней было ДЕВЯТЬ элементов, снятых
    /// руками через веб-форму: O, Na, Ge, Br, Sr, I, Cs, La, Bi. Их хватало на
    /// CsI, NaI, BGO, LaBr3 и SrI2, а CeBr3, CdTe, CZT и GSO считались тем
    /// самым грубым приближением. В базе все сто.
    /// </summary>
    public static class PartialCrossSections
    {
        public static bool HasElement(int z)
        {
            return MaterialDatabase.Has(z);
        }

        /// <summary>
        /// Сечение канала для элемента, см2/г. Интерполяция лог-лог, как у
        /// полного ослабления: линейная по значению завышала бы круто падающий
        /// фотоэффект между узлами. Исключение — участки с нулевым узлом
        /// (рождение пар ниже порога 1.022 МэВ): логарифм там брать не от
        /// чего, и такой участок интерполируется по значению линейно.
        /// </summary>
        public static double MassCrossSection(int z, double energyKev, PhotonProcess process)
        {
            MaterialDatabase.Element element;
            if (!(energyKev > 0.0) || !MaterialDatabase.TryGet(z, out element))
            {
                return 0.0;
            }

            double[] grid = element.EnergyKev;
            int n = grid.Length;
            int lo, hi;
            if (energyKev <= grid[0])
            {
                lo = hi = 0;
            }
            else if (energyKev >= grid[n - 1])
            {
                lo = hi = n - 1;
            }
            else
            {
                lo = 0;
                hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (grid[mid] <= energyKev)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }
            }

            double a = Channel(element, lo, process);
            if (lo == hi)
            {
                return a;
            }

            double x0 = grid[lo], x1 = grid[hi];
            double b = Channel(element, hi, process);
            if (!(x1 > x0))
            {
                // край поглощения: две точки на одной энергии, берётся верхняя
                return b;
            }

            double f = (Math.Log(energyKev) - Math.Log(x0)) / (Math.Log(x1) - Math.Log(x0));
            if (!(a > 0.0) || !(b > 0.0))
            {
                // канал открывается не с нуля шкалы: рождение пар ниже 1.022 МэВ
                // тождественно нулевое, логарифм там брать не от чего
                return a + f * (b - a);
            }

            // Лог-лог, как и у полного ослабления. Линейная по значению
            // интерполяция круто падающего фотоэффекта между узлами его
            // ЗАВЫШАЕТ, а завышенный фотоэффект — это завышенная доля полного
            // поглощения, то есть завышенная эффективность в пике.
            return Math.Exp(Math.Log(a) + f * (Math.Log(b) - Math.Log(a)));
        }

        static double Channel(MaterialDatabase.Element element, int i, PhotonProcess process)
        {
            switch (process)
            {
                case PhotonProcess.Coherent: return element.Channels[0][i];
                case PhotonProcess.Incoherent: return element.Channels[1][i];
                case PhotonProcess.Photoelectric: return element.Channels[2][i];
                default: return element.Channels[3][i] + element.Channels[4][i];
            }
        }
    }
}
