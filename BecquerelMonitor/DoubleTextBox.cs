using BecquerelMonitor.Properties;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
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
            if (this.Text == "")
            {
                this.Text = "0";
            }
            double num;
            if (!double.TryParse(this.Text, out num))
            {
                MessageBox.Show(Resources.ERRInputNumber, Resources.InvalidValueDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                base.SelectAll();
                e.Cancel = true;
            }
        }

        // Token: 0x06000BEA RID: 3050 RVA: 0x00048020 File Offset: 0x00046220
        public double GetValue()
        {
            double result = 0.0;
            double.TryParse(this.Text, out result);
            return result;
        }
    }
}
