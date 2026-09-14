using System;
using System.Globalization;
using System.Media;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    /// <summary>
    /// ПОЛЕ СО СТРЕЛКАМИ, У КОТОРОГО РАЗДЕЛИТЕЛЬ ДРОБНОЙ ЧАСТИ ВСЕГДА ТОЧКА
    /// (`A261`, решение Amber 06.09.2026 «лечить все 36»).
    ///
    /// ⛔ Штатный <see cref="NumericUpDown"/> печатает и разбирает своё
    /// содержимое КУЛЬТУРОЙ ПОТОКА, внутри себя, и обойти это снаружи нельзя:
    /// подмена мест <c>ToString</c>/<c>Parse</c> в форме до контрола не
    /// достаёт. Под <c>ru-RU</c> человек видел «0,50» — приказ Amber
    /// 05.09.2026 («разделитель дробной части ВСЕГДА ТОЧКА, никаких других
    /// вариантов быть не должно в принципе») на этих полях не выполнялся.
    ///
    /// ⚠ Лечить культурой потока НЕЛЬЗЯ: <c>CurrentCulture</c> управляет ещё и
    /// разбором всего остального, и файл, записанный с точкой и прочитанный с
    /// запятой, даёт не отказ, а другое число, тихо (~~<c>A242</c>~~). Поэтому
    /// лечение здесь — на стороне КОНТРОЛА.
    ///
    /// Культуру база трогает в ЧЕТЫРЁХ местах, и переопределены все четыре —
    /// правки трёх из них не хватило бы, а какой именно не хватает, видно не
    /// сразу:
    ///
    ///   1. <see cref="UpdateEditText"/> — печать (<c>value.ToString("F2",
    ///      CurrentCulture)</c> внутри закрытого <c>GetNumberText</c>);
    ///   2. <see cref="ValidateEditText"/> — разбор по уходу и по «Ввод»
    ///      (<c>Decimal.Parse(Text, CurrentCulture)</c> внутри ЗАКРЫТОГО
    ///      <c>ParseEditText</c>, которого переопределить нельзя вовсе);
    ///   3. <see cref="UpButton"/>/<see cref="DownButton"/> — стрелки зовут тот
    ///      же закрытый разбор, и набранное «0.5» терялось бы при первом же
    ///      нажатии стрелки;
    ///   4. <see cref="OnTextBoxKeyPress"/> — фильтр вводимых знаков пропускает
    ///      разделитель ТЕКУЩЕЙ культуры, то есть на русской системе СЪЕДАЕТ
    ///      набранную точку с писком: до разбора дело не доходило вовсе.
    ///
    /// ⛔ РАЗБОР — общий домашний <see cref="UserNumber"/> (~~<c>A244</c>~~), а
    /// не свой: сперва инвариантом, потом культурой системы. Печатаем всегда
    /// одинаково, читаем терпимо — человек за русской клавиатурой вправе
    /// набрать «0,5», и отказывать ему не за что; на экране он всё равно
    /// увидит «0.5». Разделителя РАЗРЯДОВ не допускает ни одна попытка
    /// (<c>NumberStyles.Float</c> его не несёт) — решение Amber 05.09.2026
    /// «группировки разрядов нет вовсе»; без этого «1.234» на немецкой системе
    /// молча стало бы тысячей.
    ///
    /// ⚠ ЧЕГО ЗДЕСЬ НАРОЧНО НЕТ. База в <c>UpdateEditText</c> молчит, пока идёт
    /// <c>BeginInit</c>/<c>EndInit</c>, — её признак <c>initializing</c> закрыт
    /// и отражением сюда не тянется. Ранняя печать безвредна: <c>EndInit</c>
    /// печатает поле заново, и значение в нём то же самое. Проверено замером —
    /// столбец <c>en-US</c> у всех полей девяти форм совпал знак в знак с
    /// прежним.
    /// </summary>
    public class InvariantNumericUpDown : NumericUpDown
    {
        /// <summary>ПЕЧАТЬ: «F&lt;разряды&gt;» инвариантной культурой.</summary>
        protected override void UpdateEditText()
        {
            // Шестнадцатеричный вид культуры не касается вовсе — отдаём базе.
            if (this.Hexadecimal)
            {
                base.UpdateEditText();
                return;
            }

            if (this.UserEdit)
            {
                this.ParseInvariant();
            }

            // ⚠ Единственное, что оставлено от базы дословно: пока человек
            //   стёр поле или набрал один минус, печатать поверх него нельзя —
            //   иначе минус исчезал бы прямо под пальцами.
            string current = this.Text;
            if ((this.Focused || this.ContainsFocus)
                && (string.IsNullOrEmpty(current) || current == "-"))
            {
                return;
            }

            string text = this.Value.ToString(
                (this.ThousandsSeparator ? "N" : "F")
                    + this.DecimalPlaces.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);

            this.ChangingText = true;
            this.Text = text;
            // Признак снимает обработчик TextChanged; если текст не изменился,
            // события нет, и признак остался бы поднятым — тогда следующая
            // правка человека считалась бы за нашу собственную.
            this.ChangingText = false;
        }

        /// <summary>РАЗБОР по «Ввод», по уходу из поля и по чтению значения.</summary>
        protected override void ValidateEditText()
        {
            this.ParseInvariant();
            this.UpdateEditText();
        }

        /// <summary>СТРЕЛКА ВВЕРХ: набранное разбирается ДО базы.</summary>
        public override void UpButton()
        {
            if (this.UserEdit)
            {
                this.ParseInvariant();
            }
            base.UpButton();
        }

        /// <summary>СТРЕЛКА ВНИЗ: то же самое.</summary>
        public override void DownButton()
        {
            if (this.UserEdit)
            {
                this.ParseInvariant();
            }
            base.DownButton();
        }

        /// <summary>
        /// ФИЛЬТР ВВОДИМЫХ ЗНАКОВ. База пропускает разделитель дробной части,
        /// разделитель разрядов и знак минуса ТЕКУЩЕЙ культуры; здесь вместо
        /// этого — цифры, ТОЧКА всегда, разделитель культуры системы (чтобы
        /// русская запятая набиралась и переводилась в точку при печати) и
        /// минус. Разделитель разрядов не пропускается: группировки нет вовсе.
        /// </summary>
        protected override void OnTextBoxKeyPress(object source, KeyPressEventArgs e)
        {
            if (this.Hexadecimal)
            {
                base.OnTextBoxKeyPress(source, e);
                return;
            }

            // Ровно то, что делает первой строкой сама база (UpDownBase):
            // событие KeyPress самого поля поднимается до всякой проверки.
            this.OnKeyPress(e);

            char c = e.KeyChar;
            NumberFormatInfo system = CultureInfo.CurrentCulture.NumberFormat;

            if (char.IsDigit(c)) return;
            if (c == '.') return;
            if (c == '-') return;
            if (system.NumberDecimalSeparator.Length == 1
                && c == system.NumberDecimalSeparator[0]) return;
            if (system.NegativeSign.Length == 1 && c == system.NegativeSign[0]) return;
            if (c == '\b') return;
            if ((Control.ModifierKeys & (Keys.Control | Keys.Alt)) != 0) return;

            e.Handled = true;
            try { SystemSounds.Beep.Play(); }
            catch (Exception) { }
        }

        /// <summary>
        /// Разбор набранного человеком — домашним <see cref="UserNumber"/>.
        /// Повторяет закрытый <c>ParseEditText</c> базы во всём остальном:
        /// пустое поле и один минус не трогают значения, выход за края
        /// подрезается, признак «правил человек» снимается в любом исходе.
        /// </summary>
        void ParseInvariant()
        {
            try
            {
                string text = this.Text;
                if (string.IsNullOrEmpty(text) || text == "-")
                {
                    return;
                }

                if (this.Hexadecimal)
                {
                    this.Value = this.Constrain(Convert.ToDecimal(Convert.ToInt32(text, 16)));
                    return;
                }

                decimal parsed;
                if (UserNumber.TryParseDecimal(text, out parsed))
                {
                    this.Value = this.Constrain(parsed);
                }
            }
            catch (Exception)
            {
                // Разбор человеческого ввода отказывать наружу не вправе —
                // так же молчит и база: значение остаётся прежним.
            }
            finally
            {
                this.UserEdit = false;
            }
        }

        decimal Constrain(decimal value)
        {
            if (value < this.Minimum) return this.Minimum;
            if (value > this.Maximum) return this.Maximum;
            return value;
        }

        public override string ToString()
        {
            return base.GetType().FullName
                + ", Minimum = " + this.Minimum.ToString(CultureInfo.InvariantCulture)
                + ", Maximum = " + this.Maximum.ToString(CultureInfo.InvariantCulture);
        }
    }
}
