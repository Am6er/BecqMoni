using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            this.InitializeComponent();
            base.Icon = Resources.becqmoni;
            // `A10`. Русский перевод этой подписи лежал в паре («версия xxxx»
            // в `AboutForm.ru.resx`), но конструктор писал поверх английский
            // литерал — и в русском окне стояло «Version 1.x.x». Номер версии
            // подставляется в переведённый образец, а не приклеивается к
            // непереводимой приставке.
            this.label2.Text = string.Format(Resources.AboutVersionFormat,
                GlobalConfigManager.GetInstance().VersionString);
            this.textBox1.Text = Resources.LibraryLicensesMessage;
        }

        void button1_Click(object sender, EventArgs e)
        {
            base.Close();
        }

        private void label7_Click(object sender, EventArgs e)
        {
            OpenLink("https://github.com/Am6er/BecqMoni");
        }

        private void label8_Click(object sender, EventArgs e)
        {
            OpenLink("https://www.youtube.com/@Am6er");
        }

        private void label9_Click(object sender, EventArgs e)
        {
            OpenLink("https://rutube.ru/channel/30585350/");
        }

        /// <summary>
        /// Открыть ссылку внешним обработчиком и НАЗВАТЬ ПРИЧИНУ, если не
        /// вышло.
        ///
        /// ⛔ `A14`. Прежде каждый из трёх щелчков был обёрнут в
        /// <c>catch (Exception) { }</c> целиком: щёлкнул — не произошло
        /// ничего. Отличить «в системе нет обработчика http» от «промахнулся
        /// мимо подписи» было невозможно ни человеку за экраном, ни тому, кто
        /// разбирает жалобу: отказ есть, читателя у него нет.
        ///
        /// ⚠ Сообщение идёт через <see cref="AppUi.Report"/>, а не прямым
        /// <c>MessageBox.Show</c>: это единственная дверь дерева, и она же
        /// оставляет путь безоконным, если окон нет.
        /// </summary>
        void OpenLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppUi.Report(string.Format(Resources.ERROpenLinkFailure, url, ex.Message),
                    Resources.ErrorDialogTitle, MessageBoxIcon.Exclamation);
            }
        }
    }
}
