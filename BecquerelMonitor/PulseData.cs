using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x0200002A RID: 42
	public class PulseData : List<Pulse>, IXmlSerializable
	{
		// Token: 0x06000236 RID: 566 RVA: 0x00008ED8 File Offset: 0x000070D8
		public XmlSchema GetSchema()
		{
			return null;
		}

		// Token: 0x06000237 RID: 567 RVA: 0x00008EDC File Offset: 0x000070DC
		public void ReadXml(XmlReader reader)
		{
			base.Clear();
			try
			{
				if (!reader.IsEmptyElement && reader.Read())
				{
					byte[] array = new byte[20000];
					while (reader.NodeType != XmlNodeType.EndElement)
					{
						int num = reader.ReadContentAsBase64(array, 0, array.Length);
						int i = 0;
						while (i < num)
						{
							long time = BitConverter.ToInt64(array, i);
							i += 8;
							double height = BitConverter.ToDouble(array, i);
							i += 8;
							int width = BitConverter.ToInt32(array, i);
							i += 4;
							Pulse item = new Pulse(time, height, width);
							base.Add(item);
						}
					}
					reader.ReadEndElement();
				}
			}
			catch (Exception ex)
			{
				throw ex;
			}
		}

		// Token: 0x06000238 RID: 568 RVA: 0x00008F90 File Offset: 0x00007190
		public void WriteXml(XmlWriter writer)
		{
			GlobalConfigInfo globalConfig = GlobalConfigManager.GetInstance().GlobalConfig;
			if (!globalConfig.DoSaveRawPulseData)
			{
				return;
			}
			int count = base.Count;
			if (count == 0)
			{
				return;
			}
			byte[] array = new byte[count * 20];
			int i = 0;
			foreach (Pulse pulse in this)
			{
				Buffer.BlockCopy(BitConverter.GetBytes(pulse.Time), 0, array, i, 8);
				i += 8;
				Buffer.BlockCopy(BitConverter.GetBytes(pulse.Height), 0, array, i, 8);
				i += 8;
				Buffer.BlockCopy(BitConverter.GetBytes(pulse.Width), 0, array, i, 4);
				i += 4;
			}
			i = 0;
			int num = array.Length;
			writer.WriteString("\r\n");
			while (i < array.Length)
			{
				writer.WriteBase64(array, i, (num < 96) ? num : 96);
				writer.WriteString("\r\n");
				i += 96;
				num -= 96;
			}
		}
	}
}
