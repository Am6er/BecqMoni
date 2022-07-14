using System;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000076 RID: 118
    public class CDATA : IXmlSerializable
    {
        // Token: 0x06000601 RID: 1537 RVA: 0x00025E24 File Offset: 0x00024024
        public CDATA()
        {
            this.text = "";
        }

        // Token: 0x06000602 RID: 1538 RVA: 0x00025E38 File Offset: 0x00024038
        public CDATA(string text)
        {
            this.text = text;
        }

        // Token: 0x06000603 RID: 1539 RVA: 0x00025E48 File Offset: 0x00024048
        public static implicit operator CDATA(string rhs)
        {
            return new CDATA(rhs);
        }

        // Token: 0x06000604 RID: 1540 RVA: 0x00025E50 File Offset: 0x00024050
        public static implicit operator string(CDATA rhs)
        {
            return rhs.text;
        }

        // Token: 0x06000605 RID: 1541 RVA: 0x00025E58 File Offset: 0x00024058
        public override string ToString()
        {
            return this.text;
        }

        // Token: 0x06000606 RID: 1542 RVA: 0x00025E60 File Offset: 0x00024060
        public XmlSchema GetSchema()
        {
            return null;
        }

        // Token: 0x06000607 RID: 1543 RVA: 0x00025E64 File Offset: 0x00024064
        public void ReadXml(XmlReader reader)
        {
            this.text = reader.ReadElementString().Replace("\n", Environment.NewLine);
        }

        // Token: 0x06000608 RID: 1544 RVA: 0x00025E84 File Offset: 0x00024084
        public void WriteXml(XmlWriter writer)
        {
            if (this.text != "")
            {
                string[] array = this.text.Split(new string[]
                {
                    "]]>"
                }, StringSplitOptions.None);
                int i;
                for (i = 0; i < array.Length - 1; i++)
                {
                    writer.WriteCData(array[i]);
                    writer.WriteString("]]>");
                }
                writer.WriteCData(array[i]);
            }
        }

        // Token: 0x0400032D RID: 813
        const string cdataTerm = "]]>";

        // Token: 0x0400032E RID: 814
        string text;
    }
}
