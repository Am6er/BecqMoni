namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class RadInstrumentVersion
    {

        private string radInstrumentComponentNameField;

        private string radInstrumentComponentVersionField;

        public RadInstrumentVersion()
        {
            this.radInstrumentComponentVersionField = "";
            this.radInstrumentComponentNameField = "";
        }

        /// <remarks/>
        public string RadInstrumentComponentName
        {
            get
            {
                return this.radInstrumentComponentNameField;
            }
            set
            {
                this.radInstrumentComponentNameField = value;
            }
        }

        /// <remarks/>
        public string RadInstrumentComponentVersion
        {
            get
            {
                return this.radInstrumentComponentVersionField;
            }
            set
            {
                this.radInstrumentComponentVersionField = value;
            }
        }
    }
}
