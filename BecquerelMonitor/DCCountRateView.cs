using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{

    public partial class DCCountRateView : ToolWindow
    {

        public DCCountRateView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
            // `A319`: мёртвое время считается двумя способами (на окне по τ прибора
            // или по всему спектру, когда τ = 0) — подсказка говорит, каким.
            // Контейнер разбирает Dispose из Designer.cs.
            this.components = new System.ComponentModel.Container();
            ToolTip deadTimeHint = new ToolTip(this.components);
            string hint = WrapWords(Resources.CountRateDeadTimeHint, 80);
            deadTimeHint.SetToolTip(this.DeadTimeLbl, hint);
            deadTimeHint.SetToolTip(this.DeadTimeValLbl, hint);
        }

        /// <summary>
        /// Перенос по словам на строки не длиннее <paramref name="width"/> знаков —
        /// подсказка WinForms сама не переносит и тянется во весь экран (проверено
        /// экраном 13.10.2026). Копия <c>MainForm.WrapWords</c>: та закрыта в MainForm.
        /// </summary>
        static string WrapWords(string text, int width)
        {
            var result = new System.Text.StringBuilder();
            int line = 0;
            foreach (string word in text.Split(' '))
            {
                if (line > 0 && line + 1 + word.Length > width)
                {
                    result.Append(Environment.NewLine);
                    line = 0;
                }
                else if (line > 0)
                {
                    result.Append(' ');
                    line++;
                }

                result.Append(word);
                line += word.Length;
            }

            return result.ToString();
        }

        public double getWindow()
        {
            return (double)this.windowControl.Value;
        } 

        
        public void UpdateInfo(double Cps, int sEffRatio, double deadTime)
        {
            this.LossCountsRatioValLbl.Text = sEffRatio.ToString(CultureInfo.InvariantCulture);
            this.cpslabel.Text = Cps.ToString("f2", CultureInfo.InvariantCulture);
            if (deadTime <= 20.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.Black;
            }
            else if (deadTime > 20.0 && deadTime <= 50.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.DarkOrange;
            }
            else if (deadTime > 50.0)
            {
                this.DeadTimeValLbl.ForeColor = Color.DarkRed;
            }
            this.DeadTimeValLbl.BackColor = this.DeadTimeValLbl.BackColor;
            this.DeadTimeValLbl.Text = deadTime.ToString("f4", CultureInfo.InvariantCulture);
        }

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        MainForm mainForm;
    }
}
