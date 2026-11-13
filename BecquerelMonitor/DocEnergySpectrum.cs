using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace BecquerelMonitor
{
    // Token: 0x0200003C RID: 60
    public partial class DocEnergySpectrum : DockContent
    {
        // Token: 0x14000006 RID: 6
        // (add) Token: 0x060002FA RID: 762 RVA: 0x0000F3BC File Offset: 0x0000D5BC
        // (remove) Token: 0x060002FB RID: 763 RVA: 0x0000F3F8 File Offset: 0x0000D5F8
        public event EventHandler CloseDocument;

        // Token: 0x14000007 RID: 7
        // (add) Token: 0x060002FC RID: 764 RVA: 0x0000F434 File Offset: 0x0000D634
        // (remove) Token: 0x060002FD RID: 765 RVA: 0x0000F470 File Offset: 0x0000D670
        public event EventHandler SaveDocument;

        // Token: 0x14000009 RID: 9
        // (add) Token: 0x06000300 RID: 768 RVA: 0x0000F524 File Offset: 0x0000D724
        // (remove) Token: 0x06000301 RID: 769 RVA: 0x0000F560 File Offset: 0x0000D760
        public event EventHandler SetLowerThreshold;

        // Token: 0x1400000A RID: 10
        // (add) Token: 0x06000302 RID: 770 RVA: 0x0000F59C File Offset: 0x0000D79C
        // (remove) Token: 0x06000303 RID: 771 RVA: 0x0000F5D8 File Offset: 0x0000D7D8
        public event EventHandler SetUpperThreshold;

        // Token: 0x1400000B RID: 11
        // (add) Token: 0x06000304 RID: 772 RVA: 0x0000F614 File Offset: 0x0000D814
        // (remove) Token: 0x06000305 RID: 773 RVA: 0x0000F650 File Offset: 0x0000D850
        public event ShowEnergyCalibrationViewEventHandler ShowEnergyCalibrationView;

        // Token: 0x1400000C RID: 12
        // (add) Token: 0x06000306 RID: 774 RVA: 0x0000F68C File Offset: 0x0000D88C
        // (remove) Token: 0x06000307 RID: 775 RVA: 0x0000F6C8 File Offset: 0x0000D8C8
        public event AddSpectrumToDocumentEventHandler AddSpectrumToDocument;

        /// <summary>
        /// Кривую эффективности спектра сменили ОТСЮДА, из документа, а не из
        /// панели управления измерением.
        ///
        /// Панель показывает ту кривую, по которой сейчас считается активность,
        /// и обновляет список только по <c>ShowDocumentStatus</c> — на своих же
        /// событиях. Выбор в диалоге перед разложением она пропускала, и в ряду
        /// «Efficiency» оставалось «(none)» при работающей кривой. Документ до
        /// панели не дотягивается (у него нет ссылки на <c>MainForm</c>), так
        /// что говорит он событием — как и обо всём остальном.
        /// </summary>
        public event EventHandler EfficiencyChanged;

        /// <summary>
        /// (`AMBER269`, полоса a269fix5) Кривую FWHM спектра поставил документ — выбором
        /// прибора перед разложением (<see cref="EnsureFsaFwhm"/>). Вкладка «Калибровка»
        /// показывает кривую спектра и обновляется по событиям главной формы; без этого
        /// события она до смены документа говорила «кривой нет» при работающей кривой.
        /// </summary>
        public event EventHandler FwhmCalibrationChanged;

        // Token: 0x1700013F RID: 319
        // (get) Token: 0x06000308 RID: 776 RVA: 0x0000F704 File Offset: 0x0000D904
        // (set) Token: 0x06000309 RID: 777 RVA: 0x0000F70C File Offset: 0x0000D90C
        public ResultDataFile ResultDataFile
        {
            get
            {
                return this.resultDataFile;
            }
            set
            {
                this.resultDataFile = value;
            }
        }

        // Token: 0x17000140 RID: 320
        // (get) Token: 0x0600030A RID: 778 RVA: 0x0000F718 File Offset: 0x0000D918
        // (set) Token: 0x0600030B RID: 779 RVA: 0x0000F720 File Offset: 0x0000D920
        public int ActiveResultDataIndex
        {
            get
            {
                return this.activeResultDataIndex;
            }
            set
            {
                this.activeResultDataIndex = value;
            }
        }

        public bool AutoSave
        {
            get
            {
                return this.autosave;
            }
            set
            {
                this.autosave = value;
            }
        }

        // Token: 0x17000141 RID: 321
        // (get) Token: 0x0600030C RID: 780 RVA: 0x0000F72C File Offset: 0x0000D92C
        // (set) Token: 0x0600030D RID: 781 RVA: 0x0000F744 File Offset: 0x0000D944
        public ResultData ActiveResultData
        {
            get
            {
                return this.resultDataFile.ResultDataList[this.activeResultDataIndex];
            }
            set
            {
                this.resultDataFile.ResultDataList[this.activeResultDataIndex] = value;
            }
        }

        // Token: 0x17000142 RID: 322
        // (get) Token: 0x0600030E RID: 782 RVA: 0x0000F760 File Offset: 0x0000D960
        // (set) Token: 0x0600030F RID: 783 RVA: 0x0000F768 File Offset: 0x0000D968
        public bool UpdateMeasurementResult
        {
            get
            {
                return this.updateMeasurementResult;
            }
            set
            {
                this.updateMeasurementResult = value;
            }
        }

        // Token: 0x17000143 RID: 323
        // (get) Token: 0x06000310 RID: 784 RVA: 0x0000F774 File Offset: 0x0000D974
        // (set) Token: 0x06000311 RID: 785 RVA: 0x0000F77C File Offset: 0x0000D97C
        public bool UpdateSpectrum
        {
            get
            {
                return this.updateSpectrum;
            }
            set
            {
                this.updateSpectrum = value;
            }
        }

        public bool UpdateSpectrumList
        {
            get
            {
                return this.updateSpectrumList;
            }
            set
            {
                this.updateSpectrumList = value;
            }
        }

        public bool UpdateDetectedPeaks
        {
            get
            {
                return this.updateDetectedPeaks;
            }
            set
            {
                this.updateDetectedPeaks = value;
            }
        }

        public bool UpdateDoseRate
        {
            get
            {
                return this.updateDoseRate;
            }
            set
            {
                this.updateDoseRate = value;
            }
        }

        // Token: 0x17000144 RID: 324
        // (get) Token: 0x06000312 RID: 786 RVA: 0x0000F788 File Offset: 0x0000D988
        // (set) Token: 0x06000313 RID: 787 RVA: 0x0000F790 File Offset: 0x0000D990
        public bool ActiveEnergyCalibration
        {
            get
            {
                return this.activeEnergyCalibration;
            }
            set
            {
                this.activeEnergyCalibration = value;
                if (this.activeEnergyCalibration)
                {
                    this.toolStripSplitButton10.Image = Properties.Resources.EnergyCalibration;
                    return;
                }
                this.toolStripSplitButton10.Image = Properties.Resources.EnergyCalibration2;
            }
        }

        // Token: 0x17000145 RID: 325
        // (get) Token: 0x06000314 RID: 788 RVA: 0x0000F7C8 File Offset: 0x0000D9C8
        // (set) Token: 0x06000315 RID: 789 RVA: 0x0000F7D0 File Offset: 0x0000D9D0
        public bool IsActivating
        {
            get
            {
                return this.isActivating;
            }
            set
            {
                this.isActivating = value;
            }
        }

        /// <summary>
        /// Набор нуклидов, выбранный для ЭТОГО спектра в панели поиска пиков;
        /// null — «все нуклиды». Такой же выбор на сеанс, как и прежде, только
        /// теперь свой у каждого документа: наборов открыто несколько, спектров
        /// тоже, и один общий выбор означал, что размеченный торием спектр
        /// переносит торий на все соседние, а вернуться к прежнему выбору
        /// нечем (R9).
        ///
        /// Живёт здесь, а не в панели: панель одна на все документы, а
        /// закрытому документу его выбор больше не нужен — так он и уходит
        /// вместе с ним, без отдельной уборки.
        ///
        /// Кто читает и держит в согласии с
        /// <see cref="NuclideDefinitionManager.ActiveSet"/> — <see cref="DCPeakDetectionView"/>.
        /// В файл не сохраняется: это выбор на сеанс, а не свойство спектра.
        /// </summary>
        public NuclideSet SelectedNuclideSet
        {
            get
            {
                return this.selectedNuclideSet;
            }
            set
            {
                this.selectedNuclideSet = value;
            }
        }

        // Token: 0x17000146 RID: 326
        // (get) Token: 0x06000316 RID: 790 RVA: 0x0000F7DC File Offset: 0x0000D9DC
        // (set) Token: 0x06000317 RID: 791 RVA: 0x0000F818 File Offset: 0x0000DA18
        public PulseDetector PulseDetector
        {
            get
            {
                DeviceController deviceController = this.ActiveResultData.MeasurementController.DeviceController;
                if (deviceController is AudioInputDeviceController)
                {
                    return ((AudioInputDeviceController)deviceController).PulseDetector;
                }
                else if (deviceController is AtomSpectraDeviceController)
                {
                    return ((AtomSpectraDeviceController)deviceController).PulseDetector;
                }
                else if (deviceController is RadiaCodeDeviceController)
                {
                    return ((RadiaCodeDeviceController)deviceController).PulseDetector;
                }
                else if (deviceController is ObsidianDeviceController)
                {
                    return ((ObsidianDeviceController)deviceController).PulseDetector;
                }
                return null;
            }
            set
            {
                DeviceController deviceController = this.ActiveResultData.MeasurementController.DeviceController;
                if (deviceController is AudioInputDeviceController)
                {
                    ((AudioInputDeviceController)deviceController).PulseDetector = value;
                }
                else if (deviceController is AtomSpectraDeviceController)
                {
                    ((AtomSpectraDeviceController)deviceController).PulseDetector = value;
                }
                else if (deviceController is RadiaCodeDeviceController)
                {
                    ((RadiaCodeDeviceController)deviceController).PulseDetector = value;
                }
                else if (deviceController is ObsidianDeviceController)
                {
                    ((ObsidianDeviceController)deviceController).PulseDetector = value;
                }
            }
        }

        // Token: 0x17000147 RID: 327
        // (get) Token: 0x06000318 RID: 792 RVA: 0x0000F854 File Offset: 0x0000DA54
        // (set) Token: 0x06000319 RID: 793 RVA: 0x0000F85C File Offset: 0x0000DA5C
        public string Filename
        {
            get
            {
                return this.filename;
            }
            set
            {
                this.filename = value;
                DocumentTextWithDirtyFlag();
            }
        }

        // Token: 0x17000148 RID: 328
        // (get) Token: 0x0600031A RID: 794 RVA: 0x0000F878 File Offset: 0x0000DA78
        // (set) Token: 0x0600031B RID: 795 RVA: 0x0000F880 File Offset: 0x0000DA80
        public bool IsNamed
        {
            get
            {
                return this.isNamed;
            }
            set
            {
                this.isNamed = value;
            }
        }

        // Token: 0x17000149 RID: 329
        // (get) Token: 0x0600031C RID: 796 RVA: 0x0000F88C File Offset: 0x0000DA8C
        public EnergySpectrumView EnergySpectrumView
        {
            get
            {
                return this.view;
            }
        }

        // Token: 0x1700014A RID: 330
        // (get) Token: 0x0600031D RID: 797 RVA: 0x0000F894 File Offset: 0x0000DA94
        // (set) Token: 0x0600031E RID: 798 RVA: 0x0000F89C File Offset: 0x0000DA9C
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
                this.SetupSaveDocumentButton();
                DocumentTextWithDirtyFlag();
            }
        }

        /// <summary>
        /// (`A145`) Сеанс полноспектрального разбора активного спектра этого
        /// документа: результат, отпечаток, кэш, фоновый счёт. Один на документ;
        /// график и окно отчёта — его подписчики, а не владельцы.
        /// </summary>
        public FullSpectrumAnalysis.FsaAnalysisSession FsaSession
        {
            get
            {
                return this.fsaSession;
            }
        }

        /// <summary>
        /// Документ закрывается — идущий счёт по возвращении обязан промолчать
        /// (поколение), а его результат никому больше не нужен.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this.fsaSession.Completed -= this.FsaSessionCompleted;
            this.fsaSession.Reset();
            base.OnFormClosed(e);
        }

        // Token: 0x0600031F RID: 799 RVA: 0x0000F8A8 File Offset: 0x0000DAA8
        public DocEnergySpectrum()
        {
            this.InitializeComponent();
            this.view = new EnergySpectrumView();
            this.view.Dock = DockStyle.Fill;
            this.view.Margin = new Padding(0);
            this.view.Name = "energySpectrumViewRuntime";
            // (`A145`, этап 2) Сеанс разбора принадлежит ДОКУМЕНТУ, а вид на
            // него только подписан: окно отчёта (этап 3) возьмёт тот же сеанс
            // у документа, и второго расчёта не будет (критерий 4).
            this.view.FsaSession = this.fsaSession;
            // (`fixFsa2`, 07.11.2026) набор нуклидов разбора — СВОЙ, а не общий выбор панели пиков
            // для активного документа (`FsaAnalysisSession.NuclideSetSource`)
            this.fsaSession.NuclideSetSource = () => this.selectedNuclideSet;
            this.fsaSession.Completed += this.FsaSessionCompleted;
            this.toolTip1.SetToolTip(this.view, AppUi.WrapTip(this.toolTip1.GetToolTip(this.energySpectrumView1)));
            this.energySpectrumView1.Controls.Add(this.view);
            this.resultDataFile = new ResultDataFile();
            this.resultDataFile.InitFormatVersion();
            this.CreateResultData();
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            this.view.VerticalUnit = globalConfig.ChartViewConfig.DefaultVerticalUnit;
            this.view.VerticalScaleType = globalConfig.ChartViewConfig.DefaultVerticalScaleType;
            this.view.ChartType = globalConfig.ChartViewConfig.DefaultChartType;
            this.view.VerticalFittingMode = globalConfig.ChartViewConfig.DefaultVerticalFittingMode;
            this.view.SmoothingMethod = globalConfig.ChartViewConfig.DefaultSmoothingMothod;
            this.view.HorizontalUnit = globalConfig.ChartViewConfig.DefaultHorizontalUnit;
            this.view.BackgroundMode = globalConfig.ChartViewConfig.DefaultBackgroundMode;
            this.view.DrawingMode = globalConfig.ChartViewConfig.DefaultDrawingMode;
            this.view.PeakMode = globalConfig.ChartViewConfig.DefaultPeakMode;
            // (`AMBER328`) Новая вкладка следует глобальной галочке FSA: галочка ВКЛ и
            // пики ВКЛ — вкладка открывается сразу в FSA, а режим фона по умолчанию
            // становится тем, куда она вернётся, когда FSA погаснет.
            this.ApplyFsaLink();
            // Пункт «Показать FSA» списка режимов фона виден, но недоступен (решение
            // Amber 12.11.2026): включается FSA теперь только своей кнопкой.
            this.ShowFsaToolStripMenuItem.Enabled = false;
            this.toolStripSplitButtonFsa.ToolTipText = AppUi.WrapTip(this.toolStripSplitButtonFsa.ToolTipText);
            this.view.HorizontalMagnification = globalConfig.ChartViewConfig.DefaultHorizontalMagnification;
            // (П259, 14.10.2026) Нижний предел поля масштаба — 0.01, а не 0.1 из
            // конструктора формы: подгонка во всю ширину у длинного спектра в
            // узком поле меньше 0.1 (8192 канала в окне 974 px — около 0.07), и
            // поле, упёршись в 0.1, возвращало виду 0.1 — «Fit» показывал две
            // трети спектра.
            this.toolStripNumUpDownScale.Minimum = MinimumHorizontalScale;
            this.view.ActionEvent += View_ActionEvent;
            this.SetToolStripIcons();
            this.toolStripNumericUpdown.NumericUpDownControl.Value = (decimal)globalConfig.ChartViewConfig.PowNum;
            toolStripContainer1.BottomToolStripPanel.SuspendLayout();
            toolStrip1.Dock = DockStyle.None;
            toolStrip2.Dock = DockStyle.None;
            toolStrip1.Location = new Point(0, 0);
            toolStrip2.Location = new Point(300, 0);
            toolStripContainer1.BottomToolStripPanel.ResumeLayout();
        }

        private void EvaluateNormByEffMode()
        {
            bool normByEffIsAvailable = this.IsNormalizeByEfficiencyAvailable();
            // (`AMBER34`, П79 15.09.2026) Пункт ЗАПЕРТ только когда кривой нет
            // вовсе — как было. Кривая ВЫБРАНА, но нормировать по ней нельзя
            // (отвергнута точкой выше единицы, сцена поля, меньше двух точек)
            // — пункт остаётся доступным, а щелчок называет причину
            // (`NormByEffToolStripMenuItem_Click`): запертый пункт без слов
            // читался как «не выбрана», хотя кривая стоит в панели измерения.
            this.NormByEffToolStripMenuItem.Enabled = normByEffIsAvailable
                || (this.ActiveResultData != null && this.ActiveResultData.Efficiency != null);

            if (!normByEffIsAvailable && this.view.BackgroundMode == BackgroundMode.NormalizeByEfficiency)
            {
                this.view.BackgroundMode = BackgroundMode.Visible;
                this.toolStripSplitButtonBgMode.Image = Properties.Resources.BG;
            }
        }

        private void SetupBGAffinityButtons()
        {
            bool isBGexists = this.IsBackgroundExists();
            this.toolStripRefreshBgButton.Enabled = isBGexists;
            this.toolStripSplitButton.Enabled = isBGexists && this.ResultDataFile.ResultDataList.Count == 1;
        }

        private void SetupSaveDocumentButton()
        {
            this.toolStripSaveButton.Enabled = this.Dirty;
        }

        /// <summary>(П259) Нижний предел поля масштаба по горизонтали.</summary>
        const decimal MinimumHorizontalScale = 0.01M;

        /// <summary>
        /// (П259) Поле масштаба переставляет ВИД (подгонка, колесо, увеличение
        /// выделения) — вид уже стоит на этом масштабе, и `ValueChanged` не должен
        /// возвращать число обратно в <see cref="EnergySpectrumView.zoom"/>.
        /// </summary>
        bool scaleEchoFromView;

        private void View_ActionEvent(object sender, EnergySpectrumActionEventArgs e)
        {
            decimal value = e.NewScaleValue;
            if (value > 10.0M)
            {
                value = 10.0M;
            } else if (value < MinimumHorizontalScale)
            {
                value = MinimumHorizontalScale;
            }
            if (e.NeedUpdateScale)
            {
                // (П259, 14.10.2026) Эхо гасится: прежде установка поля звала
                // `zoom` с числом, прошедшим double → decimal → double и предел
                // поля, — масштаб вида после «Fit» тихо становился другим, и
                // смена размера поля уже не узнавала в нём свою подгонку.
                this.scaleEchoFromView = true;
                try
                {
                    this.toolStripNumUpDownScale.NumericUpDownControl.Value = value;
                }
                finally
                {
                    this.scaleEchoFromView = false;
                }
            }
        }

        // Token: 0x06000320 RID: 800 RVA: 0x0000F9E4 File Offset: 0x0000DBE4
        public DocEnergySpectrum(string filename) : this()
        {
            this.filename = filename;
            DocumentTextWithDirtyFlag();
        }

        private void DocumentTextWithDirtyFlag()
        {
            string text = Path.GetFileNameWithoutExtension(this.filename);
            if (this.Dirty)
            {
                this.Text = "* " + text;
            } else
            {
                this.Text = text;
            }
        }

        // Token: 0x06000321 RID: 801 RVA: 0x0000FA10 File Offset: 0x0000DC10
        public ResultData CreateResultData()
        {
            ResultData resultData = new ResultData();
            this.resultDataFile.ResultDataList.Add(resultData);
            resultData.MeasurementController = new MeasurementController(this, resultData);
            this.activeResultDataIndex = this.resultDataFile.ResultDataList.Count - 1;
            if (this.deviceConfigManager.DeviceConfigList.Count > 0)
            {
                resultData.DeviceConfig = this.deviceConfigManager.DeviceConfigList[0];
                resultData.DeviceConfigReference = resultData.DeviceConfig.CreateReference();
                resultData.EnergySpectrum = new EnergySpectrum(resultData.DeviceConfig.ChannelPitch, resultData.DeviceConfig.NumberOfChannels);
                resultData.EnergySpectrum.EnergyCalibration = resultData.DeviceConfig.EnergyCalibration.Clone();
                resultData.PresetTime = resultData.DeviceConfig.DefaultMeasurementTime;
                resultData.ResultDataStatus.PresetTime = resultData.PresetTime;
                // Work on a CLONE of the global device config's method config: the old code
                // wrote FwhmCalibration/Enabled into the live DeviceConfigManager object,
                // so creating a document mutated the global config (and a later config
                // save persisted those accidental changes).
                FWHMPeakDetectionMethodConfig devcfg = (FWHMPeakDetectionMethodConfig)resultData.DeviceConfig.PeakDetectionMethodConfig;
                resultData.PeakDetectionMethodConfig = devcfg.Clone();
                FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)resultData.PeakDetectionMethodConfig;
                if (cfg.FwhmCalibration == null)
                {
                    // ⛔ ЗДЕСЬ ПРИЧИНА НЕ СПРАШИВАЕТСЯ, И ЭТО РЕШЕНИЕ, А НЕ
                    //    НЕДОСМОТР (`A240`, полоса F62, 06.09.2026). Оба пути
                    //    сюда УЖЕ имеют своего читателя, и второй был бы вторым
                    //    голосом об одной беде:
                    //    * из конструктора `DocEnergySpectrum` (строка 396) —
                    //      то есть внутри `DocumentManager.CreateDocument` и
                    //      `OpenDocument`, которые следом зовут общую дверь
                    //      `ReportMissingFwhmCalibration` (~~`A234`~~, `A240`
                    //      полоса F54). Голос там ОДИН НА ФАЙЛ и с числами;
                    //    * из `MainForm.AddNewSpectrum` — человек добавил спектр
                    //      в открытый документ, и тот же метод следом зовёт
                    //      `UpdateFwhmCalibrationView`, а вид ПОКАЗЫВАЕТ причину
                    //      на месте пустой таблицы (`ApplyFwhmRefusalHint`).
                    //    ⚠ Причину при этом ничего не теряет: `DefaultCalibration`
                    //    ничего не меняет и зависит только от трёх чисел
                    //    настроек и энергетической кривой, поэтому читатель
                    //    спрашивает её заново там, где он есть, — ровно так
                    //    устроен `DocumentManager.WhyNoFwhmCalibration`.
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, resultData.EnergySpectrum.EnergyCalibration);
                }
                if (resultData.FwhmCalibration == null)
                {
                    // Result data file doesn't contains calibration
                    // Create it from FWHMPeakDetectionMethodConfig
                    // (DefaultCalibration may return null - guard the NRE)
                    resultData.FwhmCalibration = cfg.FwhmCalibration != null ? cfg.FwhmCalibration.Clone() : null;
                } else
                {
                    // Result data file contains calibration
                    // Override resultData.PeakDetectionMethodConfig with it
                    cfg.FwhmCalibration = resultData.FwhmCalibration.Clone();
                }
                cfg.Enabled = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.DefaultPeakMode == PeakMode.Visible;
            }
            else
            {
                resultData.EnergySpectrum = new EnergySpectrum(0.04, 2500);
                resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
            }
            return resultData;
        }

        // Token: 0x06000322 RID: 802 RVA: 0x0000FB68 File Offset: 0x0000DD68
        public void DeleteActiveResultData()
        {
            if (this.resultDataFile.ResultDataList.Count > 1)
            {
                this.resultDataFile.ResultDataList.RemoveAt(this.activeResultDataIndex);
                if (this.activeResultDataIndex > this.resultDataFile.ResultDataList.Count - 1)
                {
                    this.activeResultDataIndex = this.resultDataFile.ResultDataList.Count - 1;
                }
            }
        }

        // Token: 0x06000323 RID: 803 RVA: 0x0000FBDC File Offset: 0x0000DDDC
        public void UpdateEnergySpectrum()
        {
            this.view.ResultDataList = this.resultDataFile.ResultDataList;
            this.view.ActiveResultDataIndex = this.activeResultDataIndex;
            this.RefreshView();
        }

        // Token: 0x06000324 RID: 804 RVA: 0x0000FC24 File Offset: 0x0000DE24
        public void SetDefaultHorizontalScale()
        {
            this.view.SetDefaultHorizontalScale();
        }

        // Token: 0x06000325 RID: 805 RVA: 0x0000FC34 File Offset: 0x0000DE34
        protected override string GetPersistString()
        {
            return string.Concat(new string[]
            {
                base.GetType().ToString(),
                ",",
                this.Filename,
                ",",
                this.Text
            });
        }

        // Token: 0x06000326 RID: 806 RVA: 0x0000FC98 File Offset: 0x0000DE98
        public void ClearSpectrum()
        {
            ResultData activeResultData = this.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            activeResultData.EnergySpectrum.Initialize();
            activeResultData.PulseCollection.Pulses.Clear();
            activeResultData.CountRates.Clear();
            this.RefreshView();
            resultDataStatus.TotalTime = TimeSpan.FromSeconds(0.0);
        }

        // Token: 0x06000327 RID: 807 RVA: 0x0000FCEC File Offset: 0x0000DEEC
        void saveSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.SaveDocument(this, new EventArgs());
        }

        // Token: 0x06000328 RID: 808 RVA: 0x0000FD00 File Offset: 0x0000DF00
        void closeCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.CloseDocument(this, new EventArgs());
        }

        // Token: 0x06000329 RID: 809 RVA: 0x0000FD14 File Offset: 0x0000DF14
        void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            this.saveSToolStripMenuItem.Enabled = this.Dirty;
            bool enabled = this.view.CursorX != -1 && this.view.CursorChannel > 0 && this.view.CursorChannel < this.ActiveResultData.EnergySpectrum.NumberOfChannels;
            // (П293, 14.10.2026) Решение Amber вопросником: «Гасить пункт у таких
            // приборов». У прибора без полей порога (страница настроек не отдаёт
            // LowerThresholdTextBox/UpperThresholdTextBox) щелчок открывал настройки и
            // молча ничего не ставил. Признак — у конфигурации прибора документа
            // (InputDeviceConfig.HasThresholdFields), считается на каждом открытии меню.
            InputDeviceConfig inputDeviceConfig = this.ActiveResultData.DeviceConfig?.InputDeviceConfig;
            enabled = enabled && inputDeviceConfig != null && inputDeviceConfig.HasThresholdFields;
            this.setLowerThresholdLToolStripMenuItem.Enabled = enabled;
            this.setUpperThresholdHToolStripMenuItem.Enabled = enabled;
        }

        // Token: 0x0600032A RID: 810 RVA: 0x0000FDB0 File Offset: 0x0000DFB0
        void contextMenuStrip1_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            this.view.CursorX = -1;
            this.view.Invalidate();
        }

        // Token: 0x0600032B RID: 811 RVA: 0x0000FDCC File Offset: 0x0000DFCC
        void DocEnergySpectrum_MouseDown(object sender, MouseEventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x0600032C RID: 812 RVA: 0x0000FDD4 File Offset: 0x0000DFD4
        void ClearSelection()
        {
            this.view.SelectionStart = -1;
            this.view.Invalidate();
        }

        // Token: 0x0600032D RID: 813 RVA: 0x0000FDF0 File Offset: 0x0000DFF0
        void DocEnergySpectrum_SizeChanged(object sender, EventArgs e)
        {
        }

        // Token: 0x0600032E RID: 814 RVA: 0x0000FDF4 File Offset: 0x0000DFF4
        void DocEnergySpectrum_Load(object sender, EventArgs e)
        {
        }

        // Token: 0x0600032F RID: 815 RVA: 0x0000FDF8 File Offset: 0x0000DFF8
        /// <summary>
        /// Вид документа обновлён — данные могли смениться (`A295`).
        ///
        /// ⛔ ЗАЧЕМ СОБЫТИЕ. Разбор до 07.09.2026 заказывал ОДИН потребитель —
        /// график, и только в режиме `ShowFSA` (`PrepareViewData`). Окно отчёта
        /// заказывало его лишь на смене документа, спектра, видимости и
        /// настроек, а дальше держалось цепочкой «расчёт кончился → закажи
        /// следующий». Цепочка самоподдерживается, только пока расчёты ИДУТ:
        /// стоит отпечатку совпасть (данные не менялись), как она гаснет, и
        /// разбудить её при новых отсчётах становится некому. Отсюда и жалоба:
        /// на записи спектра пики находятся, а отчёт стоит с прежним отказом,
        /// пока человек не включит `ShowFSA`.
        ///
        /// Событие поднимается на КАЖДОМ обновлении вида, потому что именно им
        /// приходят новые отсчёты. Лишним оно не бывает: подписчик зовёт
        /// `EnsureUpToDate`, а тот на совпавшем отпечатке возвращается молча.
        /// </summary>
        public event EventHandler ViewRefreshed;

        public void RefreshView()
        {
            this.EvaluateNormByEffMode();
            this.view.PrepareViewData();
            this.SetupBGAffinityButtons();
            this.SetupSaveDocumentButton();
            this.DocumentTextWithDirtyFlag();
            this.view.RecalcScrollBar();
            this.view.Invalidate();

            EventHandler handler = this.ViewRefreshed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Иконка кнопки режима фона для режима <paramref name="mode"/> — одна на все
        /// места, где режим ставится не своим пунктом меню (`AMBER328`: возврат из FSA
        /// в бывший до него режим).
        /// </summary>
        static Image BackgroundModeImage(BackgroundMode mode)
        {
            switch (mode)
            {
                case BackgroundMode.Invisible:
                    return Properties.Resources.NOBG;
                case BackgroundMode.Substract:
                    return Properties.Resources.SUB;
                case BackgroundMode.ShowContinuum:
                    return Properties.Resources.CONT;
                case BackgroundMode.ShowFSA:
                    return Properties.Resources.FSA;
                case BackgroundMode.NormalizeByEfficiency:
                    return Properties.Resources.NORM;
                default:
                    return Properties.Resources.BG;
            }
        }

        // Token: 0x06000330 RID: 816 RVA: 0x0000FE1C File Offset: 0x0000E01C
        void SetToolStripIcons()
        {
            Image image = BackgroundModeImage(this.view.BackgroundMode);
            this.toolStripSplitButtonBgMode.Image = image;
            this.UpdateFsaButton();
            image = Properties.Resources.HD1;
            switch (this.view.DrawingMode)
            {
                case DrawingMode.HighDefinition:
                    image = Properties.Resources.HD;
                    break;
                case DrawingMode.Normal:
                    image = Properties.Resources.HD1;
                    break;
            }
            this.toolStripSplitButton8.Image = image;
            image = Properties.Resources.cnt;
            switch (this.view.VerticalUnit)
            {
                case VerticalUnit.Counts:
                    image = Properties.Resources.cnt;
                    break;
                case VerticalUnit.CountsPerSecond:
                    image = Properties.Resources.cps;
                    break;
            }
            this.toolStripSplitButton1.Image = image;
            image = Properties.Resources.log;
            switch (this.view.VerticalScaleType)
            {
                case VerticalScaleType.LinearScale:
                    image = Properties.Resources.linear;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.LogarithmicScale:
                    image = Properties.Resources.log;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.PowerScale:
                    image = Properties.Resources.pow;
                    this.toolStripNumericUpdown.Enabled = true;
                    break;
            }
            this.toolStripSplitButton2.Image = image;
            image = Properties.Resources.line;
            switch (this.view.ChartType)
            {
                case ChartType.BarChart:
                    image = Properties.Resources.bar;
                    break;
                case ChartType.LineChart:
                    image = Properties.Resources.line;
                    break;
            }
            this.toolStripSplitButton3.Image = image;
            image = Properties.Resources.fit1;
            switch (this.view.VerticalFittingMode)
            {
                case VerticalFittingMode.None:
                    image = Properties.Resources.None;
                    break;
                case VerticalFittingMode.MinMax:
                    image = Properties.Resources.fit1;
                    break;
                case VerticalFittingMode.BackgroundMinMax:
                    image = Properties.Resources.bgfit;
                    break;
            }
            this.toolStripSplitButton5.Image = image;
            image = Properties.Resources.NoSmooth;
            switch (this.view.SmoothingMethod)
            {
                case SmoothingMethod.None:
                    image = Properties.Resources.NoSmooth;
                    break;
                case SmoothingMethod.SimpleMovingAverage:
                    image = Properties.Resources.SMA;
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    image = Properties.Resources.WMA;
                    break;
            }
            this.toolStripSplitButton6.Image = image;
            image = Properties.Resources.ene;
            switch (this.view.HorizontalUnit)
            {
                case HorizontalUnit.Channel:
                    image = Properties.Resources.channel;
                    break;
                case HorizontalUnit.Energy:
                    image = Properties.Resources.ene;
                    break;
            }
            this.toolStripSplitButton4.Image = image;
            image = Properties.Resources.peak;
            switch (this.view.PeakMode)
            {
                case PeakMode.Visible:
                    image = Properties.Resources.peak;
                    break;
                case PeakMode.Invisible:
                    image = Properties.Resources.nopeak;
                    break;
            }
            this.toolStripSplitButton9.Image = image;
        }

        // Token: 0x06000331 RID: 817 RVA: 0x000100AC File Offset: 0x0000E2AC
        void toolStripSplitButton7_ButtonClick(object sender, EventArgs e)
        {
            BackgroundMode backgroundMode = BackgroundMode.Invisible;
            Image image = Properties.Resources.NOBG;
            switch (this.view.BackgroundMode)
            {
                case BackgroundMode.Visible:
                    backgroundMode = BackgroundMode.Invisible;
                    image = Properties.Resources.NOBG;
                    break;
                case BackgroundMode.Invisible:
                    backgroundMode = BackgroundMode.Substract;
                    image = Properties.Resources.SUB;
                    break;
                case BackgroundMode.Substract:
                    backgroundMode = BackgroundMode.ShowContinuum;
                    image = Properties.Resources.CONT;
                    break;
                case BackgroundMode.ShowContinuum:
                    // (`AMBER328`, решение Amber 12.11.2026) FSA из цикла СНЯТ: его
                    // включает и выключает своя кнопка справа от кнопки пиков, на всех
                    // вкладках разом. Цикл, вводящий одну вкладку в FSA мимо этой
                    // кнопки, разводил бы вкладки с её галочкой. Прежде (с `A145`) FSA
                    // стоял здесь, сразу за континуумом.
                    if (this.IsNormalizeByEfficiencyAvailable())
                    {
                        backgroundMode = BackgroundMode.NormalizeByEfficiency;
                        image = Properties.Resources.NORM;
                    }
                    else
                    {
                        backgroundMode = BackgroundMode.Visible;
                        image = Properties.Resources.BG;
                    }

                    break;
                case BackgroundMode.ShowFSA:
                    // Без этой ветки кнопка, нажатая В РЕЖИМЕ FSA, попадала не
                    // в следующий режим, а в заготовку выше по функции —
                    // «спрятать фон». То есть цикл рвался в обе стороны.
                    // (`AMBER328`) Вкладка ВЫХОДИТ из FSA своей кнопкой фона, а
                    // глобальная галочка FSA остаётся как была — вернёт FSA на эту
                    // вкладку следующее включение пиков или галочки (журнал
                    // `handover-2026-11-12-a328.md`, развилка Р1).
                    if (this.IsNormalizeByEfficiencyAvailable())
                    {
                        backgroundMode = BackgroundMode.NormalizeByEfficiency;
                        image = Properties.Resources.NORM;
                    }
                    else
                    {
                        backgroundMode = BackgroundMode.Visible;
                        image = Properties.Resources.BG;
                    }

                    break;
                case BackgroundMode.NormalizeByEfficiency:
                    backgroundMode = BackgroundMode.Visible;
                    image = Properties.Resources.BG;
                    break;
            }
            this.view.BackgroundMode = backgroundMode;
            this.toolStripSplitButtonBgMode.Image = image;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
            // (`AMBER328`) Входа в FSA у цикла больше нет — значит, нет и события
            // `FsaModeEntered` отсюда: отчёт показывает вход своей кнопкой FSA.
        }

        /// <summary>
        /// Пункт включается по ТОЙ ЖЕ кривой, на которую спектр потом делится, —
        /// по своей кривой спектра. Проверять здесь набор зон, а делить на
        /// кривую прибора значило бы включать режим тогда, когда делить нечем,
        /// и не включать, когда есть чем.
        /// </summary>
        private bool IsNormalizeByEfficiencyAvailable()
        {
            // (`AMBER34`) Тот же предикат, что у самой нормировки и у команды
            // главного окна: одно правило, а не три копии `FromConfig != null`.
            return this.ActiveResultData != null
                && Utils.SpectrumAriphmetics.NormalizeRefusal(this.ActiveResultData.Efficiency) == null;
        }

        private bool IsBackgroundExists()
        {
            return this.ActiveResultData != null
                && this.ActiveResultData.BackgroundEnergySpectrum != null
                && this.ActiveResultData.BackgroundSpectrumFile != "";
        }

        // Token: 0x06000332 RID: 818 RVA: 0x00010118 File Offset: 0x0000E318
        void toolStripSplitButton7_DropDownOpening(object sender, EventArgs e)
        {
            this.showBackgroundToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Visible);
            this.hideBackgroundToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Invisible);
            this.SubstractBgToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.Substract);
            this.ShowConToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.ShowContinuum);
            this.ShowFsaToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.ShowFSA);
            // (`AMBER328`) Виден (галочкой — показан ли FSA на вкладке), но недоступен.
            this.ShowFsaToolStripMenuItem.Enabled = false;
            this.NormByEffToolStripMenuItem.Checked = (this.view.BackgroundMode == BackgroundMode.NormalizeByEfficiency);
        }

        // Token: 0x06000333 RID: 819 RVA: 0x0001015C File Offset: 0x0000E35C
        void showBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Visible;
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.BG;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Token: 0x06000334 RID: 820 RVA: 0x00010180 File Offset: 0x0000E380
        void hideBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Invisible;
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.NOBG;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        void SubstractBgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.Substract;
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.SUB;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        void ShowConToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.BackgroundMode = BackgroundMode.ShowContinuum;
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.CONT;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Полноспектральное разложение: спектр целиком раскладывается на образы
        // нуклидов и цепочек, поверх графика рисуется послойный стек. Счёт идёт
        // в фоне (см. FsaAnalysisSession), поэтому нажатие не подвешивает окно.
        void ShowFsaToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!this.EnsureFsaInputs())
            {
                return;
            }

            this.view.BackgroundMode = BackgroundMode.ShowFSA;
            // Своя иконка, а не CONT: с ней кнопка врала — «показан континуум»
            // при разложенном спектре, и два разных режима выглядели одним.
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.FSA;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
            // Явная команда документа: отчёт показывается И активируется.
            this.OnFsaModeEntered(true);
        }

        /// <summary>
        /// (`A145`, этап 3) Документ вошёл в режим полноспектрального
        /// разложения — главной форме пора показать окно отчёта. Документ до
        /// формы не дотягивается (ссылки на <c>MainForm</c> у него нет), так
        /// что говорит событием — как и обо всём остальном.
        /// <see cref="FsaModeEnteredEventArgs.Explicit"/>: true — команда
        /// меню (отчёт активировать), false — циклическая кнопка (показать,
        /// фокус графику не отдавать).
        /// </summary>
        public event EventHandler<FsaModeEnteredEventArgs> FsaModeEntered;

        void OnFsaModeEntered(bool explicitCommand)
        {
            EventHandler<FsaModeEnteredEventArgs> handler = this.FsaModeEntered;
            if (handler != null)
            {
                handler(this, new FsaModeEnteredEventArgs(explicitCommand));
            }
        }

        /// <summary>
        /// Группировка строк отчёта и лент графика: родители/дочерние
        /// (`A145`, «Семантика группировки»). ОДНО значение на документ —
        /// окно отчёта пишет его сюда, график читает отсюда; отдельно
        /// хранить у каждого нельзя, иначе цвет, имя или доля разошлись бы.
        /// Настройка представления: в файл измерения, конфигурацию прибора и
        /// отпечаток расчёта не входит.
        /// </summary>
        public FullSpectrumAnalysis.FsaGrouping FsaGrouping
        {
            get
            {
                return this.view.FsaGrouping;
            }

            set
            {
                this.view.FsaGrouping = value;
            }
        }

        /// <summary>
        /// (`AMBER45`) Слой матрицы отклика на графике: «все» (стопка как
        /// есть) или один канал. Комбо «Matrix layer» окна отчёта пишет сюда,
        /// график читает отсюда — одно значение на документ, как
        /// <see cref="FsaGrouping"/>. Настройка ПОКАЗА: живёт на сеанс, в файл
        /// измерения, конфигурацию прибора и отпечаток расчёта не входит,
        /// таблицу отчёта не меняет.
        /// </summary>
        public FullSpectrumAnalysis.FsaMatrixLayer FsaMatrixLayer
        {
            get
            {
                return this.view.FsaMatrixLayer;
            }

            set
            {
                this.view.FsaMatrixLayer = value;
            }
        }

        /// <summary>
        /// (`A50`, `A145` этап 3) Отвергнутая матрица называет причину ЗДЕСЬ —
        /// у владельца сеанса, вне отрисовки и независимо от того, в каком
        /// режиме график и открыт ли отчёт. Событие приходит из фонового
        /// потока; строка приходит один раз на файл — сеанс держит ключ
        /// сказанного.
        /// </summary>
        void FsaSessionCompleted(object sender, EventArgs e)
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        string notice = this.fsaSession.TakeResponseMatrixNotice();
                        if (!string.IsNullOrEmpty(notice))
                        {
                            AppUi.Report(notice, Properties.Resources.ResponseMatrixTitle,
                                         MessageBoxIcon.Exclamation);
                        }
                    });
                }
            }
            catch (Exception)
            {
                // окно успело закрыться — говорить уже некому
            }
        }

        // (`AMBER328`) `CanShowFsa` — «можно ли войти в FSA циклом кнопки фона молча» —
        // снята вместе со входом FSA в цикл: читателя у неё не осталось. Кнопка FSA
        // вопросов не задаёт вовсе (решение Amber 12.11.2026 «Просто включить режим, а
        // отображать причины как сейчас они отображаются»): чего разбору не хватает,
        // называет сам разбор — отказом в окне отчёта.
        //
        // ⚠ `EnsureFsaInputs` ниже остаётся у пункта меню «Показать FSA»
        // (`ShowFsaToolStripMenuItem_Click`): пункт виден, но недоступен, и звать его
        // могут только пробы и конвейер снимков гида.

        /// <summary>
        /// Двух вещей разложению может не хватать, и обеим есть замена, но
        /// назвать её должен человек.
        ///
        /// Калибровка ПШПВ задаёт форму линий; без неё считать нечего. Взять её
        /// можно у любой конфигурации прибора — только у КАКОЙ, знает
        /// пользователь: подставленная молча чужая ширина даёт правдоподобные и
        /// неверные площади.
        ///
        /// Кривая эффективности задаёт относительные веса линий внутри образа;
        /// без неё разложение считается, но низкоэнергетическая часть
        /// перекашивается. Отказаться от кривой можно — но ОСОЗНАННО, отдельным
        /// пунктом списка, а не потому, что её забыли выбрать.
        ///
        /// false — от выбора отказались, режим не включаем: молча включённое
        /// разложение на чужих числах хуже невключённого.
        /// </summary>
        bool EnsureFsaInputs()
        {
            ResultData active = this.ActiveResultData;
            if (active == null)
            {
                return false;
            }

            return this.EnsureFsaFwhm(active) && this.EnsureFsaEfficiency(active);
        }

        bool EnsureFsaFwhm(ResultData active)
        {
            if (active.FwhmCalibration != null)
            {
                return true;
            }

            List<DeviceConfigInfo> devices = this.deviceConfigManager.DeviceConfigList;
            // Предвыбор — по Guid: у документа лежит КЛОН конфигурации
            // (DocumentManager.PrepareDeviceConfig), по ссылке он ни с одной
            // строкой списка не совпадёт, и выбор молча падал на первую.
            DeviceConfigInfo current = active.DeviceConfig;
            DeviceConfigInfo preselect = current == null
                ? null
                : devices.Find(d => string.Equals(d.Guid, current.Guid, StringComparison.OrdinalIgnoreCase));
            // ⛔ Вопрос человеку — тоже модальное окно, и висит оно ровно так
            //    же, как `MessageBox` (измерено 27.08.2026: безоконная проба на
            //    этом месте стояла до убийства, `reached` есть, `returned` нет).
            //    Отказ, а не «взять первую попавшуюся конфигурацию»: подпись
            //    метода говорит прямо — подставленная молча чужая ширина даёт
            //    правдоподобные и неверные площади.
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: у спектра нет калибровки ПШПВ, а выбрать конфигурацию прибора "
                    + "вместо неё некому: разложение спрашивает об этом окном. "
                    + "Задайте FwhmCalibration спектру до включения FSA.");
            }
            DeviceConfigInfo chosen = (DeviceConfigInfo)PickOneForm.Ask(this,
                Properties.Resources.FsaNoFwhmTitle,
                Properties.Resources.FsaNoFwhmQuestion,
                devices.ConvertAll<object>(d => d),
                preselect);
            if (chosen == null)
            {
                return false;
            }

            FWHMPeakDetectionMethodConfig peakConfig =
                chosen.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            // ⛔ ПРИЧИНА ГОВОРИТСЯ ВМЕСТЕ С ОТКАЗОМ (`A240`, полоса F62,
            //    06.09.2026). Голос тут стоял и раньше, но говорил ЧТО, не
            //    говоря ПОЧЕМУ: «у конфигурации прибора „X“ калибровки ПШПВ
            //    тоже нет». Человек только что выбрал эту конфигурацию из
            //    списка своей рукой, и следующий его вопрос — «а почему её там
            //    нет»; три числа настроек (`FWHM_AT_0`, `Ch_Fwhm`,
            //    `Width_Fwhm`) отвечают на него, и взять их больше негде: ни на
            //    одной форме их нет, они приходят из `config\device\*.xml`.
            //    Голос при этом ОСТАЁТСЯ ОДИН — не добавлен, а дополнен.
            //
            // ⛔ (`AMBER269`, 02.11.2026, полоса a269fix5) КРИВАЯ ВЫБРАННОГО ПРИБОРА —
            //    ТЕМ ЖЕ ПРАВИЛОМ, ЧТО ПРИ ОТКРЫТИИ ФАЙЛА (`FwhmCalibration.StandInFromDevice`;
            //    решение Amber 02.11.2026 «Кривую прибора (Рекомендую)»): сохранённая кривая
            //    прибора; на сетке спектра, отличной от сетки прибора, — перенесённая
            //    (`RecalcWithNewChannelNum`); если по ней нельзя (нет, встроенная заглушка
            //    15/3756/103, пустышка) — три числа прибора с формой его пика. Прежде здесь
            //    стояла копия кривой прибора как есть: спектр в 64 канала после импорта с
            //    пустой конфигурацией получал кривую в каналах прибора на 1024 канала —
            //    ширину канала 30 прибора на канале 30 спектра, — и FSA раскладывал по ней
            //    молча.
            string fwhmRefusal = null;
            FwhmCalibration calibration = peakConfig == null
                ? null
                : FwhmCalibration.StandInFromDevice(peakConfig, chosen.NumberOfChannels,
                    active.EnergySpectrum.EnergyCalibration, active.EnergySpectrum.NumberOfChannels,
                    out fwhmRefusal);
            if (calibration == null)
            {
                // Выбранная конфигурация не помогла — сказать об этом, а не
                // включать режим и оставлять человека гадать.
                //
                // Без окон это УВЕДОМЛЕНИЕ, а не отказ, и вот почему: у беды
                // здесь уже ЕСТЬ читатель — `false`, который возвращается
                // следующей строкой, и вызывающий (`ShowFsaToolStripMenuItem_Click`)
                // по нему режим не включает. Признак с читателем менять на
                // бросок незачем; строка в поток ошибок лишь называет причину.
                // ⚠ Дойти сюда без окон можно только мимо `PickOneForm.Ask`
                // выше — то есть никогда; строка написана на случай, когда
                // выбор станет возможен без окна.
                // ⛔ КУЛЬТУРА ИНВАРИАНТНАЯ, А НЕ ПОТОКА (`A242`): причина
                //    приходит из `DefaultCalibration` с тремя числами,
                //    напечатанными ТОЧКОЙ, и оправа обязана держать то же
                //    соглашение — иначе две половины одной строки разойдутся
                //    разделителем. Прежде здесь стоял `string.Format` без
                //    культуры вовсе.
                string text = string.Format(CultureInfo.InvariantCulture,
                                            Properties.Resources.FsaNoFwhmSource, chosen.Name);
                if (!string.IsNullOrEmpty(fwhmRefusal))
                {
                    // Оправа «что: почему» переводу не подлежит — переведены
                    // обе её половины по отдельности.
                    text = text + " " + fwhmRefusal;
                }
                AppUi.Report(text,
                    Properties.Resources.FsaNoFwhmTitle, MessageBoxIcon.Information);
                return false;
            }

            active.FwhmCalibration = calibration;
            this.Dirty = true;
            // (`AMBER269`, полоса a269fix5) Вкладка «Калибровка» показывает кривую спектра, а
            // узнаёт о ней только при смене документа: после выбора прибора здесь она
            // продолжала говорить «у спектра нет калибровки FWHM», пока человек не
            // переключал вкладки документов (замечено экраном 02.11.2026).
            if (this.FwhmCalibrationChanged != null)
            {
                this.FwhmCalibrationChanged(this, EventArgs.Empty);
            }
            return true;
        }

        bool EnsureFsaEfficiency(ResultData active)
        {
            if (active.Efficiency != null)
            {
                return true;
            }

            // Список — ТОТ ЖЕ, что в панели измерения, и составляется тем же
            // кодом: кривая привязана к прибору и геометрии, чужие предлагать
            // нельзя, но родная кривая самого спектра — своей строкой, и
            // конфигурация прибора берётся живая, из менеджера, а не копия,
            // лежащая в спектре (см. DCControlPanel.BuildEfficiencyItems и
            // CurrentDeviceConfig). Свой список здесь не знал ни того, ни
            // другого: у спектра с кривой из файла и прибором без кривых
            // выбирать было не из чего вовсе. Первым пунктом — отказ от
            // кривой, тот самый осознанный.
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                active.Efficiency, active.FileEfficiency,
                DCControlPanel.CurrentDeviceConfig(active.DeviceConfig,
                                                   this.deviceConfigManager.DeviceConfigList),
                out selected);

            // ⛔ То же окно и тот же отказ, что у калибровки ПШПВ выше. Отказ
            //    от кривой разрешён, но ОСОЗНАННО — отдельным пунктом списка;
            //    без окон выбрать этот пункт некому, а молча посчитать без
            //    кривой значит перекосить низкоэнергетическую часть и об этом
            //    не сказать.
            if (!AppUi.HasWindows)
            {
                throw new InvalidOperationException(
                    "BecqMoni: у спектра нет кривой эффективности, а выбрать её вместо "
                    + "человека некому: разложение спрашивает об этом окном. "
                    + "Задайте Efficiency спектру до включения FSA.");
            }
            object chosen = PickOneForm.Ask(this,
                Properties.Resources.FsaNoEfficiencyTitle,
                Properties.Resources.FsaNoEfficiencyQuestion,
                items, items[selected]);
            if (chosen == null)
            {
                return false;
            }

            // Присвоение — тоже общее с панелью: родная кривая ложится тем же
            // объектом, кривая прибора — копией.
            active.Efficiency = DCControlPanel.EfficiencyFromItem(chosen);
            // Изменённым спектр помечается, только если выбрана НЕ родная
            // кривая: возврат к родной файла не меняет.
            if (active.Efficiency != null
                && !object.ReferenceEquals(active.Efficiency, active.FileEfficiency))
            {
                this.Dirty = true;
            }

            // Панель управления измерением показывает ту кривую, по которой
            // сейчас считается активность, — а выбрали её здесь.
            if (this.EfficiencyChanged != null)
            {
                this.EfficiencyChanged(this, EventArgs.Empty);
            }

            // Мощность дозы считается от той же кривой (`AMBER18`): выбор здесь
            // — тоже смена показания, его подхватит таймер главного окна.
            this.UpdateDoseRate = true;
            return true;
        }

        void NormByEffToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // (`AMBER34`, П79) Кривая выбрана, но нормировать по ней нельзя —
            // причина словами той же дверью, что прочие отказы приложения
            // (`AppUi.Report`); режим не меняется.
            string refusal = this.ActiveResultData != null
                ? Utils.SpectrumAriphmetics.NormalizeRefusal(this.ActiveResultData.Efficiency)
                : Properties.Resources.NormalizeNoCurve;
            if (refusal != null)
            {
                AppUi.Report(refusal, Properties.Resources.ErrorDialogTitle, MessageBoxIcon.Information);
                return;
            }

            this.view.BackgroundMode = BackgroundMode.NormalizeByEfficiency;
            this.toolStripSplitButtonBgMode.Image = Properties.Resources.NORM;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.RefreshView();
        }

        // Token: 0x06000335 RID: 821 RVA: 0x000101A4 File Offset: 0x0000E3A4
        void toolStripSplitButton8_ButtonClick(object sender, EventArgs e)
        {
            DrawingMode drawingMode = DrawingMode.Normal;
            Image image = Properties.Resources.HD1;
            switch (this.view.DrawingMode)
            {
                case DrawingMode.HighDefinition:
                    drawingMode = DrawingMode.Normal;
                    image = Properties.Resources.HD1;
                    break;
                case DrawingMode.Normal:
                    drawingMode = DrawingMode.HighDefinition;
                    image = Properties.Resources.HD;
                    break;
            }
            this.view.DrawingMode = drawingMode;
            this.toolStripSplitButton8.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000336 RID: 822 RVA: 0x00010210 File Offset: 0x0000E410
        void toolStripSplitButton8_DropDownOpening(object sender, EventArgs e)
        {
            this.highDefinitionViewToolStripMenuItem.Checked = (this.view.DrawingMode == DrawingMode.HighDefinition);
            this.normalViewToolStripMenuItem.Checked = (this.view.DrawingMode == DrawingMode.Normal);
        }

        // Token: 0x06000337 RID: 823 RVA: 0x00010254 File Offset: 0x0000E454
        void highDefinitionViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.DrawingMode = DrawingMode.HighDefinition;
            this.toolStripSplitButton8.Image = Properties.Resources.HD;
            this.RefreshView();
        }

        // Token: 0x06000338 RID: 824 RVA: 0x00010278 File Offset: 0x0000E478
        void normalViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.DrawingMode = DrawingMode.Normal;
            this.toolStripSplitButton8.Image = Properties.Resources.HD1;
            this.RefreshView();
        }

        // Token: 0x06000339 RID: 825 RVA: 0x0001029C File Offset: 0x0000E49C
        void toolStripSplitButton1_ButtonClick(object sender, EventArgs e)
        {
            VerticalUnit verticalUnit = VerticalUnit.Counts;
            Image image = Properties.Resources.cnt;
            switch (this.view.VerticalUnit)
            {
                case VerticalUnit.Counts:
                    verticalUnit = VerticalUnit.CountsPerSecond;
                    image = Properties.Resources.cps;
                    break;
                case VerticalUnit.CountsPerSecond:
                    verticalUnit = VerticalUnit.Counts;
                    image = Properties.Resources.cnt;
                    break;
            }
            this.view.VerticalUnit = verticalUnit;
            this.toolStripSplitButton1.Image = image;
            this.RefreshView();
        }

        // Token: 0x0600033A RID: 826 RVA: 0x00010308 File Offset: 0x0000E508
        void cpsViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalUnit = VerticalUnit.CountsPerSecond;
            this.toolStripSplitButton1.Image = Properties.Resources.cps;
            this.RefreshView();
        }

        // Token: 0x0600033B RID: 827 RVA: 0x0001032C File Offset: 0x0000E52C
        void countsViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalUnit = VerticalUnit.Counts;
            this.toolStripSplitButton1.Image = Properties.Resources.cnt;
            this.RefreshView();
        }

        // Token: 0x0600033C RID: 828 RVA: 0x00010350 File Offset: 0x0000E550
        void toolStripSplitButton1_DropDownOpening(object sender, EventArgs e)
        {
            this.cpsViewToolStripMenuItem.Checked = (this.view.VerticalUnit == VerticalUnit.CountsPerSecond);
            this.countsViewToolStripMenuItem.Checked = (this.view.VerticalUnit == VerticalUnit.Counts);
        }

        void toolStripNumericUpdown_ValueChanged(object sender, EventArgs e)
        {
            double value = (double)this.toolStripNumericUpdown.NumericUpDownControl.Value;
            if (value > 10.0)
            {
                value = 10.0;
            }
            else if (value < 0.1)
            {
                value = 0.1;
            }
            this.view.PowNum = value;
            GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
            globalConfig.ChartViewConfig.PowNum = value;
            this.RefreshView();
        }

        void ToolStripNumericUpdown_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.E)
            {
                this.toolStripNumericUpdown.NumericUpDownControl.Value = (decimal)Math.E;
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x0600033D RID: 829 RVA: 0x00010394 File Offset: 0x0000E594
        void toolStripSplitButton2_ButtonClick(object sender, EventArgs e)
        {
            VerticalScaleType verticalScaleType = VerticalScaleType.LogarithmicScale;
            Image image = Properties.Resources.log;
            switch (this.view.VerticalScaleType)
            {
                case VerticalScaleType.LinearScale:
                    verticalScaleType = VerticalScaleType.PowerScale;
                    image = Properties.Resources.pow;
                    this.toolStripNumericUpdown.Enabled = true;
                    break;
                case VerticalScaleType.PowerScale:
                    verticalScaleType = VerticalScaleType.LogarithmicScale;
                    image = Properties.Resources.log;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
                case VerticalScaleType.LogarithmicScale:
                    verticalScaleType = VerticalScaleType.LinearScale;
                    image = Properties.Resources.linear;
                    this.toolStripNumericUpdown.Enabled = false;
                    break;
            }
            this.view.VerticalScaleType = verticalScaleType;
            this.toolStripSplitButton2.Image = image;
            this.RefreshView();
        }

        // Token: 0x0600033E RID: 830 RVA: 0x00010400 File Offset: 0x0000E600
        void logarithmicViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.LogarithmicScale;
            this.toolStripSplitButton2.Image = Properties.Resources.log;
            this.toolStripNumericUpdown.Enabled = false;
            this.RefreshView();
        }

        void powToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.PowerScale;
            this.toolStripSplitButton2.Image = Properties.Resources.pow;
            this.toolStripNumericUpdown.Enabled = true;
            this.RefreshView();
        }

        // Token: 0x0600033F RID: 831 RVA: 0x00010424 File Offset: 0x0000E624
        void linearViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalScaleType = VerticalScaleType.LinearScale;
            this.toolStripSplitButton2.Image = Properties.Resources.linear;
            this.toolStripNumericUpdown.Enabled = false;
            this.RefreshView();
        }

        // Token: 0x06000340 RID: 832 RVA: 0x00010448 File Offset: 0x0000E648
        void toolStripSplitButton2_DropDownOpening(object sender, EventArgs e)
        {
            this.logarithmicViewToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.LogarithmicScale);
            this.linearViewToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.LinearScale);
            this.powToolStripMenuItem.Checked = (this.view.VerticalScaleType == VerticalScaleType.PowerScale);
        }

        // Token: 0x06000341 RID: 833 RVA: 0x0001048C File Offset: 0x0000E68C
        void toolStripSplitButton3_ButtonClick(object sender, EventArgs e)
        {
            ChartType chartType = ChartType.LineChart;
            Image image = Properties.Resources.line;
            switch (this.view.ChartType)
            {
                case ChartType.BarChart:
                    chartType = ChartType.LineChart;
                    image = Properties.Resources.line;
                    break;
                case ChartType.LineChart:
                    chartType = ChartType.BarChart;
                    image = Properties.Resources.bar;
                    break;
            }
            this.view.ChartType = chartType;
            this.toolStripSplitButton3.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000342 RID: 834 RVA: 0x000104F8 File Offset: 0x0000E6F8
        void lineGraphToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.ChartType = ChartType.LineChart;
            this.toolStripSplitButton3.Image = Properties.Resources.line;
            this.RefreshView();
        }

        // Token: 0x06000343 RID: 835 RVA: 0x0001051C File Offset: 0x0000E71C
        void barGraphToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.ChartType = ChartType.BarChart;
            this.toolStripSplitButton3.Image = Properties.Resources.bar;
            this.RefreshView();
        }

        // Token: 0x06000344 RID: 836 RVA: 0x00010540 File Offset: 0x0000E740
        void toolStripSplitButton3_DropDownOpening(object sender, EventArgs e)
        {
            this.lineGraphToolStripMenuItem.Checked = (this.view.ChartType == ChartType.LineChart);
            this.barGraphToolStripMenuItem.Checked = (this.view.ChartType == ChartType.BarChart);
        }

        // Token: 0x06000345 RID: 837 RVA: 0x00010584 File Offset: 0x0000E784
        void toolStripSplitButton5_ButtonClick(object sender, EventArgs e)
        {
            VerticalFittingMode verticalFittingMode = VerticalFittingMode.MinMax;
            Image image = Properties.Resources.fit1;
            switch (this.view.VerticalFittingMode)
            {
                case VerticalFittingMode.None:
                    verticalFittingMode = VerticalFittingMode.MinMax;
                    image = Properties.Resources.fit1;
                    break;
                case VerticalFittingMode.MinMax:
                    verticalFittingMode = VerticalFittingMode.BackgroundMinMax;
                    image = Properties.Resources.bgfit;
                    break;
                case VerticalFittingMode.BackgroundMinMax:
                    verticalFittingMode = VerticalFittingMode.None;
                    image = Properties.Resources.None;
                    break;
            }
            this.view.VerticalFittingMode = verticalFittingMode;
            this.toolStripSplitButton5.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000346 RID: 838 RVA: 0x00010600 File Offset: 0x0000E800
        void autoFitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.MinMax;
            this.toolStripSplitButton5.Image = Properties.Resources.fit1;
            this.RefreshView();
        }

        // Token: 0x06000347 RID: 839 RVA: 0x00010624 File Offset: 0x0000E824
        void autoFitBgToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.BackgroundMinMax;
            this.toolStripSplitButton5.Image = Properties.Resources.bgfit;
            this.RefreshView();
        }

        // Token: 0x06000348 RID: 840 RVA: 0x00010648 File Offset: 0x0000E848
        void noneToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.VerticalFittingMode = VerticalFittingMode.None;
            this.toolStripSplitButton5.Image = Properties.Resources.None;
            this.view.Invalidate();
        }

        // Token: 0x06000349 RID: 841 RVA: 0x00010674 File Offset: 0x0000E874
        void toolStripSplitButton5_DropDownOpening(object sender, EventArgs e)
        {
            this.autoFitToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.MinMax);
            this.autoFitBgToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.BackgroundMinMax);
            this.noneToolStripMenuItem.Checked = (this.view.VerticalFittingMode == VerticalFittingMode.None);
        }

        // Token: 0x0600034A RID: 842 RVA: 0x000106D0 File Offset: 0x0000E8D0
        void toolStripSplitButton6_ButtonClick(object sender, EventArgs e)
        {
            SmoothingMethod smoothingMethod = SmoothingMethod.None;
            Image image = Properties.Resources.NoSmooth;
            switch (this.view.SmoothingMethod)
            {
                case SmoothingMethod.None:
                    smoothingMethod = SmoothingMethod.SimpleMovingAverage;
                    image = Properties.Resources.SMA;
                    break;
                case SmoothingMethod.SimpleMovingAverage:
                    smoothingMethod = SmoothingMethod.WeightedMovingAverage;
                    image = Properties.Resources.WMA;
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    smoothingMethod = SmoothingMethod.None;
                    image = Properties.Resources.NoSmooth;
                    break;
            }
            this.view.SmoothingMethod = smoothingMethod;
            this.toolStripSplitButton6.Image = image;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034B RID: 843 RVA: 0x0001074C File Offset: 0x0000E94C
        void noSmoothingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.None;
            this.toolStripSplitButton6.Image = Properties.Resources.NoSmooth;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034C RID: 844 RVA: 0x00010770 File Offset: 0x0000E970
        void simpleMovingAverageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.SimpleMovingAverage;
            this.toolStripSplitButton6.Image = Properties.Resources.SMA;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034D RID: 845 RVA: 0x00010794 File Offset: 0x0000E994
        void weightedMovingAverageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.SmoothingMethod = SmoothingMethod.WeightedMovingAverage;
            this.toolStripSplitButton6.Image = Properties.Resources.WMA;
            this.UpdateDetectedPeaks = true;
            this.RefreshView();
        }

        // Token: 0x0600034E RID: 846 RVA: 0x000107B8 File Offset: 0x0000E9B8
        void toolStripSplitButton6_DropDownOpening(object sender, EventArgs e)
        {
            this.noSmoothingToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.None);
            this.simpleMovingAverageToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.SimpleMovingAverage);
            this.weightedMovingAverageToolStripMenuItem.Checked = (this.view.SmoothingMethod == SmoothingMethod.WeightedMovingAverage);
        }

        // Token: 0x0600034F RID: 847 RVA: 0x00010814 File Offset: 0x0000EA14
        void toolStripSplitButton4_ButtonClick(object sender, EventArgs e)
        {
            HorizontalUnit horizontalUnit = HorizontalUnit.Energy;
            Image image = Properties.Resources.ene;
            switch (this.view.HorizontalUnit)
            {
                case HorizontalUnit.Channel:
                    horizontalUnit = HorizontalUnit.Energy;
                    image = Properties.Resources.ene;
                    break;
                case HorizontalUnit.Energy:
                    horizontalUnit = HorizontalUnit.Channel;
                    image = Properties.Resources.channel;
                    break;
            }
            this.view.HorizontalUnit = horizontalUnit;
            this.toolStripSplitButton4.Image = image;
            this.RefreshView();
        }

        // Token: 0x06000350 RID: 848 RVA: 0x00010880 File Offset: 0x0000EA80
        void energyViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.HorizontalUnit = HorizontalUnit.Energy;
            this.toolStripSplitButton4.Image = Properties.Resources.ene;
            this.RefreshView();
        }

        // Token: 0x06000351 RID: 849 RVA: 0x000108A4 File Offset: 0x0000EAA4
        void channelViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.HorizontalUnit = HorizontalUnit.Channel;
            this.toolStripSplitButton4.Image = Properties.Resources.channel;
            this.RefreshView();
        }

        // Token: 0x06000352 RID: 850 RVA: 0x000108C8 File Offset: 0x0000EAC8
        void toolStripSplitButton4_DropDownOpening(object sender, EventArgs e)
        {
            this.energyViewToolStripMenuItem.Checked = (this.view.HorizontalUnit == HorizontalUnit.Energy);
            this.channelViewToolStripMenuItem.Checked = (this.view.HorizontalUnit == HorizontalUnit.Channel);
        }

        // Token: 0x06000353 RID: 851 RVA: 0x0001090C File Offset: 0x0000EB0C
        // (`AMBER328`, решение Amber 13.11.2026 «Пики станут общими») Кнопка пиков и оба
        // её пункта действуют на ВСЕ открытые вкладки и пишут состояние в общую
        // настройку `ChartViewConfig.DefaultPeakMode` — одним путём,
        // <see cref="SetPeakModeEverywhere"/>.
        void toolStripSplitButton9_ButtonClick(object sender, EventArgs e)
        {
            SetPeakModeEverywhere(this.view.PeakMode == PeakMode.Visible ? PeakMode.Invisible : PeakMode.Visible, this);
        }

        // Token: 0x06000354 RID: 852 RVA: 0x00010978 File Offset: 0x0000EB78
        void toolStripSplitButton9_DropDownOpening(object sender, EventArgs e)
        {
            this.showPeaksToolStripMenuItem.Checked = (this.view.PeakMode == PeakMode.Visible);
            this.hidePeaksToolStripMenuItem.Checked = (this.view.PeakMode == PeakMode.Invisible);
        }

        // Token: 0x06000355 RID: 853 RVA: 0x000109BC File Offset: 0x0000EBBC
        void showPeaksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPeakModeEverywhere(PeakMode.Visible, this);
        }

        // Token: 0x06000356 RID: 854 RVA: 0x000109E0 File Offset: 0x0000EBE0
        void hidePeaksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPeakModeEverywhere(PeakMode.Invisible, this);
        }

        /// <summary>
        /// (`AMBER328`, решение Amber 13.11.2026 «Пики станут общими» и «Запоминать, как
        /// галочку FSA») Пики для всего приложения: записать в
        /// `ChartViewConfig.DefaultPeakMode` (её же показывает окно настроек, и с неё
        /// открывается новая вкладка), сохранить конфиг — только в приложении, как
        /// <see cref="SetFsaModeEverywhere"/>, — и привести ВСЕ открытые вкладки:
        /// пики, картинка кнопки, признак метода, затем правило показа FSA (пики
        /// выключили — FSA гаснет везде; включили при галочке FSA — возвращается везде).
        /// Зовут кнопка пиков, оба её пункта и окно настроек (`GlobalConfigForm`), то
        /// есть поведение у всех путей одно. <paramref name="origin"/> — вкладка, чьей
        /// кнопкой нажато (входит в список, даже если менеджер документов её не знает),
        /// или null. Возвращает число приведённых вкладок.
        /// </summary>
        internal static int SetPeakModeEverywhere(PeakMode mode, DocEnergySpectrum origin)
        {
            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            manager.GlobalConfig.ChartViewConfig.DefaultPeakMode = mode;
            if (FullSpectrumAnalysis.FsaCalculationOptions.ApplicationSource != null)
            {
                manager.SaveConfigFile();
            }

            int count = 0;
            foreach (DocEnergySpectrum document in OpenDocuments(origin))
            {
                document.ApplyPeakMode(mode);
                document.RefreshViewWithFsaLink();
                count++;
            }
            return count;
        }

        /// <summary>
        /// Пики этой вкладки — без перерисовки. ⚠ Признак `Enabled` пишется в
        /// настройки поиска пиков СПЕКТРА (у всех спектров документа, как при
        /// загрузке — `CreateResultData`), но НЕ в конфигурацию прибора: у спектра
        /// она — живой объект менеджера приборов, и кнопка прежде молча меняла
        /// общую конфигурацию (а пункты списка — нет, находка Н4 полосы a328).
        /// Читателя, решающего по `Enabled` показ пиков, нет ни у прибора, ни у
        /// спектра: показ решает `view.PeakMode`, а общее состояние живёт в
        /// `DefaultPeakMode` (журнал `handover-2026-11-12-a328.md`, доделка a328b).
        /// </summary>
        void ApplyPeakMode(PeakMode mode)
        {
            bool visible = mode == PeakMode.Visible;
            this.view.PeakMode = mode;
            this.toolStripSplitButton9.Image = visible ? Properties.Resources.peak : Properties.Resources.nopeak;
            if (this.resultDataFile != null && this.resultDataFile.ResultDataList != null)
            {
                foreach (ResultData resultData in this.resultDataFile.ResultDataList)
                {
                    FWHMPeakDetectionMethodConfig cfg = resultData != null
                        ? resultData.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                        : null;
                    if (cfg != null)
                    {
                        cfg.Enabled = visible;
                    }
                }
            }
            this.UpdateDetectedPeaks = visible;
        }

        /// <summary>
        /// Все открытые вкладки (`DocumentManager.DocumentList`) и вкладка-источник,
        /// если менеджер её не знает; закрытые пропускаются.
        /// </summary>
        static List<DocEnergySpectrum> OpenDocuments(DocEnergySpectrum origin)
        {
            List<DocEnergySpectrum> documents = new List<DocEnergySpectrum>();
            foreach (DocEnergySpectrum document in DocumentManager.GetInstance().DocumentList)
            {
                if (document != null && !document.IsDisposed && !documents.Contains(document))
                {
                    documents.Add(document);
                }
            }
            if (origin != null && !origin.IsDisposed && !documents.Contains(origin))
            {
                documents.Add(origin);
            }
            return documents;
        }

        // ====================================================================
        // (`AMBER328`, постановка и решения Amber 12.11.2026) Кнопка-флажок FSA
        // справа от кнопки пиков и её связь с поиском пиков.
        //
        // АВТОМАТ. Галочка FSA одна на приложение и живёт в конфиге между
        // запусками (`ChartViewConfig.FsaModeEnabled`). Пики тоже общие (решение
        // Amber 13.11.2026 «Пики станут общими»): кнопка пиков и её пункты меняют
        // `ChartViewConfig.DefaultPeakMode` и ВСЕ вкладки разом
        // (<see cref="SetPeakModeEverywhere"/>); у вкладки остаётся копия —
        // `view.PeakMode`. Правило показа одно:
        //     FSA показан на вкладке ⇔ галочка ВКЛ И пики вкладки ВКЛ
        // (<see cref="IsFsaShownBy"/>). Отсюда переходы:
        //   П1 пики ВЫКЛ при галочке ВКЛ — FSA гаснет на всех вкладках, каждая
        //      возвращается в режим фона, бывший до FSA; галочка НЕ сбрасывается;
        //   П2 пики ВКЛ при галочке ВКЛ — FSA возвращается сам, везде;
        //   П3 кнопка FSA ВЫКЛ — галочка сброшена, FSA гаснет на ВСЕХ вкладках;
        //      пики дальше на FSA не влияют (без галочки показа нет);
        //   П4 кнопка FSA ВКЛ — галочка поднята, FSA загорается на всех вкладках.
        //      ВКЛЮЧИТЬ галочку можно только при включённых пиках (решение Amber
        //      13.11.2026 «Кнопка FSA доступна к включению только после включения
        //      поиска пиков.»): при пиках ВЫКЛ и галочке ВЫКЛ кнопка недоступна, пункт
        //      «Включить» недоступен всегда, пока пики ВЫКЛ; ВЫКЛЮЧИТЬ — можно всегда
        //      (<see cref="UpdateFsaButton"/>, <see cref="RequestFsaMode"/>).
        // Связь срабатывает на СОБЫТИЯХ — конструктор вкладки, кнопка пиков и её
        // пункты, пики по умолчанию в окне настроек, кнопка FSA и её пункты, — а не на
        // каждом обновлении вида: вкладку,
        // выведенную из FSA кнопкой фона, следующее обновление назад не тянет
        // (развилка Р1 журнала `handover-2026-11-12-a328.md`).
        // ====================================================================

        enum FsaLinkStep
        {
            None,
            Entered,
            Left
        }

        /// <summary>Правило показа FSA на вкладке (`AMBER328`) — единственное место, где оно записано.</summary>
        internal static bool IsFsaShownBy(bool fsaModeEnabled, PeakMode peakMode)
        {
            return fsaModeEnabled && peakMode == PeakMode.Visible;
        }

        static bool GlobalFsaModeEnabled
        {
            get
            {
                return GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.FsaModeEnabled;
            }
        }

        /// <summary>
        /// Режим фона, в котором вкладка была, когда в неё вошёл FSA: туда она и
        /// вернётся, когда FSA погаснет (прежде выход из FSA всегда вёл в
        /// «Показать»). До первого входа — «Показать».
        /// </summary>
        BackgroundMode backgroundModeBeforeFsa = BackgroundMode.Visible;

        /// <summary>
        /// Куда вернуться из FSA: в бывший до него режим, если в нём можно стоять
        /// сейчас. Нормировка по эффективности без годной кривой — «Показать», тем
        /// же правилом, что у <see cref="EvaluateNormByEffMode"/>.
        /// </summary>
        BackgroundMode BackgroundModeAfterFsa()
        {
            BackgroundMode mode = this.backgroundModeBeforeFsa;
            if (mode == BackgroundMode.ShowFSA
                || (mode == BackgroundMode.NormalizeByEfficiency && !this.IsNormalizeByEfficiencyAvailable()))
            {
                mode = BackgroundMode.Visible;
            }
            return mode;
        }

        /// <summary>
        /// Привести вкладку к правилу показа. Вид не перерисовывает — это делает
        /// вызывающий, и он же после перерисовки говорит о входе
        /// (<see cref="OnFsaModeEntered"/>), как прежде делал пункт меню.
        /// Вопросов не задаёт (решение Amber 12.11.2026 «Просто включить режим, а
        /// отображать причины как сейчас они отображаются»): спектр без кривой или
        /// FWHM входит в FSA, а чего не хватает, называет отказ самого разбора.
        /// </summary>
        FsaLinkStep ApplyFsaLink()
        {
            bool shown = IsFsaShownBy(GlobalFsaModeEnabled, this.view.PeakMode);
            FsaLinkStep step = FsaLinkStep.None;
            if (shown && this.view.BackgroundMode != BackgroundMode.ShowFSA)
            {
                this.backgroundModeBeforeFsa = this.view.BackgroundMode;
                this.view.BackgroundMode = BackgroundMode.ShowFSA;
                step = FsaLinkStep.Entered;
            }
            else if (!shown && this.view.BackgroundMode == BackgroundMode.ShowFSA)
            {
                this.view.BackgroundMode = this.BackgroundModeAfterFsa();
                step = FsaLinkStep.Left;
            }

            if (step != FsaLinkStep.None)
            {
                this.toolStripSplitButtonBgMode.Image = BackgroundModeImage(this.view.BackgroundMode);
                this.UpdateDoseRate = true;
                // Пики выключены — их обновления не заказывать: обработчик пиков
                // только что сбросил признак нарочно.
                if (this.view.PeakMode == PeakMode.Visible)
                {
                    this.UpdateDetectedPeaks = true;
                }
            }

            this.UpdateFsaButton();
            return step;
        }

        /// <summary>Перерисовать вкладку после смены пиков, приведя её к правилу показа FSA.</summary>
        void RefreshViewWithFsaLink()
        {
            FsaLinkStep step = this.ApplyFsaLink();
            this.RefreshView();
            if (step == FsaLinkStep.Entered)
            {
                // Как циклическая кнопка прежде: отчёт показывается, фокус остаётся
                // у графика — человек нажал кнопку на панели графика.
                this.OnFsaModeEntered(false);
            }
        }

        /// <summary>
        /// Галочка FSA для всего приложения: записать, сохранить конфиг (только в
        /// приложении — у проб общей настройки нет, как у `FSAReportView`) и
        /// привести к правилу ВСЕ открытые вкладки. <paramref name="origin"/> —
        /// вкладка, чьей кнопкой нажато (она входит в список, даже если менеджер
        /// документов её не знает). Возвращает число вкладок, сменивших режим.
        /// </summary>
        internal static int SetFsaModeEverywhere(bool enabled, DocEnergySpectrum origin)
        {
            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            manager.GlobalConfig.ChartViewConfig.FsaModeEnabled = enabled;
            if (FullSpectrumAnalysis.FsaCalculationOptions.ApplicationSource != null)
            {
                manager.SaveConfigFile();
            }

            int changed = 0;
            foreach (DocEnergySpectrum document in OpenDocuments(origin))
            {
                FsaLinkStep step = document.ApplyFsaLink();
                if (step == FsaLinkStep.None)
                {
                    continue;
                }

                changed++;
                document.RefreshView();
                if (step == FsaLinkStep.Entered)
                {
                    // Отчёт показывает главная форма и только для активной вкладки
                    // (`MainForm.DocEnergySpectrum_FsaModeEntered`).
                    document.OnFsaModeEntered(false);
                }
            }
            return changed;
        }

        void toolStripSplitButtonFsa_ButtonClick(object sender, EventArgs e)
        {
            this.RequestFsaMode(!GlobalFsaModeEnabled);
        }

        void toolStripSplitButtonFsa_DropDownOpening(object sender, EventArgs e)
        {
            bool enabled = GlobalFsaModeEnabled;
            this.fsaOnToolStripMenuItem.Checked = enabled;
            this.fsaOffToolStripMenuItem.Checked = !enabled;
            this.UpdateFsaButton();
        }

        void fsaOnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.RequestFsaMode(true);
        }

        void fsaOffToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.RequestFsaMode(false);
        }

        /// <summary>
        /// (`AMBER328`, решение Amber 13.11.2026 «Кнопка FSA доступна к включению только
        /// после включения поиска пиков.») Можно ли с этой вкладки ВКЛЮЧИТЬ галочку FSA:
        /// только при включённых пиках. Выключение этим не ограничено.
        /// </summary>
        bool CanEnableFsaMode
        {
            get
            {
                return this.view.PeakMode == PeakMode.Visible;
            }
        }

        /// <summary>
        /// Галочка FSA с кнопки и её пунктов — один путь для всех трёх. Включить при
        /// пиках ВЫКЛ нельзя: кнопка и пункт в этом состоянии недоступны
        /// (<see cref="UpdateFsaButton"/>), а здесь тот же запрет стоит для обработчика,
        /// позванного мимо недоступного пункта. false — ничего не сделано.
        /// </summary>
        bool RequestFsaMode(bool enabled)
        {
            if (enabled && !this.CanEnableFsaMode)
            {
                this.UpdateFsaButton();
                return false;
            }
            SetFsaModeEverywhere(enabled, this);
            return true;
        }

        /// <summary>
        /// Кнопка показывает ГАЛОЧКУ, а не показ на этой вкладке: щелчок меняет
        /// галочку, и картинка обязана говорить, что именно он сменит. ВКЛ — цветная
        /// иконка FSA, ВЫКЛ — серая, как у пар «пики/нет пиков» и «фон/нет фона».
        ///
        /// Доступность (решение Amber 13.11.2026): при пиках ВЫКЛ включить FSA нельзя —
        /// пункт «Включить» недоступен, а при галочке ВЫКЛ недоступна и вся кнопка
        /// (её щелчок был бы включением; WinForms рисует её блёклой). При галочке ВКЛ
        /// кнопка доступна и при пиках ВЫКЛ: её щелчок — выключение, а оно можно всегда.
        /// </summary>
        void UpdateFsaButton()
        {
            bool flag = GlobalFsaModeEnabled;
            bool canEnable = this.CanEnableFsaMode;
            this.toolStripSplitButtonFsa.Image = flag ? Properties.Resources.FSA : FsaOffImage();
            this.toolStripSplitButtonFsa.Enabled = flag || canEnable;
            this.fsaOnToolStripMenuItem.Enabled = canEnable;
            this.fsaOffToolStripMenuItem.Enabled = true;
        }

        static Image fsaOffImage;

        /// <summary>
        /// Серая иконка «FSA выкл» — из той же `FSA.bmp`, двумя серыми соседних
        /// иконок «выкл»; белый фон остаётся белым (прозрачный цвет кнопки).
        /// Своего файла картинки нет нарочно: вторая копия рисунка разошлась бы с
        /// первой при первой же правке.
        /// </summary>
        static Image FsaOffImage()
        {
            if (fsaOffImage == null)
            {
                Bitmap source = Properties.Resources.FSA;
                Bitmap grey = new Bitmap(source.Width, source.Height);
                for (int y = 0; y < source.Height; y++)
                {
                    for (int x = 0; x < source.Width; x++)
                    {
                        Color c = source.GetPixel(x, y);
                        if (c.R == 255 && c.G == 255 && c.B == 255)
                        {
                            grey.SetPixel(x, y, Color.White);
                            continue;
                        }
                        // Две серых соседних иконок «выкл» (`nopeak`, `NOBG`): тёмное —
                        // 808080, светлое — C0C0C0. Порог посередине разводит синий пик
                        // (тёмный) и золотые (светлые), чтобы рисунок не слился в пятно.
                        double luminance = 0.30 * c.R + 0.59 * c.G + 0.11 * c.B;
                        grey.SetPixel(x, y, luminance < 128.0 ? Color.FromArgb(0x80, 0x80, 0x80) : Color.FromArgb(0xC0, 0xC0, 0xC0));
                    }
                }
                source.Dispose();
                fsaOffImage = grey;
            }
            return fsaOffImage;
        }

        // Token: 0x06000357 RID: 855 RVA: 0x00010A04 File Offset: 0x0000EC04
        void toolStripSplitButton10_ButtonClick(object sender, EventArgs e)
        {
            if (this.activeEnergyCalibration)
            {
                if (!this.isActivating)
                {
                    this.ActiveEnergyCalibration = false;
                    this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
                }
            }
            else
            {
                this.ActiveEnergyCalibration = true;
                this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
            }
            this.isActivating = false;
        }

        // Token: 0x06000358 RID: 856 RVA: 0x00010A74 File Offset: 0x0000EC74
        void toolStripSplitButton10_DropDownOpening(object sender, EventArgs e)
        {
            this.showEnergyCalibrationToolToolStripMenuItem.Checked = this.activeEnergyCalibration;
            this.hideEnergyCalibrationToolToolStripMenuItem.Checked = !this.activeEnergyCalibration;
        }

        // Token: 0x06000359 RID: 857 RVA: 0x00010A9C File Offset: 0x0000EC9C
        void showEnergyCalibrationToolToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ActiveEnergyCalibration = true;
            this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
        }

        // Token: 0x0600035A RID: 858 RVA: 0x00010ABC File Offset: 0x0000ECBC
        void hideEnergyCalibrationToolToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ActiveEnergyCalibration = false;
            this.ShowEnergyCalibrationView(this, new ShowEnergyCalibrationViewEventArgs(this.activeEnergyCalibration));
        }

        // Token: 0x0600035F RID: 863 RVA: 0x00010BAC File Offset: 0x0000EDAC
        void toolStripContainer1_BottomToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000360 RID: 864 RVA: 0x00010BB4 File Offset: 0x0000EDB4
        void toolStripContainer1_LeftToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000361 RID: 865 RVA: 0x00010BBC File Offset: 0x0000EDBC
        void toolStripContainer1_TopToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000362 RID: 866 RVA: 0x00010BC4 File Offset: 0x0000EDC4
        void toolStripContainer1_RightToolStripPanel_Click(object sender, EventArgs e)
        {
            this.ClearSelection();
        }

        // Token: 0x06000363 RID: 867 RVA: 0x00010BCC File Offset: 0x0000EDCC
        void toolStripEnergyCalibrationButton1_EnergyCalibrationChanged(object sender, EnergyCalibrationChangedEventArgs e)
        {
            ResultData activeResultData = this.ActiveResultData;
            activeResultData.EnergySpectrum.EnergyCalibration = e.EnergyCalibration;
            this.RefreshView();
            this.updateMeasurementResult = true;
            this.Dirty = true;
        }

        // Token: 0x06000364 RID: 868 RVA: 0x00010C0C File Offset: 0x0000EE0C
        void toolStripButton1_Click(object sender, EventArgs e)
        {
            this.view.FitHorizontalScale();
        }

        void toolStripButton3_Click(object sender, EventArgs e)
        {
            this.view.SetScale11();
        }

        // Token: 0x06000365 RID: 869 RVA: 0x00010C1C File Offset: 0x0000EE1C
        void showAllChannelsAToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.view.FitHorizontalScale();
        }

        // Token: 0x06000366 RID: 870 RVA: 0x00010C2C File Offset: 0x0000EE2C
        void toolStripButton2_Click(object sender, EventArgs e)
        {
            this.view.ZoominSelectedRegion();
        }

        // Token: 0x06000367 RID: 871 RVA: 0x00010C3C File Offset: 0x0000EE3C
        void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.view.ZoominSelectedRegion();
        }

        // Token: 0x06000369 RID: 873 RVA: 0x00010C6C File Offset: 0x0000EE6C
        void setLowerThresholdLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SetLowerThreshold != null)
            {
                this.SetLowerThreshold(this, new EventArgs());
            }
        }

        void toolStripScreenShotButton_Click(object sender, EventArgs e)
        {
            this.view.takeScreenshot();
        }

        void toolStripRefreshBgButton_Click(object sender, EventArgs e)
        {
            if (!IsBackgroundExists()) return;
            // (`AMBER201`, мелочь 2.15, 05.10.2026) Прежний фон запоминается: файл,
            // который не загрузился, больше не снимает вместе с собой и тот фон,
            // что уже был в документе (встроенный в файл спектра — второго его
            // источника нет нигде).
            EnergySpectrum previousBackground = this.ActiveResultData.BackgroundEnergySpectrum;
            string previousPath = this.ActiveResultData.BackgroundSpectrumPathname;
            if (this.ActiveResultData.BackgroundSpectrumPathname == "")
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = Properties.Resources.BackgroundSelectionDialogTitle;
                openFileDialog.Filter = this.ActiveResultData.BackgroundSpectrumFile + "|" + this.ActiveResultData.BackgroundSpectrumFile + "";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;
                if (openFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                this.ActiveResultData.BackgroundSpectrumPathname = openFileDialog.FileName;
            }
            DocumentManager.GetInstance().LoadBackgroundSpectrum(this.ActiveResultData);
            if (object.ReferenceEquals(this.ActiveResultData.BackgroundEnergySpectrum, previousBackground))
            {
                // Файл не загрузился (о причине уже сказал `LoadBackgroundSpectrum`):
                // документ остаётся с прежним фоном и прежним путём.
                this.ActiveResultData.BackgroundSpectrumPathname = previousPath;
                return;
            }
            // (`AMBER256`, решение Amber 20.10.2026 «Принимать везде») Отказа
            // «Несовместимые параметры каналов.» здесь больше нет: фон с другим
            // числом каналов `LoadBackgroundSpectrum` перекладывает по энергии
            // в шкалу спектра (`DocumentManager.AlignBackgroundChannels`).
            this.Dirty = true;
            this.UpdateEnergySpectrum();
        }

        void toolStripSaveButton_Click(object sender, EventArgs e)
        {
            if (!IsNamed)
            {
                DocumentManager.GetInstance().SaveDocumentWithName(this);
            } else
            {
                DocumentManager.GetInstance().SaveDocument(this);
            }
            
        }

        void toolStripSplitButton_Click(object sender, EventArgs e)
        {
            DocumentManager.GetInstance().SplitDocEnergySpectrum(this);
            // (`AMBER168`, 05.10.2026) UpdateSpectrum здесь НЕ ставится: это флаг
            // «идёт набор», снимают его только «Стоп» и уставка, и после «отделить
            // фон» он оставался навсегда — таймер окна писал активному спектру
            // EndTime = «сейчас» и перерисовывал его без конца. Разовую перерисовку
            // панелей дают разовые флаги ниже.
            this.UpdateMeasurementResult = true;
            this.UpdateDetectedPeaks = true;
            this.UpdateDoseRate = true;
            this.UpdateSpectrumList = true;
            this.UpdateEnergySpectrum();
        }

        void toolStripNumUpDownScale_ValueChanged(object sender, EventArgs e)
        {
            if (this.scaleEchoFromView)
            {
                return;
            }
            this.view.zoom(this.toolStripNumUpDownScale.NumericUpDownControl.Value);
        }

        void toolStripNumUpDownScale_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                this.view.zoom(this.toolStripNumUpDownScale.NumericUpDownControl.Value);
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x0600036A RID: 874 RVA: 0x00010C8C File Offset: 0x0000EE8C
        void setUpperThresholdHToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.SetUpperThreshold != null)
            {
                this.SetUpperThreshold(this, new EventArgs());
            }
        }

        // Token: 0x0600036B RID: 875 RVA: 0x00010CAC File Offset: 0x0000EEAC
        void DocEnergySpectrum_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] array = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string path in array)
                {
                    if (Path.GetExtension(path) != ".xml")
                    {
                        return;
                    }
                    if (!File.Exists(path))
                    {
                        return;
                    }
                }
                e.Effect = DragDropEffects.Copy;
            }
        }

        // Token: 0x0600036C RID: 876 RVA: 0x00010D34 File Offset: 0x0000EF34
        void DocEnergySpectrum_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] pathnames = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (this.AddSpectrumToDocument != null)
                {
                    this.AddSpectrumToDocument(this, new AddSpectrumToDocumentEventArgs(pathnames));
                    this.Dirty = true;
                }
            }
        }

        // Token: 0x0600036D RID: 877 RVA: 0x00010D94 File Offset: 0x0000EF94
        void DocEnergySpectrum_Activated(object sender, EventArgs e)
        {
        }

        // Token: 0x0600036E RID: 878 RVA: 0x00010D98 File Offset: 0x0000EF98
        void DocEnergySpectrum_Click(object sender, EventArgs e)
        {
        }

        // Token: 0x0600036F RID: 879 RVA: 0x00010D9C File Offset: 0x0000EF9C
        void DocEnergySpectrum_MouseUp(object sender, MouseEventArgs e)
        {
        }

        // Token: 0x04000149 RID: 329
        DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();

        // Token: 0x0400014B RID: 331
        ResultDataFile resultDataFile;

        // Token: 0x0400014C RID: 332
        int activeResultDataIndex;

        // Token: 0x0400014D RID: 333
        string filename;

        // Token: 0x0400014E RID: 334
        bool isNamed = true;

        // Token: 0x0400014F RID: 335
        bool dirty;

        // Token: 0x04000150 RID: 336
        EnergySpectrumView view;

        readonly FullSpectrumAnalysis.FsaAnalysisSession fsaSession =
            new FullSpectrumAnalysis.FsaAnalysisSession();

        // Token: 0x04000151 RID: 337
        bool updateMeasurementResult;

        // Token: 0x04000152 RID: 338
        bool updateSpectrum;

        bool updateDetectedPeaks = false;

        bool updateDoseRate = false;

        bool updateSpectrumList = false;

        // Token: 0x04000153 RID: 339
        bool activeEnergyCalibration;

        // Token: 0x04000154 RID: 340
        bool isActivating;

        bool autosave = false;

        // Новый документ наследует набор, выбранный сейчас: спектры одной пробы
        // открывают пачкой, и размечать каждый заново значило бы делать руками
        // то, что до сих пор получалось само. Дальше их выборы расходятся —
        // каждый живёт своим.
        NuclideSet selectedNuclideSet = NuclideDefinitionManager.GetInstance().ActiveSet;
    }

    /// <summary>Довод события <see cref="DocEnergySpectrum.FsaModeEntered"/>.</summary>
    public sealed class FsaModeEnteredEventArgs : EventArgs
    {
        public FsaModeEnteredEventArgs(bool explicitCommand)
        {
            this.Explicit = explicitCommand;
        }

        /// <summary>true — явная команда меню документа; false — циклическая кнопка.</summary>
        public bool Explicit { get; private set; }
    }
}
