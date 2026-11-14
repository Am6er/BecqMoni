using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using XPTable.Editors;
using XPTable.Models;

namespace BecquerelMonitor
{
    /// <summary>
    /// ⛔ (`AMBER276`, 29.10.2026) ЕДИНАЯ ВКЛАДКА КАЛИБРОВКИ: ЭНЕРГИЯ И FWHM — ОДИН
    /// ПРОЦЕСС И ОДНА ТАБЛИЦА. Amber, консоль, дословно: «Объединить калибровки по
    /// энергии и FWHM в один процесс на одной вкладке, сейчас их две. Выбор пика для
    /// линии калибровки делать как сейчас это делается в FWHM. Таблица калибровки -
    /// единая. В файл спектра помимо калибровки fwhm теперь попадает и калибровка по
    /// энергии.» Вопросником того же дня, дословно: «Строка идёт в обе подгонки» и
    /// «Всё из обеих (Рекомендую)» — ни одна прежняя возможность двух вкладок не
    /// пропадает: ручной ввод коэффициентов полинома, веса, «собрать всё», графики
    /// обеих калибровок, вид кривой FWHM, форма пика, сохранение в прибор (с кривизной
    /// тракта, `AMBER271`). Автокалибровка с её настройкой снята вместе со
    /// стабилизатором пиков (`AMBER280`).
    ///
    /// КАК УСТРОЕНО.
    ///   * Пик берётся выделением на графике (событие <c>PeakPickuped</c>, как прежде у
    ///     FWHM): центроид, FWHM и статистики формы пика (<c>CalcPeakFitValues</c>).
    ///     Второй вход — «Добавить в калибровку» таблицы найденных пиков
    ///     (<see cref="AddDetectedPeak"/>): тот же расчёт по диапазону вокруг пика.
    ///   * Строки — <see cref="ResultData.CalibrationLines"/>: опоры кривой FWHM, то есть
    ///     в файле они лежат в секции FWHM, дополненной признаком линии
    ///     (<see cref="CalibrationPeak.EnergyLine"/>) и отсчётами канала. Элемент
    ///     <c>EnergyCalibration</c> не трогается: Amber, дословно, «текущее
    ///     местоположение коэффициентов калибровки ни в коем случае нельзя трогать».
    ///   * «Рассчитать» (<see cref="RunCalibration"/>) — обе подгонки: энергия по
    ///     линиям (прежняя подгонка вкладки энергии, <c>SolveGuarded</c>), FWHM по всем
    ///     строкам (прежняя подгонка вкладки FWHM, выбор формы пика).
    ///   * ⛔ Линия энергии — только строка с ВВЕДЁННОЙ энергией или подписанный пик
    ///     таблицы найденных пиков (решение Amber вопросником 29.10.2026, дословно:
    ///     «Только с введённой энергией (Рекомендую)»). Выделенная щелчком строка без
    ///     ввода и серые опоры (прибор, умолчание, прежний файл) — только в FWHM.
    ///
    /// ⚠ Имя класса — прежней вкладки FWHM: этот класс строят по имени пробы
    /// (`FwhmViewReachProbeO13`, `ModalReachProbeF22`, `FwhmReaderProbeF62`,
    /// `P314FwhmProbe`, `P321FwhmSameGridProbe`, `CultureProbeO14`) и зовут отражением
    /// методы с прежними именами. Окно приложения — наследник
    /// <see cref="DCEnergyCalibrationView"/>: его имя типа и есть строка раскладки окон
    /// (<c>PersistString</c>), которую несут поставочная раскладка, раскладки людей и
    /// прежние версии.
    /// </summary>
    public partial class DCFwhmCalibrationView : ToolWindow
    {
        MainForm mainForm;

        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        /// <summary>
        /// Кривая FWHM вида: ссылка на кривую документа либо ещё не посчитанный выбор
        /// вида кривой (см. <see cref="SelectCurveComboBox_SelectedIndexChanged"/>);
        /// <c>null</c> — у спектра кривой нет.
        /// </summary>
        FwhmCalibration fwhmCalibration;

        int lastSelectedIndex = -1;

        int minPeaksRequirement = 2;

        bool peakPickupProcessing = false;

        EnergySpectrumView pickupSubscribedView;

        /// <summary>Таблица и вид кривой не менялись после последнего расчёта (или документа ещё не трогали).</summary>
        bool calibrationDone = true;

        /// <summary>Последний «Рассчитать» прошёл, и с тех пор ничего не менялось — строка состояния говорит «выполнено».</summary>
        bool calibrationRan;

        bool updatingCurveSelection;

        /// <summary>Степень полинома ставит код (по числу линий) — правка человека не отмечается изменением.</summary>
        bool settingPolynomialOrder;

        /// <summary>Число линий, по которому степень полинома ставилась в последний раз (-1 — ещё не ставилась).</summary>
        int polynomialOrderLines = -1;

        string expGaussExpLeftParameterLabelText;
        string expGaussExpRightParameterLabelText;

        /// <summary>
        /// ПРИЧИНА, ПО КОТОРОЙ КРИВОЙ РАЗРЕШЕНИЯ У СПЕКТРА НЕТ (`A240`, полоса
        /// F62, 06.09.2026), — словами и с тремя числами настроек. Заполняется
        /// последней попыткой <see cref="EnsureFwhmCalibration"/>, читается
        /// <see cref="ApplyFwhmRefusalHint"/>. <c>null</c> — причины нет.
        /// </summary>
        string fwhmRefusal;

        /// <summary>
        /// Подпись пустой таблицы, какой её положил конструктор форм (из resx, то есть
        /// переведённая): причина отказа пишется НА ЕЁ МЕСТО и по исчезновении
        /// причины подпись возвращается своя.
        /// </summary>
        string collectedPeaksNoItemsText;

        /// <summary>(`AMBER283`) Подсказка подписи сверху из resx — полный текст инструкции.</summary>
        readonly string label1TipBase;

        /// <summary>(`AMBER283`) Заголовок рамки энергии из resx — формула полинома степени ≤ 4.</summary>
        readonly string energyGroupTitle;

        /// <summary>(`AMBER283`) Строки самой формы (итог под таблицей, выбор пиков, заголовок окна формы пика).</summary>
        readonly System.ComponentModel.ComponentResourceManager ownResources;

        /// <summary>
        /// ⛔ (`AMBER283`, решение Amber вопросником 02.11.2026, дословно: «Итог строкой под
        /// таблицей (Рекомендую)») ИТОГ ПОСЛЕДНЕГО «ВЫПОЛНИТЬ КАЛИБРОВКУ» — строкой под
        /// таблицей: что принято (СКО энергии и FWHM, κ или причина, почему нет, форма пика),
        /// что отвергнуто. Та же строка — в строке состояния главного окна (там СКО полным
        /// словом): прежде после отказа подгонки FWHM главное окно писало «Калибровка
        /// выполнена. Энергия…», а под таблицей стояло «Изменить значения и нажать кнопку
        /// выполнить.» — два сигнала говорили разное. <c>null</c> — расчёта не было или
        /// таблицу с тех пор правили (<see cref="MarkModified"/>).
        /// </summary>
        string runSummary;

        /// <summary>
        /// (`AMBER283`, решение Amber 02.11.2026: «Добавить пик» — до «Отмены») Строки, заведённые
        /// выделением пика в этом виде и ещё без введённой энергии: энергия такой строки
        /// вводится ОДНИМ щелчком по её ячейке (<see cref="CollectedPeaksTable_CellClick"/>).
        /// Прочие строки — как прежде, двойным щелчком.
        /// </summary>
        readonly HashSet<CalibrationPeak> freshLines = new HashSet<CalibrationPeak>();

        /// <summary>
        /// (`AMBER283`, решение Amber 02.11.2026: «Окно формы пика — по требованию») Сравнение форм
        /// пика последнего расчёта (прежде — модальное окно на каждое «Выполнить калибровку»)
        /// и спектр, к которому оно относится. Показывается в окне «График», пункт «Функция пика».
        /// </summary>
        string peakShapeComparisonText;
        ResultData peakShapeComparisonOwner;

        /// <summary>Шкала энергии вида (копия шкалы спектра; правится полями коэффициентов и «Рассчитать»).</summary>
        PolynomialEnergyCalibration energyCalibration;

        /// <summary>Шкала прибора — цель «Сброса».</summary>
        PolynomialEnergyCalibration defaultEnergyCalibration;

        /// <summary>Квадратичная шкала для RadiaCode/Obsidian (их прибор держит шкалу степени 2).</summary>
        PolynomialEnergyCalibration rc_EnergyCalibration;

        public DCFwhmCalibrationView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            // (`AMBER264`) Подсказки из дизайнера — блоком.
            AppUi.WrapToolTips(this.toolTip1, this);
            // (`AMBER283`) Подсказка поля κ из resx — основа: над ней встаёт подпись,
            // чья κ и чем подогнана (прежде — отдельной строкой под полем).
            tractCurvatureTipBase = toolTip1.GetToolTip(tractCurvatureTextBox) ?? string.Empty;
            // (`AMBER283`, вариант A) Подпись сверху — две строки; полный прежний текст —
            // её подсказкой (основа, к которой причина отказа кривой встаёт сверху). Строка
            // формулы — заголовок рамки энергии (`ShowCoefficients`).
            label1TipBase = toolTip1.GetToolTip(label1) ?? string.Empty;
            energyGroupTitle = groupBox1.Text;
            ownResources = new System.ComponentModel.ComponentResourceManager(typeof(DCFwhmCalibrationView));
            // «Добавить пик» — до «Отмены»: энергия новой строки вводится одним щелчком
            CollectedPeaksTable.CellClick += CollectedPeaksTable_CellClick;
            channelColumn.Alignment = ColumnAlignment.Right;
            energyColumn.Alignment = ColumnAlignment.Right;
            // (П259, 14.10.2026) Энергия точки — до тысячных кэВ, как в таблице линий.
            energyColumn.Format = "0.###";
            fwhmColumn.Alignment = ColumnAlignment.Right;
            weightColumn.Alignment = ColumnAlignment.Right;
            executeCalibrationButton.Enabled = false;
            expGaussExpLeftParameterLabelText = peakShapeFirstParameterLabel.Text;
            expGaussExpRightParameterLabelText = peakShapeSecondParameterLabel.Text;
            // ⚠ Пустой строкой, а не null: сеттер `Table.NoItemsText` сравнивает
            //    ЧЕРЕЗ СТАРОЕ значение.
            collectedPeaksNoItemsText = CollectedPeaksTable.NoItemsText ?? string.Empty;
            // Вид кривой спектра без кривой — простой корень, как у умолчания прибора
            // (`FwhmCalibration.DefaultCalibration`); у спектра с кривой список встанет на
            // её вид при первом обновлении.
            updatingCurveSelection = true;
            try
            {
                selectCurveComboBox.SelectedIndex = (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration;
            }
            finally
            {
                updatingCurveSelection = false;
            }
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
            UpdateTableColumns();
        }

        ResultData ActiveResultData
        {
            get
            {
                return mainForm != null && mainForm.ActiveDocument != null ? mainForm.ActiveDocument.ActiveResultData : null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Обновление вида
        // ══════════════════════════════════════════════════════════════════════

        public void UpdateFwhmCalibration(bool reset_state = false)
        {
            if (reset_state)
            {
                calibrationDone = true;
                calibrationRan = false;
                runSummary = null;
                freshLines.Clear();
                ClearPeakPickupState();
            }
            ResultData activeResultData = ActiveResultData;
            if (activeResultData == null)
            {
                fwhmCalibration = null;
                tableModel1.Rows.Clear();
                UpdateCalibrateButtonState();
                return;
            }
            EnsureFwhmCalibration(activeResultData);
            fwhmCalibration = activeResultData.FwhmCalibration;
            ApplyFwhmRefusalHint();
            UpdateData();
        }

        void EnsureFwhmCalibration(ResultData resultData)
        {
            // ⛔ ПРИЧИНА ОТКАЗА НЕ ВЫБРАСЫВАЕТСЯ (`A240`). Сбрасывается она ЗДЕСЬ, а не
            //    у читателя: причина, оставшаяся от прошлого спектра, называла бы три
            //    числа ЧУЖИХ настроек.
            fwhmRefusal = null;
            if (resultData == null || resultData.FwhmCalibration != null)
            {
                return;
            }

            // ⛔ (`AMBER269`, 02.11.2026, полоса a269fix5) СПЕКТРУ, У КОТОРОГО СБРОС СНЯЛ
            //    ПРИБОР, КРИВАЯ ЗДЕСЬ НЕ СТАВИТСЯ — то же правило, что у открытия и всех
            //    импортов (`DocumentManager.CheckDocument`, `A260`, решение Amber 06.09.2026
            //    «Снять кривую вместе с прибором»). Прежде вкладка ставила такому спектру
            //    копию кривой его СВЕЖИХ настроек — встроенную заглушку 15/3756/103: окно
            //    импорта говорило «нет модели разрешения», а вкладка «Калибровка» тут же
            //    давала спектру модель выдуманного прибора. Причина на месте пустой таблицы —
            //    та же, что назвало окно импорта (`ERRFwhmCalibrationUnset`); строки
            //    калибровки спектр без кривой копит по-прежнему (`PendingCalibrationLines`).
            if (resultData.DeviceConfigWiped)
            {
                fwhmRefusal = Resources.ERRFwhmCalibrationUnset;
                return;
            }

            FWHMPeakDetectionMethodConfig cfg = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            EnergyCalibration spectrumCalibration = resultData.EnergySpectrum != null ? resultData.EnergySpectrum.EnergyCalibration : null;
            if (cfg == null || spectrumCalibration == null)
            {
                return;
            }

            // ⛔ (`AMBER269`, 02.11.2026, полоса a269fix5) КРИВАЯ — ТЕМ ЖЕ ПРАВИЛОМ, ЧТО ПРИ
            //    ОТКРЫТИИ ФАЙЛА (`DocumentManager.StandInFwhm` → `FwhmCalibration.StandInFromDevice`;
            //    решение Amber 02.11.2026 «Кривую прибора (Рекомендую)»): сохранённая кривая
            //    прибора, на чужой сетке каналов — перенесённая (`RecalcWithNewChannelNum`), а
            //    если по ней нельзя (нет, заглушка, пустышка) — три числа прибора с формой его
            //    пика. Прежде здесь стояла копия кривой настроек как есть: на спектре другой
            //    сетки — ширины в каналах прибора, у встроенной заглушки — 15/3756/103. Кривая
            //    помечается подставленной, и смена прибора в панели ставит её заново, как после
            //    открытия. Настройки спектра (`cfg`) больше не правятся.
            string refusal;
            resultData.FwhmCalibration = DocumentManager.GetInstance().StandInFwhm(resultData, cfg, out refusal);
            if (resultData.FwhmCalibration == null)
            {
                fwhmRefusal = refusal;
            }
        }

        /// <summary>
        /// ЧИТАТЕЛЬ ПРИЧИНЫ — НА МЕСТЕ, А НЕ ОКНОМ (`A240`). Голос о том, что у спектра
        /// нет модели разрешения, уже звучит один раз на файл
        /// (<c>DocumentManager.ReportMissingFwhmCalibration</c>), а вид обновляется на
        /// каждое событие — окно отсюда было бы десятым голосом об одной беде. Причина
        /// встаёт подписью пустой таблицы и подсказкой у таблицы и у верхней подписи.
        /// </summary>
        void ApplyFwhmRefusalHint()
        {
            bool refused = !string.IsNullOrEmpty(fwhmRefusal);
            CollectedPeaksTable.NoItemsText = refused ? fwhmRefusal : collectedPeaksNoItemsText;
            toolTip1.SetToolTip(CollectedPeaksTable, refused ? AppUi.WrapTip(fwhmRefusal) : string.Empty);
            // (`AMBER283`) у верхней подписи своя подсказка — полный текст инструкции; причина встаёт над ней
            toolTip1.SetToolTip(label1, refused
                ? AppUi.WrapTip(fwhmRefusal) + Environment.NewLine + Environment.NewLine + label1TipBase
                : label1TipBase);
        }

        private void DCFwhmCalibrationView_FormLoad(object sender, EventArgs e)
        {
            UpdateFwhmCalibration();
        }

        private void DCFwhmCalibrationView_FormClosing(object sender, FormClosingEventArgs e)
        {
            ClearPeakPickupState();
        }

        void UpdateData()
        {
            UpdateTable();
            calibrationProcessingPanel.Show();
            UpdateSelectedCurveInfo();
            UpdateCalibrateButtonState();
        }

        void UpdateTable()
        {
            CollectedPeaksTable.SuspendLayout();
            tableModel1.Rows.Clear();
            ResultData resultData = ActiveResultData;
            if (resultData == null)
            {
                CollectedPeaksTable.ResumeLayout();
                return;
            }
            List<CalibrationPeak> lines = resultData.CalibrationLines;
            lines.Sort();
            int position = 1;
            int energyLines = 0;
            foreach (CalibrationPeak line in lines)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(position.ToString(CultureInfo.InvariantCulture)));
                row.Cells.Add(new Cell(line.Channel));
                Cell energyCell = new Cell(line.Energy);
                if (!line.EnergyLine)
                {
                    // Опора FWHM без энергии линии (выделена щелчком без ввода энергии,
                    // неподписанный найденный пик, прибор, умолчание, прежний файл):
                    // в подгонку энергии не идёт, пока энергию не введут.
                    energyCell.ForeColor = SystemColors.GrayText;
                }
                row.Cells.Add(energyCell);
                row.Cells.Add(new Cell(FwhmText(resultData, line)));
                Cell weightCell = line.EnergyLine ? new Cell(line.Count) : new Cell(string.Empty);
                weightCell.Editable = line.EnergyLine;
                row.Cells.Add(weightCell);
                tableModel1.Rows.Add(row);
                position++;
                if (line.EnergyLine)
                {
                    energyLines++;
                }
            }
            CollectedPeaksTable.ResumeLayout();
            // Степень полинома — по числу линий, как прежде у вкладки энергии
            // (min(4, линий − 1)); ставится, только когда число линий СМЕНИЛОСЬ, —
            // иначе всякое обновление вида затирало бы выбор человека.
            if (energyLines != polynomialOrderLines)
            {
                polynomialOrderLines = energyLines;
                settingPolynomialOrder = true;
                try
                {
                    numericUpDown6.Value = Math.Min(4, Math.Max(0, energyLines - 1));
                }
                finally
                {
                    settingPolynomialOrder = false;
                }
            }
        }

