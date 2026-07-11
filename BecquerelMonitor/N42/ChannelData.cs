using System;

namespace BecquerelMonitor.N42
{
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.8.3928.0")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true, Namespace = "http://physics.nist.gov/N42/2011/N42")]
    public partial class ChannelData
    {

        private string compressionCodeField;

        private string valueField;

        public ChannelData()
        {
            this.compressionCode = "None";
            this.valueField = "";
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlAttributeAttribute()]
        public string compressionCode
        {
            get
            {
                return this.compressionCodeField;
            }
            set
            {
                this.compressionCodeField = value;
            }
        }

        /// <remarks/>
        [System.Xml.Serialization.XmlTextAttribute()]
        public string Value
        {
            get
            {
                return this.valueField;
            }
            set
            {
                this.valueField = value;
            }
        }

        public int[] SpectrumToArray()
        {
            string[] n42SpectrimCounts = this.valueField.Replace("\n", " ").Split(new string[] { " " }, StringSplitOptions.None);
            n42SpectrimCounts = Array.FindAll(n42SpectrimCounts, isNotN42SpectrumValid);

            if (this.compressionCodeField == "CountedZeroes")
            {
                // N42 run-length encoding: a zero is followed by the number of zeros it
                // represents. This used to be silently ignored, producing a wrong spectrum.
                System.Collections.Generic.List<int> expanded = new System.Collections.Generic.List<int>();
                for (int i = 0; i < n42SpectrimCounts.Length; i++)
                {
                    int channelValue = int.Parse(n42SpectrimCounts[i]);
                    if (channelValue == 0 && i + 1 < n42SpectrimCounts.Length)
                    {
                        int zeroCount = int.Parse(n42SpectrimCounts[++i]);
                        for (int z = 0; z < zeroCount; z++)
                        {
                            expanded.Add(0);
                        }
                    }
                    else
                    {
                        expanded.Add(channelValue);
                    }
                }
                return expanded.ToArray();
            }

            int NumberOfChanels = n42SpectrimCounts.Length;

            int[] returnvalue = new int[NumberOfChanels];

            for (int i = 0; i < NumberOfChanels; i++)
            {
                returnvalue[i] = int.Parse(n42SpectrimCounts[i]);
            }

            return returnvalue;
        }

        public void SpectrumFromArray(int[] inputSpectra)
        {
            this.valueField = "";
            for (int i = 0; i < inputSpectra.Length; i++)
            {
                this.valueField = this.valueField + inputSpectra[i].ToString() + " ";

            }
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
