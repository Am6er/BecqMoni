using System;
using System.Drawing;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000121 RID: 289
	public class SerializableColor : IXmlSerializable
	{
		// Token: 0x17000420 RID: 1056
		// (get) Token: 0x06000FA4 RID: 4004 RVA: 0x000575CC File Offset: 0x000557CC
		// (set) Token: 0x06000FA5 RID: 4005 RVA: 0x000575D4 File Offset: 0x000557D4
		public Color Color
		{
			get
			{
				return this.color;
			}
			set
			{
				this.color = value;
			}
		}

		// Token: 0x06000FA6 RID: 4006 RVA: 0x000575E0 File Offset: 0x000557E0
		public SerializableColor(Color color)
		{
			this.color = color;
		}

		// Token: 0x06000FA7 RID: 4007 RVA: 0x000575F0 File Offset: 0x000557F0
		public static implicit operator SerializableColor(Color color)
		{
			return new SerializableColor(color);
		}

		// Token: 0x06000FA8 RID: 4008 RVA: 0x000575F8 File Offset: 0x000557F8
		public SerializableColor()
		{
		}

		// Token: 0x06000FA9 RID: 4009 RVA: 0x00057600 File Offset: 0x00055800
		XmlSchema IXmlSerializable.GetSchema()
		{
			return null;
		}

		// Token: 0x06000FAA RID: 4010 RVA: 0x00057604 File Offset: 0x00055804
		void IXmlSerializable.ReadXml(XmlReader reader)
		{
			reader.ReadStartElement();
			this.color = ColorTranslator.FromHtml(reader.ReadString());
			reader.ReadEndElement();
		}

		// Token: 0x06000FAB RID: 4011 RVA: 0x00057624 File Offset: 0x00055824
		void IXmlSerializable.WriteXml(XmlWriter writer)
		{
			writer.WriteString(ColorTranslator.ToHtml(this.color));
		}

		// Token: 0x040008F5 RID: 2293
		Color color;
	}
}
