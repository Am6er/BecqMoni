using BecquerelMonitor;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FsaFlagsProbe
{
    /// <summary>
    /// Две галки разбора на панели поиска пиков и связь между ними (`S77`).
    ///
    /// ЗАЧЕМ ПРОБА. До 23.08.2026 в поставке обе галки стояли ровно в тех
    /// положениях, при которых видимая не делала НИЧЕГО: «Равновесие» включено
    /// умолчанием, «Enable DB lookups for FSA» — выключено, а связывать ряд
    /// можно только там, где ряд ЕСТЬ. Состав из баз собирает его обходом
    /// `nucdb.decay_chain`; прежний путь строит компоненты по подписям найденных
    /// пиков, и структуры ряда там нет вовсе.
    ///
    /// Решением Amber зависимость сделана ВИДИМОЙ: при выключенном выводе из
    /// баз «Равновесие» гаснет. Здесь у этого правила появляется читатель —
    /// иначе оно снова станет неотличимо от «галка просто не работает».
    ///
    /// Проверяется ЧЕТЫРЕ вещи, и третья не менее важна первых двух:
    ///
    ///   1. вывод из баз выключен -> «Равновесие» недоступно;
    ///   2. вывод из баз включён   -> «Равновесие» доступно;
    ///   3. ЗНАЧЕНИЕ «Равновесия» при гашении НЕ ТРОГАЕТСЯ — погашенная галка
    ///      помнит выбор человека и оживает вместе с соседней. Гасить и
    ///      обнулять — разные вещи, и второе потеряло бы настройку молча;
    ///   4. у погашенной галки есть подсказка, у доступной её нет.
    ///
    ///     fsaflagsprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            CheckBox lookups = Field(panel, "checkBoxDbLookups");
            CheckBox equilibrium = Field(panel, "checkBoxEquilibrium");

            Console.WriteLine("=== связь галок разбора (S77) ===");

            // Человек включил «Равновесие» при работающем выводе из баз.
            lookups.Checked = true;
            equilibrium.Checked = true;
            Same("вывод из баз включён: «Равновесие» доступно", true, equilibrium.Enabled);
            Same("подсказки у доступной галки нет", "", HintOf(panel, equilibrium));

            // Выключил вывод из баз — галка обязана погаснуть, но НЕ сброситься.
            lookups.Checked = false;
            Same("вывод из баз выключен: «Равновесие» недоступно", false, equilibrium.Enabled);
            Same("значение «Равновесия» не тронуто", true, equilibrium.Checked);
            bool hasHint = !string.IsNullOrEmpty(HintOf(panel, equilibrium));
            Same("у погашенной галки есть подсказка", true, hasHint);

            // Вернул — галка оживает с прежним значением.
            lookups.Checked = true;
            Same("вернули вывод из баз: «Равновесие» снова доступно", true, equilibrium.Enabled);
            Same("и помнит прежнее значение", true, equilibrium.Checked);

            // И обратный случай: выключенное «Равновесие» тоже переживает
            // гашение — правило про доступность, а не про значение.
            equilibrium.Checked = false;
            lookups.Checked = false;
            Same("выключенное «Равновесие» переживает гашение", false, equilibrium.Checked);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);

            // ⛔ ФОРМЫ УБИРАЮТСЯ ЯВНО, и это не косметика. Без этого процесс
            // печатал «ВСЕ СОШЛИСЬ» и падал уже ПОСЛЕ, на разборе окон:
            // 0xC000041D (STATUS_FATAL_USER_CALLBACK_EXCEPTION), код возврата
            // -1073740771 вместо нуля. Проба, чей текст говорит «сошлось», а
            // код — «упало», хуже молчащей: `build_all.ps1` этого не видит, а
            // тот, кто смотрит на код, читает отказ.
            panel.Dispose();
            mainForm.Dispose();
            return bad == 0 ? 0 : 1;
        }

        static CheckBox Field(DCPeakDetectionView panel, string name)
        {
            FieldInfo f = typeof(DCPeakDetectionView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                Console.WriteLine("  ⛔ поля «{0}» на панели НЕТ — проба смотрит не туда", name);
                bad++;
                return new CheckBox();
            }

            return (CheckBox)f.GetValue(panel);
        }

        /// <summary>
        /// Текст подсказки СЕЙЧАС. ⚠ Читать поле надо каждый раз: подсказка
        /// заводится лениво, при первом же вызове правила, и снимок, взятый до
        /// него, остался бы null навсегда — на этом проба уже оступилась.
        /// </summary>
        static string HintOf(DCPeakDetectionView panel, Control control)
        {
            FieldInfo f = typeof(DCPeakDetectionView).GetField(
                "fsaToolTip", BindingFlags.Instance | BindingFlags.NonPublic);
            ToolTip tip = f == null ? null : (ToolTip)f.GetValue(panel);
            return tip == null ? "" : tip.GetToolTip(control);
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-52} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
