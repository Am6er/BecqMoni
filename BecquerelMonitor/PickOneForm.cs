using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// «Не хватает вот этого — чем заменим?»: заголовок, объяснение, список,
    /// «Выбрать» и «Отмена».
    ///
    /// Заведено вместо двух прежних способов обойтись без недостающего.
    /// Первый — молча подставить умолчание: калибровку ПШПВ по трём общим
    /// числам (15 и 103 канала на 3756-м) или разложение без кривой
    /// эффективности. Числа при этом получаются правдоподобные и чужие, а
    /// сказать об этом некому. Второй — отказать: спектр просто не идёт в
    /// работу, и человек, у которого прибор переименован, ничего сделать не
    /// может.
    ///
    /// Форма собирается кодом, без Designer: полей на ней три, а resx на
    /// каждую такую мелочь — это ещё два файла, которые придётся держать
    /// согласованными.
    /// </summary>
    public sealed class PickOneForm : Form
    {
        readonly ComboBox choices = new ComboBox();

        PickOneForm(string title, string question, IEnumerable<object> items, object selected)
        {
            this.Text = title;
            this.Icon = Resources.becqmoni;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            // Высота — по тексту вопроса, а не постоянная: у одного вопроса
            // строка, у другого пять, и обрезанный на середине вопрос — ровно
            // то, чего эта форма и заведена не допускать.
            const int Margin = 12;
            const int Width = 436;
            int textHeight = Math.Max(32, TextRenderer.MeasureText(question,
                SystemFonts.DefaultFont, new Size(Width, 0), TextFormatFlags.WordBreak).Height + 4);

            Label text = new Label
            {
                Text = question,
                Location = new Point(Margin, Margin),
                Size = new Size(Width, textHeight),
                AutoSize = false,
            };

            int comboTop = Margin + textHeight + 8;
            int buttonsTop = comboTop + 21 + 12;
            this.ClientSize = new Size(Width + 2 * Margin, buttonsTop + 23 + Margin);

            this.choices.DropDownStyle = ComboBoxStyle.DropDownList;
            this.choices.Location = new Point(Margin, comboTop);
            this.choices.Size = new Size(Width, 21);
            foreach (object item in items)
            {
                this.choices.Items.Add(item);
            }

            if (selected != null)
            {
                this.choices.SelectedItem = selected;
            }

            if (this.choices.SelectedIndex < 0 && this.choices.Items.Count > 0)
            {
                this.choices.SelectedIndex = 0;
            }

            Button ok = new Button
            {
                Text = Resources.PickOneAccept,
                DialogResult = DialogResult.OK,
                Location = new Point(Margin + Width - 156, buttonsTop),
                Size = new Size(75, 23),
                UseVisualStyleBackColor = true,
            };

            Button cancel = new Button
            {
                Text = Resources.PickOneCancel,
                DialogResult = DialogResult.Cancel,
                Location = new Point(Margin + Width - 75, buttonsTop),
                Size = new Size(75, 23),
                UseVisualStyleBackColor = true,
            };

            this.Controls.Add(text);
            this.Controls.Add(this.choices);
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.AcceptButton = ok;
            this.CancelButton = cancel;

            // Пустой список означает, что выбирать не из чего: кнопка «Выбрать»
            // в этом случае обманывала бы.
            ok.Enabled = this.choices.Items.Count > 0;
        }

        /// <summary>
        /// Спросить и вернуть выбранное. null — отказались либо выбирать было
        /// не из чего. Вызывающий обязан этот null обработать: «отмена» здесь
        /// значит «оставить как есть», а не «взять первое попавшееся».
        /// </summary>
        public static object Ask(IWin32Window owner, string title, string question,
                                 IEnumerable<object> items, object selected)
        {
            using (PickOneForm form = new PickOneForm(title, question, items, selected))
            {
                return form.ShowDialog(owner) == DialogResult.OK
                    ? form.choices.SelectedItem
                    : null;
            }
        }
    }
}
