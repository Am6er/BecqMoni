using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class FWHMPeakDetectionMethodConfig : PeakDetectionMethodConfig
    {
        
        public double Min_SNR
        {
            get
            {
                return this.min_snr;
            }
            set
            {
                this.min_snr = value;
            }
        }

        public double FWHM_AT_0
        {
            get
            {
                return this.fwhm_at_0;
            }
            set
            {
                this.fwhm_at_0 = value;
            }
        }

        public double Ch_Fwhm
        {
            get
            {
                return this.ch_fwhm;
            }
            set
            {
                this.ch_fwhm = value;
            }
        }

        public double Width_Fwhm
        {
            get
            {
                return this.width_fwhm;
            }
            set
            {
                this.width_fwhm = value;
            }
        }

        public int Max_Items
        {
            get
            {
                return this.max_items;
            }
            set
            {
                this.max_items = value;
            }
        }

        public double Tolerance
        {
            get
            {
                return this.tolerance;
            }
            set
            {
                this.tolerance = value;
            }
        }

        public double Min_Range
        {
            get
            {
                return this.min_range_en;
            }
            set
            {
                this.min_range_en = value;
            }
        }

        public double Max_Range
        {
            get
            {
                return this.max_range_en;
            }
            set
            {
                this.max_range_en = value;
            }
        }

        public decimal Min_FWHM_Tol
        {
            get
            {
                return this.min_fwhm_tol;
            }
            set
            {
                this.min_fwhm_tol = value;
            }
        }

        public decimal Max_FWHM_Tol
        {
            get
            {
                return this.max_fwhm_tol;
            }
            set
            {
                this.max_fwhm_tol = value;
            }
        }

        public bool Enabled
        {
            get
            {
                return this.enabled;
            }
            set
            {
                this.enabled = value;
            }
        }

        public int Ch_Concat
        {
            get
            {
                return this.ch_concat;
            }
            set
            {
                this.ch_concat = value;
            }
        }

        // Максимальный коэффициент расширения окна оценки континуума (SNIP)
        // относительно модельного FWHM в сторону измеренной ширины пика, если
        // реальный пик оказывается шире модели. См. SpectrumAriphmetics.BuildSnipRadius.
        public double PeakWidthWidenFactor
        {
            get
            {
                return this.peak_width_widen_factor;
            }
            set
            {
                this.peak_width_widen_factor = value;
            }
        }

        // Метод уточнения центроида пика: true — центр масс по ядру пика
        // (устойчив к шуму/плоским вершинам), false — сырой argmax (максимальный бин).
        // См. SpectrumAriphmetics.FindCentroid.
        public bool UseCenterOfMassCentroid
        {
            get
            {
                return this.use_center_of_mass_centroid;
            }
            set
            {
                this.use_center_of_mass_centroid = value;
            }
        }

        /// <summary>
        /// Состав библиотеки полноспектрального разбора выводится ИЗ ПОИСКА
        /// ПИКОВ по цепочке родителя, а не берётся подписями пиков как есть
        /// (`S57`, правило Amber 17.08.2026).
        ///
        /// Настройка живёт здесь, а не рядом с разбором, по двум причинам.
        /// Во-первых, она про то, ЧТО ДЕЛАТЬ С РЕЗУЛЬТАТОМ ПОИСКА ПИКОВ, и
        /// галка стоит в его же панели. Во-вторых, эти настройки уже устроены
        /// как надо: у прибора умолчание, у спектра своя копия
        /// (<see cref="AdoptFrom"/>), и человек, включивший вывод для одного
        /// спектра, не включает его всем разом.
        ///
        /// ⛔ Умолчание — ВЫКЛЮЧЕНО, и это не осторожность, а совместимость:
        /// выключенным разбор работает ровно так, как работал до `S57`
        /// (<see cref="FullSpectrumAnalysis.FsaLibrary.BuildFromPeaks"/>), и
        /// ни одно уже снятое число само собой не сдвинется.
        /// </summary>
        public bool DbLookupsForFsa
        {
            get
            {
                return this.db_lookups_for_fsa;
            }
            set
            {
                this.db_lookups_for_fsa = value;
            }
        }

        /// <summary>
        /// РАВНОВЕСИЕ: ряд идёт в разбор одной колонкой с одной свободной
        /// амплитудой, относительные веса членов закреплены ветвлением
        /// (`S70`, решение Amber 18.08.2026). Живёт рядом с
        /// <see cref="DbLookupsForFsa"/> и по той же причине: обе — про то, что
        /// делать с результатом поиска пиков, и галки стоят в его панели.
        ///
        /// ⛔ Умолчание — ВКЛЮЧЕНО, в отличие от <see cref="DbLookupsForFsa"/>.
        /// Разница намеренная: та галка выключена ради совместимости с уже
        /// снятыми числами, а эта чинит названную и измеренную неправду — на
        /// `Th232_29.07.2022.xml` свободные амплитуды дали Ra-224 8.22 % против
        /// положенных равновесию ≈0.9 %, потому что его единственная гамма
        /// 240.986 кэВ стоит в 2.4 кэВ от 238.632 кэВ Pb-212, вдесятеро более
        /// сильной, и при ПШПВ прибора в 52 канала обе линии — один бугор.
        ///
        /// Выключенная возвращает прежнее поведение: у каждого члена ряда своя
        /// свободная амплитуда, «разрез цепочки получается сам», — им и видно
        /// НЕРАВНОВЕСИЕ (оборванный ряд уранового стекла, `S65`).
        /// </summary>
        public bool ChainEquilibrium
        {
            get
            {
                return this.chain_equilibrium;
            }
            set
            {
                this.chain_equilibrium = value;
            }
        }

        [XmlElement(typeof(SimpleSqrtFwhmCalibration))]
        [XmlElement(typeof(SqrtFwhmCalibration))]
        [XmlElement(typeof(PowerFwhmCalibration))]
        public FwhmCalibration FwhmCalibration { get => fwhmCalibration; set => fwhmCalibration = value; }

        public FWHMPeakDetectionMethodConfig()
        {
            this.fwhmCalibration = FwhmCalibration.DefaultCalibration(this, new PolynomialEnergyCalibration());
        }

        public FWHMPeakDetectionMethodConfig(FWHMPeakDetectionMethodConfig config)
        {
            this.tolerance = config.tolerance;
            this.fwhm_at_0 = config.fwhm_at_0;
            this.ch_fwhm = config.ch_fwhm;
            this.width_fwhm = config.width_fwhm;
            this.min_snr = config.min_snr;
            this.max_items = config.max_items;
            this.min_range_en = config.min_range_en;
            this.max_range_en = config.max_range_en;
            this.min_fwhm_tol = config.min_fwhm_tol;
            this.max_fwhm_tol = config.max_fwhm_tol;
            this.ch_concat = config.ch_concat;
            this.peak_width_widen_factor = config.peak_width_widen_factor;
            this.use_center_of_mass_centroid = config.use_center_of_mass_centroid;
            this.db_lookups_for_fsa = config.db_lookups_for_fsa;
            this.chain_equilibrium = config.chain_equilibrium;
            if (config.fwhmCalibration != null)
            {
                this.fwhmCalibration = config.fwhmCalibration.Clone();
            }
            else
            {
                this.fwhmCalibration = FwhmCalibration.DefaultCalibration(this, new PolynomialEnergyCalibration());
            }
        }

        public override PeakDetectionMethodConfig Clone()
        {
            return new FWHMPeakDetectionMethodConfig(this);
        }

        /// <summary>
        /// Настройки поиска пиков из конфигурации ПРИБОРА — в спектр, с
        /// сохранением того, что принадлежит спектру.
        ///
        /// У спектра эти настройки свои: в панели поиска пиков SNR и допуск
        /// правятся для одного спектра, а не для всех разом. Копия снималась
        /// один раз, при открытии документа (см. DocumentManager), и правка на
        /// вкладке Analysis до уже открытого спектра не доходила вовсе.
        ///
        /// Две вещи правка прибора не отменяет:
        ///
        /// * <see cref="FwhmCalibration"/> вместе с формой пика — она
        ///   подбирается ПО ЭТОМУ спектру своим редактором и лежит в его файле;
        ///   ровно так же её сохраняет <c>DocEnergySpectrum.CreateResultData</c>,
        ///   перекрывая ею калибровку из конфигурации прибора;
        /// * <see cref="Enabled"/> — это состояние кнопки показа пиков на панели
        ///   спектра, а не настройка прибора.
        /// </summary>
        public static FWHMPeakDetectionMethodConfig AdoptFrom(FWHMPeakDetectionMethodConfig device,
                                                              FWHMPeakDetectionMethodConfig spectrum)
        {
            if (device == null)
            {
                return spectrum;
            }

            FWHMPeakDetectionMethodConfig adopted = (FWHMPeakDetectionMethodConfig)device.Clone();
            if (spectrum == null)
            {
                return adopted;
            }

            if (spectrum.FwhmCalibration != null)
            {
                adopted.FwhmCalibration = spectrum.FwhmCalibration.Clone();
            }

            adopted.Enabled = spectrum.Enabled;
            return adopted;
        }

        double tolerance = 10.0;

        double fwhm_at_0 = 15.0;

        double ch_fwhm = 3756.0;

        double width_fwhm = 103;

        double min_snr = 10;

        int max_items = 40;

        double min_range_en = 30; //keV

        double max_range_en = 2800; //keV

        decimal min_fwhm_tol = 1;

        decimal max_fwhm_tol = 199;

        int ch_concat = 1024;

        double peak_width_widen_factor = 1.2;

        bool use_center_of_mass_centroid = true;

        bool db_lookups_for_fsa = false;

        // Умолчание ВКЛЮЧЕНО — см. ChainEquilibrium. Старые файлы конфигурации
        // элемента не несут, и им достаётся это же значение поля.
        bool chain_equilibrium = true;

        bool enabled = true;

        FwhmCalibration fwhmCalibration = null;
    }
}
