namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class Spectrum
    {

        private string liveTimeDurationField; //PT6264.00S

        private ChannelData channelDataField;

        private string idField;

        private string radDetectorInformationReferenceField;

        private string energyCalibrationReferenceField;

        public Spectrum()
        {
            this.liveTimeDurationField = "";
            this.channelDataField = new ChannelData();
            this.idField = "someData";
            this.radDetectorInformationReferenceField = "Detector";
        }


        /// <remarks/>
        public string LiveTimeDuration
        {
            get
            {
                return this.liveTimeDurationField;
            }
            set
            {
                this.liveTimeDurationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("ChannelData", IsNullable = true)]
        public ChannelData ChannelData
        {
            get
            {
                return this.channelDataField;
            }
            set
            {
                this.channelDataField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string id
        {
            get
            {
                return this.idField;
            }
            set
            {
                this.idField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string radDetectorInformationReference
        {
            get
            {
                return this.radDetectorInformationReferenceField;
            }
            set
            {
                this.radDetectorInformationReferenceField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string energyCalibrationReference
        {
            get
            {
                return this.energyCalibrationReferenceField;
            }
            set
            {
                this.energyCalibrationReferenceField = value;
            }
        }
    }
}
