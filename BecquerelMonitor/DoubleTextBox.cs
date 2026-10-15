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
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return false;
            }

            // (`AMBER201`, подозрение полосы 3, 05.10.2026) Человек вводит ЧИСЛО:
            // «NaN», «Infinity», «-Infinity» (и «∞» по культуре системы) разбор
            // принимает как значения, и поле калибровки или порога отдавало их
            // дальше — в XML уходило «NaN», в счёт — бесконечность. Это отказ,
            // как у любого нечитаемого текста.
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0.0;
                return false;
            }
            return true;
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
        /// <summary>
        /// (П320, решение Amber 15.10.2026 вопросником, дословно: «Вернуть прежнее
        /// (Рекомендую)»; находки П312 Н14/Н15) Последний текст поля, читавшийся
        /// числом, — то, что поле вернёт, если человек уйдёт с него с нечислом.
        /// Прежним считается текст, поставленный КОДОМ (загрузка формы, смена
        /// документа, подстановка порога), и текст, прошедший проверку ухода.
        /// Набор человека (клавиатура, Ctrl+V — мимо свойства <see cref="Text"/>)
        /// прежним не становится, пока не пройдёт проверку: промежуточное «12» по
        /// дороге к «12x» прежним значением не является.
        /// </summary>
        string lastNumberText;

        // Token: 0x06000BE8 RID: 3048 RVA: 0x00047FA0 File Offset: 0x000461A0
        public DoubleTextBox()
        {
            base.Validating += this.DoubleTextBox_Validating;
        }

        /// <summary>Текст, поставленный кодом: число становится «прежним».</summary>
        public override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                base.Text = value;
                double ignored;
                if (UserNumber.TryParseDouble(value, out ignored))
                {
                    this.lastNumberText = value;
                }
            }
        }

        // Token: 0x06000BE9 RID: 3049 RVA: 0x00047FBC File Offset: 0x000461BC
        void DoubleTextBox_Validating(object sender, CancelEventArgs e)
        {
            if (this.Text == "")
            {
                this.Text = "0";
            }
            double num;
            if (UserNumber.TryParseDouble(this.Text, out num))
            {
                this.lastNumberText = this.Text;
                return;
            }

            // (П320, решение Amber 15.10.2026 «Вернуть прежнее») Нечисло при уходе
            // фокуса МОЛЧА заменяется прежним числом, фокус уходит. Прежде здесь
            // поднималось окно «Введите число» и ставилось `e.Cancel = true`: фокус
            // оставался в поле, а вместе с ним запиралось окно — в «Настройках» не
            // закрывалась «Отмена» (Н14), на панели «Управление измерением» не
            // переключались документы (Н15). Запись нечисла не пропускает и теперь:
            // окно прибора перед записью проверяет все поля (`FieldsValid`), и поле с
            // нечислом к записи уже держит прежнее число.
            this.Text = this.lastNumberText ?? "0";
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
