using System;
using System.Globalization;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    public partial class ChanNumberChangeDialog : Form
    {
        MainForm mainForm;
        bool resultStatus = false;

        public ChanNumberChangeDialog()
        {
            InitializeComponent();
        }

        public ChanNumberChangeDialog(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = (MainForm)mainForm;
            this.Icon = Resources.becqmoni;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.resultStatus = false;
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.TryAccept();
        }

        public int SendData()
        {
            if (UserNumber.TryParseInt(textBox1.Text, out int newChan))
            {
                if (resultStatus)
                {
                    return newChan;
                }
            }
            return 0;
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                this.TryAccept();
            }
        }

        /// <summary>
        /// (П322, решение Amber 15.10.2026 «64…65536 (Рекомендую)») «Ввод» закрывает окно
        /// ТОЛЬКО с числом каналов в границах <see cref="ChannelCountLimits"/>. Прежде окно
        /// закрывалось с любым текстом: нечисло и пустое поле молча отменяли пункт меню, −5
        /// давало отказ уже ПОСЛЕ закрытия (исправить число было негде), а 2000000000 ронял
        /// приложение нехваткой памяти на новом документе (П312 Н5). Теперь отказ — словами,
        /// окно остаётся открытым, введённое выделено для исправления.
        /// </summary>
        void TryAccept()
        {
            int newChan;
            if (!UserNumber.TryParseInt(this.textBox1.Text, out newChan) || !ChannelCountLimits.Accepts(newChan))
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRDeviceConfigChannelsNotPositive,
                                           this.textBox1.Text, ChannelCountLimits.Min, ChannelCountLimits.Max),
                             Resources.InvalidValueDialogTitle, MessageBoxIcon.Exclamation);
                this.resultStatus = false;
                this.textBox1.Focus();
                this.textBox1.SelectAll();
                return;
            }
            this.resultStatus = true;
            Close();
        }
    }

    /// <summary>
    /// (П322, решение Amber 15.10.2026 «64…65536 (Рекомендую)») Границы числа каналов,
    /// которое вводит человек: конфигурация прибора (<c>DeviceConfigForm</c>, «Основное»)
    /// и «Изменить число каналов» (<see cref="ChanNumberChangeDialog"/>). Правило одно и
    /// живёт здесь; копия «64 … 65536» в третьем месте разошлась бы с этими двумя молча.
    ///
    /// Низ 64 — прежний порог «Изменить число каналов» (<c>MainForm.ConcatSpectrumsStripMenuItem_Click</c>);
    /// верх 65536 — с запасом над наибольшим числом каналов у приборов и в корпусе (16384).
    /// </summary>
    public static class ChannelCountLimits
    {
        public const int Min = 64;

        public const int Max = 65536;

        public static bool Accepts(int channels)
        {
            return channels >= Min && channels <= Max;
        }
    }
}