        /// <summary>
        /// (`AMBER276`) Столбец FWHM — в каналах и в процентах. Amber 29.10.2026, консоль,
        /// дословно: «AMBER276 - в столбце FWHM должны быть цифры как в каналах, так и в %».
        /// Вид — как у таблицы найденных пиков (`DCPeakDetectionView.RefreshTable`: «30,
        /// 6.8%»): каналы — до тысячных (П314, ширина выделения дробная), процент — до
        /// десятых, ширина по шкале спектра в канале строки, к энергии этого канала. Числа —
        /// инвариантной культурой, точкой, без группировки (`A242`). Нет шкалы или энергия
        /// канала не больше нуля — одни каналы.
        /// </summary>
        internal static string FwhmText(ResultData resultData, CalibrationPeak line)
        {
            string channels = line.FWHM.ToString("0.###", CultureInfo.InvariantCulture);
            EnergyCalibration calibration = resultData != null && resultData.EnergySpectrum != null
                ? resultData.EnergySpectrum.EnergyCalibration
                : null;
            if (calibration == null)
            {
                return channels;
            }
            double center = calibration.ChannelToEnergy((double)line.Channel);
            double left = calibration.ChannelToEnergy(line.Channel - line.FWHM / 2.0);
            double right = calibration.ChannelToEnergy(line.Channel + line.FWHM / 2.0);
            double percent = 100.0 * (right - left) / center;
            if (!(center > 0.0) || double.IsNaN(percent) || double.IsInfinity(percent))
            {
                return channels;
            }
            return channels + ", " + percent.ToString("f1", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>FWHM в каналах из текста ячейки «каналы, процент%»: число до первой запятой.</summary>
        static double FwhmChannelsOf(string text)
        {
            string head = text ?? string.Empty;
            int comma = head.IndexOf(',');
            if (comma >= 0)
            {
                head = head.Substring(0, comma);
            }
            return UserNumber.ParseDouble(head.Trim());
        }

        void UpdateTableColumns()
        {
            weightColumn.Visible = checkBox2.Checked;
        }

        FwhmCalibration CurveOfSelectedType()
        {
            if (selectCurveComboBox.SelectedIndex == (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration)
            {
                return new SqrtFwhmCalibration();
            }
            if (selectCurveComboBox.SelectedIndex == (int)FwhmCalibration.FwhmCalibrationCurve.PowerFwhmCalibration)
            {
                return new PowerFwhmCalibration();
            }
            return new SimpleSqrtFwhmCalibration();
        }

        void UpdateSelectedCurveInfo()
        {
            if (fwhmCalibration != null)
            {
                int targetSelectedIndex =
                    fwhmCalibration is SimpleSqrtFwhmCalibration
                        ? (int)FwhmCalibration.FwhmCalibrationCurve.SimpleSqrtFwhmCalibration
                    : fwhmCalibration is PowerFwhmCalibration
                        ? (int)FwhmCalibration.FwhmCalibrationCurve.PowerFwhmCalibration
                        : (int)FwhmCalibration.FwhmCalibrationCurve.SqrtFwhmCalibration;

                if (selectCurveComboBox.SelectedIndex != targetSelectedIndex)
                {
                    updatingCurveSelection = true;
                    try
                    {
                        selectCurveComboBox.SelectedIndex = targetSelectedIndex;
                    }
                    finally
                    {
                        updatingCurveSelection = false;
                    }
                }
            }

            // Без кривой у спектра — формула и требование вида, выбранного в списке:
            // первая подгонка заведёт спектру кривую этого вида.
            FwhmCalibration shown = fwhmCalibration ?? CurveOfSelectedType();
            curveFormulaLabel.Text = shown.GetFormula();
            minPeaksRequirement = shown.MinPeaksRequirement();
            minPeaksRequirementLabel.Text = String.Format(CultureInfo.InvariantCulture, Resources.MinPeaksRequirement, minPeaksRequirement);
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
            if (fwhmCalibration != null)
            {
                UpdatePeakShapeInfo();
            }
            else
            {
                FitPeakShapeRows(false, false);
            }
        }

        /// <summary>
        /// ⛔ (`AMBER283`, 02.11.2026) ФУНКЦИЯ ПИКА — СТРОКОЙ В РАМКЕ «КРИВАЯ КАЛИБРОВКИ FWHM».
        /// Amber, консоль, дословно, к снимку панели: «4.Сделать более компактно.» и ко второму
        /// снимку «Групбокс сделай»; раскладка — вариант A дизайнера (вопросником 02.11.2026,
        /// дословно: «A — по пути, всё на виду (Рекомендую)»). Прежде это была рамка «Выбранная
        /// функция пика» в две строки с кнопкой «Просмотр»; теперь — строка «Функция пика:
        /// Гауссиана» рядом с «Мин. пиков: N», а «Просмотр» стал третьим пунктом окна «График»
        /// (вопросником, дословно: «Только «Просмотр» → в «График» (Рекомендую)»). Строка
        /// параметров формы (<see cref="peakShapeInfoPanel"/>) встаёт под ней только у
        /// не-гауссовой формы; высота рамки и панели идёт за этим, и прокрутка не тянет пустое
        /// место. Всё — от границ самих элементов, без чисел: так это верно при любом масштабе
        /// шрифта.
        /// </summary>
        /// <param name="curveVisible">у спектра есть кривая — строка функции пика видна</param>
        /// <param name="showParameters">форма не гауссова — видна и строка её параметров</param>
        void FitPeakShapeRows(bool curveVisible, bool showParameters)
        {
            peakShapeTypeLabel.Visible = curveVisible;
            peakShapeTypeValueLabel.Visible = curveVisible;
            peakShapeInfoPanel.Visible = curveVisible && showParameters;
            int bottom = Math.Max(minPeaksRequirementLabel.Bottom, selectCurveComboBox.Bottom);
            if (curveVisible)
            {
                bottom = Math.Max(bottom, showParameters ? peakShapeInfoPanel.Bottom : peakShapeTypeValueLabel.Bottom);
            }
            calibrationProcessingPanel.Height = bottom + 10;
            // `Bottom` — в координатах прокрученного клиента; низ содержимого — без сдвига прокрутки
            int contentBottom = calibrationProcessingPanel.Bottom - AutoScrollPosition.Y + 2;
            if (AutoScrollMinSize.Height != contentBottom)
            {
                AutoScrollMinSize = new Size(AutoScrollMinSize.Width, contentBottom);
            }
        }

        void UpdatePeakShapeInfo()
        {
            int peakType = FwhmCalibration.IsSupportedPeakType(fwhmCalibration.PeakType)
                ? fwhmCalibration.PeakType
                : FwhmCalibration.GaussianPeakType;
            bool showParameters = peakType != FwhmCalibration.GaussianPeakType;

            peakShapeTypeValueLabel.Text = GetPeakShapeName(peakType);
            peakShapeFirstParameterLabel.Visible = showParameters;
            peakShapeFirstParameterValueLabel.Visible = showParameters;
            peakShapeSecondParameterLabel.Visible = showParameters;
            peakShapeSecondParameterValueLabel.Visible = showParameters;
            FitPeakShapeRows(true, showParameters);

            if (!showParameters)
            {
                return;
            }

            if (peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                peakShapeFirstParameterLabel.Text = expGaussExpLeftParameterLabelText;
                peakShapeSecondParameterLabel.Text = expGaussExpRightParameterLabelText;
                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.ExpGaussExpLeftTail.ToString("0.0", CultureInfo.InvariantCulture);
                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.ExpGaussExpRightTail.ToString("0.0", CultureInfo.InvariantCulture);
            }
            else
            {
                peakShapeFirstParameterLabel.Text = Resources.ResourceManager.GetString("VoigtRelativeSigmaLabel");
                peakShapeSecondParameterLabel.Text = Resources.ResourceManager.GetString("VoigtRelativeGammaLabel");
                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.VoigtSigma.ToString("0.0", CultureInfo.InvariantCulture);
                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.VoigtGamma.ToString("0.0", CultureInfo.InvariantCulture);
            }
        }

        internal static string GetPeakShapeName(int peakType)
        {
            switch (peakType)
            {
                case FwhmCalibration.ExpGaussExpPeakType:
                    return Resources.ResourceManager.GetString("PeakShapeExpGaussExp");
                case FwhmCalibration.VoigtPeakType:
                    return Resources.ResourceManager.GetString("PeakShapeVoigt");
                default:
                    return Resources.ResourceManager.GetString("PeakShapeGaussian");
            }
        }

        void SelectCurveComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingCurveSelection || selectCurveComboBox.SelectedIndex == -1 || selectCurveComboBox.SelectedIndex == lastSelectedIndex || lastSelectedIndex == -1)
            {
                lastSelectedIndex = selectCurveComboBox.SelectedIndex;
                if (fwhmCalibration == null && !updatingCurveSelection)
                {
                    UpdateSelectedCurveInfo();
                    UpdateCalibrateButtonState();
                }
                return;
            }

            if (fwhmCalibration != null)
            {
                FwhmCalibration previousCalibration = fwhmCalibration;
                FwhmCalibration chosen = CurveOfSelectedType();
                chosen.CalibrationPeaks = fwhmCalibration.ClonePeaks();
                fwhmCalibration = chosen;
                CopyPeakShapeSettings(previousCalibration, fwhmCalibration);
            }
            // (П314, 15.10.2026) Новая форма кривой ещё НЕ ПОСЧИТАНА — и итог прежнего
            // расчёта под таблицей уже не о ней (`AMBER283`).
            calibrationDone = false;
            calibrationRan = false;
            runSummary = null;
            UpdateSelectedCurveInfo();
            UpdateCalibrateButtonState();
            lastSelectedIndex = selectCurveComboBox.SelectedIndex;
        }

        static void CopyPeakShapeSettings(FwhmCalibration source, FwhmCalibration target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.PeakType = source.PeakType;
            target.ExpGaussExpLeftTail = source.ExpGaussExpLeftTail;
            target.ExpGaussExpRightTail = source.ExpGaussExpRightTail;
            target.VoigtSigma = source.VoigtSigma;
            target.VoigtGamma = source.VoigtGamma;
        }

        static int EnergyLineCount(List<CalibrationPeak> lines)
        {
            int n = 0;
            if (lines != null)
            {
                foreach (CalibrationPeak line in lines)
                {
                    if (line != null && line.EnergyLine)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        void UpdateCalibrateButtonState()
        {
            ResultData resultData = ActiveResultData;
            bool hasDocument = resultData != null;
            List<CalibrationPeak> lines = hasDocument ? resultData.CalibrationLines : null;
            int rows = lines != null ? lines.Count : 0;
            int energyLines = EnergyLineCount(lines);
            bool canFwhm = rows >= minPeaksRequirement && selectCurveComboBox.SelectedIndex != -1;
            bool canEnergy = energyLines >= 1;

            // (`AMBER283`) Выбор пиков идёт до «Отмены» — «Выполнить калибровку» во время
            // выбора доступна и сама его завершает (`ExecuteCalibrationButton_Click`).
            if (!hasDocument ||
                selectCurveComboBox.SelectedIndex == -1 ||
                (CollectedPeaksTable.SelectedItems.Length > 0 && CollectedPeaksTable.SelectedItems[0].Index < 0) ||
                (lastSelectedIndex == selectCurveComboBox.SelectedIndex && calibrationDone) ||
                !(canFwhm || canEnergy))
            {
                executeCalibrationButton.Enabled = false;
            }
            else
            {
                executeCalibrationButton.Enabled = true;
            }
            saveToDeviceCfgButton.Enabled = hasDocument;
            getAllPeaksButton.Enabled = hasDocument && isCalibrationPeaksExist() > 1;
            // (`AMBER283`) лишний пик, выделенный посреди выбора, снимается, не прерывая выбор
            removePeakButton.Enabled = hasDocument && rows > 0;
            // (`AMBER283`) Одна кнопка «График» на оба графика: доступна, когда есть хотя
            // бы один из двух (прежние условия двух кнопок — через «или»).
            viewCalibrationButton.Enabled = hasDocument && (energyLines > 1 || fwhmCalibration != null);
            viewPeakShapeButton.Enabled = fwhmCalibration != null;
            addPeakButton.Enabled = hasDocument && !peakPickupProcessing;
            cancelAddPeakButton.Enabled = peakPickupProcessing;

            // Строка под таблицей: идёт выбор пиков — как его вести; был расчёт и таблицу с
            // тех пор не правили — его итог (`runSummary`); иначе — приглашение рассчитать.
            string line;
            if (peakPickupProcessing)
            {
                line = OwnText("PickupNextPeak", Resources.MSGPickUpCalibrationPeak);
            }
            else if (!string.IsNullOrEmpty(runSummary))
            {
                line = runSummary;
            }
            else
            {
                line = Resources.MSGProceedCalibration;
            }
            label36.Text = line;
            // строка в две строки с многоточием — целиком в подсказке
            toolTip1.SetToolTip(label36, AppUi.WrapTip(line));
        }

        /// <summary>Таблица или вид кривой изменились — расчёт снова нужен; прежний итог уже не о ней.</summary>
        void MarkModified()
        {
            calibrationDone = false;
            calibrationRan = false;
            runSummary = null;
        }

        /// <summary>(`AMBER283`) Строка resx самой формы; нет её — запасная.</summary>
        string OwnText(string name, string fallback)
        {
            string text = ownResources != null ? ownResources.GetString(name) : null;
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Строки таблицы: щелчок по спектру, таблица найденных пиков, правка, удаление
        // ══════════════════════════════════════════════════════════════════════

        private void AddPeakButton_Click(object sender, EventArgs e)
        {
            if (mainForm.ActiveDocument != null)
            {
                peakPickupProcessing = true;
                // Remember the subscribed view - see ClearPeakPickupState.
                this.pickupSubscribedView = mainForm.ActiveDocument.EnergySpectrumView;
                this.pickupSubscribedView.PeakPickuped += this.EnergySpectrumView_PeakPickuped;
            }
            UpdateCalibrateButtonState();
        }

        void EnergySpectrumView_PeakPickuped(object sender, PeakPickupedEventArgs e)
        {
            if (!peakPickupProcessing)
            {
                return;
            }

            // Сторож по образцу `GetAllPeaksButton_Click` (`A236`).
            ResultData pickupResultData = ActiveResultData;
            EnsureFwhmCalibration(pickupResultData);
            if (pickupResultData == null || pickupResultData.EnergySpectrum == null)
            {
                ClearPeakPickupState();
                UpdateCalibrateButtonState();
                return;
            }

            CalibrationPeak newPeak = e.CalibrationPeak;
            foreach (CalibrationPeak peak in pickupResultData.CalibrationLines)
            {
                if (peak.Channel == newPeak.Channel)
                {
                    string PeakExistText = String.Format(CultureInfo.InvariantCulture, Resources.ERRPeakExist, peak.FWHM, peak.Channel);
                    // ⛔ `A245`: метод лежит на БЕЗОКОННОМ пути (`ModalReachProbeF22`
                    //    зовёт его отражением) — дверь `AppUi.Report`, не голое окно.
                    AppUi.Report(PeakExistText, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    return;
                }
            }
            // ⛔ (`AMBER276`, решение Amber вопросником 29.10.2026, дословно: «Только с
            //    введённой энергией (Рекомендую)») Выделенная щелчком строка — ещё НЕ линия
            //    калибровки по энергии: она идёт в подгонку FWHM, а в подгонку энергии —
            //    только когда человек введёт энергию её линии (правка ячейки «Энергия»).
            //    Прежде она шла и в энергию с оценкой по нынешней шкале, и «Рассчитать»
            //    ради одной FWHM молча переподгоняло шкалу по этим оценкам: две строки на
            //    ASN16 (полином 2) роняли степень до 1 и уводили линию 2614.5 на −82.9 кэВ.
            newPeak = FitLine(pickupResultData, newPeak, e.PeakStartSelection, e.PeakEndSelection, false);
            // Энергия новой строки — по нынешней шкале В ЕЁ КАНАЛЕ, до сотых кэВ, как
            // прежде у вкладки энергии; показана серой, пока её не введут.
            newPeak.Energy = EnergyAtChannel(pickupResultData, newPeak.Channel);
            // (`AMBER270`) центр масс пика для κ — по спектру, где строку завели
            RememberCentroid(newPeak, pickupResultData.EnergySpectrum);

            pickupResultData.CalibrationLines.Add(newPeak);
            mainForm.ActiveDocument.Dirty = true;
            MarkModified();
            // ⛔ (`AMBER283`, решение Amber вопросником 02.11.2026: «Добавить пик» — до
            //    «Отмены») Выбор НЕ снимается после одного пика: следующие пики выделяются
            //    подряд, пока не нажата «Отмена» (или «Выполнить калибровку»). Новая строка
            //    подсвечивается, и её энергия вводится одним щелчком по ячейке.
            freshLines.Add(newPeak);
            UpdateFwhmCalibration();
            SelectLineRow(newPeak);
            UpdateCalibrateButtonState();
        }

        /// <summary>(`AMBER283`) Подсветить строку таблицы этой линии калибровки (после пересборки таблицы).</summary>
        void SelectLineRow(CalibrationPeak line)
        {
            ResultData resultData = ActiveResultData;
            if (resultData == null || line == null)
            {
                return;
            }
            int index = resultData.CalibrationLines.IndexOf(line);
            if (index < 0 || index >= tableModel1.Rows.Count)
            {
                return;
            }
            tableModel1.Selections.Clear();
            tableModel1.Selections.SelectCell(index, 2);
            CollectedPeaksTable.EnsureVisible(index, 2);
        }

        /// <summary>
        /// (`AMBER283`) Энергия свежей строки (выделенной в этом виде и ещё без введённой
        /// энергии, <see cref="freshLines"/>) — правкой по ОДНОМУ щелчку по её ячейке «Энергия».
        /// Прочие ячейки — как прежде, двойным щелчком.
        /// </summary>
        void CollectedPeaksTable_CellClick(object sender, XPTable.Events.CellMouseEventArgs e)
        {
            ResultData resultData = ActiveResultData;
            if (e == null || e.Column != 2 || resultData == null || freshLines.Count == 0)
            {
                return;
            }
            List<CalibrationPeak> lines = resultData.CalibrationLines;
            if (e.Row < 0 || e.Row >= lines.Count || !freshLines.Contains(lines[e.Row]))
            {
                return;
            }
            CollectedPeaksTable.EditCell(e.Row, 2);
        }

        /// <summary>
        /// Подгонка формы пика по выделению (как прежде у вкладки FWHM) и отметка линии.
        /// <paramref name="energyLine"/> — у строки есть энергия ЛИНИИ (подписанный пик
        /// таблицы найденных пиков): она идёт в обе подгонки; без неё — только в FWHM,
        /// пока энергию не введут (решение Amber 29.10.2026 «Только с введённой
        /// энергией»). Отсчёты канала (вес) считаются у обеих.
        /// </summary>
        static CalibrationPeak FitLine(ResultData resultData, CalibrationPeak peak, int start, int end, bool energyLine)
        {
            SpectrumAriphmetics sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
            CalibrationPeak fitted = sa.CalcPeakFitValues(peak, start, end);
            sa.Dispose();
            fitted.EnergyLine = energyLine;
            int[] spectrum = resultData.EnergySpectrum.Spectrum;
            fitted.Count = spectrum != null && fitted.Channel >= 0 && fitted.Channel < spectrum.Length
                ? spectrum[fitted.Channel]
                : 0;
            return fitted;
        }

        static double EnergyAtChannel(ResultData resultData, int channel)
        {
            EnergyCalibration calibration = resultData.EnergySpectrum != null ? resultData.EnergySpectrum.EnergyCalibration : null;
            if (calibration == null)
            {
                return 0.0;
            }
            return (double)Math.Round((decimal)calibration.ChannelToEnergy((double)channel), 2);
        }

        /// <summary>
        /// (`AMBER276`) Второй вход единой калибровки — «Добавить в калибровку» таблицы
        /// найденных пиков (`DCPeakDetectionView`). Amber 29.10.2026, дословно: «обрати
        /// внимание на форму поиска пиков. В нем есть табличка с найденными пиками. При
        /// клике правой кнопкой мышки появляется контекстное меню «Добавить выбранный
        /// пик в калибровку».» Пик идёт в ту же таблицу и с той же подгонкой, что щелчок
        /// по спектру: центроид и FWHM считаются выделением диапазона ±1.5 FWHM пика
        /// (<c>EnergySpectrumView.PeakPickupFor</c>), не меньше ±3 каналов. Не вышло
        /// измерить ширину — строка берёт канал и FWHM поиска пиков. Энергия — линии,
        /// которой пик подписан (прежняя арифметика меню), и строка — линия калибровки
        /// по энергии. ⛔ Пик БЕЗ подписанной линии (промаха нет, энергия — его же по
        /// нынешней шкале) линией не становится: идёт только в FWHM, энергия серая, пока
        /// её не введут (решение Amber 29.10.2026 «Только с введённой энергией»). Пик в
        /// канале, который в таблице уже есть, не добавляется (как прежде у вкладки
        /// энергии — без окна).
        /// </summary>
        /// <returns>true — строка добавлена</returns>
        public bool AddDetectedPeak(Peak peak, double energy)
        {
            ResultData resultData = ActiveResultData;
            if (peak == null || resultData == null || resultData.EnergySpectrum == null)
            {
                return false;
            }
            // Кривая — до сверки каналов: умолчание по настройкам, если строится, заводит
            // спектру кривую, и строки живут уже в ней (`ResultData.CalibrationLines`).
            EnsureFwhmCalibration(resultData);
            int half =Math.Max(3, (int)Math.Ceiling(1.5 * (peak.FWHM > 0 ? peak.FWHM : 0.0)));
            int start = peak.Channel - half;
            int end = peak.Channel + half;
            PeakPickupedEventArgs picked = null;
            try
            {
                EnergySpectrumView view = mainForm.ActiveDocument.EnergySpectrumView;
                picked = view != null ? view.PeakPickupFor(start, end) : null;
            }
            catch (Exception)
            {
                picked = null;
            }
            CalibrationPeak newPeak;
            if (picked != null)
            {
                newPeak = picked.CalibrationPeak;
                start = picked.PeakStartSelection;
                end = picked.PeakEndSelection;
            }
            else
            {
                newPeak = new CalibrationPeak { Channel = peak.Channel, Energy = peak.Energy, FWHM = peak.FWHM };
            }
            foreach (CalibrationPeak line in resultData.CalibrationLines)
            {
                if (line.Channel == newPeak.Channel)
                {
                    return false;
                }
            }
            bool signedLine = peak.Nuclide != null && peak.Nuclide.Energy > 0.0;
            newPeak = FitLine(resultData, newPeak, start, end, signedLine);
            newPeak.Energy = energy;
            // (`AMBER270`) центр масс пика для κ — по спектру, где строку завели
            RememberCentroid(newPeak, resultData.EnergySpectrum);
            resultData.CalibrationLines.Add(newPeak);
            mainForm.ActiveDocument.Dirty = true;
            MarkModified();
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
            return true;
        }

        void ClearPeakPickupState()
        {
            // Unsubscribe from the view we actually subscribed to, not from whatever
            // document happens to be active now.
            if (this.pickupSubscribedView != null)
            {
                this.pickupSubscribedView.PeakPickuped -= this.EnergySpectrumView_PeakPickuped;
                this.pickupSubscribedView = null;
            }
            peakPickupProcessing = false;
            UpdateCalibrateButtonState();
        }

        private void CancelAddPeakButton_Click(object sender, EventArgs e)
        {
            ClearPeakPickupState();
        }

        private void RemovePeakButton_Click(object sender, EventArgs e)
        {
            ResultData resultData = ActiveResultData;
            if (resultData == null)
            {
                UpdateCalibrateButtonState();
                return;
            }
            List<CalibrationPeak> lines = resultData.CalibrationLines;
            int selectedItemIndex;
            if (CollectedPeaksTable.SelectedItems.Length >= 1)
            {
                selectedItemIndex = CollectedPeaksTable.SelectedItems[0].Index;
            }
            else
            {
                // (`AMBER201`, мелочь 4.10) Без выделенной строки НИЧЕГО не удаляется.
                selectedItemIndex = -1;
            }
            if (selectedItemIndex < 0 || selectedItemIndex >= lines.Count)
            {
                UpdateCalibrateButtonState();
                return;
            }
            // Плохой для одной из подгонок пик удаляется целиком (решение Amber
            // 29.10.2026: «Строка идёт в обе подгонки»).
            freshLines.Remove(lines[selectedItemIndex]);
            lines.RemoveAt(selectedItemIndex);
            mainForm.ActiveDocument.Dirty = true;
            MarkModified();
            tableModel1.Selections.Clear();
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
        }

        private void CollectedPeaksTable_EditingStopped(object sender, XPTable.Events.CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;
            // Сторож по образцу `GetAllPeaksButton_Click` (`A236`).
            ResultData editResultData = ActiveResultData;
            if (editResultData == null || row == null || row.Index < 0 || row.Index >= editResultData.CalibrationLines.Count)
            {
                UpdateCalibrateButtonState();
                return;
            }
            CalibrationPeak line = editResultData.CalibrationLines[row.Index];
            NumberCellEditor editor = e.Editor as NumberCellEditor;
            TextCellEditor textEditor = e.Editor as TextCellEditor;
            string textvalue = editor != null ? editor.TextBox.Text
                             : textEditor != null ? textEditor.TextBox.Text
                             : string.Empty;
            try
            {
                if (e.Column == 1)
                {
                    // ⛔ `AMBER188(в)`: канал разбирается в ЛОКАЛЬНУЮ и пишется в строку
                    //    только после проверки границы.
                    int channel = (int)UserNumber.ParseDecimal(textvalue);
                    int[] spectrum = editResultData.EnergySpectrum.Spectrum;
                    if (channel < 0 || channel >= spectrum.Length)
                    {
                        throw new Exception(Resources.ERRCalibrationChannelExceed);
                    }
                    line.Channel = channel;
                    line.Count = spectrum[channel];
                }
                else if (e.Column == 2)
                {
                    double entered = UserNumber.ParseDouble(textvalue);
                    // (`AMBER283`) Свежая строка открывается на правку ОДНИМ щелчком — то
                    // есть и случайным. Ушли из неё, не изменив оценку, — она остаётся серой
                    // опорой FWHM, а не молча становится линией энергии. Ввели число — линия.
                    if (freshLines.Contains(line) && !line.EnergyLine && entered == line.Energy)
                    {
                        return;
                    }
                    freshLines.Remove(line);
                    // Введённая энергия — энергия линии: строка идёт и в подгонку энергии.
                    line.Energy = entered;
                    if (!line.EnergyLine)
                    {
                        line.Count = CountAt(editResultData, line.Channel);
                    }
                    line.EnergyLine = true;
                }
                else if (e.Column == 3)
                {
                    // (`AMBER276`) Ячейка — «каналы, процент%»: текст не правили — FWHM не
                    // трогается (иначе ширина урезалась бы до показанных тысячных); правили —
                    // берётся число до запятой, в каналах.
                    if (textvalue == FwhmText(editResultData, line))
                    {
                        return;
                    }
                    line.FWHM = FwhmChannelsOf(textvalue);
                }
                else if (e.Column == 4)
                {
                    line.Count = UserNumber.ParseInt(textvalue);
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                AppUi.Report(String.Format(CultureInfo.InvariantCulture, Resources.ERRAddCalibrationPoints, textvalue, ex.Message),
                             Resources.ErrorExclamation, MessageBoxIcon.Error);
                e.Cancel = true;
                // `AMBER188(в)`: ячейка после отказа перерисовывается из модели ПОСЛЕ
                // выхода из события правки.
                this.BeginInvoke(new Action(this.UpdateTable));
                return;
            }
            mainForm.ActiveDocument.Dirty = true;
            MarkModified();
            UpdateCalibrateButtonState();
            // Цвет энергии, порядок по каналу и степень полинома — из модели, после
            // выхода из события правки.
            this.BeginInvoke(new Action(delegate
            {
                UpdateTable();
                UpdateCalibrateButtonState();
            }));
        }

        static int CountAt(ResultData resultData, int channel)
        {
            int[] spectrum = resultData.EnergySpectrum != null ? resultData.EnergySpectrum.Spectrum : null;
            return spectrum != null && channel >= 0 && channel < spectrum.Length ? spectrum[channel] : 0;
        }

        int isCalibrationPeaksExist()
        {
            int count = 0;
            if (mainForm.DocumentList != null)
            {
                foreach (DocEnergySpectrum doc in mainForm.DocumentList)
                {
                    if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
                    {
                        continue;
                    }

                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
                        if (data == null || data.CalibrationLines == null)
                        {
                            continue;
                        }

                        count += data.CalibrationLines.Count;
                    }
                }
            }
            return count;
        }

        private void GetAllPeaksButton_Click(object sender, EventArgs e)
        {
            List<CalibrationPeak> peaks = new List<CalibrationPeak>();
            HashSet<int> channels = new HashSet<int>();
            ResultData activeResultData = ActiveResultData;

            EnsureFwhmCalibration(activeResultData);
            if (activeResultData == null)
            {
                UpdateCalibrateButtonState();
                return;
            }

            // (П321, 15.10.2026; решение Amber вопросником 15.10.2026, дословно: «Брать
            // только из документов, где число каналов такое же.») Канал и FWHM строки — в
            // каналах своего спектра: строка спектра другой сетки легла бы в чужие
            // единицы. (`AMBER276`) То же правило теперь и у линий энергии: канал линии
            // спектра в 1024 канала на шкале спектра в 4096 — другая энергия.
            if (activeResultData.EnergySpectrum == null)
            {
                UpdateCalibrateButtonState();
                return;
            }
            int activeChannels = activeResultData.EnergySpectrum.NumberOfChannels;
            int skippedOtherGrid = 0;
            // (`AMBER270`) спектр, откуда строка, — по индексу `peaks`: центр масс пика линии
            // для κ мерится по НЕМУ, а не по спектру, в чью таблицу строка уходит
            List<EnergySpectrum> sources = new List<EnergySpectrum>();
            // (`AMBER295`, «κ только по строкам спектра») откуда строка: пусто — свой спектр
            List<string> origins = new List<string>();

            if (mainForm.DocumentList != null)
            {
                foreach (DocEnergySpectrum doc in mainForm.DocumentList)
                {
                    if (doc == null || doc.ResultDataFile == null || doc.ResultDataFile.ResultDataList == null)
                    {
                        continue;
                    }

                    foreach (ResultData data in doc.ResultDataFile.ResultDataList)
                    {
                        if (data != null && data.CalibrationLines != null && data.CalibrationLines.Count > 0)
                        {
                            if (data.EnergySpectrum == null || data.EnergySpectrum.NumberOfChannels != activeChannels)
                            {
                                skippedOtherGrid += data.CalibrationLines.Count;
                                continue;
                            }
                            peaks.AddRange(data.CalibrationLines);
                            string origin = data == activeResultData ? string.Empty : SourceName(doc, data);
                            for (int k = 0; k < data.CalibrationLines.Count; k++)
                            {
                                sources.Add(data.EnergySpectrum);
                                origins.Add(origin);
                            }
                        }
                    }
                }
                // ⛔ (`AMBER295`, находка критика `crit270k`) строки активного спектра — ПЕРВЫМИ: сбор
                // оставляет одну строку на канал, и при активном документе, открытом последним, его
                // свои строки вытеснялись чужими на тех же каналах (те помечались «другая съёмка» и
                // не шли в κ). Порядок внутри своих и внутри чужих — прежний (порядок документов).
                List<int> order = Enumerable.Range(0, peaks.Count).Where(k => origins[k].Length == 0)
                                            .Concat(Enumerable.Range(0, peaks.Count).Where(k => origins[k].Length > 0)).ToList();
                peaks = order.Select(k => peaks[k]).ToList();
                sources = order.Select(k => sources[k]).ToList();
                origins = order.Select(k => origins[k]).ToList();
                List<CalibrationPeak> target = activeResultData.CalibrationLines;
                if (peaks.Count > 0)
                {
                    // (`AMBER201`, подозрение полосы 4) В документ — КОПИИ строк: правка
                    // строки здесь не должна молча менять таблицу ДРУГОГО спектра.
                    List<CalibrationPeak> copies = CalibrationPeak.ClonePeaks(peaks);
                    target.Clear();
                    for (int i = 0; i < copies.Count; i++)
                    {
                        CalibrationPeak peak = copies[i];
                        if (channels.Add(peak.Channel))
                        {
                            RememberCentroid(peak, sources[i]);
                            // (`AMBER295`) строка ДРУГОГО документа помечается источником (пометка
                            // идёт в файл); своя остаётся своей, даже если спектр набирался дальше.
                            // Строка, уже помеченная прежним сбором, свою пометку сохраняет.
                            if (origins[i].Length > 0 && string.IsNullOrEmpty(peak.Source))
                            {
                                peak.Source = origins[i];
                            }
                            target.Add(peak);
                        }
                    }
                    mainForm.ActiveDocument.Dirty = true;
                    MarkModified();
                }
                UpdateFwhmCalibration();
                UpdateCalibrateButtonState();
                if (skippedOtherGrid > 0)
                {
                    // Окно через `AppUi.Report`: метод зовут отражением безоконные пробы.
                    AppUi.Report(String.Format(CultureInfo.InvariantCulture, Resources.MSGFwhmPeaksOtherGridSkipped, skippedOtherGrid),
                                 Resources.NotificationDialogTitle, MessageBoxIcon.Information);
                }
            }
        }

        /// <summary>
        /// (`AMBER295`) Имя источника строки для пометки «другая съёмка»: имя файла документа,
        /// без файла — время начала съёмки спектра; не пусто.
        /// </summary>
        static string SourceName(DocEnergySpectrum doc, ResultData data)
        {
            string name = null;
            try
            {
                name = doc != null && !string.IsNullOrEmpty(doc.Filename) ? System.IO.Path.GetFileName(doc.Filename) : null;
            }
            catch (ArgumentException)
            {
                name = null;
            }
            if (string.IsNullOrEmpty(name) && data != null && data.StartTime != default(DateTime))
            {
                name = data.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            return string.IsNullOrEmpty(name) ? "?" : name;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  «Рассчитать»: подгонка энергии по линиям и FWHM по всем строкам
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// (`AMBER276`) Линии калибровки по энергии — точками прежней подгонки вкладки
        /// энергии (канал, энергия, отсчёты), по возрастанию канала. Энергия переходит в
        /// <c>decimal</c> тем же путём, каким прежде её держала таблица энергии, и решатель
        /// получает ровно прежние числа.
        /// </summary>
        internal static List<CalibrationPoint> EnergyPoints(List<CalibrationPeak> lines)
        {
            List<CalibrationPoint> points = new List<CalibrationPoint>();
            if (lines == null)
            {
                return points;
            }
            foreach (CalibrationPeak line in lines.OrderBy(l => l.Channel))
            {
                if (line != null && line.EnergyLine)
                {
                    points.Add(new CalibrationPoint(line.Channel, (decimal)line.Energy, line.Count));
                }
            }
            return points;
        }

        /// <summary>
        /// (`AMBER276`) Подгонка шкалы энергии — прежняя арифметика «Выполнить
        /// калибровку» вкладки энергии без изменений (`AMBER188`, `S42`): одна точка
        /// получает синтетическую (0, 0) в ЛОКАЛЬНОЙ копии списка, степень 0 значит 1,
        /// степень понижается сторожем изгиба (<c>SolveGuarded</c>). <c>null</c> —
        /// годной подгонки нет.
        ///
        /// (`AMBER270`) Кривизна тракта подгоняется РЯДОМ, а не здесь
        /// (<see cref="FitTractCurvature"/>): прямая карта разбора FSA, которая её
        /// читает, берёт у полинома калибровки только две точки (энергию в точке нуля и
        /// канал верхней линии), а между ними кладёт свет в канал сама. Полином и κ —
        /// две разные модели одних точек; полином остаётся прежним побитово.
        /// </summary>
        internal static double[] SolveEnergy(List<CalibrationPoint> energyPoints, int polynomialOrder, int channels, bool weighted, out int usedOrder)
        {
            usedOrder = 0;
            int order = polynomialOrder == 0 ? 1 : polynomialOrder;
            List<CalibrationPoint> points = new List<CalibrationPoint>(energyPoints);
            if (points.Count == 1)
            {
                points.Add(new CalibrationPoint(0, 0, 0));
            }
            return CalibrationSolver.SolveGuarded(points, order, channels, weighted, out usedOrder);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Кривизна тракта (`AMBER270`)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>(`AMBER270`) Итог подгонки кривизны тракта: κ либо причина, по которой её нет.</summary>
        public sealed class TractCurvatureFit
        {
            /// <summary>κ подогнана.</summary>
            public bool Ok;
            /// <summary>κ, 1/МэВ шкалы карты FSA.</summary>
            public double Kappa;
            /// <summary>Свет в точке нуля карты z₀, кэВ.</summary>
            public double ZeroLightKev;
            /// <summary>Наибольший промах линии по подогнанной карте, каналы.</summary>
            public double WorstChannels;
            /// <summary>Линий в подгонке.</summary>
            public int Lines;
            /// <summary>
            /// Из них — каналом центра масс пика (<see cref="CentroidOf"/>). С `AMBER295` у
            /// подгонки по строкам это все линии: строка с неизмеренным центром отбрасывается
            /// (<see cref="Rejected"/>), а не идёт целым каналом.
            /// </summary>
            public int CentroidLines;
            /// <summary>Верхняя линия набора, кэВ.</summary>
            public double TopLineKev;
            /// <summary>Имя фотонной кривой света (как у разбора: <c>FsaLightScale.CurveFor</c>).</summary>
            public string Curve;
            /// <summary>Вещество, по которому взята кривая.</summary>
            public string Material;
            /// <summary>Причина отказа словами (ресурс), null — подогнана.</summary>
            public string Refusal;
            /// <summary>
            /// (`AMBER295`, 14.11.2026) Линии, не пошедшие в κ, по возрастанию энергии: центр
            /// пика не измерен либо промах от подгонки больше <see cref="TractCurvatureMissFwhm"/>.
            /// </summary>
            public List<TractLineRejection> Rejected = new List<TractLineRejection>();
            /// <summary>Отказ «линий меньше трёх» (после отбора) — его текст заменяет обёртка по строкам.</summary>
            public bool TooFewLines;
            /// <summary>
            /// (`AMBER295`) Сумм-пики, пошедшие в κ светом суммы: {E, E₁, E₂}, кэВ
            /// (<see cref="SumPartsOf"/>).
            /// </summary>
            public List<double[]> SumPeaks = new List<double[]>();
            /// <summary>
            /// (`AMBER295`, третий круг) Сумм-пик, взятый кнопкой самой: {E, E₁, E₂, канал центра};
            /// null — не брался (<see cref="FindAutoSum"/>).
            /// </summary>
            public double[] AutoSum;
            /// <summary>
            /// (`AMBER295`) Сумм-пик, который искали и не нашли: {E, E₁, E₂, ожидаемый канал, число
            /// каналов спектра, если пик за его краем, иначе 0}; null — не искали или нашли.
            /// </summary>
            public double[] AutoSumMissing;
        }

        /// <summary>(`AMBER295`) Линия, отброшенная из подгонки κ, и почему.</summary>
        public sealed class TractLineRejection
        {
            /// <summary>Энергия линии, кэВ.</summary>
            public double Energy;
            /// <summary>Центр пика по спектру строки не измерен (<see cref="CentroidOf"/> — <c>NaN</c>).</summary>
            public bool CentreNotMeasured;
            /// <summary>
            /// (`AMBER295`) Строка другой съёмки: пришла «Собрать всё» из другого документа
            /// (<see cref="CalibrationPeak.Source"/>); в κ не идёт.
            /// </summary>
            public bool OtherAcquisition;
            /// <summary>Промах центра пика от подгонки «свет → канал», каналы (при <see cref="CentreNotMeasured"/> — <c>NaN</c>).</summary>
            public double MissChannels = double.NaN;
            /// <summary>Тот же промах в долях FWHM строки.</summary>
            public double MissFwhm = double.NaN;
        }

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Отказ с причиной
        /// (Рекомендую)») ПОРОГ ПРОМАХА ЛИНИИ ДЛЯ κ, доли FWHM строки: линии, чей центр пика
        /// отходит от подгонки «свет → канал» дальше, не согласны (порядок отбора — в ядре,
        /// перегрузка с FWHM линий). Промах мерится подгонкой без весов (в обоих режимах) по
        /// центрам гаусса. Обоснование — журнал `handover/handover-2026-11-14-a270k.md` §2: на
        /// годных спектрах, где κ пишется (Ra-226 Gamma-1S, восемь; ASN16 природные с 1764 кэВ),
        /// наибольший промах 0.059 FWHM; у Th-232 NaI (дублет 911 + 969 кэВ) — до 0.114; у
        /// мутантов критика `crit270k` (центр линии сдвинут на 0.2…0.5 FWHM) — 0.111…0.177, с
        /// промахом 140 каналов — 0.80…1.67. При центре масс (первый круг) годные доходили до
        /// 0.164, с весами — до 0.228, и порог стоял 0.25: мутанты 0.2–0.5 FWHM им не ловились.
        /// </summary>
        public const double TractCurvatureMissFwhm = 0.10;

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Две линии ≥ 1.5 МэВ
        /// (Рекомендую)») Нижняя граница «верхних» линий, кэВ: годных линий от неё нужно не
        /// меньше двух — они проверяют друг друга; иначе κ не подгоняется.
        /// </summary>
        public const double TractCurvatureHighLineKev = 1500.0;

        /// <summary>
        /// (`AMBER295`) Допуск совпадения энергии сумм-пика с суммой двух линий таблицы, кэВ:
        /// энергии строк вписаны из библиотеки с точностью до сотых — тысячных, вручную — до
        /// десятых (1173.2 + 1332.5 = 2505.7).
        /// </summary>
        public const double TractCurvatureSumToleranceKev = 0.3;

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Да, не меньше четырёх
        /// (Рекомендую)») Наименьшее число годных линий для κ: свободных чисел три, на трёх
        /// линиях квадрат проходит через все точки и линиям нечем проверить друг друга.
        /// </summary>
        public const int TractCurvatureMinLines = 4;

        /// <summary>
        /// (`AMBER295`) Доля силы каскада, начиная с которой одиночная гамма-линия того же нуклида
        /// при энергии суммы делает эту энергию фотопиком, а не сумм-пиком: сумм-пик слабее
        /// каскада на полную эффективность второго кванта (порядка 0.01…0.3), и одиночная
        /// линия в 5 % каскада уже сопоставима с ним. Bi-214 1729.6 (2.8 % при силе 609.3 → 1120.3
        /// около 15) — фотопик; Y-88 2734.0 (0.7 % при 93) и Tl-208 3197.7 (нет) — суммы.
        /// </summary>
        public const double TractCurvatureCrossoverShare = 0.05;


        /// <summary>
        /// ⛔ (`AMBER270`, 29.10.2026) ВЕЩЕСТВО КРИСТАЛЛА СПЕКТРА — ДЛЯ КРИВОЙ СВЕТА, тем
        /// же путём, что у разбора FSA: вещество геометрии кривой эффективности спектра
        /// (<c>EfficiencySimulator.ScintillatorNameOf</c> — его же кладут в разбор
        /// <c>FsaMatrixBinding.Bind</c> и <c>FsaEfficiency</c>). Геометрии нет — поле
        /// прибора «Вещество кристалла» (<c>DeviceConfigInfo.CrystalMaterialName</c>,
        /// `A276`: геометрия первична, поле — запасной источник). Пусто — вещество не
        /// названо.
        /// </summary>
        public static string ScintillatorOf(ResultData resultData)
        {
            if (resultData == null)
            {
                return "";
            }
            EfficiencyConfigData efficiency = resultData.Efficiency;
            try
            {
                if (efficiency != null && efficiency.HasGeometry && efficiency.Geometry != null && efficiency.Geometry.Crystal != null)
                {
                    string name = EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(efficiency.Geometry);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
                string named = resultData.DeviceConfig != null ? resultData.DeviceConfig.CrystalMaterialName : null;
                if (!string.IsNullOrEmpty(named))
                {
                    EfficiencyMaker.GeometryMaterialLibrary.Entry entry = EfficiencyMaker.GeometryMaterialLibrary.ByName(named);
                    EfficiencyMaker.GeometryMaterial material = entry != null
                        ? EfficiencyMaker.GeometryMaterialLibrary.Make(entry, entry.Density)
                        : null;
                    if (material != null)
                    {
                        EfficiencyMaker.GeometryModel probe = new EfficiencyMaker.GeometryModel();
                        probe.Crystal = material;
                        return EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(probe);
                    }
                }
            }
            catch (Exception)
            {
                // библиотека веществ или геометрия не читаются — вещество не названо
            }
            return "";
        }

        /// <summary>
        /// ⛔ (`AMBER270`, 29.10.2026) КРИВИЗНА ТРАКТА ПО ТОЧКАМ КАЛИБРОВКИ ПО ЭНЕРГИИ.
        /// Amber, консоль, дословно: «При калибровке по энергии так же проводить
        /// калибровку по кривизне тракта.» Вопросником того же дня, дословно: «Спектра,
        /// прибор — умолчание (Рекомендую)» — κ ложится в спектр, в прибор — кнопкой
        /// сохранения.
        ///
        /// ФОРМУЛА — ТА ЖЕ, ЧТО У РАЗБОРА (<c>FsaAnalyzer.LightToChannel</c> прямой карты,
        /// <c>AdcScaleOf</c>, <c>TractChannel</c>): фотопик энергии E даёт свет
        /// x = E + s(E), s — фотонная кривая света вещества кристалла
        /// (<c>FsaLightScale.ShiftKev</c>, опорная энергия и порог — умолчания разбора),
        /// и стоит в канале c = c₀ + v·(1 + q·v), v = ℓ/G, q = κ·G/1000, где
        /// ℓ = (x − z₀)·s₁ — свет в кэВ шкалы карты от точки нуля (s₁ = (E₁ − e₀)/(x₁ − z₀),
        /// E₁ — верхняя линия, e₀ — энергия калибровки в точке нуля карты), G — шаг карты,
        /// c₀ — канал точки нуля (умолчания разбора). Это квадрат по свету:
        /// c = c₀ + a·(x − z₀) + b·(x − z₀)², a = s₁/G, b = κ·s₁²/(1000·G), то есть
        /// κ = 1000·b/(a·s₁). Подгоняется МНК c(x) второй степени (веса — как у шкалы
        /// энергии), z₀ — корень c(z₀) = c₀, κ — по формуле выше. Три свободных числа —
        /// нуль света, шаг и κ: линий нужно не меньше трёх.
        ///
        /// Без кривой света кривизну фотопиков в каналах несёт полином калибровки, и
        /// отделить от неё кривизну тракта нечем (у разбора то же: прямая карта
        /// включается только с кривой) — тогда κ не подгоняется, и причина возвращается
        /// словами (<see cref="TractCurvatureFit.Refusal"/>).
        ///
        /// ⛔ (решения Amber вопросником 30.10.2026, дословно) Набор линий — «Только с
        /// линиями до ≥ 2 МэВ (Рекомендую)»: без линии от <see cref="TractCurvatureTopLineKev"/>
        /// κ не подгоняется (ядро, отказ словами). Канал линии — «Центр масс пика
        /// (Рекомендую)»: дробный центр масс пика строки по спектру (<see cref="CentroidOf"/>),
        /// а не целый канал строки; строки таблицы и файл не меняются. Целые каналы стоили
        /// κ до ±0.001 1/МэВ на 1024 каналах (журнал `amber280` §3).
        /// </summary>
        /// <param name="lines">строки единой калибровки; в подгонку идут линии энергии
        /// (<see cref="CalibrationPeak.EnergyLine"/>), канал — один раз, как у шкалы энергии</param>
        /// <param name="spectrum">спектр, чья это таблица (активный документ), — по нему мерится
        /// центр масс его строк, у которых он ещё не измерен (<see cref="CentroidOf"/>)</param>
        /// <remarks>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Отказ с причиной
        /// (Рекомендую)») ОТБОР ЛИНИЙ. Прежде строка с неизмеренным центром шла в κ целым
        /// каналом, а промах линии не проверялся, и κ ложилась в спектр как есть: на граните
        /// ASN16 — −0.0224 1/МэВ при промахе линии на 140 каналов, у `BrazilNuts` — −0.036
        /// (верное ≈ −0.009; находка полос `a295k` и `crit295k`). Теперь: центр не измерен —
        /// линия вон; промах от подгонки больше <see cref="TractCurvatureMissFwhm"/> FWHM —
        /// вон (ядро); годных меньше трёх — отказ, κ спектра прежняя, причина называет
        /// отброшенные линии.
        /// </remarks>
        public static TractCurvatureFit FitTractCurvature(List<CalibrationPeak> lines, EnergySpectrum spectrum,
                                                            EnergyCalibration energyScale, string scintillator, bool weighted)
        {
            var channels = new List<double>();
            var energies = new List<double>();
            var weights = new List<double>();
            var fwhms = new List<double>();
            var parts = new List<double[]>();
            var notMeasured = new List<TractLineRejection>();
            var seen = new HashSet<int>();
            if (lines != null)
            {
                // энергии СВОИХ линий таблицы — для распознавания сумм-пиков (`SumPartsOf`)
                List<double> tableEnergies = lines.Where(l => l != null && l.EnergyLine && l.Energy > 0.0 && string.IsNullOrEmpty(l.Source))
                                                  .Select(l => l.Energy).ToList();
                // порядок и отбор — как у точек шкалы энергии (`EnergyPoints`)
                foreach (CalibrationPeak line in lines.Where(l => l != null).OrderBy(l => l.Channel))
                {
                    if (!line.EnergyLine || !seen.Add(line.Channel))
                    {
                        continue;
                    }
                    // ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «κ только по
                    // строкам спектра (Рекомендую)») строка из «Собрать всё» — другой съёмки (другое
                    // усиление, другая сцена): в шкалу энергии и FWHM идёт, в κ — нет
                    if (!string.IsNullOrEmpty(line.Source))
                    {
                        notMeasured.Add(new TractLineRejection { Energy = line.Energy, OtherAcquisition = true });
                        continue;
                    }
                    double centroid = CentroidOf(line, spectrum);
                    if (double.IsNaN(centroid))
                    {
                        // (`AMBER295`) центр не измерен — строка не на пике своего спектра
                        // либо чужая: её целый канал в κ больше не идёт
                        notMeasured.Add(new TractLineRejection { Energy = line.Energy, CentreNotMeasured = true });
                        continue;
                    }
                    channels.Add(centroid);
                    energies.Add(line.Energy);
                    weights.Add(weighted && line.Count > 0 ? (double)line.Count : 1.0);
                    fwhms.Add(line.FWHM);
                    parts.Add(SumPartsOf(line.Energy, tableEnergies));
                }
            }
            // ⛔ (`AMBER295`, третий круг; решение Amber вопросником 14.11.2026, дословно: «Сумм-пик
            // 3197.7 как вторая (Рекомендую)») ВТОРАЯ ВЕРХНЯЯ ЛИНИЯ — СУММ-ПИК, НАЙДЕННЫЙ САМ. Когда
            // годных линий от 1500 кэВ меньше двух (торий: сверху одна 2614.5), кнопка ищет сумм-пик
            // каскадной пары линий таблицы (583.2 + 2614.5 = 3197.7 кэВ Tl-208) у ожидаемого канала
            // и, если гаусс мерит его центр, берёт его строкой в κ светом суммы и называет это. Не
            // нашла — отказ с причиной «добавьте сумм-пик». В таблицу калибровки строка не
            // добавляется: шкала энергии сумм-пиком не подгоняется.
            double[] autoSum = null, autoMissing = null;
            if (spectrum != null && lines != null && energies.Count(e => e >= TractCurvatureHighLineKev) < 2)
            {
                List<double> table = lines.Where(l => l != null && l.EnergyLine && l.Energy > 0.0 && string.IsNullOrEmpty(l.Source)).Select(l => l.Energy).ToList();
                double[] pair = AutoSumPair(energies, parts, table);
                if (pair != null)
                {
                    double expected, fwhm;
                    int peakChannel;
                    double centre = FindAutoSum(spectrum, channels, energies, fwhms, parts, pair, out expected, out fwhm, out peakChannel);
                    double sumEnergy = pair[0] + pair[1];
                    if (!double.IsNaN(centre))
                    {
                        channels.Add(centre);
                        energies.Add(sumEnergy);
                        int count = spectrum.Spectrum[peakChannel];
                        weights.Add(weighted && count > 0 ? (double)count : 1.0);
                        fwhms.Add(fwhm);
                        parts.Add(pair);
                        autoSum = new[] { sumEnergy, pair[0], pair[1], centre };
                    }
                    else
                    {
                        int n = Math.Min(spectrum.NumberOfChannels, spectrum.Spectrum.Length);
                        bool outside = !double.IsNaN(expected) && !double.IsNaN(fwhm) && expected > n - 1 - 0.5 * fwhm;
                        autoMissing = new[] { sumEnergy, pair[0], pair[1], expected, outside ? n : 0.0 };
                    }
                }
            }
            TractCurvatureFit fit = FitTractCurvature(channels, energies, weights, fwhms, parts, energyScale, scintillator);
            fit.CentroidLines = fit.Lines;
            fit.AutoSum = autoSum != null && fit.SumPeaks.Any(sp => Math.Abs(sp[0] - autoSum[0]) < 1e-9) ? autoSum : (fit.Ok ? null : autoSum);
            fit.AutoSumMissing = autoMissing;
            if (notMeasured.Count > 0)
            {
                fit.Rejected.AddRange(notMeasured);
                fit.Rejected.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            }
            if (!fit.Ok)
            {
                var reason = new List<string>();
                reason.Add(fit.TooFewLines && (fit.Rejected.Count > 0 || autoSum != null)
                    ? String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureFewGoodLines,
                                    fit.Lines.ToString(CultureInfo.InvariantCulture),
                                    TractCurvatureMinLines.ToString(CultureInfo.InvariantCulture))
                    : fit.Refusal);
                if (autoMissing != null)
                {
                    reason.Add(AutoSumMissingNote(autoMissing));
                }
                if (autoSum != null)
                {
                    // сумм-пик взят сам, но κ всё равно нет: человек должен видеть, что он был в подгонке
                    reason.Add(String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureAutoSum,
                                             autoSum[0].ToString("0.###", CultureInfo.InvariantCulture),
                                             autoSum[1].ToString("0.###", CultureInfo.InvariantCulture),
                                             autoSum[2].ToString("0.###", CultureInfo.InvariantCulture),
                                             autoSum[3].ToString("0.0", CultureInfo.InvariantCulture)));
                }
                if (fit.Rejected.Count > 0)
                {
                    reason.Add(RejectedNote(fit));
                }
                if (autoSum != null)
                {
                    // (`AMBER295`, находка 1 второго круга критика) сумм-пик взят, а κ всё равно нет —
                    // выход тот же, что при сумм-пике за краем спектра (решение Amber «κ по Ra-226»)
                    reason.Add(Resources.MSGTractCurvatureUseRa226);
                }
                fit.Refusal = string.Join("; ", reason);
            }
            return fit;
        }

        /// <summary>
        /// (`AMBER295`, третий круг) Каскадная пара для сумм-пика, найденного самим: две одиночные
        /// измеренные линии с суммой от <see cref="TractCurvatureHighLineKev"/>, которой нет
        /// строкой в таблице, и наибольшей силой каскада в `nucdb` (<see cref="CascadeFraction"/>). null — пары нет.
        /// </summary>
        static double[] AutoSumPair(List<double> energies, List<double[]> parts, List<double> table)
        {
            double[] best = null;
            double bestFraction = 0.0;
            for (int i = 0; i < energies.Count; i++)
            {
                for (int j = i + 1; j < energies.Count; j++)
                {
                    if (parts[i] != null || parts[j] != null)
                    {
                        continue;
                    }
                    double sum = energies[i] + energies[j];
                    if (sum < TractCurvatureHighLineKev || table.Any(e => Math.Abs(e - sum) <= TractCurvatureSumToleranceKev))
                    {
                        continue;
                    }
                    double fraction = CascadeFraction(Math.Min(energies[i], energies[j]), Math.Max(energies[i], energies[j]));
                    if (fraction > bestFraction || (fraction == bestFraction && fraction > 0.0 && sum > best[0] + best[1]))
                    {
                        bestFraction = fraction;
                        best = new[] { Math.Min(energies[i], energies[j]), Math.Max(energies[i], energies[j]) };
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// (`AMBER295`, третий круг) Ищет сумм-пик пары <paramref name="pair"/>: ожидаемый канал —
        /// c₁ + c₂ − c₀ (свет суммы — сумма светов; c₀ — канал нуля по прямой МНК «энергия →
        /// центр» одиночных линий), FWHM — от верхней линии пары как √E; в окне ±1.5 FWHM берётся
        /// канал наибольшего счёта, а центр — гауссом на прямой (<see cref="LineCentroid"/>).
        /// NaN — пика нет (центр не измерен или вне окна).
        /// </summary>
        static double FindAutoSum(EnergySpectrum spectrum, List<double> channels, List<double> energies, List<double> fwhms,
                                  List<double[]> parts, double[] pair, out double expected, out double fwhm, out int peakChannel)
        {
            expected = double.NaN;
            fwhm = double.NaN;
            peakChannel = -1;
            int i1 = -1, i2 = -1;
            double sx = 0.0, sy = 0.0, sxx = 0.0, sxy = 0.0;
            int k = 0;
            for (int i = 0; i < energies.Count; i++)
            {
                if (parts[i] != null)
                {
                    continue;
                }
                if (i1 < 0 && Math.Abs(energies[i] - pair[0]) < 1e-9) i1 = i;
                if (i2 < 0 && Math.Abs(energies[i] - pair[1]) < 1e-9) i2 = i;
                sx += energies[i];
                sy += channels[i];
                sxx += energies[i] * energies[i];
                sxy += energies[i] * channels[i];
                k++;
            }
            double det = k * sxx - sx * sx;
            if (i1 < 0 || i2 < 0 || k < 2 || !(Math.Abs(det) > 0.0))
            {
                return double.NaN;
            }
            double slope = (k * sxy - sx * sy) / det;
            double zero = (sy - slope * sx) / k;
            expected = channels[i1] + channels[i2] - zero;
            fwhm = fwhms[i2] * Math.Sqrt((pair[0] + pair[1]) / pair[1]);
            int n = Math.Min(spectrum.NumberOfChannels, spectrum.Spectrum.Length);
            int half = Math.Max(3, (int)Math.Ceiling(1.5 * fwhm));
            if (!(slope > 0.0) || expected > n - 1 - 0.5 * fwhm)
            {
                // сумм-пик лежит за краем спектра (Gamma-1S: 1024 канала до ~3 МэВ)
                return double.NaN;
            }
            int start = Math.Max(0, (int)Math.Round(expected) - half);
            int end = Math.Min(n - 1, (int)Math.Round(expected) + half);
            if (start >= end)
            {
                return double.NaN;
            }
            int top = start;
            for (int c = start; c <= end; c++)
            {
                if (spectrum.Spectrum[c] > spectrum.Spectrum[top])
                {
                    top = c;
                }
            }
            peakChannel = top;
            double centre = LineCentroid(spectrum, top, fwhm);
            if (double.IsNaN(centre) || Math.Abs(centre - expected) > 1.5 * fwhm)
            {
                return double.NaN;
            }
            return centre;
        }

        /// <summary>(`AMBER295`) Причина «сумм-пик не найден — добавьте его строкой».</summary>
        static string AutoSumMissingNote(double[] missing)
        {
            if (missing.Length > 4 && missing[4] > 0.0)
            {
                return String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureAutoSumOutside,
                                     missing[0].ToString("0.###", CultureInfo.InvariantCulture),
                                     missing[1].ToString("0.###", CultureInfo.InvariantCulture),
                                     missing[2].ToString("0.###", CultureInfo.InvariantCulture),
                                     missing[3].ToString("0", CultureInfo.InvariantCulture),
                                     missing[4].ToString("0", CultureInfo.InvariantCulture));
            }
            return String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureAutoSumMissing,
                                 missing[0].ToString("0.###", CultureInfo.InvariantCulture),
                                 missing[1].ToString("0.###", CultureInfo.InvariantCulture),
                                 missing[2].ToString("0.###", CultureInfo.InvariantCulture),
                                 double.IsNaN(missing[3]) ? "?" : missing[3].ToString("0", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER295`) Отброшенные линии словами: «отброшено N из M: E кэВ — причина; …».
        /// Пусто — не отброшено ни одной.
        /// </summary>
        internal static string RejectedNote(TractCurvatureFit fit)
        {
            if (fit == null || fit.Rejected == null || fit.Rejected.Count == 0)
            {
                return string.Empty;
            }
            // (`AMBER295`, находка 5 критика `crit270k`) свёрнуто по причине: строки без
            // центра — одним перечнем энергий (не больше шести, дальше «+N»), иначе длинный
            // перечень вытеснял из строки под таблицей итог FWHM
            var parts = new List<string>();
            List<string> other = fit.Rejected.Where(r => r.OtherAcquisition)
                                    .Select(r => r.Energy.ToString("0.###", CultureInfo.InvariantCulture)).ToList();
            if (other.Count > 0)
            {
                string list = string.Join(", ", other.Take(6));
                if (other.Count > 6)
                {
                    list += " +" + (other.Count - 6).ToString(CultureInfo.InvariantCulture);
                }
                parts.Add(String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureLineOtherAcquisition, list));
            }
            List<string> noCentre = fit.Rejected.Where(r => r.CentreNotMeasured)
                                       .Select(r => r.Energy.ToString("0.###", CultureInfo.InvariantCulture)).ToList();
            if (noCentre.Count > 0)
            {
                string list = string.Join(", ", noCentre.Take(6));
                if (noCentre.Count > 6)
                {
                    list += " +" + (noCentre.Count - 6).ToString(CultureInfo.InvariantCulture);
                }
                parts.Add(String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureLineNoCentre, list));
            }
            foreach (TractLineRejection r in fit.Rejected.Where(r => !r.CentreNotMeasured && !r.OtherAcquisition))
            {
                parts.Add(String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureLineMiss,
                                        r.Energy.ToString("0.###", CultureInfo.InvariantCulture),
                                        RoundUp(r.MissFwhm).ToString("0.00", CultureInfo.InvariantCulture),
                                        r.MissChannels.ToString("0.0", CultureInfo.InvariantCulture),
                                        TractCurvatureMissFwhm.ToString("0.00", CultureInfo.InvariantCulture)));
            }
            return String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureRejected,
                                 fit.Rejected.Count.ToString(CultureInfo.InvariantCulture),
                                 (fit.Rejected.Count + fit.Lines).ToString(CultureInfo.InvariantCulture),
                                 string.Join("; ", parts));
        }

        /// <summary>
        /// ⛔ (`AMBER270`, решение Amber вопросником 30.10.2026 о наборе линий для κ,
        /// дословно: «Только с линиями до ≥ 2 МэВ (Рекомендую)») Нижняя граница
        /// верхней линии набора, кэВ: без линии от неё κ не подгоняется. Замер полосы: набор
        /// с верхом 2614 кэВ (Th-228) дал κ сумм-пиков с точностью +0.0003…+0.0010 1/МэВ, набор
        /// с верхом 1408 кэВ (Eu-152) — втрое-вчетверо выше: ошибку фотонной кривой света
        /// ниже 400 кэВ подгонка без длинного плеча принимает за кривизну тракта.
        /// </summary>
        public const double TractCurvatureTopLineKev = 2000.0;

        /// <summary>
        /// ⛔ (`AMBER270`, решение Amber вопросником 30.10.2026, дословно: «Центр масс пика
        /// (Рекомендую)») КАНАЛ ЛИНИИ ДЛЯ κ — ДРОБНЫЙ ЦЕНТР МАСС ЕЁ ПИКА ПО ЕЁ СПЕКТРУ:
        /// <list type="number">
        /// <item>измеренный, когда строка заведена (щелчок по пику, «Добавить в калибровку»)
        /// или собрана «Собрать всё» из документа, откуда она (<see cref="CalibrationPeak.SpectrumCentroid"/>,
        /// пока канал строки тот же);</item>
        /// <item>иначе — по <paramref name="spectrum"/> (<see cref="LineCentroid"/>), но только
        /// если строка — ЕГО: отсчёты в канале строки (<see cref="CalibrationPeak.Count"/>) те
        /// же, что у спектра в этом канале. ⛔ Без этой сверки строка, собранная из другого
        /// спектра и прочитанная из файла прежней версии (центра в нём нет; с `AMBER295` он пишется в файл),
        /// мерилась бы по ЧУЖОМУ пику: замер пробы `TractCurvatureFitProbeA270` на шести
        /// источниках G1S24 — κ 0.00776 вместо 0.00591 1/МэВ, промах линии до 6.5 канала.</item>
        /// </list>
        /// <c>NaN</c> — центр не измерен (строка не этого спектра либо пика у её канала нет):
        /// строка в κ не идёт (`AMBER295`, прежде шла своим целым каналом).
        /// </summary>
        public static double CentroidOf(CalibrationPeak line, EnergySpectrum spectrum)
        {
            if (line == null)
            {
                return double.NaN;
            }
            if (line.SpectrumCentroidChannel == line.Channel && !double.IsNaN(line.SpectrumCentroid))
            {
                return line.SpectrumCentroid;
            }
            int[] counts = spectrum != null ? spectrum.Spectrum : null;
            if (counts == null || line.Channel < 0 || line.Channel >= counts.Length || line.Count <= 0 || counts[line.Channel] != line.Count)
            {
                return double.NaN;
            }
            return LineCentroid(spectrum, line.Channel, line.FWHM);
        }

        /// <summary>
        /// (`AMBER270`) Мерит центр пика строки (с `AMBER295` — гаусс на прямой) по её спектру и запоминает его в строке
        /// (<see cref="CalibrationPeak.SpectrumCentroid"/>): когда строка заведена (спектр
        /// может набираться дальше — отсчёты канала разойдутся с весом строки) и когда она
        /// уходит «Собрать всё» в таблицу ДРУГОГО спектра. Не измерился — строка без него.
        /// </summary>
        static void RememberCentroid(CalibrationPeak line, EnergySpectrum ownSpectrum)
        {
            if (line == null)
            {
                return;
            }
            double centroid = CentroidOf(line, ownSpectrum);
            line.SpectrumCentroid = centroid;
            line.SpectrumCentroidChannel = double.IsNaN(centroid) ? -1 : line.Channel;
        }

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Гаусс для κ
        /// (Рекомендую)»; отменяет для κ её же «Центр масс пика (Рекомендую)» 30.10.2026)
        /// ЦЕНТР ПИКА СТРОКИ ДЛЯ κ — ГАУСС НА ПРЯМОЙ у канала <paramref name="channel"/>:
        /// подгонка y = b₀ + b₁·(x − x̄) + A·exp(−(x − μ)²/2σ²) по отсчётам файла (а не по
        /// показанному виду — κ не зависит от вычтенного на экране фона), веса Пуассона, окно
        /// ±<see cref="TractCentreWindowFwhm"/> FWHM строки, не меньше ±3 каналов; начало — центр
        /// масс тем же способом, что при выделении пика (<see cref="SpectrumAriphmetics.FindCentroid(EnergySpectrum, int, int, int, bool)"/>),
        /// σ — FWHM строки / 2.355 и держится в <see cref="TractCentreSigmaMin"/>…<see cref="TractCentreSigmaMax"/> от неё. Прежний центр масс ядра пика
        /// смещался на слабых пиках и на склоне комптона: у `ASN16_Charoite` 1460.8 кэВ — на
        /// 19 каналов (0.13 FWHM), и κ чароитов выходила −0.0025 и −0.0126 при верной ≈ −0.009
        /// (журнал `a295k`, раздел 1). <c>NaN</c> — центр не измерен: окно плоское, подгонка
        /// не сошлась, площадь не положительна, ошибка центра больше ¼ FWHM либо центр ушёл
        /// от канала строки дальше max(1, ½ FWHM) (строка стоит не на пике этого спектра).
        /// Имя прежнее (его зовут пробы), смысл — «центр пика для κ».
        /// </summary>
        public static double LineCentroid(EnergySpectrum spectrum, int channel, double fwhmChannels)
        {
            if (spectrum == null || spectrum.Spectrum == null)
            {
                return double.NaN;
            }
            int n = Math.Min(spectrum.NumberOfChannels, spectrum.Spectrum.Length);
            if (channel < 0 || channel >= n)
            {
                return double.NaN;
            }
            double fwhm = fwhmChannels > 0.0 && !double.IsInfinity(fwhmChannels) ? fwhmChannels : 0.0;
            int seedHalf = Math.Max(3, (int)Math.Ceiling(1.5 * fwhm));
            double seed = new SpectrumAriphmetics().FindCentroid(spectrum, channel, Math.Max(0, channel - seedHalf),
                                                                  Math.Min(n - 1, channel + seedHalf), true);
            if (double.IsNaN(seed) || double.IsInfinity(seed))
            {
                seed = channel;
            }
            int half = Math.Max(3, (int)Math.Ceiling(TractCentreWindowFwhm * fwhm));
            int start = Math.Max(0, channel - half);
            int end = Math.Min(n - 1, channel + half);
            double sigma0 = fwhm > 0.0 ? fwhm / 2.3548200450309493 : Math.Max(1.0, (end - start) / 6.0);
            double centre = GaussOnLineCentre(spectrum.Spectrum, start, end, seed, sigma0);
            if (double.IsNaN(centre) || double.IsInfinity(centre) || Math.Abs(centre - channel) > Math.Max(1.0, 0.5 * fwhm))
            {
                return double.NaN;
            }
            return centre;
        }

        /// <summary>
        /// (`AMBER295`) Окно гаусса центра пика для κ, доли FWHM строки в каждую сторону: то же
        /// ±1.5 FWHM, каким «Добавить в калибровку» выделяет пик (<see cref="AddDetectedPeak"/>).
        /// </summary>
        public const double TractCentreWindowFwhm = 1.5;

        /// <summary>
        /// (`AMBER295`) Пределы σ гаусса центра пика от σ строки (FWHM строки — из калибровки
        /// FWHM, она известна с точностью порядка десяти процентов). Опыт полосы (журнал
        /// `a270k`, раздел 2): при пределах 0.5…2 гаусс на дублетах NaI (911 + 969 кэВ Ac-228)
        /// расширялся и съедал соседа — центр 911 не мерился у 6 спектров Th-232 (набор 239…2615), κ
        /// `G1S24_Th232_Petri` 0.00014; при 0.85…1.2 — не мерился у двух, κ −0.0072. На ASN16 пределы
        /// κ не меняют (−0.00867 и −0.00868 у `ASN16_Th232_Am241`).
        /// </summary>
        public const double TractCentreSigmaMin = 0.85;

        /// <summary>(`AMBER295`) Верхний предел σ гаусса центра пика от σ строки (<see cref="TractCentreSigmaMin"/>).</summary>
        public const double TractCentreSigmaMax = 1.2;

        /// <summary>
        /// (`AMBER295`) Центр гаусса на прямой в окне [<paramref name="start"/>, <paramref name="end"/>]
        /// — Левенберг — Марквардт по пяти числам (b₀, b₁, A, μ, σ), веса 1/max(y, 1). <c>NaN</c> —
        /// не сошлось, площадь не положительна или ошибка центра (ковариация, умноженная на
        /// max(1, χ²/ndf)) больше ¼ FWHM.
        /// </summary>
        static double GaussOnLineCentre(int[] counts, int start, int end, double seed, double sigma0)
        {
            int m = end - start + 1;
            if (m < 7 || !(sigma0 > 0.0))
            {
                return double.NaN;
            }
            double[] x = new double[m], y = new double[m], w = new double[m];
            for (int i = 0; i < m; i++)
            {
                x[i] = start + i;
                y[i] = counts[start + i];
                w[i] = 1.0 / Math.Max(y[i], 1.0);
            }
            double xc = 0.5 * (x[0] + x[m - 1]);
            double left = (y[0] + y[1]) / 2.0, right = (y[m - 1] + y[m - 2]) / 2.0;
            double b1 = (right - left) / Math.Max(1.0, x[m - 1] - x[0] - 1.0);
            double b0 = 0.5 * (left + right);
            int k0 = (int)Math.Round(seed) - start;
            k0 = Math.Max(0, Math.Min(m - 1, k0));
            double a = y[k0] - (b0 + b1 * (x[k0] - xc));
            if (!(a > 0.0))
            {
                return double.NaN;
            }
            double[] p = { b0, b1, a, seed, sigma0 };
            double sMin = TractCentreSigmaMin * sigma0, sMax = TractCentreSigmaMax * sigma0;
            Func<double[], double> chi2 = q =>
            {
                double sum = 0.0;
                for (int i = 0; i < m; i++)
                {
                    double d = (x[i] - q[3]) / q[4];
                    double r = y[i] - (q[0] + q[1] * (x[i] - xc) + q[2] * Math.Exp(-0.5 * d * d));
                    sum += w[i] * r * r;
                }
                return sum;
            };
            double current = chi2(p);
            double lambda = 1e-3;
            double[,] jtj = new double[5, 5];
            bool converged = false;
            for (int iteration = 0; iteration < 200; iteration++)
            {
                double[] jtr = new double[5];
                Array.Clear(jtj, 0, jtj.Length);
                for (int i = 0; i < m; i++)
                {
                    double d = (x[i] - p[3]) / p[4];
                    double g = Math.Exp(-0.5 * d * d);
                    double r = y[i] - (p[0] + p[1] * (x[i] - xc) + p[2] * g);
                    double[] jac = { 1.0, x[i] - xc, g, p[2] * g * d / p[4], p[2] * g * d * d / p[4] };
                    for (int u = 0; u < 5; u++)
                    {
                        jtr[u] += w[i] * jac[u] * r;
                        for (int v = 0; v < 5; v++)
                        {
                            jtj[u, v] += w[i] * jac[u] * jac[v];
                        }
                    }
                }
                bool stepTaken = false;
                for (int attempt = 0; attempt < 30 && !stepTaken; attempt++)
                {
                    double[,] aug = new double[5, 6];
                    for (int u = 0; u < 5; u++)
                    {
                        for (int v = 0; v < 5; v++)
                        {
                            aug[u, v] = jtj[u, v] + (u == v ? lambda * Math.Max(jtj[u, u], 1e-300) : 0.0);
                        }
                        aug[u, 5] = jtr[u];
                    }
                    double[] delta = SolveDense(aug, 5);
                    if (delta == null)
                    {
                        lambda *= 10.0;
                        continue;
                    }
                    double[] trial = new double[5];
                    for (int u = 0; u < 5; u++)
                    {
                        trial[u] = p[u] + delta[u];
                    }
                    trial[4] = Math.Max(sMin, Math.Min(sMax, trial[4]));
                    trial[3] = Math.Max(x[0], Math.Min(x[m - 1], trial[3]));
                    double next = trial[2] > 0.0 ? chi2(trial) : double.PositiveInfinity;
                    if (next <= current)
                    {
                        double change = current - next;
                        double move = Math.Abs(trial[3] - p[3]);
                        p = trial;
                        current = next;
                        lambda = Math.Max(lambda / 10.0, 1e-12);
                        stepTaken = true;
                        if (change <= 1e-10 * Math.Max(current, 1e-300) && move < 1e-7)
                        {
                            converged = true;
                        }
                    }
                    else
                    {
                        lambda *= 10.0;
                    }
                }
                if (!stepTaken)
                {
                    // шаг не уменьшает χ² ни при каком λ — минимум достигнут
                    converged = true;
                }
                if (converged)
                {
                    break;
                }
            }
            if (!converged || !(p[2] > 0.0))
            {
                return double.NaN;
            }
            // ошибка центра: (JᵀWJ)⁻¹ на минимуме, умноженная на max(1, χ²/ndf)
            double[,] cov = new double[5, 6];
            for (int u = 0; u < 5; u++)
            {
                for (int v = 0; v < 5; v++)
                {
                    cov[u, v] = jtj[u, v];
                }
                cov[u, 5] = u == 3 ? 1.0 : 0.0;
            }
            double[] column = SolveDense(cov, 5);
            if (column == null || !(column[3] > 0.0))
            {
                return double.NaN;
            }
            double scaleChi = Math.Max(1.0, current / Math.Max(1, m - 5));
            double sigmaMu = Math.Sqrt(column[3] * scaleChi);
            if (sigmaMu > 0.25 * 2.3548200450309493 * sigma0)
            {
                return double.NaN;
            }
            return p[3];
        }

        /// <summary>Гаусс с выбором главного по столбцу для расширенной матрицы n×(n+1); null — вырождена.</summary>
        static double[] SolveDense(double[,] m, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (!(Math.Abs(m[pivot, col]) > 1e-300))
                {
                    return null;
                }
                if (pivot != col)
                {
                    for (int k = 0; k <= n; k++)
                    {
                        double tmp = m[col, k];
                        m[col, k] = m[pivot, k];
                        m[pivot, k] = tmp;
                    }
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double f = m[r, col] / m[col, col];
                    for (int k = col; k <= n; k++)
                    {
                        m[r, k] -= f * m[col, k];
                    }
                }
            }
            double[] x = new double[n];
            for (int r = 0; r < n; r++)
            {
                x[r] = m[r, n] / m[r, r];
                if (double.IsNaN(x[r]) || double.IsInfinity(x[r]))
                {
                    return null;
                }
            }
            return x;
        }

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «Считать свет суммой
        /// (Рекомендую)») СТРОКА СУММ-ПИКА. Строка таблицы признака «сумм-пик» не несёт — у неё
        /// только энергия, поэтому сумм-пик распознаётся так: в таблице есть две ДРУГИЕ линии
        /// энергии E₁ + E₂ = E (в пределах <see cref="TractCurvatureSumToleranceKev"/>), и `nucdb`
        /// знает их каскадное совпадение (`v_gamma_coincidence`, любой нуклид) — как у Co-60
        /// 1173.2 + 1332.5 = 2505.7 и Y-88 898.0 + 1836.1 = 2734.1. Тогда свет строки —
        /// L(E₁) + L(E₂), а не L(E). Возвращает {E₁, E₂} либо null (одиночная линия). Пары нет в
        /// таблице или база не читается — строка одиночная, как прежде.
        /// </summary>
        public static double[] SumPartsOf(double energy, IList<double> tableEnergies)
        {
            if (!(energy > 0.0) || tableEnergies == null)
            {
                return null;
            }
            double tolerance = TractCurvatureSumToleranceKev;
            List<double> others = tableEnergies.Where(e => e > 0.0 && e < energy - tolerance).Distinct().OrderBy(e => e).ToList();
            for (int i = 0; i < others.Count; i++)
            {
                for (int j = i; j < others.Count; j++)
                {
                    if (Math.Abs(others[i] + others[j] - energy) <= tolerance && CascadePair(others[i], others[j]))
                    {
                        return new[] { others[i], others[j] };
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// (`AMBER295`) Знает ли `nucdb` каскадное совпадение квантов <paramref name="a"/> и
        /// <paramref name="b"/> кэВ (в любом порядке, допуск <see cref="TractCurvatureSumToleranceKev"/>).
        /// База не читается — нет.
        /// </summary>
        static bool CascadePair(double a, double b)
        {
            return CascadeFraction(a, b) > 0.0;
        }

        /// <summary>
        /// (`AMBER295`) Сила каскада квантов a и b в `nucdb` (любой порядок): наибольшее
        /// «интенсивность первого кванта, % × доля совпадения» — у Tl-208 пара 583.2 + 2614.5
        /// (84.5 × 0.989) сильнее пары 860.6 + 2614.5 (12.4 × 0.989); 0 — пары нет или база не читается.
        /// Нуклид пары, у которого при энергии суммы есть своя одиночная гамма-линия не слабее
        /// <see cref="TractCurvatureCrossoverShare"/> силы каскада, в счёт не идёт: у него эта
        /// энергия — фотопик, а не сумма (Bi-214: 609.3 + 1120.3 = 1729.6 кэВ, линия 2.8 %).
        /// </summary>
        static double CascadeFraction(double a, double b)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
                if (!System.IO.File.Exists(path))
                {
                    return 0.0;
                }
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    EfficiencyMaker.MaterialDatabase.ReadOnlyConnection(path, true)))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        // сила каскада по нуклидам; у каждого — его же одиночная гамма-линия
                        // при энергии суммы (переход «через уровень», crossover)
                        command.CommandText =
                            "select c.nucid, max(c.fraction * coalesce(l.intensity_pct, 1e-6)), "
                            + "(select coalesce(max(r.intensity_num), 0) from decay_radiations r where r.parent_nucid = c.nucid "
                            + "and r.type_a = 'G' and abs(r.energy_num - $s) <= $t) "
                            + "from v_gamma_coincidence c "
                            + "left join v_gamma_coincidence_line l on l.nucid = c.nucid and l.isomer = c.isomer "
                            + "and abs(l.energy_kev - c.energy_kev) < 0.001 where "
                            + "(abs(c.energy_kev - $a) <= $t and abs(c.coinc_energy_kev - $b) <= $t) or "
                            + "(abs(c.energy_kev - $b) <= $t and abs(c.coinc_energy_kev - $a) <= $t) "
                            + "group by c.nucid";
                        command.Parameters.AddWithValue("$a", a);
                        command.Parameters.AddWithValue("$b", b);
                        command.Parameters.AddWithValue("$s", a + b);
                        command.Parameters.AddWithValue("$t", TractCurvatureSumToleranceKev);
                        double best = 0.0;
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                double strength = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                                double crossover = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                                // ⛔ у Bi-214 609.3 + 1120.3 = 1729.6 — это и его ОДИНОЧНАЯ линия 1729.6
                                // (2.8 %): такая энергия — не сумм-пик
                                if (crossover < TractCurvatureCrossoverShare * strength)
                                {
                                    best = Math.Max(best, strength);
                                }
                            }
                        }
                        return best;
                    }
                }
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        /// <summary>
        /// (`AMBER270`) Ядро подгонки κ по дробным каналам: приложение зовёт его центрами
        /// масс пиков строк (<see cref="FitTractCurvature(List{CalibrationPeak}, EnergySpectrum, EnergyCalibration, string, bool)"/>),
        /// пробы — каналами карты разбора без округления.
        /// </summary>
        public static TractCurvatureFit FitTractCurvature(IList<double> pointChannels, IList<double> pointEnergies, IList<double> pointWeights,
                                                            EnergyCalibration energyScale, string scintillator)
        {
            return FitTractCurvature(pointChannels, pointEnergies, pointWeights, null, energyScale, scintillator);
        }

        /// <summary>
        /// (`AMBER295`) То же ядро с отбором линий: <paramref name="pointFwhm"/> — FWHM линий,
        /// каналы (null — без отбора и без правила двух верхних линий, как прежде у проб).
        /// FWHM строки не задана (ноль) — промах делится на один канал.
        /// </summary>
        public static TractCurvatureFit FitTractCurvature(IList<double> pointChannels, IList<double> pointEnergies, IList<double> pointWeights,
                                                            IList<double> pointFwhm, EnergyCalibration energyScale, string scintillator)
        {
            return FitTractCurvature(pointChannels, pointEnergies, pointWeights, pointFwhm, null, energyScale, scintillator);
        }

        /// <summary>
        /// (`AMBER295`) Ядро целиком. <paramref name="pointParts"/> — у строки сумм-пика две
        /// энергии квантов {E₁, E₂}: её свет — сумма светов L(E₁) + L(E₂) (решение Amber
        /// 14.11.2026, дословно: «Считать свет суммой (Рекомендую)»; так κ Gamma-1S мерили
        /// сумм-пиками в корпусе, журнал П218 §3); null у одиночной линии или весь список null.
        /// Порядок отбора и правило двух верхних линий — в теле.
        /// </summary>
        public static TractCurvatureFit FitTractCurvature(IList<double> pointChannels, IList<double> pointEnergies, IList<double> pointWeights,
                                                            IList<double> pointFwhm, IList<double[]> pointParts,
                                                            EnergyCalibration energyScale, string scintillator)
        {
            TractCurvatureFit fit = new TractCurvatureFit();
            fit.Material = scintillator ?? "";
            FullSpectrumAnalysis.FsaAnalyzer defaults = new FullSpectrumAnalysis.FsaAnalyzer();
            string form = (defaults.AnchorLightForm ?? "").Trim().ToLowerInvariant();
            if (!defaults.AdcLinear || !defaults.AnchorLightPosition || (form != "line" && form != "peak"))
            {
                fit.Refusal = Resources.MSGTractCurvatureNotUsed;
                return fit;
            }
            if (string.IsNullOrEmpty(scintillator) && string.IsNullOrEmpty(defaults.AnchorLightCurve))
            {
                fit.Refusal = Resources.MSGTractCurvatureNoMaterial;
                return fit;
            }
            string curve = string.IsNullOrEmpty(defaults.AnchorLightCurve)
                ? FullSpectrumAnalysis.FsaLightScale.CurveFor(scintillator)
                : defaults.AnchorLightCurve;
            if (!FullSpectrumAnalysis.FsaLightScale.Known(curve))
            {
                fit.Refusal = String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureNoCurve, scintillator);
                return fit;
            }
            fit.Curve = curve;

            // свет фотопика каждой линии: x = E + s(E) — тем же вызовом и с теми же
            // умолчаниями, что у разбора (`FsaAnalyzer.CurveShiftKev`); у сумм-пика —
            // сумма светов двух квантов
            Func<double, double> lightOf = e =>
            {
                double s = defaults.AnchorLightMaxKev > 0.0 && e > defaults.AnchorLightMaxKev
                    ? 0.0
                    : FullSpectrumAnalysis.FsaLightScale.ShiftKev(curve, e, defaults.AnchorLightReferenceKev);
                return e + s;
            };
            var channels = new List<double>();
            var lights = new List<double>();
            var weights = new List<double>();
            var energies = new List<double>();
            var fwhms = new List<double>();
            var parts = new List<double[]>();
            int count = pointChannels != null && pointEnergies != null ? Math.Min(pointChannels.Count, pointEnergies.Count) : 0;
            for (int i = 0; i < count; i++)
            {
                double e = pointEnergies[i];
                double ch = pointChannels[i];
                if (!(e > 0.0) || double.IsNaN(ch) || double.IsInfinity(ch))
                {
                    continue;
                }
                double[] sum = pointParts != null && i < pointParts.Count ? pointParts[i] : null;
                double light = sum == null ? lightOf(e) : lightOf(sum[0]) + lightOf(sum[1]);
                if (double.IsNaN(light) || double.IsInfinity(light))
                {
                    continue;
                }
                double w = pointWeights != null && i < pointWeights.Count && pointWeights[i] > 0.0 ? pointWeights[i] : 1.0;
                channels.Add(ch);
                lights.Add(light);
                energies.Add(e);
                weights.Add(w);
                double fw = pointFwhm != null && i < pointFwhm.Count ? pointFwhm[i] : double.NaN;
                fwhms.Add(fw > 0.0 && !double.IsInfinity(fw) ? fw : 1.0);
                parts.Add(sum);
            }
            fit.Lines = channels.Count;
            fit.TopLineKev = energies.Count > 0 ? energies.Max() : 0.0;
            // (`AMBER295`) с отбором (путь приложения) — не меньше четырёх линий: «нечем проверить»;
            // без отбора (ядро проб) — три свободных числа, как прежде
            int minLines = pointFwhm != null ? TractCurvatureMinLines : 3;
            if (channels.Count < minLines)
            {
                fit.TooFewLines = true;
                fit.Refusal = String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureFewLines,
                                            channels.Count.ToString(CultureInfo.InvariantCulture),
                                            minLines.ToString(CultureInfo.InvariantCulture));
                return fit;
            }
            // ⛔ решение Amber 30.10.2026 «Только с линиями до ≥ 2 МэВ» — ЗАМЕНЕНО у приложения её
            // решением 14.11.2026, дословно: «Заменить новым (Рекомендую)» — правилом двух годных
            // линий от 1500 кэВ (ниже). Остаётся только у ядра проб без отбора (`pointFwhm` null).
            if (pointFwhm == null && fit.TopLineKev < TractCurvatureTopLineKev)
            {
                fit.Refusal = String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureNoHighLine,
                                            TractCurvatureTopLineKev.ToString("0", CultureInfo.InvariantCulture),
                                            fit.TopLineKev.ToString("0.##", CultureInfo.InvariantCulture));
                return fit;
            }

            // МНК c(x) = p0 + p1·x + p2·x² (свет — от середины, чтобы система была обусловлена)
            double mid;
            double[] p = SolveTractQuadratic(channels, lights, weights, out mid);
            // ⛔ (`AMBER295`) ОТБОР ПО ПРОМАХУ. Три свободных числа: на трёх линиях квадрат
            // проходит через все точки и промаха не видно; на четырёх промах виден, но КАКАЯ
            // линия промахнулась — нет (остаток с одной степенью свободы ложится на линии
            // одним и тем же вектором, какая бы ни ошиблась). Поэтому: линии согласны (промах
            // каждой от подгонки по всем ≤ порога) — подгонка как есть; не согласны — пробуется
            // снять каждую по очереди. Ровно одна снятая даёт согласный остаток — она вон
            // (промах — от подгонки по остальным). Больше одной (на четырёх линиях — всегда) —
            // промахнувшуюся не определить; ни одной — промахнулись несколько: отказ (решение
            // Amber 14.11.2026 о четырёх линиях, дословно: «Отказ (Рекомендую)»). Угадывать
            // нельзя: снятие «самой мешающей» на мутанте с двумя промахами из пяти снимало
            // верхнюю линию 2614.5 кэВ — у неё наибольший рычаг.
            // Промах мерится подгонкой БЕЗ весов в обоих режимах: с весами по отсчётам линия
            // со слабым пиком отходит от квадрата дальше, и порог зависел бы от режима (находка
            // критика `crit270k`: `AS80_Th232_v2` с весами — 0.228 FWHM на годных линиях).
            while (pointFwhm != null && p != null && channels.Count > 3)
            {
                double um;
                double[] up = SolveTractQuadratic(channels, lights, Ones(channels.Count), out um);
                if (up == null)
                {
                    break;
                }
                double spread = WorstMissFwhm(up, um, channels, lights, fwhms);
                if (!(spread > TractCurvatureMissFwhm))
                {
                    break;
                }
                int best = -1, agreeing = 0;
                double bestSpread = double.PositiveInfinity;
                double[] bestP = null;
                double bestMid = 0.0;
                for (int k = 0; k < channels.Count; k++)
                {
                    List<double> c = Without(channels, k), l = Without(lights, k), f = Without(fwhms, k);
                    double kMid;
                    double[] kp = SolveTractQuadratic(c, l, Ones(c.Count), out kMid);
                    if (kp == null)
                    {
                        continue;
                    }
                    double kSpread = c.Count > 3 ? WorstMissFwhm(kp, kMid, c, l, f) : 0.0;
                    if (!(kSpread > TractCurvatureMissFwhm))
                    {
                        agreeing++;
                    }
                    if (kSpread < bestSpread)
                    {
                        best = k;
                        bestSpread = kSpread;
                        bestP = kp;
                        bestMid = kMid;
                    }
                }
                if (best < 0 || agreeing != 1)
                {
                    fit.Lines = channels.Count;
                    fit.Refusal = String.Format(CultureInfo.InvariantCulture,
                                                agreeing > 1 ? Resources.MSGTractCurvatureAmbiguous : Resources.MSGTractCurvatureSeveralMiss,
                                                channels.Count.ToString(CultureInfo.InvariantCulture),
                                                RoundUp(spread).ToString("0.00", CultureInfo.InvariantCulture),
                                                TractCurvatureMissFwhm.ToString("0.00", CultureInfo.InvariantCulture));
                    return fit;
                }
                double bt = lights[best] - bestMid;
                double bestMiss = Math.Abs(bestP[0] + bestP[1] * bt + bestP[2] * bt * bt - channels[best]);
                fit.Rejected.Add(new TractLineRejection { Energy = energies[best], MissChannels = bestMiss, MissFwhm = bestMiss / fwhms[best] });
                channels.RemoveAt(best);
                lights.RemoveAt(best);
                energies.RemoveAt(best);
                weights.RemoveAt(best);
                fwhms.RemoveAt(best);
                parts.RemoveAt(best);
                p = SolveTractQuadratic(channels, lights, weights, out mid);
            }
            if (fit.Rejected.Count > 0)
            {
                fit.Lines = channels.Count;
                fit.TopLineKev = energies.Max();
                if (channels.Count < minLines)
                {
                    fit.TooFewLines = true;
                    fit.Refusal = String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureFewLines,
                                                channels.Count.ToString(CultureInfo.InvariantCulture),
                                                minLines.ToString(CultureInfo.InvariantCulture));
                    return fit;
                }
            }
            // ⛔ (`AMBER295`, решение Amber 14.11.2026 вопросником, дословно: «Две линии ≥ 1.5 МэВ
            // (Рекомендую)») ВЕРХ ПРОВЕРЯЮТ ДВЕ ЛИНИИ: верхняя линия держит шкалу карты и сильнее
            // всех тянет κ, а промах её одной не виден (мутант критика: 2614.5 +40 кан. у
            // `G1S24_Th228_P5` давал κ 0.033). Годных линий от 1500 кэВ (сумм-пик — по своей
            // энергии) нужно не меньше двух, иначе отказ.
            if (pointFwhm != null)
            {
                int high = energies.Count(e => e >= TractCurvatureHighLineKev);
                if (high < 2)
                {
                    fit.Refusal = String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureNoTwoHigh,
                                                TractCurvatureHighLineKev.ToString("0", CultureInfo.InvariantCulture),
                                                high.ToString(CultureInfo.InvariantCulture));
                    return fit;
                }
            }
            double edge = defaults.AdcZeroAtChannelEdge ? -0.5 : 0.0;
            double zeroT;
            if (p == null || !TractZero(p, edge, lights.Min() - mid, out zeroT))
            {
                fit.Refusal = Resources.MSGTractCurvatureFailed;
                return fit;
            }
            double slope = p[1] + 2.0 * p[2] * zeroT;
            double zeroLight = zeroT + mid;

            // шкала карты: верхняя ОДИНОЧНАЯ линия держится на месте, как у разбора (сумм-пик
            // её не заменяет: у разбора верх шкалы — фотопик); одиночных нет — верхняя вообще
            int top = -1;
            for (int i = 0; i < energies.Count; i++)
            {
                if (parts[i] == null && (top < 0 || energies[i] > energies[top]))
                {
                    top = i;
                }
            }
            if (top < 0)
            {
                top = 0;
                for (int i = 1; i < energies.Count; i++)
                {
                    if (energies[i] > energies[top])
                    {
                        top = i;
                    }
                }
            }
            double e0;
            try
            {
                e0 = energyScale.ChannelToEnergy(0.0);
                if (defaults.AdcZeroAtChannelEdge)
                {
                    e0 -= 0.5 * (energyScale.ChannelToEnergy(1.0) - e0);
                }
            }
            catch (Exception)
            {
                e0 = double.NaN;
            }
            double scale = (energies[top] - e0) / (lights[top] - zeroLight);
            double kappa = 1000.0 * p[2] / (slope * scale);
            if (!(slope > 0.0) || !(scale > 0.0) || double.IsNaN(kappa) || double.IsInfinity(kappa))
            {
                fit.Refusal = Resources.MSGTractCurvatureFailed;
                return fit;
            }
            double worst = 0.0;
            for (int i = 0; i < channels.Count; i++)
            {
                double t = lights[i] - mid;
                double c = p[0] + p[1] * t + p[2] * t * t;
                worst = Math.Max(worst, Math.Abs(c - channels[i]));
                if (parts[i] != null)
                {
                    fit.SumPeaks.Add(new[] { energies[i], parts[i][0], parts[i][1] });
                }
            }
            fit.Ok = true;
            fit.Kappa = kappa;
            fit.ZeroLightKev = zeroLight;
            fit.WorstChannels = worst;
            return fit;
        }

        /// <summary>Веса «без весов» — единицы.</summary>
        static List<double> Ones(int n)
        {
            var ones = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                ones.Add(1.0);
            }
            return ones;
        }

        /// <summary>
        /// МНК c(x) = p0 + p1·t + p2·t², t = x − <paramref name="mid"/> (среднее света линий),
        /// веса — <paramref name="weights"/>; null — система вырождена. Арифметика — прежняя
        /// побитово (до `AMBER295` стояла в ядре строкой).
        /// </summary>
        static double[] SolveTractQuadratic(List<double> channels, List<double> lights, List<double> weights, out double mid)
        {
            mid = lights.Average();
            double[,] m = new double[3, 4];
            for (int i = 0; i < channels.Count; i++)
            {
                double t = lights[i] - mid;
                double[] basis = { 1.0, t, t * t };
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        m[r, c] += weights[i] * basis[r] * basis[c];
                    }
                    m[r, 3] += weights[i] * basis[r] * channels[i];
                }
            }
            return SolveSmall(m);
        }

        /// <summary>(`AMBER295`) Наибольший промах линии от квадрата <paramref name="p"/>, доли её FWHM.</summary>
        static double WorstMissFwhm(double[] p, double mid, List<double> channels, List<double> lights, List<double> fwhms)
        {
            double worst = 0.0;
            for (int i = 0; i < channels.Count; i++)
            {
                double t = lights[i] - mid;
                worst = Math.Max(worst, Math.Abs(p[0] + p[1] * t + p[2] * t * t - channels[i]) / fwhms[i]);
            }
            return worst;
        }

        /// <summary>
        /// Доли FWHM для показа — вверх до сотых: промах выше порога не печатается равным ему
        /// (0.2501 при пороге 0.25 — «0.26», а не «0.25»).
        /// </summary>
        static double RoundUp(double value)
        {
            return Math.Ceiling(value * 100.0 - 1e-9) / 100.0;
        }

        /// <summary>Копия списка без элемента <paramref name="skip"/>.</summary>
        static List<double> Without(List<double> list, int skip)
        {
            var copy = new List<double>(list.Count - 1);
            for (int i = 0; i < list.Count; i++)
            {
                if (i != skip)
                {
                    copy.Add(list[i]);
                }
            }
            return copy;
        }

        /// <summary>
        /// Корень c(t) = <paramref name="edge"/> квадрата p0 + p1·t + p2·t² — точка нуля карты:
        /// тот, где карта растёт, ближайший к нижней линии <paramref name="lowT"/>.
        /// </summary>
        static bool TractZero(double[] p, double edge, double lowT, out double zeroT)
        {
            zeroT = double.NaN;
            double a = p[2], b = p[1], c = p[0] - edge;
            var roots = new List<double>();
            if (Math.Abs(a) < 1e-300 || Math.Abs(a * lowT * lowT) < 1e-12 * Math.Abs(b * lowT))
            {
                if (b != 0.0)
                {
                    roots.Add(-c / b);
                }
            }
            else
            {
                double d = b * b - 4.0 * a * c;
                if (d >= 0.0)
                {
                    double sq = Math.Sqrt(d);
                    // устойчивая запись корней квадрата
                    double q = -0.5 * (b + (b >= 0.0 ? sq : -sq));
                    if (q != 0.0)
                    {
                        roots.Add(c / q);
                    }
                    roots.Add(q / a);
                }
            }
            double best = double.PositiveInfinity;
            foreach (double t in roots)
            {
                if (double.IsNaN(t) || double.IsInfinity(t) || !(b + 2.0 * a * t > 0.0))
                {
                    continue;
                }
                double distance = Math.Abs(t - lowT);
                if (distance < best)
                {
                    best = distance;
                    zeroT = t;
                }
            }
            return !double.IsNaN(zeroT);
        }

        /// <summary>Гаусс с выбором главного по столбцу для расширенной матрицы 3×4; null — вырождена.</summary>
        static double[] SolveSmall(double[,] m)
        {
            int n = 3;
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (!(Math.Abs(m[pivot, col]) > 1e-300))
                {
                    return null;
                }
                if (pivot != col)
                {
                    for (int k = 0; k <= n; k++)
                    {
                        double tmp = m[col, k];
                        m[col, k] = m[pivot, k];
                        m[pivot, k] = tmp;
                    }
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double f = m[r, col] / m[col, col];
                    for (int k = col; k <= n; k++)
                    {
                        m[r, k] -= f * m[col, k];
                    }
                }
            }
            double[] x = new double[n];
            for (int r = 0; r < n; r++)
            {
                x[r] = m[r, n] / m[r, r];
                if (double.IsNaN(x[r]) || double.IsInfinity(x[r]))
                {
                    return null;
                }
            }
            return x;
        }

        private void ExecuteCalibrationButton_Click(object sender, EventArgs e)
        {
            // (`AMBER283`) выбор пиков идёт до «Отмены»; расчёт его завершает
            ClearPeakPickupState();
            RunCalibration();
        }

        /// <summary>
        /// «Рассчитать» — один процесс: подгонка энергии по линиям калибровки (если они
        /// есть) и подгонка FWHM по всем строкам (если строк хватает виду кривой).
        /// Каждая подгонка пишет в КОПИЮ и применяется только после своих проверок
        /// (`AMBER188`, `AMBER189`): отказ одной не трогает ни её шкалу, ни другую,
        /// уже принятую. Возвращает true, если каждая начатая подгонка принята.
        /// </summary>
        internal bool RunCalibration()
        {
            DocEnergySpectrum activeDocument = mainForm.ActiveDocument;
            ResultData resultData = ActiveResultData;
            if (activeDocument == null || resultData == null || resultData.EnergySpectrum == null)
            {
                return false;
            }
            int channels = resultData.EnergySpectrum.NumberOfChannels;
            List<CalibrationPeak> lines = resultData.CalibrationLines;
            bool refused = false;
            bool energyDone = false;
            double energyMse = 0.0;
            TractCurvatureFit tractFit = null;

            // ── 1. Энергия ───────────────────────────────────────────────────────
            List<CalibrationPoint> points;
            try
            {
                points = EnergyPoints(lines);
            }
            catch (OverflowException)
            {
                points = null;
            }
            bool energyRefused = false;
            bool fwhmRefused = false;
            if (points == null)
            {
                AppUi.Report(Resources.ERRInvalidChannelOrEnergyValues, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                refused = true;
                energyRefused = true;
            }
            else if (points.Count > 0 && energyCalibration != null)
            {
                int requestedOrder = (int)numericUpDown6.Value;
                int usedOrder;
                double[] matrix = null;
                try
                {
                    matrix = SolveEnergy(points, requestedOrder, channels, checkBox2.Checked, out usedOrder);
                }
                catch (Exception)
                {
                    matrix = null;
                }
                if (matrix == null)
                {
                    AppUi.Report(Resources.ERRInvalidChannelOrEnergyValues, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                    refused = true;
                    energyRefused = true;
                }
                else
                {
                    PolynomialEnergyCalibration fitted = (PolynomialEnergyCalibration)energyCalibration.Clone();
                    fitted.PolynomialOrder = matrix.Length - 1;
                    fitted.Coefficients = matrix;
                    if (!fitted.CheckCalibration(channels: channels))
                    {
                        AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                        refused = true;
                        energyRefused = true;
                    }
                    else
                    {
                        energyCalibration = fitted;
                        SyncRcEnergyCalibration();
                        FitRcEnergyCalibration(resultData, points, requestedOrder == 0 ? 1 : requestedOrder);
                        ShowCoefficients();
                        List<CalibrationPoint> msePoints = new List<CalibrationPoint>(points);
                        if (msePoints.Count == 1)
                        {
                            msePoints.Add(new CalibrationPoint(0, 0, 0));
                        }
                        energyMse = checkBox2.Checked
                            ? CalibrationSolver.WMSE(matrix, msePoints)
                            : CalibrationSolver.MSE(matrix, msePoints);
                        // (`AMBER270`) кривизна тракта — тем же движением, по тем же
                        // линиям; в спектр (`AMBER271`). Не вышло — κ спектра прежняя,
                        // а причина идёт в строку состояния и под поле κ. Канал линии —
                        // центр масс её пика, набор — с линией от 2 МэВ (решения Amber
                        // 30.10.2026). (`AMBER295`) Линия без измеренного центра или с
                        // промахом — вон; годных меньше трёх или промахнувшуюся не
                        // определить — отказ, κ спектра прежняя (Amber 14.11.2026).
                        // (`AMBER295`, «Писать центр в файл») центр пика — в строку, пока отсчёты
                        // канала ещё те: строка, у которой центра нет, получает его здесь, и он
                        // уезжает в файл вместе со строкой
                        foreach (CalibrationPeak line in lines)
                        {
                            if (line != null && line.EnergyLine && !(line.SpectrumCentroidChannel == line.Channel && !double.IsNaN(line.SpectrumCentroid)))
                            {
                                RememberCentroid(line, resultData.EnergySpectrum);
                            }
                        }
                        // ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «„Веса“ для κ
                        // (Рекомендую)») κ подгоняется С ВЕСАМИ по счёту пика всегда, какой бы режим
                        // ни стоял на вкладке; промах для отбора мерится без весов (ядро)
                        tractFit = FitTractCurvature(lines, resultData.EnergySpectrum, fitted, ScintillatorOf(resultData), true);
                        if (tractFit.Ok)
                        {
                            resultData.TractCurvature = tractFit.Kappa;
                        }
                        lastTractFit = tractFit;
                        ShowTractCurvature();
                        UpdateEnergyCalibration();
                        energyDone = true;
                    }
                }
            }

            // ── 2. FWHM ──────────────────────────────────────────────────────────
            bool fwhmDone = false;
            double fwhmMse = 0.0;
            FwhmCalibration template = fwhmCalibration ?? CurveOfSelectedType();
            if (lines.Count >= template.MinPeaksRequirement())
            {
                // (`AMBER189` (а)) Решатель пишет в КОПИЮ; документ получает её только
                // после проверки — отказ кривую документа не трогает.
                FwhmCalibration fitted = FitCopyOrRefuse(resultData);
                if (fitted == null)
                {
                    refused = true;
                    fwhmRefused = true;
                }
                else
                {
                    if (HasPeakShapeComparisonData(fitted.CalibrationPeaks))
                    {
                        SelectGlobalPeakShape(fitted, fitted.CalibrationPeaks);
                    }
                    fwhmMse = CalibrationSolver.MSE(fitted, fitted.CalibrationPeaks);
                    CommitFittedCurve(activeDocument, fitted);
                    fwhmDone = true;
                }
            }

            calibrationDone = !refused;
            calibrationRan = !refused && (energyDone || fwhmDone);
            // ⛔ (`AMBER283`, «Итог строкой под таблицей») Один итог — в два места: строка
            //    под таблицей (СКО сокращённо) и строка состояния главного окна (полным
            //    словом, как прежде). Отказ одной из подгонок тоже итог: «выполнена
            //    частично» / «не выполнена» и какая часть отвергнута — прежде главное окно
            //    в этом случае писало «Калибровка выполнена», а под таблицей стояло
            //    приглашение рассчитать.
            if (energyDone || fwhmDone || energyRefused || fwhmRefused)
            {
                string panelLine = RunSummaryText(OwnText("SummaryMseShort", "MSE"), energyDone, energyRefused, energyMse, tractFit,
                                                  fwhmDone, fwhmRefused, fwhmMse);
                runSummary = panelLine;
                mainForm.SetStatusTextLeft(RunSummaryText(Resources.MSGMSE, energyDone, energyRefused, energyMse, tractFit,
                                                          fwhmDone, fwhmRefused, fwhmMse));
            }
            UpdateCalibrateButtonState();
            return !refused;
        }

        /// <summary>
        /// (`AMBER283`) Итог «Выполнить калибровку» строкой: исход целиком, затем энергия (СКО,
        /// кривизна тракта или причина, почему её нет) и FWHM (СКО и форма пика); отвергнутая
        /// часть — словом «отказ». <paramref name="mseLabel"/> — подпись СКО: полная для строки
        /// состояния главного окна, сокращённая для строки под таблицей.
        /// </summary>
        string RunSummaryText(string mseLabel, bool energyDone, bool energyRefused, double energyMse, TractCurvatureFit tractFit,
                              bool fwhmDone, bool fwhmRefused, double fwhmMse)
        {
            bool anyDone = energyDone || fwhmDone;
            bool anyRefused = energyRefused || fwhmRefused;
            StringBuilder status = new StringBuilder(!anyDone
                ? OwnText("SummaryNotDone", "Calibration not done.")
                : anyRefused
                    ? OwnText("SummaryPartlyDone", "Calibration partly done.")
                    : Resources.MSGUnifiedCalibrationDone);
            string refusedWord = OwnText("SummaryRefused", "refused");
            if (energyDone)
            {
                status.AppendFormat(CultureInfo.InvariantCulture, " {0}: {1} = {2:0.00000};", Resources.MSGEnergyShort, mseLabel, energyMse);
            }
            else if (energyRefused)
            {
                status.AppendFormat(CultureInfo.InvariantCulture, " {0}: {1};", Resources.MSGEnergyShort, refusedWord);
            }
            if (fwhmDone)
            {
                status.AppendFormat(CultureInfo.InvariantCulture, " FWHM: {0} = {1:0.00000}", mseLabel, fwhmMse);
                if (fwhmCalibration != null)
                {
                    int peakType = FwhmCalibration.IsSupportedPeakType(fwhmCalibration.PeakType)
                        ? fwhmCalibration.PeakType
                        : FwhmCalibration.GaussianPeakType;
                    status.Append(", ").AppendFormat(CultureInfo.InvariantCulture, OwnText("SummaryPeakShape", "peak shape: {0}"),
                                                     GetPeakShapeName(peakType));
                }
                status.Append(';');
            }
            else if (fwhmRefused)
            {
                status.AppendFormat(CultureInfo.InvariantCulture, " FWHM: {0};", refusedWord);
            }
            // (`AMBER295`, находка 2 второго круга критика `crit270k`) итог κ — ПОСЛЕДНИМ: его
            // причина бывает длинной (торий, отброшенные линии) и вытесняла итог FWHM из двух
            // видимых строк под таблицей
            if (energyDone && tractFit != null)
            {
                status.Append(' ').Append(TractCurvatureStatus(tractFit)).Append(';');
            }
            return status.ToString().TrimEnd(';', ' ');
        }

        /// <summary>
        /// Квадратичная шкала RadiaCode/Obsidian — прежняя ветка «Выполнить калибровку»
        /// вкладки энергии (`AMBER188` (е)): под <c>try</c> и только при трёх точках и
        /// больше; не вышло — остаётся то, что дал <see cref="SyncRcEnergyCalibration"/>.
        /// </summary>
        void FitRcEnergyCalibration(ResultData resultData, List<CalibrationPoint> energyPoints, int polynomOrder)
        {
            List<CalibrationPoint> points = new List<CalibrationPoint>(energyPoints);
            if (points.Count == 1)
            {
                points.Add(new CalibrationPoint(0, 0, 0));
            }
            DeviceConfigInfo deviceConfig = resultData.DeviceConfig;
            if (deviceConfig == null || string.IsNullOrEmpty(deviceConfig.Guid))
            {
                return;
            }
            if (!(deviceConfig.InputDeviceConfig is RadiaCodeDeviceConfig || deviceConfig.InputDeviceConfig is ObsidianDeviceConfig)
                || polynomOrder < 2 || points.Count < 3)
            {
                return;
            }
            double[] rcMatrix = null;
            try
            {
                rcMatrix = this.checkBox2.Checked
                    ? CalibrationSolver.SolveWeighted(points, 2)
                    : CalibrationSolver.Solve(points, 2);
            }
            catch (Exception)
            {
                rcMatrix = null;
            }
            if (rcMatrix != null && rcMatrix.Length == 3
                && !Array.Exists(rcMatrix, v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                rc_EnergyCalibration = new PolynomialEnergyCalibration();
                rc_EnergyCalibration.Coefficients = rcMatrix;
                rc_EnergyCalibration.PolynomialOrder = 2;
            }
        }

        /// <summary>
        /// Подгонка кривой FWHM в КОПИЮ (`AMBER189` (а)) по ВСЕМ строкам таблицы (линии
        /// и опоры). Вид кривой — выбранный в списке (у спектра без кривой — тоже).
        /// Возвращает подогнанную копию или <c>null</c> после окна отказа. Отказ «ширина
        /// ≤ 0 на нижних каналах» — с подсказкой взять степенную кривую (решение Amber
        /// 05.10.2026: «Отвергать с подсказкой (Рекомендую)»). Окна — через
        /// <c>AppUi.Report</c>: метод лежит на пути, который пробы зовут отражением.
        /// </summary>
        FwhmCalibration FitCopyOrRefuse(ResultData resultData)
        {
            int channels = resultData.EnergySpectrum.Spectrum.Length;
            FwhmCalibration fitted = fwhmCalibration != null ? fwhmCalibration.Clone() : CurveOfSelectedType();
            fitted.CalibrationPeaks = CalibrationPeak.ClonePeaks(resultData.CalibrationLines);
            bool ok;
            try
            {
                ok = fitted.PerformCalibration(channels);
            }
            catch (ArgumentException)
            {
                // Решатель MathNet бросает на негодной системе — это тот же отказ.
                ok = false;
            }
            if (ok)
            {
                return fitted;
            }
            if (fitted.LastCheck == FwhmCalibration.FwhmCheckResult.NonPositiveWidth)
            {
                int last = -1;
                while (last + 1 < channels && !(fitted.ChannelToFwhm(last + 1) > 0.0))
                {
                    last++;
                }
                last = Math.Max(last, 0);
                EnergyCalibration spectrumCalibration = resultData.EnergySpectrum.EnergyCalibration;
                string energy = spectrumCalibration != null
                    ? spectrumCalibration.ChannelToEnergy((double)last).ToString("f1", CultureInfo.InvariantCulture)
                    : "?";
                AppUi.Report(string.Format(CultureInfo.InvariantCulture, Resources.ERRFwhmNonPositiveWidth,
                                           last.ToString(CultureInfo.InvariantCulture), energy),
                             Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            else if (fitted.LastCheck == FwhmCalibration.FwhmCheckResult.NotFinite)
            {
                AppUi.Report(Resources.ERRFwhmNotFinite, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            else
            {
                AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            return null;
        }

        /// <summary>
        /// Подогнанная кривая — в документ (П314): документ помечается изменённым и пики
        /// спектра пересчитываются по новой ширине, только если кривая ДЕЙСТВИТЕЛЬНО
        /// сменилась (<see cref="SameCurve"/>). У спектра, у которого кривой не было,
        /// строки из памяти уходят в неё (<see cref="ResultData.PendingCalibrationLines"/>).
        /// </summary>
        void CommitFittedCurve(DocEnergySpectrum document, FwhmCalibration fitted)
        {
            ResultData resultData = document.ActiveResultData;
            bool changed = !SameCurve(resultData.FwhmCalibration, fitted);
            resultData.FwhmCalibration = fitted;
            resultData.PendingCalibrationLines = new List<CalibrationPeak>();
            if (changed)
            {
                document.Dirty = true;
            }
            calibrationDone = true;
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
            if (changed)
            {
                mainForm.UpdateDetectedPeakView();
                document.UpdateEnergySpectrum();
            }
        }

        /// <summary>
        /// Две кривые FWHM совпадают ВСЕМ, что пишется в файл: форма, коэффициенты
        /// (побитово), строки (с признаком линии и отсчётами), форма пика и её
        /// параметры, суммы χ².
        /// </summary>
        static bool SameCurve(FwhmCalibration a, FwhmCalibration b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            if (a.GetType() != b.GetType()
                || !SameBits(a.Coefficients, b.Coefficients)
                || a.PeakType != b.PeakType
                || !SameBits(a.ExpGaussExpLeftTail, b.ExpGaussExpLeftTail)
                || !SameBits(a.ExpGaussExpRightTail, b.ExpGaussExpRightTail)
                || !SameBits(a.VoigtSigma, b.VoigtSigma)
                || !SameBits(a.VoigtGamma, b.VoigtGamma)
                || !SameBits(a.GaussianChi2Total, b.GaussianChi2Total)
                || !SameBits(a.ExpGaussExpChi2Total, b.ExpGaussExpChi2Total)
                || !SameBits(a.VoigtChi2Total, b.VoigtChi2Total)
                || !SameBits(a.Chi2pNdp, b.Chi2pNdp))
            {
                return false;
            }
            List<CalibrationPeak> pa = a.CalibrationPeaks, pb = b.CalibrationPeaks;
            if (pa == null || pb == null)
            {
                return pa == null && pb == null;
            }
            if (pa.Count != pb.Count)
            {
                return false;
            }
            for (int i = 0; i < pa.Count; i++)
            {
                if (pa[i].Channel != pb[i].Channel
                    || !SameBits(pa[i].Energy, pb[i].Energy)
                    || !SameBits(pa[i].FWHM, pb[i].FWHM)
                    || pa[i].EnergyLine != pb[i].EnergyLine
                    || pa[i].Count != pb[i].Count)
                {
                    return false;
                }
            }
            return true;
        }

        static bool SameBits(double x, double y)
        {
            return BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);
        }

        static bool SameBits(double[] x, double[] y)
        {
            if (x == null || y == null)
            {
                return x == null && y == null;
            }
            if (x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!SameBits(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        void SelectGlobalPeakShape(FwhmCalibration target, List<CalibrationPeak> peaks)
        {
            const int TailSteps = 50;
            int peakCount = peaks.Count;
            double gaussianChi2 = 0.0;
            int gaussianNdp = 0;
            bool hasGaussian = peakCount > 0;

            foreach (CalibrationPeak peak in peaks)
            {
                if (!IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp))
                {
                    hasGaussian = false;
                    break;
                }

                gaussianChi2 += peak.GaussianChi2;
                gaussianNdp += peak.GaussianNdp;
            }

            double expGaussExpChi2 = Double.PositiveInfinity;
            int expGaussExpNdp = 0;
            int bestExpGaussExpCandidate = -1;
            double voigtChi2 = Double.PositiveInfinity;
            int voigtNdp = 0;
            int bestVoigtCandidate = -1;

            for (int candidateIndex = 0; candidateIndex < TailSteps * TailSteps; candidateIndex++)
            {
                double candidateChi2 = 0.0;
                int candidateNdp = 0;
                bool hasCandidateForEveryPeak = peakCount > 0;
                foreach (CalibrationPeak peak in peaks)
                {
                    if (peak.ExpGaussExpCandidateChi2 == null || peak.ExpGaussExpCandidateNdp == null ||
                        candidateIndex >= peak.ExpGaussExpCandidateChi2.Length ||
                        candidateIndex >= peak.ExpGaussExpCandidateNdp.Length ||
                        !IsValidFitStatistic(peak.ExpGaussExpCandidateChi2[candidateIndex], peak.ExpGaussExpCandidateNdp[candidateIndex]))
                    {
                        hasCandidateForEveryPeak = false;
                        break;
                    }

                    candidateChi2 += peak.ExpGaussExpCandidateChi2[candidateIndex];
                    candidateNdp += peak.ExpGaussExpCandidateNdp[candidateIndex];
                }

                if (hasCandidateForEveryPeak && candidateChi2 < expGaussExpChi2)
                {
                    expGaussExpChi2 = candidateChi2;
                    // The two tail parameters are shared by all peaks and are
                    // therefore subtracted once, not once per calibration peak.
                    expGaussExpNdp = candidateNdp + 2 * (peakCount - 1);
                    bestExpGaussExpCandidate = candidateIndex;
                }

                candidateChi2 = 0.0;
                candidateNdp = 0;
                hasCandidateForEveryPeak = peakCount > 0;
                foreach (CalibrationPeak peak in peaks)
                {
                    if (peak.VoigtCandidateChi2 == null || peak.VoigtCandidateNdp == null ||
                        candidateIndex >= peak.VoigtCandidateChi2.Length ||
                        candidateIndex >= peak.VoigtCandidateNdp.Length ||
                        !IsValidFitStatistic(peak.VoigtCandidateChi2[candidateIndex], peak.VoigtCandidateNdp[candidateIndex]))
                    {
                        hasCandidateForEveryPeak = false;
                        break;
                    }

                    candidateChi2 += peak.VoigtCandidateChi2[candidateIndex];
                    candidateNdp += peak.VoigtCandidateNdp[candidateIndex];
                }

                if (hasCandidateForEveryPeak && candidateChi2 < voigtChi2)
                {
                    voigtChi2 = candidateChi2;
                    // Sigma and gamma are also global shape parameters.
                    voigtNdp = candidateNdp + 2 * (peakCount - 1);
                    bestVoigtCandidate = candidateIndex;
                }
            }

            int peakType = FwhmCalibration.GaussianPeakType;
            double selectedChi2 = hasGaussian ? gaussianChi2 : Double.PositiveInfinity;
            int selectedNdp = hasGaussian ? gaussianNdp : 0;
            double selectedScore = hasGaussian
                ? GetPeakShapeAicScore(FwhmCalibration.GaussianPeakType, gaussianChi2)
                : Double.PositiveInfinity;
            if (bestExpGaussExpCandidate >= 0)
            {
                double expGaussExpScore = GetPeakShapeAicScore(FwhmCalibration.ExpGaussExpPeakType, expGaussExpChi2);
                if (expGaussExpScore < selectedScore)
                {
                    peakType = FwhmCalibration.ExpGaussExpPeakType;
                    selectedChi2 = expGaussExpChi2;
                    selectedNdp = expGaussExpNdp;
                    selectedScore = expGaussExpScore;
                }
            }
            if (bestVoigtCandidate >= 0)
            {
                double voigtScore = GetPeakShapeAicScore(FwhmCalibration.VoigtPeakType, voigtChi2);
                if (voigtScore < selectedScore)
                {
                    peakType = FwhmCalibration.VoigtPeakType;
                    selectedChi2 = voigtChi2;
                    selectedNdp = voigtNdp;
                    selectedScore = voigtScore;
                }
            }

            target.PeakType = peakType;
            target.ExpGaussExpLeftTail = 1.0;
            target.ExpGaussExpRightTail = 1.0;
            target.VoigtSigma = 1.0;
            target.VoigtGamma = 1.0;
            target.GaussianChi2Total = hasGaussian ? gaussianChi2 : -1.0;
            target.ExpGaussExpChi2Total = bestExpGaussExpCandidate >= 0 ? expGaussExpChi2 : -1.0;
            target.VoigtChi2Total = bestVoigtCandidate >= 0 ? voigtChi2 : -1.0;
            target.Chi2pNdp = selectedNdp > 0 && !Double.IsInfinity(selectedChi2)
                ? selectedChi2 / selectedNdp
                : 0.0;

            if (peakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                target.ExpGaussExpLeftTail = (bestExpGaussExpCandidate / TailSteps + 1) * 0.1;
                target.ExpGaussExpRightTail = (bestExpGaussExpCandidate % TailSteps + 1) * 0.1;
            }
            else if (peakType == FwhmCalibration.VoigtPeakType)
            {
                target.VoigtSigma = (bestVoigtCandidate / TailSteps + 1) * 0.1;
                target.VoigtGamma = (bestVoigtCandidate % TailSteps + 1) * 0.1;
            }

            ShowGlobalPeakFitComparisonTable(
                peakType,
                hasGaussian ? gaussianChi2 : -1.0,
                hasGaussian ? gaussianNdp : -1,
                bestExpGaussExpCandidate >= 0 ? expGaussExpChi2 : -1.0,
                bestExpGaussExpCandidate >= 0 ? expGaussExpNdp : -1,
                bestExpGaussExpCandidate >= 0 ? GetExpGaussExpParametersText(bestExpGaussExpCandidate, TailSteps) : GetResourceText("PeakFitChiTableUnavailable", "n/a"),
                bestVoigtCandidate >= 0 ? voigtChi2 : -1.0,
                bestVoigtCandidate >= 0 ? voigtNdp : -1,
                bestVoigtCandidate >= 0 ? GetVoigtParametersText(bestVoigtCandidate, TailSteps) : GetResourceText("PeakFitChiTableUnavailable", "n/a"));
        }

        static bool IsValidFitStatistic(double chi2, int ndp)
        {
            return chi2 >= 0.0 && ndp > 0 && !Double.IsNaN(chi2) && !Double.IsInfinity(chi2);
        }

        static double GetPeakShapeAicScore(int peakType, double chi2)
        {
            if (Double.IsNaN(chi2) || Double.IsInfinity(chi2) || chi2 < 0.0)
            {
                return Double.PositiveInfinity;
            }

            // Relative AIC comparison: Gaussian is the baseline, while the other
            // families add two global shape parameters and therefore pay +4.
            switch (peakType)
            {
                case FwhmCalibration.ExpGaussExpPeakType:
                case FwhmCalibration.VoigtPeakType:
                    return chi2 + 4.0;
                default:
                    return chi2;
            }
        }

        static bool HasPeakShapeComparisonData(List<CalibrationPeak> peaks)
        {
            if (peaks == null || peaks.Count == 0)
            {
                return false;
            }

            foreach (CalibrationPeak peak in peaks)
            {
                if (!IsValidFitStatistic(peak.GaussianChi2, peak.GaussianNdp) ||
                    peak.ExpGaussExpCandidateChi2 == null || peak.ExpGaussExpCandidateNdp == null ||
                    peak.VoigtCandidateChi2 == null || peak.VoigtCandidateNdp == null)
                {
                    return false;
                }
            }

            return true;
        }

        void ShowGlobalPeakFitComparisonTable(
            int selectedPeakType,
            double gaussianChi2,
            int gaussianNdp,
            double expGaussExpChi2,
            int expGaussExpNdp,
            string expGaussExpParameters,
            double voigtChi2,
            int voigtNdp,
            string voigtParameters)
        {
            List<PeakFitComparisonItem> items = new List<PeakFitComparisonItem>
            {
                new PeakFitComparisonItem(FwhmCalibration.GaussianPeakType, GetPeakShapeName(FwhmCalibration.GaussianPeakType), gaussianChi2, gaussianNdp, "-"),
                new PeakFitComparisonItem(FwhmCalibration.ExpGaussExpPeakType, GetPeakShapeName(FwhmCalibration.ExpGaussExpPeakType), expGaussExpChi2, expGaussExpNdp, expGaussExpParameters),
                new PeakFitComparisonItem(FwhmCalibration.VoigtPeakType, GetPeakShapeName(FwhmCalibration.VoigtPeakType), voigtChi2, voigtNdp, voigtParameters)
            };

            items.Sort(delegate (PeakFitComparisonItem left, PeakFitComparisonItem right)
            {
                bool leftValid = !Double.IsNaN(left.Score) && !Double.IsInfinity(left.Score);
                bool rightValid = !Double.IsNaN(right.Score) && !Double.IsInfinity(right.Score);
                if (leftValid && rightValid)
                {
                    int scoreComparison = left.Score.CompareTo(right.Score);
                    if (scoreComparison != 0)
                    {
                        return scoreComparison;
                    }

                    return (left.Chi2 / left.Ndp).CompareTo(right.Chi2 / right.Ndp);
                }
                if (leftValid)
                {
                    return -1;
                }
                if (rightValid)
                {
                    return 1;
                }
                return String.Compare(left.CurveName, right.CurveName, StringComparison.CurrentCulture);
            });

            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.AppendFormat(
                CultureInfo.InvariantCulture,
                GetResourceText("PeakFitChiTablePeakSummary", "Selected shape: {0}."),
                GetPeakShapeName(selectedPeakType));
            messageBuilder.AppendLine();
            messageBuilder.AppendLine();

            foreach (PeakFitComparisonItem item in items)
            {
                messageBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} = {2}; {3} = {4}; {5}: {6}",
                    item.CurveName,
                    GetResourceText("PeakFitChiTableScoreColumn", "Score"),
                    FormatPeakShapeScore(item.Score),
                    GetResourceText("PeakFitChiTableChi2PerNdpColumn", "chi2/ndp"),
                    FormatPeakFitRatio(item.Chi2, item.Ndp),
                    GetResourceText("PeakFitChiTableParametersColumn", "Parameters"),
                    item.Parameters);
                messageBuilder.AppendLine();
            }

            // ⛔ (`AMBER283`, решение Amber вопросником 02.11.2026, дословно: «Окно формы пика —
            //    по требованию») Сравнение больше не всплывает модальным окном на каждое
            //    «Выполнить калибровку»: выбор формы прежний, а таблица сравнения ложится в
            //    пункт «Функция пика» окна «График» и открывается, когда её хотят посмотреть.
            //    Без окон (пробы) — строкой в поток ошибок, как прежде.
            string title = GetResourceText("PeakFitChiTableTitle", "Peak fit comparison");
            string comparison = messageBuilder.ToString().TrimEnd();
            peakShapeComparisonText = title + Environment.NewLine + comparison;
            peakShapeComparisonOwner = ActiveResultData;
            if (!AppUi.HasWindows)
            {
                AppUi.Report(comparison, title, MessageBoxIcon.Information);
            }
        }

        string GetExpGaussExpParametersText(int candidateIndex, int tailSteps)
        {
            return String.Format(
                CultureInfo.InvariantCulture,
                "{0}={1:0.0}; {2}={3:0.0}",
                expGaussExpLeftParameterLabelText,
                (candidateIndex / tailSteps + 1) * 0.1,
                expGaussExpRightParameterLabelText,
                (candidateIndex % tailSteps + 1) * 0.1);
        }

        string GetVoigtParametersText(int candidateIndex, int tailSteps)
        {
            return String.Format(
                CultureInfo.InvariantCulture,
                "{0}={1:0.0}; {2}={3:0.0}",
                Resources.ResourceManager.GetString("VoigtRelativeSigmaLabel"),
                (candidateIndex / tailSteps + 1) * 0.1,
                Resources.ResourceManager.GetString("VoigtRelativeGammaLabel"),
                (candidateIndex % tailSteps + 1) * 0.1);
        }

        string FormatPeakFitRatio(double chi2, int ndp)
        {
            return IsValidFitStatistic(chi2, ndp)
                ? (chi2 / ndp).ToString("0.#####", CultureInfo.InvariantCulture)
                : GetResourceText("PeakFitChiTableUnavailable", "n/a");
        }

        string FormatPeakShapeScore(double score)
        {
            return !Double.IsNaN(score) && !Double.IsInfinity(score)
                ? score.ToString("0.#####", CultureInfo.InvariantCulture)
                : GetResourceText("PeakFitChiTableUnavailable", "n/a");
        }

        static string GetResourceText(string resourceName, string fallback)
        {
            string value = Resources.ResourceManager.GetString(resourceName);
            return String.IsNullOrEmpty(value) ? fallback : value;
        }

        sealed class PeakFitComparisonItem
        {
            public PeakFitComparisonItem(int peakType, string curveName, double chi2, int ndp, string parameters)
            {
                PeakType = peakType;
                CurveName = curveName;
                Chi2 = chi2;
                Ndp = ndp;
                Parameters = parameters;
                Score = GetPeakShapeAicScore(peakType, chi2);
            }

            public int PeakType { get; private set; }
            public string CurveName { get; private set; }
            public double Chi2 { get; private set; }
            public int Ndp { get; private set; }
            public string Parameters { get; private set; }
            public double Score { get; private set; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Сохранение в прибор
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// (`AMBER276`) Кривая FWHM для конфигурации прибора: КОПИЯ без признака линий и
        /// без отсчётов. Линии — свойство ЭТОГО спектра (его каналы и его энергии);
        /// спектр, который возьмёт кривую у прибора, получит их опорами FWHM (серыми), а
        /// не чужими линиями энергии. Файл прибора при этом не меняет вида: элементы
        /// <c>EnergyLine</c>/<c>Count</c> пишутся только у линий.
        /// </summary>
        internal static FwhmCalibration ForDevice(FwhmCalibration calibration)
        {
            if (calibration == null)
            {
                return null;
            }
            FwhmCalibration copy = calibration.Clone();
            foreach (CalibrationPeak peak in copy.CalibrationPeaks)
            {
                peak.EnergyLine = false;
                peak.Count = 0;
            }
            return copy;
        }

        /// <summary>
        /// «Сохранить в конфигурацию прибора» — одна кнопка для обеих калибровок.
        /// Шкала энергии — какая стоит в полях (прежняя кнопка вкладки энергии, `A2`,
        /// `AMBER188`), с кривизной тракта спектра (`AMBER271`); кривая FWHM —
        /// подогнанная заново по строкам таблицы, как прежде делала кнопка вкладки FWHM
        /// (`AMBER189` (а), П314), а если строк меньше, чем нужно виду кривой, — та,
        /// что стоит у спектра. Отказ подгонки FWHM или записи — не меняется НИЧЕГО ни
        /// в приборе, ни в спектре.
        /// </summary>
        private void SaveToDeviceCfgButton_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData resultData = ActiveResultData;
            if (activeDocument == null || resultData == null || energyCalibration == null)
            {
                return;
            }
            // (`AMBER271`) если κ прибора сменится, вопрос говорит это числами.
            string question = Resources.MSGSaveCalibration;
            string curvatureNote = TractCurvatureChangeNote(resultData);
            if (curvatureNote != null)
            {
                question += "\n\n" + curvatureNote;
            }
            if (MessageBox.Show(question, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
            {
                return;
            }
            DeviceConfigInfo deviceConfig = resultData.DeviceConfig;
            if (deviceConfig == null || deviceConfig.Guid == null || deviceConfig.Guid == "")
            {
                AppUi.Report(Resources.ERRDeviceConfigNotSelected, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return;
            }
            if (!this.energyCalibration.CheckCalibration(channels: resultData.EnergySpectrum.NumberOfChannels))
            {
                AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return;
            }
            FWHMPeakDetectionMethodConfig peakDetectionMethodConfig = deviceConfig.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            FwhmCalibration fitted = null;
            FwhmCalibration template = fwhmCalibration ?? CurveOfSelectedType();
            if (peakDetectionMethodConfig != null && (fwhmCalibration != null || resultData.FwhmCalibration != null))
            {
                if (resultData.CalibrationLines.Count >= template.MinPeaksRequirement())
                {
                    // ⛔ ПОДГОНЯЕТСЯ КОПИЯ (`AMBER189` (а)).
                    fitted = FitCopyOrRefuse(resultData);
                    if (fitted == null)
                    {
                        return;
                    }
                }
                else if (resultData.FwhmCalibration != null)
                {
                    fitted = resultData.FwhmCalibration.Clone();
                }
            }

            // ⛔ СНИМОК ПРЕЖНИХ ЗНАЧЕНИЙ (`A2`): объект конфигурации прибора в памяти один
            // на всех, и при сорванной записи всё возвращается на место.
            EnergyCalibration previousEnergyCalibration = deviceConfig.EnergyCalibration;
            RadiaCodeDeviceConfig rc_deviceConfig = deviceConfig.InputDeviceConfig as RadiaCodeDeviceConfig;
            ObsidianDeviceConfig obs_deviceConfig = deviceConfig.InputDeviceConfig as ObsidianDeviceConfig;
            PolynomialEnergyCalibration previousRcCalibration =
                rc_deviceConfig != null ? rc_deviceConfig.RC_EnergyCalibration : null;
            PolynomialEnergyCalibration previousObsCalibration =
                obs_deviceConfig != null ? obs_deviceConfig.OBS_EnergyCalibration : null;
            double previousTractCurvature = deviceConfig.TractCurvature;
            FwhmCalibration previousDeviceFwhm = peakDetectionMethodConfig != null ? peakDetectionMethodConfig.FwhmCalibration : null;

            // ⛔ `AMBER188(б)`: КОПИЯ — прибор, документ и вид не держат один объект.
            deviceConfig.EnergyCalibration = this.energyCalibration.Clone();
            // (`AMBER271`) кривизна тракта — спектра; у спектра без своей κ это κ самого
            // прибора, и запись её не меняет.
            deviceConfig.TractCurvature = resultData.TractCurvature;
            if (rc_deviceConfig != null)
            {
                if (this.energyCalibration.PolynomialOrder == 2)
                {
                    rc_deviceConfig.RC_EnergyCalibration = (PolynomialEnergyCalibration)this.energyCalibration.Clone();
                }
                else if (this.energyCalibration.PolynomialOrder > 2 && this.rc_EnergyCalibration != null)
                {
                    rc_deviceConfig.RC_EnergyCalibration = (PolynomialEnergyCalibration)this.rc_EnergyCalibration.Clone();
                }
                else
                {
                    rc_deviceConfig.RC_EnergyCalibration = null;
                }
            }
            else if (obs_deviceConfig != null)
            {
                if (this.energyCalibration.PolynomialOrder == 2)
                {
                    obs_deviceConfig.OBS_EnergyCalibration = (PolynomialEnergyCalibration)this.energyCalibration.Clone();
                }
                else if (this.energyCalibration.PolynomialOrder > 2 && this.rc_EnergyCalibration != null)
                {
                    obs_deviceConfig.OBS_EnergyCalibration = (PolynomialEnergyCalibration)this.rc_EnergyCalibration.Clone();
                }
                else
                {
                    obs_deviceConfig.OBS_EnergyCalibration = null;
                }
            }
            if (fitted != null)
            {
                peakDetectionMethodConfig.FwhmCalibration = ForDevice(fitted);
            }
            // ⛔ ОТВЕТ МЕНЕДЖЕРА ЧИТАЕТСЯ (`A2`). При отказе не применяется НИЧЕГО:
            // обещание кнопки — запись, а половинный исход (спектр пересчитан, файл
            // прежний) переживает перезапуск молча.
            if (!DeviceConfigManager.GetInstance().SaveConfig(deviceConfig))
            {
                deviceConfig.EnergyCalibration = previousEnergyCalibration;
                deviceConfig.TractCurvature = previousTractCurvature;
                if (rc_deviceConfig != null)
                {
                    rc_deviceConfig.RC_EnergyCalibration = previousRcCalibration;
                }
                if (obs_deviceConfig != null)
                {
                    obs_deviceConfig.OBS_EnergyCalibration = previousObsCalibration;
                }
                if (peakDetectionMethodConfig != null)
                {
                    peakDetectionMethodConfig.FwhmCalibration = previousDeviceFwhm;
                }
                AppUi.Report(Resources.ERRCalibrationNotSavedToDevice, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                return;
            }
            resultData.EnergySpectrum.EnergyCalibration = this.energyCalibration.Clone();
            if (fitted != null)
            {
                // (П314) В документ — тем же хвостом, что у «Рассчитать».
                CommitFittedCurve(activeDocument, fitted.Clone());
            }
            mainForm.UpdateDeviceConfigForm();
        }

        /// <summary>
        /// (`AMBER271`) Строка вопроса кнопки «Сохранить в конфигурацию прибора»
        /// о кривизне тракта: null, когда κ прибора не меняется (у спектра своей
        /// κ нет или она та же), иначе — прежняя и новая κ прибора, точкой
        /// (`A242`). Сравнение побитовое: «почти та же» κ — уже другая.
        /// </summary>
        internal static string TractCurvatureChangeNote(ResultData data)
        {
            if (data == null || data.DeviceConfig == null)
            {
                return null;
            }

            double device = data.DeviceConfig.TractCurvature;
            double spectrum = data.TractCurvature;
            if (spectrum.Equals(device))
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, Resources.MSGSaveEnergyCalibrationTractCurvature,
                                 device.ToString("R", CultureInfo.InvariantCulture),
                                 spectrum.ToString("R", CultureInfo.InvariantCulture));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Графики и форма пика
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Пункт окна графиков, выбранный в последний раз (`AMBER283`): следующее окно
        /// открывается на нём, если этот график у спектра есть.
        /// </summary>
        static int lastGraphView = CalibrationGraphWindow.EnergyView;

        /// <summary>
        /// ⛔ (`AMBER283`, 02.11.2026) ОДНА КНОПКА «ГРАФИК» НА ОБА ГРАФИКА КАЛИБРОВКИ. Amber,
        /// консоль, дословно: «Графики смотрят в похожие классы, их можно объединить в одну
        /// кнопку а в самом открытом окне выбирать Energy/FWHM в combobox.» Прежде здесь
        /// было две кнопки: «График» (шкала энергии, <see cref="CalibrationGraph"/>) и
        /// «Просмотр ...» (кривая FWHM, <see cref="FWHMCalibrationGraph"/>), каждая со своим
        /// окном. Теперь оба вида строятся здесь — каждый по прежнему правилу и только если
        /// он у спектра есть — и показываются ОДНИМ окном (<see cref="CalibrationGraphWindow"/>)
        /// с выбором «Энергия / FWHM». Итоги обоих разбираются после закрытия окна прежней
        /// арифметикой.
        ///
        /// ГРАФИК FWHM. Сторож по образцу `GetAllPeaksButton_Click` (`A236`) и
        /// энергокалибровка — четвёртое звено (`A243`): <c>Init</c> разыменовывает шкалу
        /// энергии первой строкой. ⛔ Поле `fwhmCalibration` здесь НЕ переприсваивается:
        /// между обновлениями вида оно законно держит ещё не сохранённый выбор вида кривой.
        /// «Сохранить новые точки» графика (П314) переписывает строки ДОКУМЕНТА — документ
        /// изменён.
        ///
        /// ГРАФИК ЭНЕРГИИ (прежняя кнопка «График» вкладки энергии): линии таблицы —
        /// точками графика; «Сохранить новые точки» переписывает
        /// <c>ResultData.CalibrationPoints</c>, и сдвинутые каналы возвращаются в строки
        /// по порядку (график точек не добавляет и не снимает). Строки, куда они
        /// возвращаются, — те же МЕСТА списка, что при открытии окна: если в том же окне
        /// и график FWHM переписал строки документа (их копиями в том же порядке), энергия
        /// ложится в новые строки, а не в снятые. Таблица отмечается изменённой, только
        /// если график что-то записал (прежде — после всякого закрытия графика энергии,
        /// и одно окно на два графика гасило бы «выполнено» и тогда, когда смотрели одну
        /// FWHM).
        /// </summary>
        private void ViewCalibrationButton_Click(object sender, EventArgs e)
        {
            ResultData resultData = ActiveResultData;
            EnsureFwhmCalibration(resultData);
            if (resultData == null || resultData.EnergySpectrum == null)
            {
                UpdateCalibrateButtonState();
                return;
            }

            // ── вид шкалы энергии: две линии и больше и шкала вида ───────────────
            List<CalibrationPeak> linesAtOpen = resultData.CalibrationLines;
            List<int> energyRows = new List<int>();
            for (int i = 0; i < linesAtOpen.Count; i++)
            {
                if (linesAtOpen[i] != null && linesAtOpen[i].EnergyLine)
                {
                    energyRows.Add(i);
                }
            }
            // порядок — как у точек графика (`EnergyPoints`: по каналу, устойчиво)
            energyRows = energyRows.OrderBy(i => linesAtOpen[i].Channel).ToList();
            CalibrationGraph energyGraph = null;
            List<CalibrationPoint> points = null;
            if (energyCalibration != null && energyRows.Count > 1)
            {
                points = EnergyPoints(linesAtOpen);
                resultData.CalibrationPoints = points;
                energyGraph = new CalibrationGraph(this.mainForm);
                energyGraph.SetCalibration(this.energyCalibration, resultData.EnergySpectrum.NumberOfChannels, (int)this.numericUpDown6.Value, this.checkBox2.Checked);
                energyGraph.SetCalibrationPoints(points);
            }

            // ── вид кривой FWHM: кривая вида и документа и шкала энергии спектра ──
            FWHMCalibrationGraph fwhmGraph = null;
            if (fwhmCalibration != null
                && resultData.FwhmCalibration != null
                && resultData.EnergySpectrum.EnergyCalibration != null)
            {
                fwhmGraph = new FWHMCalibrationGraph(this.mainForm);
                fwhmGraph.Init(fwhmCalibration, resultData.EnergySpectrum.NumberOfChannels);
            }

            // ── (`AMBER283`) вид функции пика: прежняя кнопка «Просмотр» рамки FWHM — третьим
            //    пунктом этого окна (решение Amber вопросником 02.11.2026, дословно: «Только
            //    «Просмотр» → в «График» (Рекомендую)»); под кривой — сравнение форм пика
            //    последнего расчёта (`peakShapeComparisonText`, «Окно формы пика — по требованию»)
            Control peakShapeView = fwhmCalibration != null ? BuildPeakShapeView(resultData) : null;

            if (energyGraph == null && fwhmGraph == null && peakShapeView == null)
            {
                UpdateCalibrateButtonState();
                return;
            }

            Control[] built = { energyGraph, fwhmGraph, peakShapeView };
            int start = lastGraphView >= 0 && lastGraphView < built.Length ? lastGraphView : CalibrationGraphWindow.EnergyView;
            if (built[start] == null)
            {
                // открыть на первом пункте, который у спектра есть
                start = Array.FindIndex(built, v => v != null);
            }
            using (CalibrationGraphWindow window = new CalibrationGraphWindow(this.mainForm, energyGraph, fwhmGraph, peakShapeView, start))
            {
                // без владельца — как прежние два окна графиков (владелец — активное окно)
                window.ShowDialog();
                lastGraphView = window.SelectedView;
            }

            bool changed = false;
            if (fwhmGraph != null && fwhmGraph.PointsUpdated)
            {
                mainForm.ActiveDocument.Dirty = true;
                fwhmCalibration.CalibrationPeaks = CalibrationPeak.ClonePeaks(resultData.FwhmCalibration.CalibrationPeaks);
                changed = true;
            }
            if (energyGraph != null)
            {
                List<CalibrationPoint> updated = resultData.CalibrationPoints;
                List<CalibrationPeak> linesNow = resultData.CalibrationLines;
                if (!ReferenceEquals(updated, points) && updated != null && updated.Count == energyRows.Count
                    && linesNow.Count == linesAtOpen.Count)
                {
                    for (int i = 0; i < energyRows.Count; i++)
                    {
                        CalibrationPeak line = linesNow[energyRows[i]];
                        line.Channel = updated[i].Channel;
                        line.Energy = (double)updated[i].Energy;
                        line.Count = updated[i].Count;
                    }
                    mainForm.ActiveDocument.Dirty = true;
                    changed = true;
                }
                resultData.CalibrationPoints = new List<CalibrationPoint>();
            }
            if (changed)
            {
                MarkModified();
            }
            UpdateTable();
            UpdateCalibrateButtonState();
        }

        /// <summary>
        /// (`AMBER283`) Вид «Функция пика» окна графиков: прежнее окно предпросмотра формы пика
        /// (<see cref="PeakShapePreviewGraph"/>, не верхним окном, а видом) и под ним — сравнение
        /// форм пика последнего «Выполнить калибровку» этого спектра, если оно было.
        /// </summary>
        Control BuildPeakShapeView(ResultData resultData)
        {
            PeakShapePreviewGraph graph = new PeakShapePreviewGraph(this.mainForm);
            graph.Init(fwhmCalibration, OwnText("PeakShapePreviewTitle", "Selected peak function"));
            graph.TopLevel = false;
            graph.FormBorderStyle = FormBorderStyle.None;
            graph.MinimumSize = Size.Empty;
            graph.Dock = DockStyle.Fill;
            // вид рисует от размера клиента — при растяжении окна перерисовать целиком
            graph.Resize += delegate { graph.Invalidate(); };
            graph.Visible = true;

            TextBox comparison = new TextBox();
            comparison.Multiline = true;
            comparison.ReadOnly = true;
            comparison.WordWrap = true;
            comparison.ScrollBars = ScrollBars.Vertical;
            comparison.Dock = DockStyle.Bottom;
            comparison.Height = 96;
            comparison.TabStop = false;
            comparison.Text = peakShapeComparisonText != null && ReferenceEquals(peakShapeComparisonOwner, resultData)
                ? peakShapeComparisonText
                : OwnText("PeakShapeComparisonPending", "The comparison of the peak shapes appears here after “Execute Calibration”, when every row of the table has its peak shape measured.");

            Panel host = new Panel();
            host.Text = graph.Text;
            // порядок важен: заполняющий вид — первым (наверх по Z), строка сравнения — снизу
            host.Controls.Add(graph);
            host.Controls.Add(comparison);
            return host;
        }

        private void ViewPeakShapeButton_Click(object sender, EventArgs e)
        {
            if (fwhmCalibration == null)
            {
                return;
            }

            PeakShapePreviewGraph graph = new PeakShapePreviewGraph(this.mainForm);
            // (`AMBER283`) Заголовок окна — прежний текст рамки «Выбранная функция пика»:
            // рамки на панели больше нет, текст лежит в resx формы.
            string title = new System.ComponentModel.ComponentResourceManager(typeof(DCFwhmCalibrationView)).GetString("PeakShapePreviewTitle");
            graph.Init(fwhmCalibration, title);
            graph.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Шкала энергии: коэффициенты руками, сброс, веса, степень
        // ══════════════════════════════════════════════════════════════════════

        public void LoadCalibrationPoints()
        {
            UpdateFwhmCalibration();
        }

        public void UpdateEnergyCalibrationConfig()
        {
        }

        public void SetEnergyCalibration(EnergyCalibration energyCalibration, EnergyCalibration defaultEnergyCalibration)
        {
            this.energyCalibration = (PolynomialEnergyCalibration)energyCalibration.Clone();
            this.defaultEnergyCalibration = (PolynomialEnergyCalibration)defaultEnergyCalibration.Clone();
            this.SyncRcEnergyCalibration();
            this.ShowCoefficients();
            // (`AMBER270`) другой спектр — подпись прежней подгонки κ к нему не относится
            this.lastTractFit = null;
            this.ShowTractCurvature();
            this.UpdateTable();
            this.UpdateCalibrateButtonState();
        }

        /// <summary>
        /// Пять полей коэффициентов — из шкалы вида, инвариантной культурой (`A242`).
        /// Показанный текст запоминается (<see cref="shownCoefficientText"/>): см.
        /// <see cref="setNewCalibration"/>.
        /// </summary>
        void ShowCoefficients()
        {
            this.numericUpDown1.Text = "0";
            this.numericUpDown2.Text = "0";
            this.numericUpDown3.Text = "0";
            this.numericUpDown4.Text = "0";
            this.numericUpDown5.Text = "0";
            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);
            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);
            if (this.energyCalibration.PolynomialOrder >= 2)
            {
                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);
            }
            if (this.energyCalibration.PolynomialOrder >= 3)
            {
                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString(CultureInfo.InvariantCulture);
            }
            // (`AMBER269`) `>=`, а не `==`: шкала выше 4-й степени годна с 01.11.2026,
            //   и её x⁴ прежде не показывался вовсе. Членов выше x⁴ полей нет;
            //   правка поля их сохраняет (`PolynomialEnergyCalibration.WithCoefficient`),
            //   а строка над полями показывает такую шкалу формулой целиком, иначе
            //   старшие члены были бы на экране не видны вовсе.
            if (this.energyCalibration.PolynomialOrder >= 4)
            {
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);
            }
            if (this.formulaHintText == null)
            {
                this.formulaHintText = this.textBox15.Text;
            }
            this.textBox15.Text = this.energyCalibration.PolynomialOrder > 4
                ? this.energyCalibration.ToString()
                : this.formulaHintText;
            // (`AMBER283`, вариант A) Строка формулы — заголовок рамки энергии (`textBox15`
            // скрыт и держит прежнее значение для читателей): шкалу степени выше 4 заголовок
            // показывает целиком, как прежде эта строка.
            this.groupBox1.Text = this.energyCalibration.PolynomialOrder > 4
                ? this.energyCalibration.ToString()
                : this.energyGroupTitle;
            foreach (TextBox box in new[] { this.numericUpDown1, this.numericUpDown2, this.numericUpDown3, this.numericUpDown4, this.numericUpDown5 })
            {
                this.shownCoefficientText[box] = box.Text;
            }
        }

        /// <summary>Текст, которым поле коэффициента показано (см. <see cref="setNewCalibration"/>).</summary>
        readonly Dictionary<TextBox, string> shownCoefficientText = new Dictionary<TextBox, string>();

        /// <summary>(`AMBER269`) Подпись над полями из ресурса — её возвращают шкале степени ≤ 4.</summary>
        string formulaHintText;

        // ── (`AMBER270`) кривизна тракта спектра: показ, правка, причина ────────

        /// <summary>Последняя подгонка κ этого вида (для подписи под полем); null — не было.</summary>
        TractCurvatureFit lastTractFit;

        /// <summary>Текст, которым поле κ показано (сторож неизменённого текста, как у коэффициентов).</summary>
        string shownTractCurvatureText;

        /// <summary>Итог подгонки κ строкой состояния: число и вещество либо причина.</summary>
        internal static string TractCurvatureStatus(TractCurvatureFit fit)
        {
            if (fit == null)
            {
                return string.Empty;
            }
            return fit.Ok
                ? String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureFitted,
                                fit.Kappa.ToString("0.00000", CultureInfo.InvariantCulture), fit.Curve,
                                fit.Lines.ToString(CultureInfo.InvariantCulture)) + RejectedTail(fit)
                : fit.Refusal;
        }

        /// <summary>
        /// (`AMBER295`) Хвост итога подогнанной κ, когда часть линий в неё не пошла: какие и
        /// почему (<see cref="RejectedNote"/>). Пусто — пошли все. Прежний хвост `AMBER270`
        /// «центр не измерен: взят канал строки» снят: такая строка в κ больше не идёт.
        /// </summary>
        static string RejectedTail(TractCurvatureFit fit)
        {
            string tail = string.Empty;
            if (fit.AutoSum != null)
            {
                tail += "; " + String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureAutoSum,
                                             fit.AutoSum[0].ToString("0.###", CultureInfo.InvariantCulture),
                                             fit.AutoSum[1].ToString("0.###", CultureInfo.InvariantCulture),
                                             fit.AutoSum[2].ToString("0.###", CultureInfo.InvariantCulture),
                                             fit.AutoSum[3].ToString("0.0", CultureInfo.InvariantCulture));
            }
            List<double[]> listed = fit.SumPeaks == null ? new List<double[]>()
                : fit.SumPeaks.Where(sp => fit.AutoSum == null || Math.Abs(sp[0] - fit.AutoSum[0]) > 1e-9).ToList();
            if (listed.Count > 0)
            {
                tail += "; " + String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureSumNote,
                    string.Join(", ", listed.Select(sp => String.Format(CultureInfo.InvariantCulture, "{0} = {1} + {2}",
                        sp[0].ToString("0.###", CultureInfo.InvariantCulture),
                        sp[1].ToString("0.###", CultureInfo.InvariantCulture),
                        sp[2].ToString("0.###", CultureInfo.InvariantCulture)))));
            }
            string note = RejectedNote(fit);
            return note.Length > 0 ? tail + "; " + note : tail;
        }

        /// <summary>Подсказка поля κ из resx (см. конструктор): основа, над которой встаёт <see cref="tractCurvatureNote"/>.</summary>
        readonly string tractCurvatureTipBase;

        /// <summary>
        /// Подпись поля κ: чья κ (своя у спектра / значение прибора), а после «Выполнить
        /// калибровку» — по чему подогнана или почему нет. Пусто — документа нет.
        /// </summary>
        string tractCurvatureNote = string.Empty;

        /// <summary>
        /// Цвет числа κ в покое (`AMBER283`): серое — значение прибора, чёрное — своё у
        /// спектра; то же правило, что у серой энергии строки таблицы («от прибора»).
        /// Синий (правится) и красный (не число) ставит правка, как прежде.
        /// </summary>
        Color TractCurvatureRestColor()
        {
            ResultData resultData = ActiveResultData;
            return resultData != null && !resultData.HasOwnTractCurvature ? SystemColors.GrayText : SystemColors.WindowText;
        }

        /// <summary>
        /// Поле κ — кривизна тракта, которой разбирается спектр (<see cref="ResultData.TractCurvature"/>:
        /// своя либо прибора), и подпись к нему: чья она, а после «Выполнить калибровку» —
        /// по чему подогнана или почему нет.
        ///
        /// ⛔ (`AMBER283`, 02.11.2026) Подпись — ПОДСКАЗКОЙ поля, а не строкой под ним.
        /// Amber, консоль, дословно, к снимку панели: «1.Убрать.»; вопросником того же дня,
        /// дословно: «Тогда оставь, но сделай компактно». Поле, подгонка, «Сброс» и «Сохр. в
        /// конф. устр.» — прежние; поле κ — одна строка рядом с коэффициентами, без второй
        /// строки «значение прибора». Чья κ — видно цветом числа
        /// (<see cref="TractCurvatureRestColor"/>), подпись целиком — первой строкой
        /// подсказки поля и его метки; итог подгонки, как прежде, ещё и в строке состояния.
        /// </summary>
        void ShowTractCurvature()
        {
            ResultData resultData = ActiveResultData;
            if (resultData == null)
            {
                this.tractCurvatureTextBox.Text = string.Empty;
                this.shownTractCurvatureText = string.Empty;
                this.tractCurvatureNote = string.Empty;
                this.toolTip1.SetToolTip(this.tractCurvatureTextBox, this.tractCurvatureTipBase);
                this.toolTip1.SetToolTip(this.tractCurvatureLabel, this.tractCurvatureTipBase);
                return;
            }
            this.tractCurvatureTextBox.Text = resultData.TractCurvature.ToString("R", CultureInfo.InvariantCulture);
            this.tractCurvatureTextBox.ForeColor = TractCurvatureRestColor();
            this.shownTractCurvatureText = this.tractCurvatureTextBox.Text;
            string note = resultData.HasOwnTractCurvature ? Resources.MSGTractCurvatureOwn : Resources.MSGTractCurvatureDevice;
            if (this.lastTractFit != null)
            {
                // подпись короткая: число уже в поле; полный текст — в строке состояния
                note = this.lastTractFit.Ok
                    ? String.Format(CultureInfo.InvariantCulture, Resources.MSGTractCurvatureFittedNote, this.lastTractFit.Curve,
                                    this.lastTractFit.Lines.ToString(CultureInfo.InvariantCulture)) + RejectedTail(this.lastTractFit)
                    : this.lastTractFit.Refusal;
            }
            this.tractCurvatureNote = note ?? string.Empty;
            string tip = string.IsNullOrEmpty(this.tractCurvatureNote)
                ? this.tractCurvatureTipBase
                : AppUi.WrapTip(this.tractCurvatureNote) + Environment.NewLine + Environment.NewLine + this.tractCurvatureTipBase;
            this.toolTip1.SetToolTip(this.tractCurvatureTextBox, tip);
            this.toolTip1.SetToolTip(this.tractCurvatureLabel, tip);
        }

        /// <summary>
        /// Правка κ руками: текст не правили — ничего; правили — своя κ спектра (`AMBER271`),
        /// документ изменён, разбор пересчитается. Нечисло — поле краснеет, κ прежняя.
        /// </summary>
        void SetTractCurvatureFromField()
        {
            ResultData resultData = ActiveResultData;
            if (resultData == null || this.mainForm.ActiveDocument == null)
            {
                return;
            }
            if (this.tractCurvatureTextBox.Text == this.shownTractCurvatureText)
            {
                this.tractCurvatureTextBox.ForeColor = TractCurvatureRestColor();
                return;
            }
            double value;
            if (!UserNumber.TryParseDouble(this.tractCurvatureTextBox.Text, out value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                this.tractCurvatureTextBox.ForeColor = Color.Red;
                return;
            }
            if (resultData.HasOwnTractCurvature && resultData.TractCurvature.Equals(value))
            {
                this.shownTractCurvatureText = this.tractCurvatureTextBox.Text;
                this.tractCurvatureTextBox.ForeColor = TractCurvatureRestColor();
                return;
            }
            resultData.TractCurvature = value;
            this.lastTractFit = null;
            this.ShowTractCurvature();
            this.UpdateEnergyCalibration();
        }

        void tractCurvatureTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                this.SetTractCurvatureFromField();
                e.SuppressKeyPress = true;
            }
            else
            {
                this.tractCurvatureTextBox.ForeColor = Color.Blue;
            }
        }

        void tractCurvatureTextBox_Leave(object sender, EventArgs e)
        {
            this.SetTractCurvatureFromField();
        }

        void button1_Click(object sender, EventArgs e)
        {
            if (this.defaultEnergyCalibration == null || ActiveResultData == null)
            {
                return;
            }
            // (`AMBER270`, находка 2 `AMBER271`) «Сброс» возвращает и κ прибора: спектр
            // снова разбирается кривизной своего прибора, как шкалой прибора.
            ActiveResultData.ForgetTractCurvature();
            this.lastTractFit = null;
            this.ShowTractCurvature();
            this.energyCalibration = (PolynomialEnergyCalibration)this.defaultEnergyCalibration.Clone();
            this.SyncRcEnergyCalibration();
            this.numericUpDown1.ForeColor = Color.Black;
            this.numericUpDown2.ForeColor = Color.Black;
            this.numericUpDown3.ForeColor = Color.Black;
            this.numericUpDown4.ForeColor = Color.Black;
            this.numericUpDown5.ForeColor = Color.Black;
            this.ShowCoefficients();
            this.UpdateEnergyCalibration();
        }

        void UpdateEnergyCalibration()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration = this.energyCalibration.Clone();
            activeDocument.Dirty = true;
            activeDocument.UpdateEnergySpectrum();
            this.mainForm.ShowMeasurementResult(true);
            this.mainForm.UpdateDetectedPeakView();
            // (`AMBER283`) шкала сменилась (сброс, правка коэффициента или κ руками) — итог
            // прежнего расчёта под таблицей уже не о ней; «Выполнить калибровку» кладёт свой
            // итог после этого вызова
            this.runSummary = null;
            this.UpdateCalibrateButtonState();
        }

        // ⛔ (`AMBER280`, 29.10.2026) Галка «Автокалибровка» (`checkBox1`) и кнопка её
        // настройки «Изменить...» (`button5`) сняты вместе со стабилизатором пиков. Amber,
        // консоль, дословно: «1. Автокалибратор - полностью удалить из бекмони.»

        void checkBox2_Click(object sender, EventArgs e)
        {
            MarkModified();
            this.UpdateTableColumns();
            UpdateCalibrateButtonState();
        }

        void numericUpDown6_ValueChanged(object sender, EventArgs e)
        {
            if (settingPolynomialOrder)
            {
                return;
            }
            MarkModified();
            UpdateCalibrateButtonState();
        }

        void setNewCalibration(TextBox t, int order)
        {
            if (this.energyCalibration == null || ActiveResultData == null)
            {
                return;
            }
            PolynomialEnergyCalibration pe = this.energyCalibration;
            // ⛔ (`AMBER276`, находка экраном 29.10.2026) Текст поля не правили — шкала не
            // трогается. Поле печатает double пятнадцатью знаками, и прежде выход из поля,
            // в которое человек только зашёл, разбирал этот текст в СОСЕДНИЙ double
            // (0.00091681334111664073 → 0.000916813341116641): шкала менялась в последних
            // битах, документ помечался изменённым, и такая шкала уходила в файл и в прибор.
            string shown;
            if (this.shownCoefficientText.TryGetValue(t, out shown) && shown == t.Text)
            {
                t.ForeColor = Color.Black;
                return;
            }
            try
            {
                double result = fromStringtoDouble(t.Text);

                // ⛔ `AMBER188(б, д)`: новая шкала строится ОБЩИМ помощником в НОВОМ
                // объекте и присваивается только после проверки («Подъём до введённой»).
                PolynomialEnergyCalibration newPe = PolynomialEnergyCalibration.WithCoefficient(pe, order, result);

                if (newPe.Equals(pe))
                {
                    t.ForeColor = Color.Black;
                    return;
                }

                if (!newPe.CheckCalibration(channels: ActiveResultData.EnergySpectrum.NumberOfChannels))
                {
                    throw new Exception();
                }
                this.energyCalibration = newPe;

                t.ForeColor = Color.Black;
                this.shownCoefficientText[t] = t.Text;
                this.SyncRcEnergyCalibration();
                this.UpdateEnergyCalibration();
            }
            catch
            {
                AppUi.Report(Resources.CalibrationFunctionError, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                t.ForeColor = Color.Red;
            }
        }

        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            CoefficientKeyDown(this.numericUpDown1, 2, e);
        }

        void numericUpDown1_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown1, 2);
        }

        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            CoefficientKeyDown(this.numericUpDown2, 1, e);
        }

        void numericUpDown2_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown2, 1);
        }

        void numericUpDown3_KeyDown(object sender, KeyEventArgs e)
        {
            CoefficientKeyDown(this.numericUpDown3, 0, e);
        }

        void numericUpDown3_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown3, 0);
        }

        void numericUpDown4_KeyDown(object sender, KeyEventArgs e)
        {
            CoefficientKeyDown(this.numericUpDown4, 4, e);
        }

        void numericUpDown4_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown4, 4);
        }

        void numericUpDown5_KeyDown(object sender, KeyEventArgs e)
        {
            CoefficientKeyDown(this.numericUpDown5, 3, e);
        }

        void numericUpDown5_Leave(object sender, EventArgs e)
        {
            setNewCalibration(this.numericUpDown5, 3);
        }

        void CoefficientKeyDown(TextBox box, int order, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                setNewCalibration(box, order);
                e.SuppressKeyPress = true;
            }
            else
            {
                box.ForeColor = Color.Blue;
            }
        }

        double fromStringtoDouble(string str)
        {
            double result;
            if (UserNumber.TryParseDouble(str, out result))
            {
                // Let's try not to prohibit the user from entering data that seems strange.
                // For example: kromek CZT have absolute offset = -400
                return result;
            }
            throw new Exception();
        }

        void SyncRcEnergyCalibration()
        {
            if (this.energyCalibration != null && this.energyCalibration.PolynomialOrder == 2)
            {
                this.rc_EnergyCalibration = (PolynomialEnergyCalibration)this.energyCalibration.Clone();
            }
            else
            {
                this.rc_EnergyCalibration = null;
            }
        }
    }
}
