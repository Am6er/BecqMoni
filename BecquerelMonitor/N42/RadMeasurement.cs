namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class RadMeasurement
    {

        private string measurementClassCodeField;

        private string startDateTimeField;

        private string realTimeDurationField;

        private Spectrum[] spectrumField;

        private GrossCounts[] grossCountsField;

        private string idField;

        /// <remarks/>
        public string MeasurementClassCode
        {
            get
            {
                return this.measurementClassCodeField;
            }
            set
            {
                this.measurementClassCodeField = value;
            }
        }

        /// <remarks/>
        public string StartDateTime
        {
            get
            {
                return this.startDateTimeField;
            }
            set
            {
                this.startDateTimeField = value;
            }
        }

        /// <remarks/>
        public string RealTimeDuration
        {
            get
            {
                return this.realTimeDurationField;
            }
            set
            {
                this.realTimeDurationField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("Spectrum")]
        public Spectrum[] Spectrum
        {
            get
            {
                return this.spectrumField;
            }
            set
            {
                this.spectrumField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("GrossCounts")]
        public GrossCounts[] GrossCounts
        {
            get
            {
                return this.grossCountsField;
            }
            set
            {
                this.grossCountsField = value;
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
    }
}
