using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor
{
    public class ResultData
    {
        [XmlIgnore]
        public ResultDataStatus ResultDataStatus
        {
            get
            {
                return this.resultDataStatus;
            }
            set
            {
                this.resultDataStatus = value;
            }
        }

        [XmlIgnore]
        public MeasurementController MeasurementController
        {
            get
            {
                return this.measurementController;
            }
            set
            {
                this.measurementController = value;
            }
        }

        public SampleInfoData SampleInfo
        {
            get
            {
                return this.sampleInfo;
            }
            set
            {
                this.sampleInfo = value;
            }
        }

        [XmlIgnore]
        public DeviceConfigInfo DeviceConfig
        {
            get
            {
                return this.deviceConfig;
            }
            set
            {
                this.deviceConfig = value;
            }
        }

        public DeviceConfigReference DeviceConfigReference
        {
            get
            {
                return this.deviceConfigReference;
            }
            set
            {
                this.deviceConfigReference = value;
            }
        }

        /// <summary>
        /// Кривая эффективности, по которой считается активность ЭТОГО спектра.
        /// Пусто — не выбрана: тогда активность не считается, и об этом
        /// говорится, а не подставляется что попало.
        ///
        /// Хранится ПОЛНОЙ КОПИЕЙ, а не ссылкой, — в отличие от конфигурации
        /// прибора и набора зон рядом, которые ссылками и остались. Причина не
        /// в единообразии, а в том, что файл спектра отправляют другому
        /// человеку: конфигурации этого прибора у него нет вовсе, и по ссылке
        /// он не восстановит ни кривую, ни геометрию, в которой она получена.
        /// Ссылка тут молча превратилась бы в «эффективности нет».
        /// </summary>
        public EfficiencyConfigData Efficiency
        {
            get
            {
                return this.efficiency;
            }
            set
            {
                this.efficiency = value;
                this.AttachEmbeddedMatrix();
            }
        }

        /// <summary>
        /// (`AMBER202`, решения Amber 05.10.2026 «Внутри файла спектра
        /// (Рекомендую)», «Настройка, умолчание ВКЛ (Рекомендую)») МАТРИЦА
        /// ОТКЛИКА выбранной кривой — необязательным блоком сразу за
        /// <see cref="Efficiency"/>: файл спектра уходит другому человеку, и
        /// без матрицы разбор у него шёл «без матрицы», хотя кривая с
        /// геометрией приезжала.
        ///
        /// ЗАПИСЬ — вычисляемая: блок собирает
        /// <see cref="ResponseMatrixStore.EmbedFor"/> из склада (или
        /// пересылает приехавший) в момент сериализации. Так блок получают ВСЕ
        /// пути записи спектра — «Сохранить», автосохранение и три экспорта
        /// `MainForm` (вычтенный спектр, фон, выбранные спектры), — без правки
        /// каждого. Выключатель — общая настройка
        /// <see cref="GlobalConfigInfo.SaveResponseMatrixInSpectrum"/>
        /// (<see cref="ShouldSerializeEmbeddedResponseMatrix"/>).
        ///
        /// ЧТЕНИЕ — блок прикрепляется к кривой файла (позже выбранной — при
        /// том же Guid; <see cref="EfficiencyConfigData.EmbeddedMatrix"/>, в памяти), и
        /// дальше его находят читатели склада общим путём
        /// <see cref="ResponseMatrixStore.Resolve"/>. Порядок элементов в файле
        /// не важен: прикрепляют оба сеттера.
        ///
        /// Почему блок здесь, а не полем <see cref="EfficiencyConfigData"/>:
        /// запись кривой сериализуется ещё и в конфигурацию прибора, и поле там
        /// потребовало бы второго выключателя «только в спектре»; у
        /// <c>ResultData</c> другого места записи, кроме файла спектра, нет.
        /// </summary>
        public EmbeddedResponseMatrix EmbeddedResponseMatrix
        {
            get
            {
                return ResponseMatrixStore.EmbedFor(this.efficiency);
            }
            set
            {
                this.embeddedResponseMatrixRead = value;
                this.AttachEmbeddedMatrix(true);
            }
        }

        /// <summary>
        /// (`AMBER202`) Писать ли блок матрицы — общая настройка; без неё (или
        /// при недоступной конфигурации) — умолчание «писать».
        /// </summary>
        public bool ShouldSerializeEmbeddedResponseMatrix()
        {
            return GlobalConfigInfo.SaveResponseMatrixInSpectrumNow();
        }

        /// <summary>
        /// Прикрепить прочитанный блок к кривой. При чтении самого блока
        /// (<paramref name="fromFile"/>) — к кривой ЭТОГО файла при любом Guid:
        /// блок с чужим Guid (правленый руками файл) тогда отвергается
        /// читателем СЛОВАМИ («от другой кривой»), а не молча. Новая кривая,
        /// выбранная позже на панели, получает блок только при своём Guid —
        /// иначе отказ «от другой кривой» висел бы на законно другой кривой.
        /// </summary>
        void AttachEmbeddedMatrix(bool fromFile = false)
        {
            EmbeddedResponseMatrix block = this.embeddedResponseMatrixRead;
            if (block != null && this.efficiency != null && this.efficiency.EmbeddedMatrix == null
                && (fromFile
                    || string.Equals(block.EfficiencyGuid, this.efficiency.Guid, StringComparison.OrdinalIgnoreCase)))
            {
                this.efficiency.EmbeddedMatrix = block;
            }
        }

        /// <summary>
        /// Та же кривая, но как она пришла В ФАЙЛЕ, — ТОТ ЖЕ объект, что лежал
        /// в <see cref="Efficiency"/> сразу после чтения (и после сохранения:
        /// с этого момента в файле именно он).
        ///
        /// Заведена ради списка кривых в панели измерения. Без неё родную
        /// кривую спектра было не отличить: она либо совпадала по Guid с
        /// кривой прибора и показывалась ЕЁ именем (прибор кривую с тех пор
        /// переименовали или пересчитали), либо, стоило переключиться на
        /// другую, исчезала из списка вовсе — и вернуться к ней было нечем.
        ///
        /// Не сохраняется: в файле она и так есть, в поле Efficiency. Тождество
        /// ссылок здесь и есть признак «выбрана родная»: чужая приходит копией
        /// (см. DCControlPanel), и совпасть ссылкой ей не с чем.
        /// </summary>
        [XmlIgnore]
        public EfficiencyConfigData FileEfficiency
        {
            get
            {
                return this.fileEfficiency;
            }
            set
            {
                this.fileEfficiency = value;
            }
        }

        public string BackgroundSpectrumFile
        {
            get
            {
                return this.backgroundSpectrumFile;
            }
            set
            {
                if (value != null)
                {
                    this.backgroundSpectrumFile = string.Join("", value.Split(Path.GetInvalidFileNameChars()));
                }
                else
                {
                    this.backgroundSpectrumFile = value;
                }
                
            }
        }

        [XmlIgnore]
        public string BackgroundSpectrumPathname
        {
            get
            {
                return this.backgroundSpectrumPathname;
            }
            set
            {
                this.backgroundSpectrumPathname = value;
            }
        }

        [XmlIgnore]
        public string DetectorFeature
        {
            get
            {
                return this.detectorFeature;
            }
            set
            {
                this.detectorFeature = value;
            }
        }

        /// <summary>
        /// ⛔ ЕДИНСТВЕННОЕ ЗНАЧЕНИЕ «ВРЕМЯ НАЧАЛА НАБОРА НЕИЗВЕСТНО» (`A207`,
        /// решение 06.09.2026). До него соглашений об одном положении было ДВА:
        /// двери импорта N42 (<c>N42.Util</c>) подставляли «сейчас», дверь
        /// SpecUtils (<c>DocumentManager.ImportDocumentSpecUtils</c>) —
        /// 1970-01-01 00:00:03.600 (<c>ms = (ms == 0) ? 3600 : ms</c>), и человек
        /// получал РАЗНУЮ дату на одном и том же файле в зависимости от того,
        /// каким пунктом меню его открыл.
        ///
        /// ⛔ ПОЧЕМУ ЭПОХА, А НЕ «СЕЙЧАС». Выбор не о вкусе: «сейчас» на спектре
        /// двухлетней давности НЕОТЛИЧИМО от настоящей даты — оно уезжает на
        /// вкладку пробы, в отчёт и в выгруженный файл, где выглядит как
        /// измеренное. 1970-01-01 не спутать ни с чем, и человек, увидевший его,
        /// не примет выдумку за данные файла. Голос (`A207` говорит один раз на
        /// файл) объясняет эту дату при импорте, но живёт он ровно один показ, а
        /// дата остаётся в документе навсегда — поэтому узнаваемым обязано быть
        /// САМО значение, а не только сообщение.
        ///
        /// ⚠ Почему именно эпоха Unix, а не <c>DateTime.MinValue</c>: вкладка
        /// пробы кладёт это поле в <c>DateTimePicker</c>
        /// (<c>DCSampleInfoView.cs:31</c>), у которого нижний предел
        /// <c>DateTimePicker.MinimumDateTime</c> = 1753-01-01, — 0001-01-01
        /// уронил бы показ <c>ArgumentOutOfRangeException</c>. Эпоха же лежит в
        /// пределах и уже есть в дереве: её и отдавал разбор SpecUtils.
        ///
        /// <c>Kind</c> = <c>Unspecified</c> — ровно то, что даёт
        /// <c>DateTimeOffset.FromUnixTimeMilliseconds(0).DateTime</c>, то есть
        /// значение не меняет вида у соседних полей времени.
        /// </summary>
        public static readonly DateTime UnknownStartTime =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        public DateTime StartTime
        {
            get
            {
                return this.startTime;
            }
            set
            {
                this.startTime = value;
            }
        }

        public DateTime EndTime
        {
            get
            {
                return this.endTime;
            }
            set
            {
                this.endTime = value;
            }
        }

        /// <summary>
        /// ⛔ (П263, 14.10.2026) ПРИЗНАКИ «ВРЕМЯ БЫЛО В ФАЙЛЕ» — соглашение
        /// <c>XmlSerializer</c> о членах <c>…Specified</c>: при чтении сериализатор ставит
        /// их в true, только если элемент встретился; при записи геттер всегда true, и
        /// оба элемента пишутся как прежде (формат файла не меняется).
        ///
        /// Зачем. Поля <see cref="StartTime"/>/<see cref="EndTime"/> по умолчанию —
        /// <c>DateTime.Now</c>, и файл БЕЗ этих элементов открывался с моментом
        /// открытия вместо времени набора, молча. Такие файлы есть в жизни, их пишут
        /// чужие программы и конвертеры: замер `SpectrumFilesProbeP263` на спектрах
        /// Amber — 149 из 683 без &lt;EndTime&gt; (104 поверочных ЛСРМ: начало есть,
        /// конца нет), 45 без &lt;StartTime&gt; и времени пробы (выгрузки RadiaCode и
        /// др.); в корпусе — 89 без &lt;EndTime&gt;. Конец набора «сейчас» при начале
        /// двухлетней давности — это два года «набора» в поправке на распад
        /// (<c>MeasurementResultManager.DecayToSamplingFactor</c>). Чинит
        /// <c>DocumentManager.FillAbsentTimes</c> по этим признакам.
        /// </summary>
        [XmlIgnore]
        public bool StartTimeSpecified
        {
            get
            {
                return true;
            }
            set
            {
                this.startTimeInFile = value;
            }
        }

        /// <summary>(П263) См. <see cref="StartTimeSpecified"/>.</summary>
        [XmlIgnore]
        public bool EndTimeSpecified
        {
            get
            {
                return true;
            }
            set
            {
                this.endTimeInFile = value;
            }
        }

        /// <summary>(П263) Время начала набора прочитано из файла.</summary>
        [XmlIgnore]
        public bool StartTimeInFile
        {
            get
            {
                return this.startTimeInFile;
            }
        }

        /// <summary>(П263) Время конца набора прочитано из файла.</summary>
        [XmlIgnore]
        public bool EndTimeInFile
        {
            get
            {
                return this.endTimeInFile;
            }
        }

        public int PresetTime
        {
            get
            {
                return this.presetTime;
            }
            set
            {
                this.presetTime = value;
            }
        }

        public EnergySpectrum EnergySpectrum
        {
            get
            {
                return this.energySpectrum;
            }
            set
            {
                this.energySpectrum = value;
            }
        }

        public EnergySpectrum BackgroundEnergySpectrum
        {
            get
            {
                return this.backgroundEnergySpectrum;
            }
            set
            {
                this.backgroundEnergySpectrum = value;
            }
        }

        public bool Visible
        {
            get
            {
                return this.visible;
            }
            set
            {
                this.visible = value;
            }
        }

        public PulseCollection PulseCollection
        {
            get
            {
                return this.pulseCollection;
            }
            set
            {
                this.pulseCollection = value;
            }
        }

        [XmlIgnore]
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
            }
        }

        [XmlIgnore]
        public bool Selected
        {
            get
            {
                return this.selected;
            }
            set
            {
                this.selected = value;
            }
        }

        [XmlIgnore]
        public List<Peak> DetectedPeaks
        {
            get
            {
                return this.detectedPeaks;
            }
            set
            {
                this.detectedPeaks = value;
            }
        }

        [XmlIgnore]
        public PeakDetectionMethodConfig PeakDetectionMethodConfig
        {
            get
            {
                return this.peakDetectionMethodConfig;
            }
            set
            {
                this.peakDetectionMethodConfig = value;
            }
        }

        [XmlIgnore]
        public List<CalibrationPoint> CalibrationPoints
        {
            get
            {
                return this.calibrationPoints;
            }
            set
            {
                this.calibrationPoints = value;
            }
        }

        [XmlIgnore]
        public List<Peak> CalibrationPeaks
        {
            get
            {
                return this.calibrationPeaks;
            }
            set
            {
                this.calibrationPeaks = value;
            }
        }

        [XmlIgnore]
        public List<CountRate> CountRates
        {
            get
            {
                return this.countRates;
            }
            set
            {
                this.countRates = value;
            }
        }

        [XmlElement(typeof(SimpleSqrtFwhmCalibration))]
        [XmlElement(typeof(SqrtFwhmCalibration))]
        [XmlElement(typeof(PowerFwhmCalibration))]
        public FwhmCalibration FwhmCalibration { get => fwhmCalibration; set => fwhmCalibration = value; }

        /// <summary>
        /// ⛔ (`AMBER276`, 29.10.2026) ТАБЛИЦА ЕДИНОЙ КАЛИБРОВКИ (энергия и FWHM) — ОДИН
        /// СПИСОК. Amber, консоль, дословно: «Таблица калибровки - единая. В файл спектра
        /// помимо калибровки fwhm теперь попадает и калибровка по энергии.» и «Можно
        /// модифицировать секцию ФВХМ калибровки и туда добавить энергию.»
        ///
        /// Строки живут в опорах кривой FWHM (<see cref="FwhmCalibration"/>.<c>CalibrationPeaks</c>):
        /// так они попадают в файл ВНУТРИ секции FWHM, а элемент <c>EnergyCalibration</c>
        /// спектра не трогается вовсе — его читают другие программы. Линия калибровки по
        /// энергии отмечена <see cref="CalibrationPeak.EnergyLine"/>.
        ///
        /// У спектра БЕЗ кривой FWHM (умолчание по настройкам не строится, прибор снят
        /// сбросом импорта) строкам жить в секции негде, и они держатся в
        /// <see cref="PendingCalibrationLines"/> — в памяти, как прежде точки калибровки по
        /// энергии. Первая удачная подгонка FWHM заводит спектру кривую из этих строк и
        /// уносит их в неё.
        /// </summary>
        [XmlIgnore]
        public List<CalibrationPeak> CalibrationLines
        {
            get
            {
                if (this.fwhmCalibration != null && this.fwhmCalibration.CalibrationPeaks != null)
                {
                    return this.fwhmCalibration.CalibrationPeaks;
                }
                return this.pendingCalibrationLines;
            }
        }

        /// <summary>
        /// (`AMBER276`) Строки единой калибровки у спектра без кривой FWHM (см.
        /// <see cref="CalibrationLines"/>). В файл не пишутся: секции FWHM у такого спектра
        /// нет, а заводить её с пустой кривой нельзя — прежняя версия прочла бы кривую с
        /// нулевыми коэффициентами как настоящую.
        /// </summary>
        [XmlIgnore]
        public List<CalibrationPeak> PendingCalibrationLines
        {
            get
            {
                return this.pendingCalibrationLines;
            }
            set
            {
                this.pendingCalibrationLines = value ?? new List<CalibrationPeak>();
            }
        }

        /// <summary>
        /// ⛔ (`AMBER271`, 29.10.2026) КРИВИЗНА ТРАКТА, КОТОРОЙ РАЗБИРАЕТСЯ ЭТОТ
        /// СПЕКТР, 1/МэВ (смысл числа — у <see cref="DeviceConfigInfo.TractCurvature"/>).
        /// Amber, консоль, дословно: «Кривизну тракта вписывать в файл спектра для
        /// переносимости.» Решение вопросником того же дня, дословно: «Спектра,
        /// прибор — умолчание (Рекомендую)» — «Как кривая FWHM: у спектра своя. У
        /// старых файлов без неё берётся значение прибора — их числа не сдвинутся.
        /// В прибор — кнопкой «Сохранить калибровку в конфигурацию прибора».»
        ///
        /// До этой правки κ жила только в приборе, и файл, открытый на машине без
        /// того же прибора, разбирался FSA с κ = 0 — молча другим числом.
        ///
        /// ЧТЕНИЕ: своя κ спектра (<see cref="HasOwnTractCurvature"/>), а без неё —
        /// κ его прибора, живьём: старый файл, свежий набор и спектр, которому
        /// прибор только что назначен, разбираются ровно тем же числом, что до
        /// правки. Нечисло — нуль, как у <c>FsaAnalyzer.AdoptDevice</c>.
        ///
        /// ЗАПИСЬ — элементом <c>&lt;TractCurvature&gt;</c>, когда спектр несёт свою
        /// κ ИЛИ стоит на настоящем приборе (с Guid): файл уносит ту κ, которой
        /// его разбирали. Спектр без прибора (прибор не найден на этой машине,
        /// снят сбросом импорта, спектр — результат вычитания или обрезки) поля
        /// не получает: иначе файл чужого прибора, пересохранённый здесь, вернулся
        /// бы к хозяину с κ = 0 вместо его κ. Число пишет и читает
        /// <c>XmlSerializer</c> (<c>XmlConvert</c>: инвариантная культура, круг
        /// без потерь), запятой в файле не бывает ни при какой культуре потока.
        /// Нечисло в файле — как поля нет: спектр остаётся без своей κ.
        /// </summary>
        public double TractCurvature
        {
            get
            {
                double k = this.tractCurvatureOwn
                    ? this.tractCurvature
                    : (this.deviceConfig != null ? this.deviceConfig.TractCurvature : 0.0);
                return double.IsNaN(k) || double.IsInfinity(k) ? 0.0 : k;
            }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return;
                }
                this.tractCurvature = value;
                this.tractCurvatureOwn = true;
            }
        }

        /// <summary>
        /// (`AMBER271`) Писать ли <see cref="TractCurvature"/> — соглашение
        /// <c>XmlSerializer</c> о членах <c>…Specified</c> (как у
        /// <see cref="StartTimeSpecified"/>): своя κ или настоящий прибор. При
        /// чтении сериализатор ставит сюда true, если элемент был, — своя κ при этом
        /// уже отмечена сеттером значения, поэтому сеттер здесь пуст.
        /// </summary>
        [XmlIgnore]
        public bool TractCurvatureSpecified
        {
            get
            {
                return this.tractCurvatureOwn
                       || (this.deviceConfig != null && !string.IsNullOrEmpty(this.deviceConfig.Guid));
            }
            set
            {
            }
        }

        /// <summary>(`AMBER271`) Своя κ у спектра есть: пришла в файле или записана в файл (<see cref="KeepWrittenTractCurvature"/>).</summary>
        [XmlIgnore]
        public bool HasOwnTractCurvature
        {
            get
            {
                return this.tractCurvatureOwn;
            }
        }

        /// <summary>
        /// (`AMBER271`) Снять свою κ — спектр снова берёт κ своего прибора. Зовут
        /// те, кто ставит спектру калибровку ПРИБОРА: назначение прибора в панели
        /// измерения (<c>DCControlPanel.ApplyDeviceConfigChange</c>, энергетическая
        /// и FWHM-калибровки там же берутся у прибора) и сброс настройки импорта
        /// (<c>DocumentManager.ResetSpectrumConfig</c>, прибор снят вместе с
        /// калибровкой).
        /// </summary>
        public void ForgetTractCurvature()
        {
            this.tractCurvatureOwn = false;
            this.tractCurvature = 0.0;
        }

        /// <summary>
        /// (`AMBER271`) Спектр записан в СВОЙ файл — κ, которую файл унёс, теперь
        /// своя (как кривая эффективности после записи, <c>DocumentManager.PrepareEfficiency</c>):
        /// иначе правка κ в окне прибора двигала бы разбор на экране, а файл и
        /// повторное открытие держали бы прежнюю — экран и диск разошлись бы
        /// молча. Зовёт <c>DocumentManager.SaveDocument</c> после удачной записи.
        /// </summary>
        public void KeepWrittenTractCurvature()
        {
            if (!this.tractCurvatureOwn && this.TractCurvatureSpecified)
            {
                this.tractCurvature = this.TractCurvature;
                this.tractCurvatureOwn = true;
            }
        }

        /// <summary>
        /// ПРИБОР У СПЕКТРА СНЯТ СБРОСОМ НАСТРОЙКИ, И УМОЛЧАНИЕ КРИВОЙ
        /// РАЗРЕШЕНИЯ ПО НЕМУ НЕ СТРОИТСЯ (`A260`, решение Amber
        /// 06.09.2026: «Снять кривую вместе с прибором»).
        ///
        /// Ставится <c>DocumentManager.ResetSpectrumConfig</c> — сбросом,
        /// которым обе двери импорта встречают файл с другим числом каналов
        /// (и любой файл при настройке «импортировать с пустой конфигурацией»).
        /// Читается <c>DocumentManager.CheckDocument</c>: у помеченного
        /// спектра он кривую НЕ достраивает.
        ///
        /// ⛔ Признак нужен именно отдельный, и вот почему его нельзя
        /// вывести из соседних полей. Сброс ставит спектру СВЕЖИЕ настройки
        /// поиска пиков, а у них встроенные 15/3756/103, по которым прямая
        /// растёт всегда, — то есть умолчание по ним строится, и получилась
        /// бы модель разрешения ВЫДУМАННОГО прибора (~~`A257`~~). Пустыми
        /// настройками (null) обойтись тоже нельзя: к типу их приводят без
        /// проверки в четырёх местах панели графика.
        ///
        /// Снимается, когда прибор у спектра снова появляется
        /// (<c>DocumentManager.PrepareDeviceConfig</c>), и наследуется
        /// спектрами, которые дверь импорта заводит по образцу документа
        /// (<c>DocumentManager.NewResultDataLike</c>).
        ///
        /// В файл не пишется: это состояние ОДНОГО импорта, а не свойство
        /// спектра. Сохранённый и открытый заново документ получает прибор
        /// по ссылке, и тогда признак взяться неоткуда.
        /// </summary>
        [XmlIgnore]
        public bool DeviceConfigWiped
        {
            get
            {
                return this.deviceConfigWiped;
            }
            set
            {
                this.deviceConfigWiped = value;
            }
        }

        public ResultData()
        {
        }

        public ResultData(ResultData_093b old)
        {
            this.sampleInfo = old.SampleInfo;
            this.deviceConfig = old.DeviceConfig;
            this.deviceConfigReference = old.DeviceConfigReference;
            this.startTime = old.StartTime;
            this.endTime = old.EndTime;
            this.backgroundSpectrumFile = old.BackgroundSpectrumFile;
            this.backgroundSpectrumPathname = old.BackgroundSpectrumPathname;
            this.energySpectrum = old.EnergySpectrum;
            // (`AMBER201`, подозрение полосы 2, 05.10.2026) Старый класс заводил фон
            // ВСЕГДА (`new EnergySpectrum()`), и файл без фона несёт пустой элемент
            // (ноль каналов, отсчётов нет). Это «фона нет», а не фон: иначе
            // CheckDocument, теперь зовущийся и этой дверью, судил бы документ
            // негодным и предлагал сбросить калибровку.
            this.backgroundEnergySpectrum = HasCounts(old.BackgroundEnergySpectrum) ? old.BackgroundEnergySpectrum : null;
            this.pulseCollection = old.PulseCollection;
            // 0.93b stored a linear calibration as two scalars. Build a fresh linear
            // calibration instead of writing Coefficients[2] into whatever array the
            // deserializer produced (the default calibration has only 2 coefficients ->
            // IndexOutOfRange) and only touch the background spectrum if it exists
            // (old files without background -> NullReferenceException).
            if (this.energySpectrum != null)
            {
                PolynomialEnergyCalibration polynomialEnergyCalibration = new PolynomialEnergyCalibration();
                polynomialEnergyCalibration.PolynomialOrder = 1;
                polynomialEnergyCalibration.Coefficients = new double[] { old.EnergyOffset, old.EnergyCoefficient };
                this.energySpectrum.EnergyCalibration = polynomialEnergyCalibration;
            }
            if (this.backgroundEnergySpectrum != null)
            {
                PolynomialEnergyCalibration polynomialEnergyCalibration2 = new PolynomialEnergyCalibration();
                polynomialEnergyCalibration2.PolynomialOrder = 1;
                polynomialEnergyCalibration2.Coefficients = new double[] { old.EnergyOffset, old.EnergyCoefficient };
                this.backgroundEnergySpectrum.EnergyCalibration = polynomialEnergyCalibration2;
            }
        }

        public ResultData(ResultData_097b old)
        {
            // (`AMBER201`, мелочь 2.11) Преобразователь заработал (его зовёт
            // `DocumentManager.TryReadFormat097b`), и пустые части старого файла
            // больше не роняют его: фона в файле может не быть вовсе, а
            // карточки пробы, прибора и импульсов — остаться умолчаниями.
            this.sampleInfo = old.SampleInfo ?? new SampleInfoData();
            this.deviceConfig = old.DeviceConfig ?? new DeviceConfigInfo();
            this.deviceConfigReference = old.DeviceConfigReference;
            this.startTime = old.StartTime;
            this.endTime = old.EndTime;
            this.backgroundSpectrumFile = old.BackgroundSpectrumFile;
            this.backgroundSpectrumPathname = old.BackgroundSpectrumPathname;
            this.energySpectrum = old.EnergySpectrum != null ? new EnergySpectrum(old.EnergySpectrum) : null;
            // Пустой элемент фона (старый класс заводил его всегда) — «фона нет».
            this.backgroundEnergySpectrum = old.BackgroundEnergySpectrum != null
                                            && old.BackgroundEnergySpectrum.Spectrum != null
                                            && old.BackgroundEnergySpectrum.Spectrum.Length > 0
                ? new EnergySpectrum(old.BackgroundEnergySpectrum)
                : null;
            if (old.PulseCollection != null)
            {
                this.pulseCollection = old.PulseCollection;
            }
        }

        /// <summary>(`AMBER201`) Спектр старого файла с отсчётами — не пустой элемент.</summary>
        static bool HasCounts(EnergySpectrum spectrum)
        {
            return spectrum != null && spectrum.Spectrum != null && spectrum.Spectrum.Length > 0;
        }

        public ResultData Clone()
        {
            // Своя копия, а не общий объект: два спектра с одной кривой
            // правились бы за одно, а кривая у спектра — снимок на момент
            // измерения и меняться следом за прибором не должна.
            EfficiencyConfigData efficiencyCopy = this.Efficiency != null ? this.Efficiency.Copy() : null;

            // Признак «выбрана родная» — тождество ссылок, и в копии оно должно
            // сохраниться: иначе дубль спектра терял бы пометку «из файла» и
            // строку, по которой к своей кривой можно вернуться.
            ResultData copy = new ResultData
            {
                SampleInfo = this.SampleInfo.Clone(),
                DeviceConfig = this.DeviceConfig,
                DeviceConfigReference = this.DeviceConfigReference,
                Efficiency = efficiencyCopy,
                StartTime = this.StartTime,
                EndTime = this.EndTime,
                PresetTime = this.PresetTime,
                BackgroundEnergySpectrum = this.BackgroundEnergySpectrum != null ? this.BackgroundEnergySpectrum.Clone() : null,
                BackgroundSpectrumFile = this.BackgroundSpectrumFile,
                BackgroundSpectrumPathname = this.BackgroundSpectrumPathname,
                EnergySpectrum = this.EnergySpectrum.Clone(),
                PulseCollection = this.PulseCollection.Clone(),
                // FwhmCalibration can legitimately be null (DefaultCalibration may fail
                // on a non-monotonic default curve).
                FwhmCalibration = this.FwhmCalibration != null ? this.FwhmCalibration.Clone() : null,
                // `A260`: копия спектра, у которого прибор снят сбросом, тоже без
                //   прибора — иначе `CheckDocument` достроил бы ЕЙ кривую по
                //   встроенным умолчаниям, и снятие обходилось бы дублированием.
                DeviceConfigWiped = this.DeviceConfigWiped
            };

            // (`AMBER201`, мелочь 2.16, 05.10.2026) Настройки поиска пиков и разбора
            // FSA живут в спектре ([XmlIgnore]) — копия получала ВСТРОЕННЫЕ
            // умолчания вместо настроек оригинала: «Отделить фон» давал второй
            // спектр документа, который искал пики и раскладывался FSA с чужими
            // порогами. `Visible` не копируется нарочно: копию (отделённый фон,
            // вычтенный спектр, фон в файл) показывают как новый спектр.
            if (this.PeakDetectionMethodConfig != null)
            {
                copy.PeakDetectionMethodConfig = this.PeakDetectionMethodConfig.Clone();
            }

            // (`AMBER271`) своя κ спектра — копии тоже своя (вычтенный спектр и фон
            // в файл уносят ту κ, которой разбирался оригинал)
            copy.tractCurvature = this.tractCurvature;
            copy.tractCurvatureOwn = this.tractCurvatureOwn;

            // (`AMBER276`) строки единой калибровки спектра без кривой FWHM — копии тоже
            // (строки спектра С кривой копируются вместе с ней, выше)
            copy.pendingCalibrationLines = CalibrationPeak.ClonePeaks(this.pendingCalibrationLines);

            copy.FileEfficiency = object.ReferenceEquals(this.Efficiency, this.FileEfficiency)
                ? efficiencyCopy
                : (this.FileEfficiency != null ? this.FileEfficiency.Copy() : null);
            return copy;
        }

        ResultDataStatus resultDataStatus = new ResultDataStatus();

        MeasurementController measurementController;

        SampleInfoData sampleInfo = new SampleInfoData();

        DeviceConfigInfo deviceConfig = new DeviceConfigInfo();

        DeviceConfigReference deviceConfigReference = new DeviceConfigReference();

        EfficiencyConfigData efficiency;

        EfficiencyConfigData fileEfficiency;

        EmbeddedResponseMatrix embeddedResponseMatrixRead;

        DateTime startTime = DateTime.Now;

        DateTime endTime = DateTime.Now;

        bool startTimeInFile;

        bool endTimeInFile;

        int presetTime;

        string backgroundSpectrumFile = "";

        string backgroundSpectrumPathname = "";

        EnergySpectrum energySpectrum = new EnergySpectrum();

        EnergySpectrum backgroundEnergySpectrum;

        PulseCollection pulseCollection = new PulseCollection();

        bool dirty;

        bool visible = true;

        bool selected;

        string detectorFeature;

        List<Peak> detectedPeaks = new List<Peak>();

        PeakDetectionMethodConfig peakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();

        FwhmCalibration fwhmCalibration = null;

        /// <summary>(`AMBER271`) Своя κ спектра, 1/МэВ; значима только при <see cref="tractCurvatureOwn"/>.</summary>
        double tractCurvature;

        /// <summary>(`AMBER271`) Своя κ есть (<see cref="HasOwnTractCurvature"/>).</summary>
        bool tractCurvatureOwn;

        bool deviceConfigWiped;

        List<Peak> calibrationPeaks = new List<Peak>();

        List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();

        /// <summary>(`AMBER276`) Строки единой калибровки, пока у спектра нет кривой FWHM (<see cref="PendingCalibrationLines"/>).</summary>
        List<CalibrationPeak> pendingCalibrationLines = new List<CalibrationPeak>();

        List<CountRate> countRates = new List<CountRate>();
    }
}
