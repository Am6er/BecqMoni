using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000072 RID: 114
    [ToolboxBitmap(typeof(TextBox))]
    public class IntegerTextBox : TextBox
    {
        /// <summary>
        /// (П320, решение Amber 15.10.2026 «Вернуть прежнее») Последний текст поля,
        /// читавшийся целым, — то, что поле вернёт при уходе с нечислом. Устроено
        /// так же, как у <see cref="DoubleTextBox"/>: прежним становится текст,
        /// поставленный кодом, и текст, прошедший проверку ухода, — не набор.
        /// </summary>
        string lastNumberText;

        // Token: 0x060005C8 RID: 1480 RVA: 0x000253CC File Offset: 0x000235CC
        public IntegerTextBox()
        {
            base.Validating += this.IntegerTextBox_Validating;
        }

        /// <summary>Текст, поставленный кодом: целое становится «прежним».</summary>
        public override string Text
        {
            get
            {
                return base.Text;
            }
            set
            {
                base.Text = value;
                int ignored;
                if (UserNumber.TryParseInt(value, out ignored))
                {
                    this.lastNumberText = value;
                }
            }
        }

        // Token: 0x060005C9 RID: 1481 RVA: 0x000253E8 File Offset: 0x000235E8
        void IntegerTextBox_Validating(object sender, CancelEventArgs e)
        {
            if (this.Text == "")
            {
                this.Text = "0";
            }
            int num;
            // `A244`: разбор ввода человека — инвариантом, с уступкой культуре
            // системы (см. `UserNumber` в `DoubleTextBox.cs`).
            if (UserNumber.TryParseInt(this.Text, out num))
            {
                this.lastNumberText = this.Text;
                return;
            }

            // (П320, решение Amber 15.10.2026 «Вернуть прежнее») Нечисло при уходе
            // фокуса МОЛЧА заменяется прежним целым, фокус уходит — без окна
            // «Введите целое число» и без `e.Cancel`, которое запирало фокус: на
            // панели «Управление измерением» с нечислом в пределе времени не
            // переключались документы (П312 Н15). См. `DoubleTextBox_Validating`.
            this.Text = this.lastNumberText ?? "0";
        }

        // Token: 0x060005CA RID: 1482 RVA: 0x0002544C File Offset: 0x0002364C
        public int GetValue()
        {
            int result = 0;
            UserNumber.TryParseInt(this.Text, out result);
            return result;
        }
    }
}
