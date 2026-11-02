using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    /// <summary>
    /// ⛔ (`AMBER283`, 02.11.2026) ОДНО ОКНО ГРАФИКОВ КАЛИБРОВКИ: ЭНЕРГИЯ И FWHM. Amber,
    /// консоль, дословно: «Графики смотрят в похожие классы, их можно объединить в одну
    /// кнопку а в самом открытом окне выбирать Energy/FWHM в combobox.»
    ///
    /// КАК УСТРОЕНО. Прежде у панели «Калибровка» было две кнопки и два окна:
    /// «График» (<see cref="CalibrationGraph"/>, шкала энергии по линиям) и «Просмотр ...»
    /// (<see cref="FWHMCalibrationGraph"/>, кривая FWHM по опорам). Оба класса стали
    /// ВИДАМИ (<c>UserControl</c>) и живут в этом окне; список вверху окна выбирает,
    /// какой из двух виден, и переключает без закрытия окна. Оба вида строятся панелью
    /// ДО показа окна (каждый — по своему снимку данных, как прежде) и живут, пока
    /// открыто окно: перетащенные точки и «Сохранить новые точки» одного вида
    /// переживают переключение на другой. Что сделано в окне, панель разбирает после
    /// его закрытия (<c>DCFwhmCalibrationView.ViewCalibrationButton_Click</c>).
    ///
    /// Вид, которого у спектра нет (у энергии — меньше двух линий с введённой энергией,
    /// у FWHM и функции пика — нет кривой), в списке остаётся: выбор показывает на его месте
    /// причину словами, а не пустое поле и не молча пропавший пункт.
    ///
    /// (`AMBER283`, второй заход; решение Amber вопросником 02.11.2026, дословно: «Только
    /// «Просмотр» → в «График» (Рекомендую)») Третий пункт — «Функция пика»: прежняя кнопка
    /// «Просмотр» панели открывала своё окно предпросмотра формы пика; теперь это вид того же
    /// окна, а под кривой — сравнение форм пика последнего расчёта (решение «Окно формы пика —
    /// по требованию»: прежде оно всплывало модальным окном на каждое «Выполнить калибровку»).
    /// </summary>
    public partial class CalibrationGraphWindow : Form
    {
        /// <summary>Пункт списка «Энергия».</summary>
        public const int EnergyView = 0;

        /// <summary>Пункт списка «FWHM».</summary>
        public const int FwhmView = 1;

        /// <summary>Пункт списка «Функция пика».</summary>
        public const int PeakShapeView = 2;

        readonly Control[] views = new Control[3];

        readonly string[] refusals = new string[3];

        /// <summary>Заголовок окна из resx — для вида, которого нет.</summary>
        readonly string windowTitle;

        public CalibrationGraphWindow()
        {
            InitializeComponent();
            ComponentResourceManager resources = new ComponentResourceManager(typeof(CalibrationGraphWindow));
            this.refusals[EnergyView] = resources.GetString("MSGEnergyGraphUnavailable") ?? string.Empty;
            this.refusals[FwhmView] = resources.GetString("MSGFwhmGraphUnavailable") ?? string.Empty;
            this.refusals[PeakShapeView] = resources.GetString("MSGPeakShapeUnavailable") ?? string.Empty;
            this.windowTitle = this.Text;
        }

        /// <param name="energyGraph">вид шкалы энергии, готовый к показу; null — его у спектра нет</param>
        /// <param name="fwhmGraph">вид кривой FWHM, готовый к показу; null — его у спектра нет</param>
        /// <param name="peakShapeView">вид функции пика, готовый к показу (его <c>Text</c> — заголовок окна); null — его нет</param>
        /// <param name="view">пункт, с которого окно открывается (<see cref="EnergyView"/> / <see cref="FwhmView"/> / <see cref="PeakShapeView"/>)</param>
        public CalibrationGraphWindow(MainForm mainForm, CalibrationGraph energyGraph, FWHMCalibrationGraph fwhmGraph, Control peakShapeView, int view)
            : this()
        {
            this.Icon = Resources.becqmoni;
            if (mainForm != null && mainForm.Width > 0 && mainForm.Height > 0)
            {
                // размер — как был у каждого из двух прежних окон
                this.Size = new Size(mainForm.Width * 3 / 4, mainForm.Height * 3 / 4);
            }
            this.Attach(EnergyView, energyGraph);
            this.Attach(FwhmView, fwhmGraph);
            this.Attach(PeakShapeView, peakShapeView);
            this.SelectedView = view;
        }

        void Attach(int index, Control view)
        {
            if (view == null)
            {
                return;
            }
            view.Dock = DockStyle.Fill;
            view.Visible = false;
            this.graphPanel.Controls.Add(view);
            this.views[index] = view;
        }

        /// <summary>Пункт, выбранный в списке (<see cref="EnergyView"/> / <see cref="FwhmView"/> / <see cref="PeakShapeView"/>).</summary>
        public int SelectedView
        {
            get
            {
                return this.viewComboBox.SelectedIndex;
            }
            set
            {
                int index = value >= EnergyView && value < this.views.Length ? value : EnergyView;
                if (this.viewComboBox.SelectedIndex != index)
                {
                    this.viewComboBox.SelectedIndex = index;
                }
                else
                {
                    this.ShowView(index);
                }
            }
        }

        void ViewComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.ShowView(this.viewComboBox.SelectedIndex);
        }

        void ShowView(int index)
        {
            if (index < 0 || index >= this.views.Length)
            {
                return;
            }
            Control shown = this.views[index];
            for (int i = 0; i < this.views.Length; i++)
            {
                if (this.views[i] != null)
                {
                    this.views[i].Visible = i == index;
                }
            }
            this.unavailableLabel.Visible = shown == null;
            if (shown == null)
            {
                this.unavailableLabel.Text = this.refusals[index];
                this.Text = this.windowTitle;
                return;
            }
            this.Text = string.IsNullOrEmpty(shown.Text) ? this.windowTitle : shown.Text;
            shown.Invalidate();
        }
    }
}
