using BecquerelMonitor.Properties;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000072 RID: 114
    [ToolboxBitmap(typeof(TextBox))]
    public class IntegerTextBox : TextBox
    {
        // Token: 0x060005C8 RID: 1480 RVA: 0x000253CC File Offset: 0x000235CC
        public IntegerTextBox()
        {
            base.Validating += this.IntegerTextBox_Validating;
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
            if (!UserNumber.TryParseInt(this.Text, out num))
            {
                // ⛔ `A245`, полоса F20 05.09.2026 — то же, что в
                //    `DoubleTextBox_Validating`: голое модальное окно на
                //    безоконном пути. Сторож `check_headless.py` этого места
                //    не называл только потому, что до него не дотягивалась ни
                //    одна проба; чинится вместе с соседом, иначе одно поле
                //    ввода из двух осталось бы с окном, а разницы между ними
                //    нет никакой.
                AppUi.Report(Resources.ERRInputInteger, Resources.InvalidValueDialogTitle, MessageBoxIcon.Exclamation);
                base.SelectAll();
                e.Cancel = true;
            }
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
