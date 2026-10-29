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
    /// пропадает: ручной ввод коэффициентов полинома, автокалибровка с её настройкой,
    /// веса, «собрать всё», графики обеих калибровок, вид кривой FWHM, форма пика,
    /// сохранение в прибор (с кривизной тракта, `AMBER271`).
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

            FWHMPeakDetectionMethodConfig cfg = resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            EnergyCalibration spectrumCalibration = resultData.EnergySpectrum != null ? resultData.EnergySpectrum.EnergyCalibration : null;
            if (cfg == null || spectrumCalibration == null)
            {
                return;
            }

            if (cfg.FwhmCalibration == null)
            {
                string refusal;
                cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, spectrumCalibration, out refusal);
                if (cfg.FwhmCalibration == null)
                {
                    fwhmRefusal = refusal;
                }
            }

            if (cfg.FwhmCalibration != null)
            {
                resultData.FwhmCalibration = cfg.FwhmCalibration.Clone();
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
            toolTip1.SetToolTip(label1, refused ? AppUi.WrapTip(fwhmRefusal) : string.Empty);
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
                    // Опора FWHM без энергии линии (прибор, умолчание, прежний файл):
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
            peakShapeInfoGroupBox.Visible = fwhmCalibration != null;
            if (fwhmCalibration != null)
            {
                UpdatePeakShapeInfo();
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
            // (П314, 15.10.2026) Новая форма кривой ещё НЕ ПОСЧИТАНА.
            calibrationDone = false;
            calibrationRan = false;
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

            if (!hasDocument ||
                peakPickupProcessing ||
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
            removePeakButton.Enabled = hasDocument && rows > 0 && !peakPickupProcessing;
            viewEnergyGraphButton.Enabled = hasDocument && energyLines > 1;
            viewCalibrationButton.Enabled = hasDocument && fwhmCalibration != null;
            viewPeakShapeButton.Enabled = fwhmCalibration != null;
            addPeakButton.Enabled = hasDocument && !peakPickupProcessing;
            cancelAddPeakButton.Enabled = peakPickupProcessing;

            if (peakPickupProcessing)
            {
                label36.Text = Resources.MSGPickUpCalibrationPeak;
            }
            else if (calibrationRan && calibrationDone)
            {
                label36.Text = Resources.MSGUnifiedCalibrationDone;
            }
            else
            {
                label36.Text = Resources.MSGProceedCalibration;
            }
        }

        /// <summary>Таблица или вид кривой изменились — расчёт снова нужен.</summary>
        void MarkModified()
        {
            calibrationDone = false;
            calibrationRan = false;
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
            newPeak = FitLine(pickupResultData, newPeak, e.PeakStartSelection, e.PeakEndSelection);
            // Энергия новой строки — по нынешней шкале В ЕЁ КАНАЛЕ, до сотых кэВ, как
            // прежде у вкладки энергии: человек вводит энергию линии, а не введённая
            // строка с собственной шкалой согласна (канал в подгонке целый).
            newPeak.Energy = EnergyAtChannel(pickupResultData, newPeak.Channel);

            pickupResultData.CalibrationLines.Add(newPeak);
            mainForm.ActiveDocument.Dirty = true;
            MarkModified();
            ClearPeakPickupState();
            UpdateFwhmCalibration();
            UpdateCalibrateButtonState();
        }

        /// <summary>
        /// Подгонка формы пика по выделению (как прежде у вкладки FWHM) и отметка линии:
        /// строка единой калибровки идёт в обе подгонки.
        /// </summary>
        static CalibrationPeak FitLine(ResultData resultData, CalibrationPeak peak, int start, int end)
        {
            SpectrumAriphmetics sa = new SpectrumAriphmetics(resultData.EnergySpectrum);
            CalibrationPeak fitted = sa.CalcPeakFitValues(peak, start, end);
            sa.Dispose();
            fitted.EnergyLine = true;
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
        /// по энергии. Пик в канале, который в таблице уже есть, не добавляется (как
        /// прежде у вкладки энергии — без окна).
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
            newPeak = FitLine(resultData, newPeak, start, end);
            newPeak.Energy = energy;
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
                    // Введённая энергия — энергия линии: строка идёт и в подгонку энергии.
                    line.Energy = UserNumber.ParseDouble(textvalue);
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
                        }
                    }
                }
                List<CalibrationPeak> target = activeResultData.CalibrationLines;
                if (peaks.Count > 0)
                {
                    // (`AMBER201`, подозрение полосы 4) В документ — КОПИИ строк: правка
                    // строки здесь не должна молча менять таблицу ДРУГОГО спектра.
                    List<CalibrationPeak> copies = CalibrationPeak.ClonePeaks(peaks);
                    target.Clear();
                    foreach (CalibrationPeak peak in copies)
                    {
                        if (channels.Add(peak.Channel))
                        {
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
        /// ⚠ Сюда же ляжет кривизна тракта (`AMBER270`): третьим параметром карты
        /// «канал ← энергия» наряду с полиномом, и результат станет парой
        /// (коэффициенты, κ).
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

        private void ExecuteCalibrationButton_Click(object sender, EventArgs e)
        {
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
            if (points == null)
            {
                AppUi.Report(Resources.ERRInvalidChannelOrEnergyValues, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
                refused = true;
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
            UpdateCalibrateButtonState();
            if (energyDone || fwhmDone)
            {
                StringBuilder status = new StringBuilder(Resources.MSGUnifiedCalibrationDone);
                if (energyDone)
                {
                    status.AppendFormat(CultureInfo.InvariantCulture, " {0}: {1} = {2:0.00000};", Resources.MSGEnergyShort, Resources.MSGMSE, energyMse);
                }
                if (fwhmDone)
                {
                    status.AppendFormat(CultureInfo.InvariantCulture, " FWHM: {0} = {1:0.00000}", Resources.MSGMSE, fwhmMse);
                }
                mainForm.SetStatusTextLeft(status.ToString());
            }
            return !refused;
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

            ShowWideMessage(
                GetResourceText("PeakFitChiTableTitle", "Peak fit comparison"),
                messageBuilder.ToString().TrimEnd());
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

        void ShowWideMessage(string title, string message)
        {
            if (!AppUi.HasWindows)
            {
                // Без окон — строкой (метод на пути «Рассчитать», который зовут пробы).
                AppUi.Report(message, title, MessageBoxIcon.Information);
                return;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(490, 156);
                dialog.Font = this.Font;

                TextBox messageTextBox = new TextBox();
                messageTextBox.Dock = DockStyle.Fill;
                messageTextBox.Multiline = true;
                messageTextBox.ReadOnly = true;
                messageTextBox.BorderStyle = BorderStyle.None;
                messageTextBox.BackColor = SystemColors.Window;
                messageTextBox.WordWrap = true;
                messageTextBox.ScrollBars = ScrollBars.None;
                messageTextBox.TabStop = false;
                messageTextBox.Text = message;

                Panel contentPanel = new Panel();
                contentPanel.Dock = DockStyle.Top;
                contentPanel.Height = 112;
                contentPanel.Padding = new Padding(12, 10, 12, 0);
                contentPanel.Controls.Add(messageTextBox);

                FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
                buttonPanel.Dock = DockStyle.Bottom;
                buttonPanel.Height = 36;
                buttonPanel.FlowDirection = FlowDirection.RightToLeft;
                buttonPanel.Padding = new Padding(0, 6, 12, 6);
                buttonPanel.WrapContents = false;

                Button okButton = new Button();
                okButton.Text = GetResourceText("PeakFitChiTableCloseButton", "Close");
                okButton.DialogResult = DialogResult.OK;
                okButton.Size = new Size(90, 24);
                buttonPanel.Controls.Add(okButton);

                dialog.AcceptButton = okButton;
                dialog.CancelButton = okButton;
                dialog.Controls.Add(buttonPanel);
                dialog.Controls.Add(contentPanel);
                dialog.ShowDialog(this);
            }
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

        private void ViewCalibrationButton_Click(object sender, EventArgs e)
        {
            // Сторож по образцу `GetAllPeaksButton_Click` (`A236`) и энергокалибровка —
            // четвёртое звено (`A243`): `Init` разыменовывает шкалу энергии первой строкой.
            // ⛔ Поле `fwhmCalibration` здесь НЕ переприсваивается: между обновлениями
            // вида оно законно держит ещё не сохранённый выбор вида кривой.
            ResultData graphResultData = ActiveResultData;
            EnsureFwhmCalibration(graphResultData);
            if (fwhmCalibration == null || graphResultData == null
                || graphResultData.FwhmCalibration == null
                || graphResultData.EnergySpectrum == null
                || graphResultData.EnergySpectrum.EnergyCalibration == null)
            {
                UpdateCalibrateButtonState();
                return;
            }

            FWHMCalibrationGraph graph = new FWHMCalibrationGraph(this.mainForm);
            graph.Init(fwhmCalibration, graphResultData.EnergySpectrum.NumberOfChannels);
            DialogResult result = graph.ShowDialog();
            if (result == DialogResult.Yes)
            {
                // (П314) «Обновить опоры» графика переписала строки ДОКУМЕНТА — документ
                // изменён.
                mainForm.ActiveDocument.Dirty = true;
                MarkModified();
                fwhmCalibration.CalibrationPeaks = CalibrationPeak.ClonePeaks(graphResultData.FwhmCalibration.CalibrationPeaks);
                UpdateTable();
                UpdateCalibrateButtonState();
            }
        }

        /// <summary>
        /// График калибровки по энергии (прежняя кнопка «График» вкладки энергии):
        /// линии таблицы — точками графика; «Обновить точки» графика переписывает
        /// <c>ResultData.CalibrationPoints</c>, и сдвинутые каналы возвращаются в
        /// строки по порядку (график точек не добавляет и не снимает).
        /// </summary>
        private void ViewEnergyGraphButton_Click(object sender, EventArgs e)
        {
            ResultData resultData = ActiveResultData;
            if (resultData == null || resultData.EnergySpectrum == null || energyCalibration == null)
            {
                return;
            }
            List<CalibrationPeak> energyLines = resultData.CalibrationLines
                .Where(l => l != null && l.EnergyLine).OrderBy(l => l.Channel).ToList();
            List<CalibrationPoint> points = EnergyPoints(resultData.CalibrationLines);
            resultData.CalibrationPoints = points;
            CalibrationGraph graph = new CalibrationGraph(this.mainForm);
            graph.SetCalibration(this.energyCalibration, resultData.EnergySpectrum.NumberOfChannels, (int)this.numericUpDown6.Value, this.checkBox2.Checked);
            graph.SetCalibrationPoints(points);
            graph.ShowDialog();
            List<CalibrationPoint> updated = resultData.CalibrationPoints;
            if (!ReferenceEquals(updated, points) && updated != null && updated.Count == energyLines.Count)
            {
                for (int i = 0; i < energyLines.Count; i++)
                {
                    energyLines[i].Channel = updated[i].Channel;
                    energyLines[i].Energy = (double)updated[i].Energy;
                    energyLines[i].Count = updated[i].Count;
                }
                mainForm.ActiveDocument.Dirty = true;
            }
            resultData.CalibrationPoints = new List<CalibrationPoint>();
            MarkModified();
            UpdateTable();
            UpdateCalibrateButtonState();
        }

        private void ViewPeakShapeButton_Click(object sender, EventArgs e)
        {
            if (fwhmCalibration == null)
            {
                return;
            }

            PeakShapePreviewGraph graph = new PeakShapePreviewGraph(this.mainForm);
            graph.Init(fwhmCalibration, this.peakShapeInfoGroupBox.Text);
            graph.ShowDialog(this);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Шкала энергии: коэффициенты руками, сброс, автокалибровка, веса, степень
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
            if (this.energyCalibration.PolynomialOrder == 4)
            {
                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);
            }
            foreach (TextBox box in new[] { this.numericUpDown1, this.numericUpDown2, this.numericUpDown3, this.numericUpDown4, this.numericUpDown5 })
            {
                this.shownCoefficientText[box] = box.Text;
            }
        }

        /// <summary>Текст, которым поле коэффициента показано (см. <see cref="setNewCalibration"/>).</summary>
        readonly Dictionary<TextBox, string> shownCoefficientText = new Dictionary<TextBox, string>();

        void button1_Click(object sender, EventArgs e)
        {
            if (this.defaultEnergyCalibration == null || ActiveResultData == null)
            {
                return;
            }
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
        }

        public void SetStabilizerState(ResultData resultData)
        {
            this.checkBox1.Checked = resultData.ResultDataStatus.Stabilization;
        }

        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            activeDocument.ActiveResultData.ResultDataStatus.Stabilization = this.checkBox1.Checked;
            this.mainForm.UpdateCalibrationPeak();
        }

        void button5_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            this.mainForm.ShowDeviceConfigStabilizerForm(activeDocument.ActiveResultData.DeviceConfig);
        }

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
