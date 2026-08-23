using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x0200004F RID: 79
    public partial class DCPeakDetectionView : ToolWindow
    {
        // Token: 0x0600043D RID: 1085 RVA: 0x00014210 File Offset: 0x00012410
        public DCPeakDetectionView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();

            this.RefreshNuclideSets();
        }

        // Token: 0x0600043E RID: 1086 RVA: 0x0001423C File Offset: 0x0001243C
        public void ShowPeakDetectionResult()
        {
            this.FormLoading = true;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            // Набор нуклидов этого документа встаёт в список ПЕРВЫМ делом:
            // ниже метод не раз выходит досрочно — нет результата, нет
            // конфигурации прибора, нет калибровки ПШПВ, — а список наборов
            // виден всегда, и чужой выбор в нём читался бы как свой (R9).
            this.ShowNuclideSetOf(activeDocument);
            if (activeDocument == null || activeDocument.ActiveResultData == null)
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            DeviceConfigInfo deviceConfigInfo = activeResultData.DeviceConfig;
            if (deviceConfigInfo == null)
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                return;
            }
            if (deviceConfigInfo.Guid == null)
            {
                List<DeviceConfigInfo> deviceConfigInfos = DeviceConfigManager.GetInstance().DeviceConfigList;
                DateTime maxTime = new DateTime();
                DeviceConfigInfo lastConfigInfo = null;
                foreach (DeviceConfigInfo devinfo in deviceConfigInfos)
                {
                    if (lastConfigInfo == null)
                    {
                        lastConfigInfo = devinfo;
                        maxTime = devinfo.LastUpdated;
                    } else
                    {
                        if (devinfo.LastUpdated > maxTime)
                        {
                            lastConfigInfo=devinfo;
                            maxTime=devinfo.LastUpdated;
                        }
                    }
                }
                deviceConfigInfo = lastConfigInfo;
                if (deviceConfigInfo != null && deviceConfigInfo.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fallbackConfig)
                {
                    activeResultData.DeviceConfig.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fallbackConfig.Clone();
                }
            }
            // Give the ResultData its OWN config copy only when it has none. The old code
            // unconditionally assigned the device config's object (without Clone) on every
            // refresh, so SNR/tolerance became shared between documents and edits mutated
            // the global device config.
            if (!(activeResultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && deviceConfigInfo != null
                && deviceConfigInfo.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig deviceMethodConfig)
            {
                activeResultData.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)deviceMethodConfig.Clone();
            }
            if (!(activeResultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig))
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                return;
            }
            this.numericUpDown1.Minimum = 1;
            this.numericUpDown1.Maximum = 10000;
            this.numericUpDown1.Increment = 1;
            // Don't overwrite a value the user is editing: this method runs every 2 s
            // from the main timer during recording and used to reset the field.
            if (!this.numericUpDown1.Focused)
            {
                decimal minSnr = (decimal)fwhmPeakDetectionMethodConfig.Min_SNR;
                if (this.numericUpDown1.Value != minSnr)
                {
                    this.numericUpDown1.Value = minSnr;
                }
            }

            this.numericUpDown3.Minimum = 0;
            this.numericUpDown3.Maximum = 100;
            this.numericUpDown3.Increment = 0.1m;
            if (!this.numericUpDown3.Focused)
            {
                decimal tolerance = (decimal)fwhmPeakDetectionMethodConfig.Tolerance;
                if (this.numericUpDown3.Value != tolerance)
                {
                    this.numericUpDown3.Value = tolerance;
                }
            }

            // Галка ставится под поднятым FormLoading, как и оба числа выше:
            // подстановка кодом выбором человека не является и ни пересчёта,
            // ни перерисовки за собой не тянет. Фокус здесь проверять нечего —
            // галку не «редактируют», а щёлкают, и присвоение того же значения
            // события не поднимает.
            this.checkBoxDbLookups.Checked = fwhmPeakDetectionMethodConfig.DbLookupsForFsa;
            this.checkBoxEquilibrium.Checked = fwhmPeakDetectionMethodConfig.ChainEquilibrium;
            this.UpdateEquilibriumEnabled();

            this.FormLoading = false;
            this.UpdatePeakDetectionResult();
            this.RefreshTable();
        }

        public async void UpdatePeakDetectionResult()
        {
            if (isProcessing)
            {
                // Don't silently drop the request ("drop, not queue"): re-run once the
                // current detection finishes, so e.g. an SNR change made mid-run is not lost.
                refreshPending = true;
                return;
            }
            isProcessing = true;

            try
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                if (activeDocument == null)
                {
                    return;
                }
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fWHMConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                if (activeResultData.FwhmCalibration == null)
                {
                    // No calibration - nothing to detect. This used to throw
                    // NotImplementedException, silently swallowed by the catch below.
                    return;
                }
                // Snapshot the detection inputs on the UI thread before handing off to the
                // background Task.Run. DetectPeak used to run against the live ResultData: the
                // device loop mutates EnergySpectrum.Spectrum in place (via originalContext.Post
                // on the UI thread) and the config fields can change mid-run, so the background
                // thread could observe a half-written spectrum or a torn config. EnergySpectrum
                // /config Clone() deep-copy their arrays, so the snapshot is fully detached.
                ResultData snapshot = new ResultData
                {
                    EnergySpectrum = activeResultData.EnergySpectrum.Clone(),
                    BackgroundEnergySpectrum = activeResultData.BackgroundEnergySpectrum != null
                        ? activeResultData.BackgroundEnergySpectrum.Clone()
                        : null,
                    PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fWHMConfig.Clone(),
                    // Clone the calibration too (guarded like ResultData.Clone): passing the live
                    // reference let the background detector see a mid-edit FWHM calibration if the
                    // user changed it via the UI during Task.Run.
                    FwhmCalibration = activeResultData.FwhmCalibration != null
                        ? activeResultData.FwhmCalibration.Clone()
                        : null
                };
                BackgroundMode bgMode = activeDocument.EnergySpectrumView.BackgroundMode;
                SmoothingMethod smoothMethod = activeDocument.EnergySpectrumView.SmoothingMethod;

                // Список нуклидов тоже снимается здесь, на UI-потоке:
                // NuclideSetForm правит и сортирует его in-place, а перечисление
                // живого списка из Task.Run валилось бы на "Collection was
                // modified" — прямо в catch ниже, унося с собой всю детекцию.
                List<NuclideDefinition> nuclideDefinitions =
                    new List<NuclideDefinition>(NuclideDefinitionManager.GetInstance().NuclideDefinitions);

                PeakDetector peakDetector = new PeakDetector();
                List<Peak> peaks = await Task.Run(() => peakDetector.DetectPeak(snapshot,
                    bgMode,
                    smoothMethod,
                    this.selectedNuclideSet,
                    nuclideDefinitions));
                activeResultData.DetectedPeaks = new List<Peak>(peaks);
                // Refresh only if the user is still on the same document: RefreshTable()
                // reads the CURRENT ActiveResultData and used to show peaks of a foreign
                // spectrum after switching documents mid-detection.
                if (this.mainForm.ActiveDocument == activeDocument)
                {
                    RefreshTable();
                }
            }
            catch (Exception ex)
            {
                // Don't swallow silently - at least leave a trace.
                System.Diagnostics.Trace.WriteLine("Peak detection failed: " + ex.Message);
            }
            finally
            {
                isProcessing = false;
                if (refreshPending)
                {
                    refreshPending = false;
                    UpdatePeakDetectionResult();
                }
            }
        }

        public void RefreshTable()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || activeDocument.ActiveResultData == null)
            {
                this.tableModel1.Rows.Clear();
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (activeResultData.DetectedPeaks == null)
            {
                this.tableModel1.Rows.Clear();
                return;
            }
            List<Peak> peaks = new List<Peak>(activeResultData.DetectedPeaks);
            if (peaks != null)
            {
                // if peaks exist, update table
                EnergyCalibration energyCalibration = activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration;
                if (energyCalibration == null)
                {
                    this.tableModel1.Rows.Clear();
                    return;
                }

                this.tableModel1.Rows.Clear();
                foreach (Peak peak in peaks)
                {
                    Row row = new Row();
                    string text = Resources.UnknownNuclide;
                    string text2 = "";
                    if (peak.Nuclide != null)
                    {
                        text = peak.Nuclide.Name;
                        if (peak.Nuclide.Energy > 0.0)
                        {
                            double num = peak.Energy - peak.Nuclide.Energy;
                            double num2 = (peak.Energy - peak.Nuclide.Energy) / peak.Nuclide.Energy * 100.0;
                            text2 = num.ToString("f2") + " (" + num2.ToString("f2") + "%)";
                        }
                    }
                    int snr = (int)peak.SNR;
                    row.Cells.Add(new Cell(text));
                    row.Cells.Add(new Cell(peak.Energy.ToString("f2"), Math.Round(peak.Energy, 2)));
                    row.Cells.Add(new Cell(text2));
                    row.Cells.Add(new Cell(peak.Channel.ToString(), peak.Channel));
                    row.Cells.Add(new Cell(snr.ToString(), snr));

                    double leftEnergy = energyCalibration.ChannelToEnergy(peak.Channel - peak.FWHM / 2.0);
                    double rightEnergy = energyCalibration.ChannelToEnergy(peak.Channel + peak.FWHM / 2.0);
                    double resolution = 100.0 * (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)peak.Channel);

                    row.Cells.Add(new Cell(peak.FWHM.ToString("f0") + ", " + resolution.ToString("f1") + "% ±" + peak.FWHM_DELTA.ToString("f1")));
                    this.tableModel1.Rows.Add(row);
                }
                activeDocument.RefreshView();
                //this.table1.AutoResizeColumnWidths();
            }

        }

        public void RefreshNuclideSets()
        {
            // Выбор запоминается ДО очистки списка: Items.Clear() сбрасывает
            // SelectedIndex в -1 и поднимает SelectedIndexChanged. Список
            // обновляют по закрытии редактора наборов, и набор для поиска молча
            // слетал на «все нуклиды» после каждого захода туда.
            NuclideSet wanted = this.selectedNuclideSet;

            // Перестройка списка — не выбор человека, и обработчик её больше не
            // видит вовсе. Одного восстановления в конце теперь мало: тот же
            // обработчик пишет выбор в активный ДОКУМЕНТ, и стёртую там память
            // никакая строка ниже не вернула бы (R9).
            this.updatingNuclideSets = true;
            try
            {
                this.FillNuclideSets(wanted);
            }
            finally
            {
                this.updatingNuclideSets = false;
            }

            // Удалённый набор обязан забыть и ДОКУМЕНТ. Иначе он держит ссылку
            // на то, чего больше нет, до ближайшего обновления панели — а
            // список к этому времени уже показывает «все нуклиды».
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                activeDocument.SelectedNuclideSet = this.selectedNuclideSet;
            }
        }

        /// <summary>
        /// Поставить в список набор ЭТОГО документа и сделать его текущим.
        /// Зовётся из <see cref="ShowPeakDetectionResult"/>, то есть на каждой
        /// смене активного документа и на каждом обновлении панели по таймеру,
        /// — поэтому молчит: подстановка выбора кодом не считается выбором
        /// человека и не запускает ни поиска пиков, ни перерисовки.
        ///
        /// Набор могли удалить, пока документ лежал в фоне. Тогда выбор честно
        /// возвращается к «всем нуклидам» — и в самом документе тоже, а не
        /// только в списке.
        /// </summary>
        void ShowNuclideSetOf(DocEnergySpectrum document)
        {
            if (document == null)
            {
                return;
            }

            // Выбор документа снимается ПЕРВЫМ: перестройка списка ниже
            // приводит документ в согласие со списком, то есть кладёт в это
            // поле выбор ПРЕДЫДУЩЕГО документа. Восстанавливать надо снятое, а
            // не то, что осталось после неё.
            NuclideSet wanted = document.SelectedNuclideSet;

            // Список мог отстать от самих наборов: правят их в соседнем окне, а
            // перечитывается он по его закрытии. Строка «все нуклиды» делает
            // список на единицу длиннее — расхождение видно по счёту.
            if (this.comboBoxNuclSet.Items.Count != this.nuclideManager.NuclideSets.Count + 1)
            {
                this.RefreshNuclideSets();
            }

            int index = wanted == null ? -1 : this.nuclideManager.NuclideSets.IndexOf(wanted);
            document.SelectedNuclideSet = index >= 0 ? wanted : null;

            this.updatingNuclideSets = true;
            try
            {
                this.comboBoxNuclSet.SelectedIndex = index >= 0 ? index + 1 : 0;
            }
            finally
            {
                this.updatingNuclideSets = false;
            }

            // Текущий набор — набор активного документа: по нему ищет пики
            // панель и рисует линии интенсивностей график.
            this.selectedNuclideSet = document.SelectedNuclideSet;
        }

        /// <summary>
        /// Собственно перестройка списка. Вынесена отдельным методом, чтобы
        /// флаг «список меняет код» снимался ровно на выходе, каким бы он ни
        /// был, — и чтобы у <c>try</c> было одно тело, а не половина метода.
        /// </summary>
        void FillNuclideSets(NuclideSet wanted)
        {
            this.comboBoxNuclSet.Items.Clear();
            // Строка берётся из ресурсов: русский перевод для неё лежал в
            // `DCPeakDetectionView.ru.resx` с 2024 года, но читателя у него не
            // было — поле ниже объявлялось и НИКОГДА не присваивалось, и в
            // русском окне всегда стояло английское «--- All Nuclides ---»
            // (W18, 12.08.2026). Ключ переехал в `Properties/Resources`, где
            // лежат все строки, нужные коду.
            string allNuclidesText = Properties.Resources.NuclideSetAllNuclides;
            if (string.IsNullOrEmpty(allNuclidesText))
            {
                allNuclidesText = "--- All Nuclides ---";
            }

            this.comboBoxNuclSet.Items.Add(allNuclidesText);
            foreach (NuclideSet set in this.nuclideManager.NuclideSets)
            {
                this.comboBoxNuclSet.Items.Add(set.Name);
            }

            // Набор мог быть удалён в редакторе — тогда IndexOf даёт -1, и
            // выбор честно возвращается к «всем нуклидам», а не остаётся
            // указывать на то, чего больше нет.
            int index = wanted == null ? -1 : this.nuclideManager.NuclideSets.IndexOf(wanted);
            this.comboBoxNuclSet.SelectedIndex = index >= 0 ? index + 1 : 0;
            // Обработчик выбора сюда не доходит (список меняет код), поэтому
            // поле выставляется руками — и заодно чистится, если набор удалили.
            this.selectedNuclideSet = index >= 0 ? wanted : null;
        }

        // Token: 0x06000440 RID: 1088 RVA: 0x00014468 File Offset: 0x00012668
        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Min_SNR = (double)((int)this.numericUpDown1.Value);
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x06000442 RID: 1090 RVA: 0x000145BC File Offset: 0x000127BC
        void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Tolerance = (double)this.numericUpDown3.Value;
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        /// <summary>
        /// «Состав FSA из баз» (`S57`): библиотеку полноспектрального разбора
        /// собирать не по подписям найденных пиков, а по цепочке родителя из
        /// `nucdb`/`matdb`.
        ///
        /// Поиска пиков галка НЕ КАСАЕТСЯ — ни одного пика от неё не появится и
        /// не исчезнет, — поэтому детекция здесь не перезапускается. Касается
        /// она разложения, и только если оно сейчас на экране: в остальных
        /// режимах фона считать нечего, а включённое позже разложение возьмёт
        /// новое значение само (галка входит в отпечаток `FsaOverlay`).
        /// </summary>
        void checkBoxDbLookups_CheckedChanged(object sender, EventArgs e)
        {
            this.UpdateEquilibriumEnabled();
            this.ApplyFsaFlag((config, view) => config.DbLookupsForFsa = view.checkBoxDbLookups.Checked);
        }

        /// <summary>
        /// Подсказка у погашенной галки. Заводится кодом, а не конструктором
        /// формы: одна подсказка на одну галку — не повод трогать `.Designer.cs`
        /// и обе `.resx` конструктора (`W21` — про то, чем это кончается).
        /// </summary>
        ToolTip fsaToolTip;

        /// <summary>
        /// «Равновесие» доступно ТОЛЬКО при выводе состава из баз (`S77`,
        /// решение Amber 23.08.2026).
        ///
        /// ⛔ Причина не в обвязке, а в существе: связывать ряд можно там, где
        /// ряд ЕСТЬ. Состав из баз (`FsaSampleLibrary`) собирает его обходом
        /// `nucdb.decay_chain`; прежний путь (`FsaLibrary.BuildFromPeaks`)
        /// строит компоненты по ПОДПИСЯМ найденных пиков, и структуры ряда там
        /// нет вовсе. До этой правки в поставке обе галки стояли ровно в тех
        /// положениях, при которых видимая не делала НИЧЕГО: «Равновесие»
        /// включено умолчанием, вывод из баз — выключен.
        ///
        /// ⚠ Само ЗНАЧЕНИЕ галки при этом не трогается и в конфиг не пишется:
        /// погашенная галка помнит свой выбор и оживает вместе с соседней.
        /// Гасить и обнулять — разные вещи, и второе потеряло бы настройку
        /// человека молча.
        /// </summary>
        void UpdateEquilibriumEnabled()
        {
            bool available = this.checkBoxDbLookups.Checked;
            this.checkBoxEquilibrium.Enabled = available;
            if (this.fsaToolTip == null)
            {
                this.fsaToolTip = new ToolTip();
            }

            this.fsaToolTip.SetToolTip(this.checkBoxEquilibrium,
                                       available ? string.Empty : Resources.FSAEquilibriumNeedsDbLookups);
        }

        /// <summary>
        /// «Равновесие» (`S70`): ряд идёт в разбор ОДНОЙ колонкой с одной
        /// свободной амплитудой, относительные веса членов закреплены
        /// ветвлением. Умолчание — ВКЛЮЧЕНО, в отличие от соседней галки.
        ///
        /// Поиска пиков не касается ровно так же, как и соседняя, — меняется
        /// состав библиотеки разбора, а не найденные пики.
        /// </summary>
        void checkBoxEquilibrium_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyFsaFlag((config, view) => config.ChainEquilibrium = view.checkBoxEquilibrium.Checked);
        }

        /// <summary>
        /// Обе галки разбора устроены одинаково, и обработчик у них общий: путь
        /// «в копию спектра, в умолчание прибора, на диск, перечитать вид»
        /// длинный, и написанный дважды он однажды разошёлся бы.
        ///
        /// ⛔ Пишется В ДВА МЕСТА (решение Amber 18.08.2026). В копию СПЕКТРА —
        /// иначе нажатие не влияет на то, что человек сейчас видит. В умолчание
        /// ПРИБОРА и на диск — иначе положение галки не переживает ни следующий
        /// спектр, ни перезапуск; до `S70` не делалось ни того, ни другого, и в
        /// этом была вся строка.
        ///
        /// ⛔ Прибор сохраняется ТИХО
        /// (<see cref="DeviceConfigManager.SaveConfigQuiet"/>): обычное
        /// сохранение рассылает событие, а по нему настройки прибора
        /// переносятся во ВСЕ открытые спектры этого прибора. Решение то же:
        /// «умолчание прибора меняем, а уже сохранённую копию спектра не
        /// трогаем» — соседние документы остаются при своём.
        /// </summary>
        void ApplyFsaFlag(Action<FWHMPeakDetectionMethodConfig, DCPeakDetectionView> set)
        {
            if (this.FormLoading)
            {
                return;
            }

            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || activeDocument.ActiveResultData == null)
            {
                return;
            }

            if (!(activeDocument.ActiveResultData.PeakDetectionMethodConfig
                    is FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig))
            {
                return;
            }

            set(fwhmPeakDetectionMethodConfig, this);
            this.SaveFsaFlagsToDevice(activeDocument.ActiveResultData, fwhmPeakDetectionMethodConfig);

            // Режим фона смотрим у ВИДА этого документа, а не у панели: панель
            // одна, документов много, и переключение «Show FSA» живёт там (R9).
            if (activeDocument.EnergySpectrumView == null
                || activeDocument.EnergySpectrumView.BackgroundMode != BackgroundMode.ShowFSA)
            {
                return;
            }

            // Перечитать заново. Отпечаток разложения содержит обе галки,
            // поэтому подготовка данных вида увидит, что готовый результат
            // устарел, и закажет счёт; сам счёт идёт в фоне и окна не держит.
            activeDocument.RefreshView();
        }

        /// <summary>
        /// Обе галки — в умолчание прибора и на диск. Прибор берётся из
        /// менеджера по Guid: именно ту запись читает
        /// <see cref="FWHMPeakDetectionMethodConfig.AdoptFrom"/> при открытии
        /// следующего спектра, и правка её копии никуда бы не дошла.
        /// </summary>
        void SaveFsaFlagsToDevice(ResultData resultData, FWHMPeakDetectionMethodConfig source)
        {
            if (resultData.DeviceConfigReference == null
                || string.IsNullOrEmpty(resultData.DeviceConfigReference.Guid))
            {
                return;
            }

            DeviceConfigManager manager = DeviceConfigManager.GetInstance();
            DeviceConfigInfo device;
            if (!manager.DeviceConfigMap.TryGetValue(resultData.DeviceConfigReference.Guid, out device)
                || device == null
                || !(device.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig devicePeak))
            {
                return;
            }

            if (devicePeak.DbLookupsForFsa == source.DbLookupsForFsa
                && devicePeak.ChainEquilibrium == source.ChainEquilibrium)
            {
                return;
            }

            devicePeak.DbLookupsForFsa = source.DbLookupsForFsa;
            devicePeak.ChainEquilibrium = source.ChainEquilibrium;
            manager.SaveConfigQuiet(device);
        }

        void ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            int channel = 0;
            decimal diff;
            decimal energy;
            foreach (Row row in this.table1.SelectedItems)
            {
                try
                {
                    channel = Convert.ToInt32(row.Cells[3].Text);
                    if (row.Cells[2].Text.Length > 1)
                    {
                        diff = Convert.ToDecimal(row.Cells[2].Text.Split(new string[] { " " }, StringSplitOptions.None)[0]);
                    } else
                    {
                        diff = 0;
                    }
                    energy = Convert.ToDecimal(row.Cells[1].Text) - diff;
                    if (this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum.Length > channel)
                    {
                        this.mainForm.addCalibration(channel, energy, this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum[channel]);
                    } else
                    {
                        throw new Exception(Resources.ERRCalibrationChannelExceed);
                    }
                    
                } catch (Exception ex)
                {
                    MessageBox.Show(String.Format(Resources.ERRAddCalibrationPoints, channel.ToString(), ex.Message), Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void ToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            decimal energy = Convert.ToDecimal(this.table1.SelectedItems[0].Cells[1].Text);
            this.mainForm.CallNucBaseSearch(energy);
        }

        void ToolStripMenuItem1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.table1.SelectedItems.Length == 0)
            {
                this.toolStripMenuItem1.Enabled = false;
                this.toolStripMenuItem2.Enabled = false;
            } else
            {
                this.toolStripMenuItem1.Enabled = true;
                this.toolStripMenuItem2.Enabled = true;
            }
        }


        // Token: 0x06000443 RID: 1091 RVA: 0x00014614 File Offset: 0x00012814
        void button1_Click(object sender, EventArgs e)
        {
            this.mainForm.ShowNuclideDefinitionForm();
        }

        private void comboBoxNuclSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Список перестраивает или подставляет КОД — при перечитывании
            // наборов и при переходе на другой документ. Выбором человека это
            // не является: ни пересчёта, ни записи в документ за собой не
            // тянет, а поля выставит тот, кто эту подстановку затеял.
            if (this.updatingNuclideSets)
            {
                return;
            }

            if (this.comboBoxNuclSet.SelectedIndex > 0)
            {
                this.selectedNuclideSet = this.nuclideManager.NuclideSets[this.comboBoxNuclSet.SelectedIndex - 1];
            }
            else
            {
                this.selectedNuclideSet = null;
            }

            // Выбор принадлежит ДОКУМЕНТУ и запоминается за ним: вернувшись к
            // этому спектру, человек застанет свой набор, а не тот, что выбран
            // для соседнего (R9).
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                activeDocument.SelectedNuclideSet = this.selectedNuclideSet;
            }

            this.UpdatePeakDetectionResult();

            // От выбора зависит не только таблица пиков, но и картинка: линии
            // интенсивностей рисуются по выбранному набору. Поиск пиков идёт в
            // фоне и перерисует график когда-нибудь потом (а при пустом
            // документе не перерисует вовсе), линиям же ждать нечего.
            if (activeDocument != null)
            {
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x040001B3 RID: 435
        MainForm mainForm;

        // Token: 0x040001B4 RID: 436
        DocumentManager documentManager = DocumentManager.GetInstance();

        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

        bool FormLoading = false;

        /// <summary>
        /// Набор для поиска пиков — АКТИВНОГО документа. Своего поля у панели
        /// больше нет: тот же выбор решает, чьи линии интенсивностей рисовать
        /// на графике, а график до панели не дотягивается. Второе поле рядом
        /// рано или поздно разошлось бы с этим, поэтому оно одно —
        /// <see cref="NuclideDefinitionManager.ActiveSet"/>.
        ///
        /// Хранится выбор при этом у документа
        /// (<see cref="DocEnergySpectrum.SelectedNuclideSet"/>), а здесь стоит
        /// выбор того из них, который сейчас на экране: панель одна, документов
        /// много (R9). Держит их в согласии <see cref="ShowNuclideSetOf"/>.
        /// </summary>
        private NuclideSet selectedNuclideSet
        {
            get { return this.nuclideManager.ActiveSet; }
            set { this.nuclideManager.ActiveSet = value; }
        }

        /// <summary>
        /// Список наборов сейчас перестраивает или подставляет код — событие
        /// <c>SelectedIndexChanged</c> в этот момент не значит выбора человека.
        /// </summary>
        private bool updatingNuclideSets;

        private bool isProcessing = false;

        private bool refreshPending = false;
    }
}
