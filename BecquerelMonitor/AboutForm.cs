using BecquerelMonitor.Properties;
using System;
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
            GuideLinks.OpenLink("https://github.com/Am6er/BecqMoni");
        }

        private void label8_Click(object sender, EventArgs e)
        {
            GuideLinks.OpenLink("https://www.youtube.com/@Am6er");
        }

        private void label9_Click(object sender, EventArgs e)
        {
            GuideLinks.OpenLink("https://rutube.ru/channel/30585350/");
        }

        // `AMBER250` (Amber 20.10.2026, вопросником: «Адрес сайта, как GitHub»): гид на сайте,
        // на языке интерфейса — ?lang=ru или ?lang=en.
        private void label10_Click(object sender, EventArgs e)
        {
            GuideLinks.OpenLink(GuideLinks.SiteWithLang);
        }
    }
}
