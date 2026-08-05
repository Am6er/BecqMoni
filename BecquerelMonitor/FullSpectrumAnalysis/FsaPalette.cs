using System;
using System.Collections.Generic;
using System.Drawing;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Цвета слоёв разложения.
    ///
    /// Прежде здесь была именная таблица на два десятка нуклидов плюс короткий
    /// запасной список. Она не работала по двум причинам сразу. Во-первых,
    /// таблица написана под ЦЕПОЧКИ (Th-232, Ra-226, U-238), а библиотека FSA
    /// строится из найденных пиков и оперирует ДОЧЕРНИМИ (Ac-228, Pb-212,
    /// Bi-214, Tl-208) — их в таблице не было ни одного, и запасной путь был не
    /// запасным, а основным. Во-вторых, запасной список состоял ровно из цветов
    /// самой таблицы, так что столкновение было гарантировано устройством:
    /// на ториевом спектре корпуса Pb-212 и Ra-226 заливались одним оранжевым,
    /// на цезиевом Backscatter180 получал розовый Cs-137.
    ///
    /// Теперь список один — <see cref="Palette"/>, 64 цвета. Он посчитан жадной
    /// выборкой самой удалённой точки в CIELAB по решётке sRGB, из которой
    /// выброшено слишком тёмное (сливается с фоном графика), слишком светлое
    /// (сливается с линией модели) и слишком серое (серое оставлено за
    /// подложкой). Минимальное расстояние между любыми двумя цветами набора —
    /// ΔE76 = 22 при пороге различимости около 2.3.
    ///
    /// Привязки «нуклид → цвет» больше нет и вести её не надо. Но цвет всё же
    /// не случайный: место в палитре берётся из хеша ИМЕНИ компонента, поэтому
    /// один и тот же нуклид получает один и тот же цвет в разных спектрах, и
    /// картинки по-прежнему читаются одной легендой. Если место занято другим
    /// слоем ЭТОГО спектра, берётся следующее свободное — внутри кадра
    /// столкновений не бывает по построению.
    /// </summary>
    public static class FsaPalette
    {
        /// <summary>
        /// 64 цвета, различимых на тёмном фоне. Порядок — как их выбирала
        /// жадная выборка, то есть первые максимально далеки друг от друга;
        /// у спектра с пятью слоями цвета берутся из начала списка.
        /// </summary>
        static readonly int[] Palette =
        {
            0xCC55BB, 0x00FF00, 0xFF9900, 0x55EEEE,
            0x447700, 0x6611FF, 0xEE0044, 0x0099FF,
            0xDDAA99, 0xCCDD00, 0x116677, 0x44EE88,
            0xFF11FF, 0x994411, 0xCCDD88, 0x5566FF,
            0xDDBBFF, 0x885577, 0xFF7799, 0xFF4400,
            0x44AA77, 0xAA8822, 0x55BB00, 0x77CCFF,
            0xFF0099, 0x666633, 0x9900BB, 0x6655AA,
            0xFF9966, 0xAA2266, 0xFFCC00, 0xBB88FF,
            0x885544, 0x7788BB, 0xAADDBB, 0xFFCC77,
            0x99AA22, 0x226644, 0xEE66FF, 0x33EEBB,
            0x55AAAA, 0xBB3344, 0xBB2211, 0x33AA44,
            0xFF11CC, 0xFF99DD, 0xAAEE66, 0x00EE55,
            0xDD6611, 0x99EE00, 0xAAAA77, 0x8844BB,
            0x0044FF, 0xFF6655, 0xEEDD55, 0xBB8855,
            0xBB22FF, 0x99EE99, 0xDD1166, 0xAA77BB,
            0x0066DD, 0xCC99BB, 0xCC7777, 0x007733
        };

        /// <summary>
        /// Подложка и «прочее» — не компоненты, а остаток: серый, вне палитры.
        /// Серого в палитре нет нарочно, поэтому спутать их не с чем.
        /// </summary>
        static readonly Color ContinuumColor = ColorFromHex(0xB0B7BD);

        static readonly Color OtherColor = ColorFromHex(0x9E9E9E);

        /// <summary>
        /// Раздать цвета слоям одного спектра. Считается один раз на разложение:
        /// цвет зависит от состава кадра (разрешение столкновений), поэтому
        /// спрашивать его послойно нельзя — ответ был бы разным у отрисовки и у
        /// легенды.
        /// </summary>
        public static Dictionary<string, Color> Assign(IEnumerable<string> names)
        {
            Dictionary<string, Color> map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            if (names == null)
            {
                return map;
            }

            HashSet<int> taken = new HashSet<int>();
            foreach (string name in names)
            {
                if (name == null || map.ContainsKey(name))
                {
                    continue;
                }

                if (string.Equals(name, FsaResult.ContinuumLayerName, StringComparison.OrdinalIgnoreCase))
                {
                    map[name] = ContinuumColor;
                    continue;
                }

                if (string.Equals(name, FsaResult.OtherLayerName, StringComparison.OrdinalIgnoreCase))
                {
                    map[name] = OtherColor;
                    continue;
                }

                int slot = Slot(name);
                int chosen = -1;

                // Минимального расстояния в наборе (ΔE 22) хватает, чтобы цвета
                // различались вообще, но мало для тонких соседних лент: хеш
                // способен посадить рядом два оранжевых. Поэтому место ищется с
                // требованием отстоять от уже занятых, и требование ослабляется
                // только если при нём свободного места не нашлось.
                foreach (double minDistance in Thresholds)
                {
                    for (int step = 0; step < Palette.Length && chosen < 0; step++)
                    {
                        int candidate = (slot + step) % Palette.Length;
                        if (taken.Contains(candidate) || !FarEnough(candidate, taken, minDistance))
                        {
                            continue;
                        }

                        chosen = candidate;
                    }

                    if (chosen >= 0)
                    {
                        break;
                    }
                }

                // Слоёв больше, чем цветов в палитре, быть не может: их число
                // ограничено сверху лимитом названных плюс горсткой мешающих.
                if (chosen < 0)
                {
                    chosen = slot;
                }

                taken.Add(chosen);
                map[name] = ColorFromHex(Palette[chosen]);
            }

            return map;
        }

        /// <summary>
        /// Требования к расстоянию, по убыванию строгости. Последнее — ноль:
        /// раздача обязана завершиться при любом числе слоёв.
        /// </summary>
        static readonly double[] Thresholds = { 35.0, 25.0, 0.0 };

        /// <summary>Палитра в CIELAB — считается один раз, при первом обращении.</summary>
        static readonly double[][] PaletteLab = BuildLab();

        static double[][] BuildLab()
        {
            double[][] lab = new double[Palette.Length][];
            for (int i = 0; i < Palette.Length; i++)
            {
                lab[i] = ToLab(Palette[i]);
            }

            return lab;
        }

        static bool FarEnough(int candidate, HashSet<int> taken, double minDistance)
        {
            if (minDistance <= 0.0)
            {
                return true;
            }

            double[] a = PaletteLab[candidate];
            foreach (int other in taken)
            {
                double[] b = PaletteLab[other];
                double dl = a[0] - b[0], da = a[1] - b[1], db = a[2] - b[2];
                if (dl * dl + da * da + db * db < minDistance * minDistance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>sRGB -> CIELAB (D65). Нужен только для сравнения цветов между собой.</summary>
        static double[] ToLab(int rgb)
        {
            double r = Linear(((rgb >> 16) & 0xFF) / 255.0);
            double g = Linear(((rgb >> 8) & 0xFF) / 255.0);
            double b = Linear((rgb & 0xFF) / 255.0);

            double x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047;
            double y = r * 0.2126729 + g * 0.7151522 + b * 0.0721750;
            double z = (r * 0.0193339 + g * 0.1191920 + b * 0.9503041) / 1.08883;

            double fx = F(x), fy = F(y), fz = F(z);
            return new[] { 116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz) };
        }

        static double Linear(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double F(double t)
        {
            return t > 216.0 / 24389.0
                ? Math.Pow(t, 1.0 / 3.0)
                : (841.0 / 108.0) * t + 4.0 / 29.0;
        }

        /// <summary>
        /// Место в палитре по имени. FNV-1a, а не <c>string.GetHashCode</c>:
        /// последний не обещает постоянства между запусками и версиями среды, а
        /// цвет нуклида обязан быть одним и тем же и завтра, и на чужой машине.
        /// </summary>
        static int Slot(string name)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < name.Length; i++)
                {
                    hash ^= char.ToUpperInvariant(name[i]);
                    hash *= 16777619u;
                }

                return (int)(hash % (uint)Palette.Length);
            }
        }

        /// <summary>Подпись слоя: мешающие образы называются по-человечески.</summary>
        public static string DisplayName(string component)
        {
            if (string.IsNullOrEmpty(component))
            {
                return "";
            }

            if (component.StartsWith("Xray-", StringComparison.OrdinalIgnoreCase))
            {
                return component.Substring(5);
            }

            if (string.Equals(component, FsaResult.ContinuumLayerName, StringComparison.Ordinal))
            {
                return Properties.Resources.FSALegendContinuum;
            }

            return component;
        }

        static Color ColorFromHex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
    }
}
