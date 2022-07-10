namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class GrossCounts
    {

        private string totalCountsField;

        private string idField;

        private string radDetectorInformationReferenceField;

        public GrossCounts()
        {
            this.totalCountsField = "0";
            this.idField = "Grosssome";
            this.radDetectorInformationReferenceField = "Detector";
        }

        /// <remarks/>
        public string TotalCounts
        {
            get
            {
                return this.totalCountsField;
            }
            set
            {
                this.totalCountsField = value;
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
    }
}
