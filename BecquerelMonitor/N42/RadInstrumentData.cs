using System;

namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    [System.Xml.Serialization.XmlRootAttribute(Namespace = "http://physics.nist.gov/N42/2011/N42", IsNullable = false)]
    public partial class RadInstrumentData
    {

        private RadInstrumentInformation[] radInstrumentInformationField;

        private RadDetectorInformation[] radDetectorInformationField;

        private EnergyCalibration[] energyCalibrationField;

        private RadMeasurement[] radMeasurementField;

        private string n42DocUUIDField;

        public RadInstrumentData()
        {
            this.n42DocUUIDField = Guid.NewGuid().ToString();
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("RadInstrumentInformation")]
        public RadInstrumentInformation[] RadInstrumentInformation
        {
            get
            {
                return this.radInstrumentInformationField;
            }
            set
            {
                this.radInstrumentInformationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("RadDetectorInformation")]
        public RadDetectorInformation[] RadDetectorInformation
        {
            get
            {
                return this.radDetectorInformationField;
            }
            set
            {
                this.radDetectorInformationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("EnergyCalibration")]
        public EnergyCalibration[] EnergyCalibration
        {
            get
            {
                return this.energyCalibrationField;
            }
            set
            {
                this.energyCalibrationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("RadMeasurement")]
        public RadMeasurement[] RadMeasurement
        {
            get
            {
                return this.radMeasurementField;
            }
            set
            {
                this.radMeasurementField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string n42DocUUID
        {
            get
            {
                return this.n42DocUUIDField;
            }
            set
            {
                this.n42DocUUIDField = value;
            }
        }
    }
}
