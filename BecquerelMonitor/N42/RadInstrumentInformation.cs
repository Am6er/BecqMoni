namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class RadInstrumentInformation
    {

        private string radInstrumentManufacturerNameField;

        private string radInstrumentModelNameField;

        private string radInstrumentClassCodeField;

        private RadInstrumentVersion[] radInstrumentVersionField;

        private string idField;

        /// <remarks/>
        public string RadInstrumentManufacturerName
        {
            get
            {
                return this.radInstrumentManufacturerNameField;
            }
            set
            {
                this.radInstrumentManufacturerNameField = value;
            }
        }

        /// <remarks/>
        public string RadInstrumentModelName
        {
            get
            {
                return this.radInstrumentModelNameField;
            }
            set
            {
                this.radInstrumentModelNameField = value;
            }
        }

        /// <remarks/>
        public string RadInstrumentClassCode
        {
            get
            {
                return this.radInstrumentClassCodeField;
            }
            set
            {
                this.radInstrumentClassCodeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute("RadInstrumentVersion")]
        public RadInstrumentVersion[] RadInstrumentVersion
        {
            get
            {
                return this.radInstrumentVersionField;
            }
            set
            {
                this.radInstrumentVersionField = value;
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
