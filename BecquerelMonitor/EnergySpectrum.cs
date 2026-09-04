using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class EnergySpectrum
    {
        public int NumberOfChannels
        {
            get
            {
                return this.numberOfChannels;
            }
            set
            {
                this.numberOfChannels = value;
            }
        }

        public double ChannelPitch
        {
            get
            {
                return this.channelPitch;
            }
            set
            {
                this.channelPitch = value;
            }
        }

        [XmlElement(typeof(PolynomialEnergyCalibration))]
        public EnergyCalibration EnergyCalibration
        {
            get
            {
                return this.energyCalibration;
            }
            set
            {
                this.energyCalibration = value;
            }
        }

        public long ValidPulseCount
        {
            get
            {
                return this.validPulseCount;
            }
            set
            {
                this.validPulseCount = value;
            }
        }

        public long TotalPulseCount
        {
            get
            {
                return this.totalPulseCount;
            }
            set
            {
                this.totalPulseCount = value;
            }
        }

        public double MeasurementTime
        {
            get
            {
                return this.measurementTime;
            }
            set
            {
                this.measurementTime = value;
            }
        }

        public double LiveTime
        {
            get
            {
                return this.liveTime;
            }
            set
            {
                this.liveTime = value;
            }
        }

        public long NumberOfSamples
        {
            get
            {
                return this.numberOfSamples;
            }
            set
            {
                this.numberOfSamples = value;
            }
        }

        public CDATA SerialNumber
        {
            get
            {
                return this.serialnumber;
            }
            set
            {
                this.serialnumber = value;
            }
        }

        [XmlArrayItem("DataPoint")]
        public int[] Spectrum
        {
            get
            {
                return this.spectrum;
            }
            set
            {
                this.spectrum = value;
            }
        }

        [XmlIgnore]
        public double[] DrawingSpectrum
        {
            get
            {
                return this.drawingSpectrum;
            }
            set
            {
                this.drawingSpectrum = value;
            }
        }

        public EnergySpectrum()
        {
        }

        public EnergySpectrum(double channelPitch, int numberOfChannels)
        {
            this.channelPitch = channelPitch;
            this.numberOfChannels = numberOfChannels;
            this.spectrum = new int[numberOfChannels];
        }

        public EnergySpectrum(EnergySpectrum_097b old)
        {
            this.numberOfChannels = old.NumberOfChannels;
            this.channelPitch = old.ChannelPitch;
            PolynomialEnergyCalibration polynomialEnergyCalibration = new PolynomialEnergyCalibration();
            PolynomialEnergyCalibration polynomialEnergyCalibration2 = polynomialEnergyCalibration;
            double[] array = new double[3];
            array[0] = old.EnergyOffset;
            array[1] = old.EnergyCoefficient;
            polynomialEnergyCalibration2.Coefficients = array;
            this.energyCalibration = polynomialEnergyCalibration;
            this.totalPulseCount = old.TotalPulseCount;
            this.validPulseCount = old.ValidPulseCount;
            this.measurementTime = old.MeasurementTime;
            this.numberOfSamples = old.NumberOfSamples;
            this.spectrum = old.Spectrum;
        }

        public void Increment(double value)
        {
            int channel = (int)(value / this.channelPitch);
            if (channel >= 0 && channel <= this.numberOfChannels - 1)
            {
                this.spectrum[channel]++;
                this.validPulseCount++;
            }
        }

        public void Initialize()
        {
            this.spectrum = new int[this.numberOfChannels];
            this.totalPulseCount = 0;
            this.validPulseCount = 0;
            this.measurementTime = 0.0;
            this.numberOfSamples = 0L;
            this.liveTime = 0.0;
        }

        /// <summary>
        /// ⛔ СНИМОК СПЕКТРА БЕЗ ЭНЕРГЕТИЧЕСКОЙ КАЛИБРОВКИ ОТКАЗЫВАЕТ СЛОВАМИ,
        /// А НЕ <see cref="NullReferenceException"/> (`A95`).
        ///
        /// Прежде здесь стояло голое <c>this.energyCalibration.Clone()</c>, и
        /// спектр без калибровки убивал снимок пустым NRE — «Ссылка на объект
        /// не указывает на экземпляр объекта», без единого слова о том, ЧЕГО
        /// не хватает. Дороже всего это обошлось разложению: <c>FsaOverlay</c>
        /// снимает спектр ПЕРВОЙ строкой <c>Launch</c>, ловит там всё подряд и
        /// показывал «Полноспектральное разложение не удалось» — причина до
        /// человека не доезжала вовсе. Найдено 03.09.2026 тем, что из-за этого
        /// ВПУСТУЮ проходил положительный контроль опыта: обе половины молчали
        /// одинаково.
        ///
        /// ⛔ Подставлять калибровку здесь НЕЛЬЗЯ, и это не осторожность.
        /// Единственное, что можно подставить, — <c>new
        /// PolynomialEnergyCalibration()</c>, то есть y = x: номер канала
        /// объявляется энергией. Ровно этим «исправляет» такой спектр
        /// <c>DocumentManager.CheckDocument(doCorrections: true)</c>, и там это
        /// осознанное решение над ОТКРЫВАЕМЫМ ФАЙЛОМ, о котором человека
        /// спрашивают; молча делать то же самое в операции КОПИРОВАНИЯ значило
        /// бы выдать чужую шкалу за настоящую.
        ///
        /// ⛔ Пропустить <c>null</c> насквозь — тоже нельзя: отказ переехал бы
        /// вглубь потребителя, где у него уже нет ни имени спектра, ни повода.
        ///
        /// Законен ли спектр без калибровки на этом пути — НЕТ, и это говорит
        /// само приложение: <c>CheckDocument</c> без поправок возвращает на нём
        /// <c>false</c> (документ не годен), а <c>FsaAnalyzer.Analyze</c>
        /// отдельной проверкой отказывается разбирать спектр с
        /// <c>EnergyCalibration == null</c>. Поэтому здесь — названный отказ.
        /// </summary>
        public EnergySpectrum Clone()
        {
            if (this.energyCalibration == null)
            {
                throw new InvalidOperationException(
                    "EnergySpectrum.Clone: у спектра нет энергетической калибровки"
                    + " (EnergyCalibration == null, каналов " + this.numberOfChannels
                    + "). Снять копию нечего: без шкалы энергии спектр не годен"
                    + " ни для разложения, ни для поиска пиков, а подставить"
                    + " калибровку значило бы объявить номер канала энергией.");
            }

            EnergySpectrum returnValue = new EnergySpectrum(this.channelPitch, this.numberOfChannels);
            returnValue.spectrum = (int[]) this.spectrum.Clone();
            if (this.drawingSpectrum != null)
            {
                returnValue.drawingSpectrum = (double[])this.drawingSpectrum.Clone();
            } else
            {
                returnValue.drawingSpectrum = new double[this.numberOfChannels];
            }
            
            returnValue.channelPitch = this.channelPitch;
            returnValue.numberOfChannels = this.numberOfChannels;
            returnValue.measurementTime = this.measurementTime;
            returnValue.liveTime = this.liveTime;
            returnValue.energyCalibration = this.energyCalibration.Clone();
            returnValue.numberOfSamples = this.numberOfSamples;
            returnValue.totalPulseCount = this.totalPulseCount;
            returnValue.validPulseCount = this.validPulseCount;
            returnValue.serialnumber = this.serialnumber;

            return returnValue;
        }

        int numberOfChannels;

        double channelPitch = 0.04;

        EnergyCalibration energyCalibration;

        long totalPulseCount;

        long validPulseCount;

        double measurementTime;

        double liveTime;

        long numberOfSamples;

        int[] spectrum;

        double[] drawingSpectrum;

        CDATA serialnumber;
    }
}
