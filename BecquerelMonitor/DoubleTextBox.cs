using BecquerelMonitor.Properties;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    /// <summary>
    /// Разбор числа, НАБРАННОГО ЧЕЛОВЕКОМ в поле ввода (`A244`).
    ///
    /// ⛔ Печать чисел инвариантна — разделитель дробной части ВСЕГДА ТОЧКА
    /// (правило Amber 05.09.2026). Значит и читать поле надо инвариантом:
    /// напечатанное «0.5» на русской системе прежний `double.TryParse(text)`
    /// не брал вовсе, а `GetValue` возвращал молча НОЛЬ — не отказ, а другое
    /// число. У `decimal.Parse(text)` было хуже: его умолчание несёт
    /// `AllowThousands`, и «1.5» на русской системе становилось 15.
    ///
    /// Вторая попытка — по культуре системы: человек за русской клавиатурой
    /// набирает «0,5», и отказывать ему не за что. Это не подделка культуры —
    /// печатаем всегда одинаково, читаем терпимо.
    ///
    /// ⛔ Разделителя разрядов не допускает НИ ОДНА попытка (`NumberStyles.Float`
    /// его не несёт) — решение Amber 05.09.2026 «группировку убрать вовсе»,
    /// и без него «1.234» на немецкой системе молча стало бы тысячей.
    /// </summary>
    public static class UserNumber
    {
        public static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        public static bool TryParseInt(string text, out int value)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        }

        public static bool TryParseDecimal(string text, out decimal value)
        {
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        // Бросающие обёртки: там, где прежде стоял `T.Parse(text)`, отказ был
        // исключением, и на нём держится поведение вызывающих (форма
        // конфигурации отменяет сохранение, редактор ячейки — правку). Замена
        // на `TryParse` втихую превратила бы отказ в ноль.
        public static double ParseDouble(string text)
        {
            double value;
            if (!TryParseDouble(text, out value))
            {
                throw new FormatException(text + " is not a number");
            }
            return value;
        }

        public static int ParseInt(string text)
        {
            int value;
            if (!TryParseInt(text, out value))
            {
                throw new FormatException(text + " is not an integer");
            }
            return value;
        }

        public static decimal ParseDecimal(string text)
        {
            decimal value;
            if (!TryParseDecimal(text, out value))
            {
                throw new FormatException(text + " is not a number");
            }
            return value;
        }
    }

    // Token: 0x020000F5 RID: 245
    [ToolboxBitmap(typeof(TextBox))]
    public class DoubleTextBox : TextBox
    {
        // Token: 0x06000BE8 RID: 3048 RVA: 0x00047FA0 File Offset: 0x000461A0
        public DoubleTextBox()
        {
            base.Validating += this.DoubleTextBox_Validating;
        }

        // Token: 0x06000BE9 RID: 3049 RVA: 0x00047FBC File Offset: 0x000461BC
        void DoubleTextBox_Validating(object sender, CancelEventArgs e)
        {
            if (this != null && this.Text == "")
            {
                this.Text = "0";
            }
            double num;
            if (!UserNumber.TryParseDouble(this.Text, out num))
            {
                // ⛔ `A245`, полоса F20 05.09.2026. Голое модальное окно на
                //    безоконном пути: этот обработчик зовут отражением пробы
                //    (`CultureProbeO14`, раздел `A244P1P3`), и до правки
                //    сторона ОТКАЗА поля не мерилась вовсе — плечо обходило
                //    настоящий обработчик, потому что прогон повис бы на
                //    окне. Дверь маршалится сама (~~`A241`~~), а на потоке
                //    окон вызов остаётся синхронным: в приложении вид
                //    сообщения прежний — тот же текст, тот же заголовок,
                //    та же «ОК» и тот же знак.
                AppUi.Report(Resources.ERRInputNumber, Resources.InvalidValueDialogTitle, MessageBoxIcon.Exclamation);
                base.SelectAll();
                e.Cancel = true;
            }
        }

        // Token: 0x06000BEA RID: 3050 RVA: 0x00048020 File Offset: 0x00046220
        public double GetValue()
        {
            double result = 0.0;
            UserNumber.TryParseDouble(this.Text, out result);
            return result;
        }
    }
}
