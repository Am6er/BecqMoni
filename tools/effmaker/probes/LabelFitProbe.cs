using BecquerelMonitor;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LabelFitProbe
{
    /// <summary>
    /// ВЛЕЗАЕТ ЛИ РУССКАЯ НАДПИСЬ В ОТВЕДЁННУЮ ЕЙ ШИРИНУ (`A166`).
    ///
    ///     labelfitprobe
    ///
    /// Речь о <c>labelDetectionFailed</c> вида <c>DCPeakDetectionView</c> —
    /// надписи «поиск пиков не выполнен». Ширина у неё закреплена (332), а
    /// <c>AutoEllipsis</c> поднят: перевод, который не влез, обрывается
    /// многоточием МОЛЧА, и увидеть это можно только замером.
    ///
    /// ⛔ ЧИТАЕТСЯ СОБРАННЫЙ САТЕЛЛИТ, А НЕ ИСХОДНЫЙ `.resx`. Раскладку берёт
    /// тот же <see cref="ComponentResourceManager"/>, которым пользуется сам
    /// designer-код, и тем же вызовом <c>ApplyResources</c>; текст надписи —
    /// <c>ResourceManager</c> по <c>BecquerelMonitor.Properties.Resources</c>.
    /// Значит проба судит о том, что ДЕЙСТВИТЕЛЬНО собралось в
    /// <c>ru\BecquerelMonitor.resources.dll</c>, а не о том, что написано в
    /// дереве.
    ///
    /// ⚠ Окон проба НЕ открывает: надпись меряется вне формы, тем же
    /// <see cref="TextRenderer"/>, которым её рисует <c>Label</c>
    /// (<c>UseCompatibleTextRendering</c> печатается, чтобы это было видно, а
    /// не предполагалось).
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ обязателен: замер, который «проходит» всегда,
    /// не мерит ничего. Плечо <c>контроль</c> берёт заведомо длинный текст, и
    /// он ОБЯЗАН не влезть.
    ///
    /// Коды возврата: 0 — все плечи сошлись; 1 — не сошлись; 2 — не нашлось
    /// того, что мерить (ресурсы, шрифт).
    /// </summary>
    static class Program
    {
        const string Label = "labelDetectionFailed";
        const string TextKey = "PeakDetectionFailed";

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ ТОТ ЖЕ РЕЖИМ ОТРИСОВКИ, ЧТО У ПРИЛОЖЕНИЯ, и это не украшение:
            // `Program.Main` зовёт `SetCompatibleTextRenderingDefault(false)`,
            // то есть надпись рисуется GDI (`TextRenderer`), а не GDI+. Без
            // этой строки проба мерила бы `TextRenderer`-ом надпись, которая у
            // неё самой рисуется по-другому, — и числа относились бы к разным
            // вещам. Поймано на себе: первый прогон печатал
            // `UseCompatibleTextRendering: True`.
            Application.SetCompatibleTextRenderingDefault(false);

            // Ширину можно навязать ключом. Нужно это для плеча ДО, где
            // раскладки в ресурсах ещё НЕТ: там `ApplyResources` оставляет
            // надписи её собственные 100 пкс, и вопрос «влезает ли перевод в
            // отведённые 332» без ключа не задать вовсе.
            int forced = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("--width=", StringComparison.Ordinal))
                {
                    if (!int.TryParse(a.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out forced))
                    {
                        Console.Error.WriteLine("не число: " + a);
                        return 3;
                    }
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 3;
                }
            }

            var view = new ComponentResourceManager(typeof(DCPeakDetectionView));
            var strings = new ResourceManager("BecquerelMonitor.Properties.Resources",
                                              typeof(DCPeakDetectionView).Assembly);

            Console.WriteLine("=== надпись об отказе поиска пиков: раскладка и ширина ===");

            // --- 1. четыре свойства ОБЯЗАНЫ лежать в ресурсах, а не в коде ----
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            string[] wanted = { "Location", "Size", "Anchor", "ForeColor" };
            int missing = 0;
            Console.WriteLine();
            Console.WriteLine("--- раскладка в НЕЙТРАЛЬНЫХ ресурсах ---");
            foreach (string name in wanted)
            {
                object value = null;
                try { value = view.GetObject(Label + "." + name); }
                catch (Exception error) { Console.WriteLine("  {0,-10} ОШИБКА {1}", name, error.GetType().Name); }
                if (value == null) { Console.WriteLine("  {0,-10} В РЕСУРСАХ НЕТ (лежит в designer-коде)", name); missing++; }
                else Console.WriteLine("  {0,-10} {1}", name, value);
            }

            Font font = null;
            try { font = (Font)view.GetObject("$this.Font"); }
            catch (Exception) { }
            if (font == null)
            {
                Console.Error.WriteLine("нет $this.Font в ресурсах вида — мерить нечем");
                return 2;
            }
            Console.WriteLine("  шрифт вида: {0}, {1}pt", font.Name, font.SizeInPoints);

            // --- 2. замер по культурам ---------------------------------------
            int bad = 0;
            bad += Fit(view, strings, font, "en", false, forced);
            bad += Fit(view, strings, font, "ru", false, forced);
            // ⛔ Контроль — то же плечо ru, но с заведомо длинным текстом.
            bad += Fit(view, strings, font, "ru", true, forced);

            Console.WriteLine();
            if (missing != 0)
            {
                Console.WriteLine("НЕ СОШЛОСЬ: {0} свойств раскладки нет в ресурсах — перевод их не перекроет", missing);
            }
            if (bad != 0 || missing != 0)
            {
                Console.WriteLine("НЕ СОШЛОСЬ: {0}", bad + missing);
                return 1;
            }
            Console.WriteLine("ВСЕ СОШЛИСЬ: русская надпись влезает, раскладка перекрываема, контроль отказал");
            return 0;
        }

        /// <summary>
        /// Одно плечо: культура ставится, раскладка накладывается настоящим
        /// <c>ApplyResources</c>, текст берётся из собранных ресурсов той же
        /// культуры, ширина меряется тем же <c>TextRenderer</c>.
        /// </summary>
        static int Fit(ComponentResourceManager view, ResourceManager strings, Font viewFont,
                       string culture, bool control, int forcedWidth)
        {
            CultureInfo ui = CultureInfo.GetCultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = ui;

            var label = new Label();
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Font = viewFont;
            view.ApplyResources(label, Label);

            // Свидетель того, что сателлит ДЕЙСТВИТЕЛЬНО читается: подпись
            // соседа переведена, и в нейтральных ресурсах она другая.
            var witness = new Label();
            view.ApplyResources(witness, "label4");

            string text = strings.GetString(TextKey, ui) ?? "";
            if (control)
            {
                text = text + " " + text;
            }

            Size needPad = TextRenderer.MeasureText(text, label.Font);
            Size needNoPad = TextRenderer.MeasureText(text, label.Font, new Size(int.MaxValue, int.MaxValue),
                                                     TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            int width = forcedWidth > 0 ? forcedWidth : label.Width;

            Console.WriteLine();
            Console.WriteLine("--- культура {0}{1} ---", culture, control ? ", ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (текст удвоен)" : "");
            Console.WriteLine("  сосед label4: «{0}»", witness.Text);
            Console.WriteLine("  надпись     : «{0}» ({1} знаков)", text, text.Length);
            Console.WriteLine("  ширина надписи: {0} пкс ({1}), место {2}", width,
                              forcedWidth > 0 ? "навязана ключом --width" : "Size из ресурсов", label.Location);
            Console.WriteLine("  нужно текста: {0} пкс с полями, {1} пкс без полей", needPad.Width, needNoPad.Width);
            Console.WriteLine("  UseCompatibleTextRendering: {0}", label.UseCompatibleTextRendering);

            bool fits = needPad.Width <= width;
            int spare = width - needPad.Width;
            if (control)
            {
                if (fits)
                {
                    Console.WriteLine("  ⛔ КОНТРОЛЬ ПРОВАЛЕН: заведомо длинный текст «влез», значит замер не мерит");
                    return 1;
                }
                Console.WriteLine("  контроль: не влезает, не хватает {0} пкс — замер работает", -spare);
                return 0;
            }

            if (!fits)
            {
                Console.WriteLine("  ⛔ НЕ ВЛЕЗАЕТ: не хватает {0} пкс, надпись оборвётся многоточием", -spare);
                return 1;
            }
            Console.WriteLine("  влезает, запас {0} пкс", spare);
            return 0;
        }
    }
}
