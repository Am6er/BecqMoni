namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class RadDetectorInformation
    {

        private string radDetectorCategoryCodeField;

        private string radDetectorKindCodeField;

        private string idField;

        public RadDetectorInformation()
        {
            this.radDetectorKindCodeField = "Unknown";
            this.radDetectorCategoryCodeField = "Gamma";
            this.idField = "Detector";
        }

        /// <remarks/>
        public string RadDetectorCategoryCode
        {
            get
            {
                return this.radDetectorCategoryCodeField;
            }
            set
            {
                this.radDetectorCategoryCodeField = value;
            }
        }

        /// <remarks/>
        public string RadDetectorKindCode
        {
            get
            {
                return this.radDetectorKindCodeField;
            }
            set
            {
                this.radDetectorKindCodeField = value;
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
