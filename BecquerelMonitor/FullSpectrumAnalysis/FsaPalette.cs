using System;
using System.Collections.Generic;
using System.Drawing;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Цвет закреплён за компонентом, а не за его рангом в конкретном спектре:
    /// картинки разных спектров должны читаться одной легендой. База — палитра
    /// Окабе—Ито (различима при дальтонизме), идентичность продублирована
    /// прямыми подписями у максимума слоя.
    /// </summary>
    public static class FsaPalette
    {
        static readonly Dictionary<string, Color> ByComponent = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "Th-232", ColorFromHex(0x0072B2) },   // синий
            { "Ra-226", ColorFromHex(0xD55E00) },   // киноварь
            { "U-238", ColorFromHex(0xE69F00) },    // оранжевый
            { "U-235", ColorFromHex(0x009E73) },    // зелёный
            { "K-40", ColorFromHex(0xF0E442) },     // жёлтый
            { "Cs-137", ColorFromHex(0xCC79A7) },   // розовый
            { "Am-241", ColorFromHex(0x6A3D9A) },
            { "Co-60", ColorFromHex(0x332288) },
            { "I-131", ColorFromHex(0x117733) },
            { "Eu-152", ColorFromHex(0x44AA99) },
            { "Ba-133", ColorFromHex(0x999933) },
            { "Lu-176", ColorFromHex(0x88CCEE) },
            { "Th-228", ColorFromHex(0x44AA99) },
            { "Xray-W", ColorFromHex(0x997700) },
            { "Xray-Pb", ColorFromHex(0x6B4E3D) },
            { "SE-2614", ColorFromHex(0xDDCC77) },
            { "DE-2614", ColorFromHex(0x805B3A) },
            { FsaResult.OtherLayerName, ColorFromHex(0x9E9E9E) },
            // подложка — нейтральный серый: она не компонент и не должна
            // конкурировать по цвету ни с одним из них
            { FsaResult.ContinuumLayerName, ColorFromHex(0xB0B7BD) }
        };

        static readonly Color[] Fallback =
        {
            ColorFromHex(0x0072B2), ColorFromHex(0xD55E00), ColorFromHex(0x009E73),
            ColorFromHex(0xCC79A7), ColorFromHex(0xE69F00), ColorFromHex(0x56B4E9)
        };

        public static Color ColorOf(string component, int fallbackIndex)
        {
            Color color;
            if (component != null && ByComponent.TryGetValue(component, out color))
            {
                return color;
            }

            return Fallback[Math.Abs(fallbackIndex) % Fallback.Length];
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
