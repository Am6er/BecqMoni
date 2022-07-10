using System;

namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class EnergyCalibration
    {

        private string coefficientValuesField;

        private string idField;

        public EnergyCalibration()
        {
            this.coefficientValuesField = "";
            this.idField = "unknownCalibration"; // same set in Spectrum
        }

        /// <remarks/>
        public string CoefficientValues
        {
            get
            {
                return this.coefficientValuesField;
            }
            set
            {
                this.coefficientValuesField = value;
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

        public double[] CoefficientsToArray()
        {
            string[] n42CalibrationCoeff = this.coefficientValuesField.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
            n42CalibrationCoeff = Array.FindAll(n42CalibrationCoeff, isNotN42SpectrumValid);
            double[] coefficients = new double[n42CalibrationCoeff.Length];
            for (int i = 0; i < n42CalibrationCoeff.Length; i++)
            {
                coefficients[i] = double.Parse(n42CalibrationCoeff[i]);
            }
            return coefficients;
        }

        private bool isNotN42SpectrumValid(string str)
        {
            if (str == "" || str == "\n")
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
