using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Массовые коэффициенты ослабления гамма-излучения, см2/г.
    ///
    /// Источник — NIST XCOM 3.1 (Berger, Hubbell, Seltzer). Числа больше не
    /// лежат в этом файле: полная поставка втянута в `matdb.sqlite`, откуда её
    /// и берёт <see cref="MaterialDatabase"/>. Здесь остался только вход,
    /// которым пользуется остальная программа.
    ///
    /// Что переезд дал, кроме порядка: было 92 элемента, снятых руками с
    /// поэлементных страниц NIST, стало 100 из первичных файлов поставки; и
    /// починились две опечатки в атомных массах, которые в переписанном от руки
    /// массиве не бросались в глаза — у празеодима (Z=59) было 40.9076 вместо
    /// 140.9077, потеряна ведущая единица, у протактиния (Z=91) стояла масса
    /// урана 238.0289 вместо 231.0359. Обе поймала база: пересчёт стандартной
    /// атомной массы из `nuclides` (Σ abundance × atomic_mass) и таблица
    /// `ATWTS` самого XCOM дают одно и то же.
    /// </summary>
    public static class AttenuationData
    {
        /// <summary>Атомная масса, г/моль, по Z.</summary>
        public static Dictionary<int, double> AtomicMass
        {
            get { return MaterialDatabase.AtomicMass; }
        }

        public static bool HasElement(int z)
        {
            return MaterialDatabase.Has(z);
        }

        /// <summary>
        /// Массовый коэффициент ослабления элемента, см2/г, лог-лог
        /// интерполяцией. За краями таблицы держится крайнее значение.
        ///
        /// ⛔ (`AMBER74`, П132 22.09.2026) СУММОЙ КАНАЛОВ, а не прямой по сумме
        /// в узлах: см. <see cref="PartialCrossSections.MassTotal"/> — там и
        /// довод, и числа. Прежняя строка была
        /// `MaterialDatabase.Interpolate(… element.Total, element.LogTotal …)`.
        /// </summary>
        public static double MassAttenuation(int z, double energyKev)
        {
            if (!(energyKev > 0.0))
            {
                return 0.0;
            }

            MaterialDatabase.Element element;
            if (!MaterialDatabase.TryGet(z, out element))
            {
                return 0.0;
            }

            // С готовыми логарифмами (`T43`): значения в таблице не меняются,
            // а брать от них логарифм на каждый вызов — четыре из пяти вызовов
            // `Math.Log` в самой горячей точке счёта.
            int lo, hi;
            if (element.EnergyKev == null
                || !MaterialDatabase.Bracket(element.EnergyKev, energyKev, out lo, out hi))
            {
                return 0.0;
            }

            return PartialCrossSections.MassTotal(element, lo, hi, energyKev,
                                                  Math.Log(energyKev));
        }
    }
}
