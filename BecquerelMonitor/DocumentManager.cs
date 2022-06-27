using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x0200002E RID: 46
	public class DocumentManager
	{
		// Token: 0x1700011D RID: 285
		// (get) Token: 0x06000264 RID: 612 RVA: 0x00009488 File Offset: 0x00007688
		public string Extension
		{
			get
			{
				return ".xml";
			}
		}

		// Token: 0x1700011E RID: 286
		// (get) Token: 0x06000265 RID: 613 RVA: 0x00009490 File Offset: 0x00007690
		// (set) Token: 0x06000266 RID: 614 RVA: 0x00009498 File Offset: 0x00007698
		public double DefaultEnergyScale
		{
			get
			{
				return this.defaultEnergyScale;
			}
			set
			{
				this.defaultEnergyScale = value;
			}
		}

		// Token: 0x1700011F RID: 287
		// (get) Token: 0x06000267 RID: 615 RVA: 0x000094A4 File Offset: 0x000076A4
		// (set) Token: 0x06000268 RID: 616 RVA: 0x000094AC File Offset: 0x000076AC
		public List<DocEnergySpectrum> DocumentList
		{
			get
			{
				return this.documentList;
			}
			set
			{
				this.documentList = value;
			}
		}

		// Token: 0x06000269 RID: 617 RVA: 0x000094B8 File Offset: 0x000076B8
		public static DocumentManager GetInstance()
		{
			return DocumentManager.instance;
		}

		// Token: 0x0600026B RID: 619 RVA: 0x000094DC File Offset: 0x000076DC
		public DocEnergySpectrum CreateDocument()
		{
			string filename = string.Concat(new object[]
			{
				Resources.NewFilePrefix,
				" (",
				this.serial,
				").xml"
			});
			DocEnergySpectrum docEnergySpectrum = this.CreateDocument(filename);
			docEnergySpectrum.IsNamed = false;
			return docEnergySpectrum;
		}

		// Token: 0x0600026C RID: 620 RVA: 0x00009534 File Offset: 0x00007734
		public DocEnergySpectrum CreateDocument(string filename)
		{
			this.serial++;
			DocEnergySpectrum docEnergySpectrum = new DocEnergySpectrum(filename);
			docEnergySpectrum.ActiveResultDataIndex = 0;
			docEnergySpectrum.IsNamed = true;
			this.documentList.Add(docEnergySpectrum);
			ResultData activeResultData = docEnergySpectrum.ActiveResultData;
			activeResultData.BackgroundSpectrumFile = Path.GetFileName(activeResultData.DeviceConfig.BackgroundSpectrumPathname);
			activeResultData.BackgroundSpectrumPathname = activeResultData.DeviceConfig.BackgroundSpectrumPathname;
			this.LoadBackgroundSpectrum(activeResultData);
			docEnergySpectrum.UpdateEnergySpectrum();
			return docEnergySpectrum;
		}

		// Token: 0x0600026D RID: 621 RVA: 0x000095B0 File Offset: 0x000077B0
		public DocEnergySpectrum OpenDocument()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = Resources.OpenFileDialogTitle;
			openFileDialog.Filter = Resources.SpectrumFileFilter;
			openFileDialog.FilterIndex = 1;
			openFileDialog.RestoreDirectory = true;
			if (openFileDialog.ShowDialog() != DialogResult.OK)
			{
				return null;
			}
			string fileName = openFileDialog.FileName;
			return this.OpenDocument(fileName);
		}

		// Token: 0x0600026E RID: 622 RVA: 0x0000960C File Offset: 0x0000780C
		public DocEnergySpectrum OpenDocument(string filename)
		{
			foreach (DocEnergySpectrum docEnergySpectrum in this.documentList)
			{
				if (docEnergySpectrum.IsNamed && docEnergySpectrum.Filename == filename)
				{
					MessageBox.Show(Resources.ERRAlreadyOpen, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return null;
				}
			}
			DocEnergySpectrum docEnergySpectrum2 = new DocEnergySpectrum(filename);
			GC.Collect();
			Cursor.Current = Cursors.WaitCursor;
			try
			{
				using (FileStream fileStream = new FileStream(docEnergySpectrum2.Filename, FileMode.Open))
				{
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
					docEnergySpectrum2.ResultDataFile = (ResultDataFile)xmlSerializer.Deserialize(fileStream);
				}
				bool flag = false;
				foreach (ResultData resultData in docEnergySpectrum2.ResultDataFile.ResultDataList)
				{
					if (resultData.EnergySpectrum.EnergyCalibration == null)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					using (FileStream fileStream2 = new FileStream(docEnergySpectrum2.Filename, FileMode.Open))
					{
						XmlSerializer xmlSerializer2 = new XmlSerializer(typeof(ResultDataFile_097b));
						docEnergySpectrum2.ResultDataFile = new ResultDataFile((ResultDataFile_097b)xmlSerializer2.Deserialize(fileStream2));
					}
				}
			}
			catch (Exception)
			{
				try
				{
					using (FileStream fileStream3 = new FileStream(docEnergySpectrum2.Filename, FileMode.Open))
					{
						XmlSerializer xmlSerializer3 = new XmlSerializer(typeof(ResultData_097b));
						ResultData_097b old = (ResultData_097b)xmlSerializer3.Deserialize(fileStream3);
						docEnergySpectrum2.ResultDataFile.ResultDataList[0] = new ResultData(old);
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message));
					Cursor.Current = Cursors.Default;
					return null;
				}
			}
			Cursor.Current = Cursors.Default;
			docEnergySpectrum2.IsNamed = true;
			this.documentList.Add(docEnergySpectrum2);
			this.PrepareDeviceConfig(docEnergySpectrum2.ResultDataFile);
			this.PrepareROIConfig(docEnergySpectrum2.ResultDataFile);
			foreach (ResultData resultData2 in docEnergySpectrum2.ResultDataFile.ResultDataList)
			{
				ResultDataStatus resultDataStatus = new ResultDataStatus();
				resultDataStatus.TotalTime = TimeSpan.FromSeconds(resultData2.EnergySpectrum.MeasurementTime);
				resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(resultData2.EnergySpectrum.MeasurementTime);
				if (resultData2.PresetTime == 0)
				{
					if (resultData2.EnergySpectrum.MeasurementTime > (double)resultData2.DeviceConfig.DefaultMeasurementTime)
					{
						resultData2.PresetTime = (int)resultData2.EnergySpectrum.MeasurementTime;
					}
					else
					{
						resultData2.PresetTime = resultData2.DeviceConfig.DefaultMeasurementTime;
					}
					resultDataStatus.PresetTime = resultData2.PresetTime;
				}
				else
				{
					resultDataStatus.PresetTime = resultData2.PresetTime;
				}
				resultDataStatus.TimeInSamples = resultData2.EnergySpectrum.NumberOfSamples;
				resultData2.ResultDataStatus = resultDataStatus;
				resultData2.MeasurementController = new MeasurementController(docEnergySpectrum2, resultData2);
			}
			docEnergySpectrum2.ResultDataFile.ResultDataList[0].Selected = true;
			docEnergySpectrum2.UpdateEnergySpectrum();
			return docEnergySpectrum2;
		}

		// Token: 0x0600026F RID: 623 RVA: 0x000099F4 File Offset: 0x00007BF4
		public ResultDataFile LoadDocument(DocEnergySpectrum doc, string pathname)
		{
			Cursor.Current = Cursors.WaitCursor;
			ResultDataFile resultDataFile;
			try
			{
				using (FileStream fileStream = new FileStream(pathname, FileMode.Open))
				{
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
					resultDataFile = (ResultDataFile)xmlSerializer.Deserialize(fileStream);
				}
				bool flag = false;
				foreach (ResultData resultData in resultDataFile.ResultDataList)
				{
					if (resultData.EnergySpectrum.EnergyCalibration == null)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					using (FileStream fileStream2 = new FileStream(doc.Filename, FileMode.Open))
					{
						XmlSerializer xmlSerializer2 = new XmlSerializer(typeof(ResultDataFile_097b));
						resultDataFile = new ResultDataFile((ResultDataFile_097b)xmlSerializer2.Deserialize(fileStream2));
					}
				}
			}
			catch (Exception)
			{
				try
				{
					using (FileStream fileStream3 = new FileStream(pathname, FileMode.Open))
					{
						XmlSerializer xmlSerializer3 = new XmlSerializer(typeof(ResultData));
						ResultData item = (ResultData)xmlSerializer3.Deserialize(fileStream3);
						resultDataFile = new ResultDataFile();
						resultDataFile.ResultDataList.Add(item);
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, pathname, ex.Message));
					Cursor.Current = Cursors.Default;
					return null;
				}
			}
			Cursor.Current = Cursors.Default;
			this.PrepareDeviceConfig(resultDataFile);
			this.PrepareROIConfig(resultDataFile);
			foreach (ResultData resultData2 in resultDataFile.ResultDataList)
			{
				ResultDataStatus resultDataStatus = new ResultDataStatus();
				resultDataStatus.TotalTime = TimeSpan.FromSeconds(resultData2.EnergySpectrum.MeasurementTime);
				resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(resultData2.EnergySpectrum.MeasurementTime);
				if (resultData2.PresetTime == 0)
				{
					if (resultData2.EnergySpectrum.MeasurementTime > (double)resultData2.DeviceConfig.DefaultMeasurementTime)
					{
						resultData2.PresetTime = (int)resultData2.EnergySpectrum.MeasurementTime;
					}
					else
					{
						resultData2.PresetTime = resultData2.DeviceConfig.DefaultMeasurementTime;
					}
					resultDataStatus.PresetTime = resultData2.PresetTime;
				}
				else
				{
					resultDataStatus.PresetTime = resultData2.PresetTime;
				}
				resultDataStatus.TimeInSamples = resultData2.EnergySpectrum.NumberOfSamples;
				resultData2.ResultDataStatus = resultDataStatus;
				resultData2.MeasurementController = new MeasurementController(doc, resultData2);
			}
			doc.ResultDataFile.ResultDataList[0].Selected = true;
			return resultDataFile;
		}

		// Token: 0x06000270 RID: 624 RVA: 0x00009D0C File Offset: 0x00007F0C
		public DocEnergySpectrum ImportDocument093b()
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = Resources.ImportOldFormatFileDialogTitle;
			openFileDialog.Filter = Resources.SpectrumFileFilter;
			openFileDialog.FilterIndex = 1;
			openFileDialog.RestoreDirectory = true;
			if (openFileDialog.ShowDialog() != DialogResult.OK)
			{
				return null;
			}
			string fileName = openFileDialog.FileName;
			return this.ImportDocument093b(fileName);
		}

		// Token: 0x06000271 RID: 625 RVA: 0x00009D68 File Offset: 0x00007F68
		public DocEnergySpectrum ImportDocument093b(string filename)
		{
			foreach (DocEnergySpectrum docEnergySpectrum in this.documentList)
			{
				if (docEnergySpectrum.IsNamed && docEnergySpectrum.Filename == filename)
				{
					MessageBox.Show(Resources.ERRAlreadyOpen, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return null;
				}
			}
			DocEnergySpectrum docEnergySpectrum2 = new DocEnergySpectrum(filename);
			GC.Collect();
			try
			{
				Cursor.Current = Cursors.WaitCursor;
				using (FileStream fileStream = new FileStream(docEnergySpectrum2.Filename, FileMode.Open))
				{
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultData_093b));
					ResultData value = new ResultData((ResultData_093b)xmlSerializer.Deserialize(fileStream));
					docEnergySpectrum2.ResultDataFile.ResultDataList[0] = value;
				}
				Cursor.Current = Cursors.Default;
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message));
				return null;
			}
			docEnergySpectrum2.IsNamed = true;
			this.documentList.Add(docEnergySpectrum2);
			this.PrepareDeviceConfig(docEnergySpectrum2.ResultDataFile);
			this.PrepareROIConfig(docEnergySpectrum2.ResultDataFile);
			foreach (ResultData resultData in docEnergySpectrum2.ResultDataFile.ResultDataList)
			{
				ResultDataStatus resultDataStatus = new ResultDataStatus();
				resultDataStatus.TotalTime = TimeSpan.FromSeconds(resultData.EnergySpectrum.MeasurementTime);
				resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(resultData.EnergySpectrum.MeasurementTime);
				if (resultData.PresetTime == 0)
				{
					resultDataStatus.PresetTime = resultData.DeviceConfig.DefaultMeasurementTime;
				}
				else
				{
					resultDataStatus.PresetTime = resultData.PresetTime;
				}
				resultDataStatus.TimeInSamples = resultData.EnergySpectrum.NumberOfSamples;
				resultData.ResultDataStatus = resultDataStatus;
				resultData.MeasurementController = new MeasurementController(docEnergySpectrum2, resultData);
			}
			docEnergySpectrum2.ResultDataFile.ResultDataList[0].Selected = true;
			docEnergySpectrum2.UpdateEnergySpectrum();
			return docEnergySpectrum2;
		}

		// Token: 0x06000272 RID: 626 RVA: 0x00009FD4 File Offset: 0x000081D4
		public void CloseDocument(DocEnergySpectrum doc)
		{
			this.documentList.Remove(doc);
			doc.Close();
			doc.Dispose();
			GC.Collect();
		}

		// Token: 0x06000273 RID: 627 RVA: 0x00009FF4 File Offset: 0x000081F4
		public bool SaveDocument(DocEnergySpectrum doc)
		{
			ResultDataFile resultDataFile = doc.ResultDataFile;
			try
			{
				Cursor.Current = Cursors.WaitCursor;
				using (FileStream fileStream = new FileStream(doc.Filename, FileMode.Create))
				{
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
					xmlSerializer.Serialize(fileStream, resultDataFile);
				}
				Cursor.Current = Cursors.Default;
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, doc.Filename, ex.Message));
				return false;
			}
			doc.Dirty = false;
			return true;
		}

		// Token: 0x06000274 RID: 628 RVA: 0x0000A0A4 File Offset: 0x000082A4
		public bool SaveDocumentWithName(DocEnergySpectrum doc)
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.Title = Resources.SaveFileDialogTitle;
			saveFileDialog.Filter = Resources.SpectrumFileFilter;
			saveFileDialog.FilterIndex = 1;
			saveFileDialog.RestoreDirectory = true;
			if (saveFileDialog.ShowDialog() != DialogResult.OK)
			{
				return false;
			}
			string fileName = saveFileDialog.FileName;
			foreach (DocEnergySpectrum docEnergySpectrum in this.documentList)
			{
				if (docEnergySpectrum.IsNamed && docEnergySpectrum.Filename == fileName && doc.Filename != fileName)
				{
					MessageBox.Show(Resources.ERRCannotOverwrite, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return false;
				}
			}
			doc.Filename = fileName;
			if (this.SaveDocument(doc))
			{
				doc.IsNamed = true;
			}
			return true;
		}

		// Token: 0x06000275 RID: 629 RVA: 0x0000A19C File Offset: 0x0000839C
		void PrepareDeviceConfig(ResultDataFile resultDataFile)
		{
			DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();
			foreach (ResultData resultData in resultDataFile.ResultDataList)
			{
				for (int i = 0; i < deviceConfigManager.DeviceConfigList.Count; i++)
				{
					DeviceConfigInfo deviceConfigInfo = deviceConfigManager.DeviceConfigList[i];
					if (resultData.DeviceConfigReference.Guid == deviceConfigInfo.Guid)
					{
						resultData.DeviceConfig = deviceConfigInfo;
						SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)deviceConfigInfo.PeakDetectionMethodConfig;
						resultData.PeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)simplePeakDetectionMethodConfig.Clone();
						break;
					}
				}
			}
		}

		// Token: 0x06000276 RID: 630 RVA: 0x0000A26C File Offset: 0x0000846C
		void PrepareROIConfig(ResultDataFile resultDataFile)
		{
			ROIConfigManager roiconfigManager = ROIConfigManager.GetInstance();
			foreach (ResultData resultData in resultDataFile.ResultDataList)
			{
				for (int i = 0; i < roiconfigManager.ROIConfigList.Count; i++)
				{
					ROIConfigData roiconfigData = roiconfigManager.ROIConfigList[i];
					if (resultData.ROIConfigReference.Guid == roiconfigData.Guid)
					{
						resultData.ROIConfig = roiconfigData;
						break;
					}
				}
			}
		}

		// Token: 0x06000277 RID: 631 RVA: 0x0000A318 File Offset: 0x00008518
		public void LoadBackgroundSpectrum(ResultData resultData)
		{
			string backgroundSpectrumPathname = resultData.BackgroundSpectrumPathname;
			if (backgroundSpectrumPathname != null && backgroundSpectrumPathname != "")
			{
				ResultDataFile resultDataFile;
				try
				{
					using (FileStream fileStream = new FileStream(backgroundSpectrumPathname, FileMode.Open))
					{
						XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
						resultDataFile = (ResultDataFile)xmlSerializer.Deserialize(fileStream);
					}
					bool flag = false;
					foreach (ResultData resultData2 in resultDataFile.ResultDataList)
					{
						if (resultData2.EnergySpectrum.EnergyCalibration == null)
						{
							flag = true;
							break;
						}
					}
					if (flag)
					{
						using (FileStream fileStream2 = new FileStream(backgroundSpectrumPathname, FileMode.Open))
						{
							XmlSerializer xmlSerializer2 = new XmlSerializer(typeof(ResultDataFile_097b));
							resultDataFile = new ResultDataFile((ResultDataFile_097b)xmlSerializer2.Deserialize(fileStream2));
						}
					}
				}
				catch (Exception)
				{
					try
					{
						using (FileStream fileStream3 = new FileStream(backgroundSpectrumPathname, FileMode.Open))
						{
							XmlSerializer xmlSerializer3 = new XmlSerializer(typeof(ResultData));
							ResultData item = (ResultData)xmlSerializer3.Deserialize(fileStream3);
							resultDataFile = new ResultDataFile();
							resultDataFile.ResultDataList.Add(item);
						}
					}
					catch (Exception)
					{
						MessageBox.Show(string.Format(Resources.ERRBackgroundLoadFailure, Path.GetFileName(backgroundSpectrumPathname)));
						return;
					}
				}
				resultData.BackgroundEnergySpectrum = resultDataFile.ResultDataList[0].EnergySpectrum;
			}
		}

		// Token: 0x06000278 RID: 632 RVA: 0x0000A4F8 File Offset: 0x000086F8
		public void ExportDocumentToCsv(DocEnergySpectrum doc)
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog();
			saveFileDialog.Title = Resources.CsvExportDialogTitle;
			saveFileDialog.Filter = Resources.CsvFileFilter;
			saveFileDialog.FilterIndex = 1;
			saveFileDialog.RestoreDirectory = true;
			if (saveFileDialog.ShowDialog() != DialogResult.OK)
			{
				return;
			}
			string fileName = saveFileDialog.FileName;
			EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
			try
			{
				using (StreamWriter streamWriter = new StreamWriter(fileName, false, Encoding.GetEncoding(932)))
				{
					for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
					{
						streamWriter.WriteLine(i + "," + energySpectrum.Spectrum[i]);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, fileName, ex.Message));
			}
		}

		// Token: 0x06000279 RID: 633 RVA: 0x0000A5F0 File Offset: 0x000087F0
		public void ImportCsvToDocument(DocEnergySpectrum doc, int presetTime)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = Resources.CsvImportDialogTitle;
			openFileDialog.Filter = Resources.CsvFileFilter;
			openFileDialog.FilterIndex = 1;
			openFileDialog.RestoreDirectory = true;
			if (openFileDialog.ShowDialog() != DialogResult.OK)
			{
				return;
			}
			string fileName = openFileDialog.FileName;
			List<int> list = new List<int>();
			int num = 0;
			try
			{
				using (StreamReader streamReader = new StreamReader(fileName, Encoding.GetEncoding(932)))
				{
					while (streamReader.Peek() != -1)
					{
						string[] array = streamReader.ReadLine().Split(new char[]
						{
							','
						});
						if (array.Length >= 2)
						{
							int.Parse(array[0]);
							int num2 = int.Parse(array[1]);
							list.Add(num2);
							num += num2;
						}
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, fileName, ex.Message));
			}
			EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
			energySpectrum.Initialize();
			int num3 = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (i < energySpectrum.NumberOfChannels)
				{
					energySpectrum.Spectrum[i] = list[i];
					num3 += list[i];
				}
			}
			energySpectrum.MeasurementTime = (double)presetTime;
			energySpectrum.TotalPulseCount = num;
			energySpectrum.ValidPulseCount = num3;
			ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
			resultDataStatus.TotalTime = TimeSpan.FromSeconds((double)presetTime);
			resultDataStatus.ElapsedTime = TimeSpan.FromSeconds((double)presetTime);
		}

		// Token: 0x040000B9 RID: 185
		const string extension = ".xml";

		// Token: 0x040000BA RID: 186
		int serial = 1;

		// Token: 0x040000BB RID: 187
		List<DocEnergySpectrum> documentList = new List<DocEnergySpectrum>();

		// Token: 0x040000BC RID: 188
		static DocumentManager instance = new DocumentManager();

		// Token: 0x040000BD RID: 189
		double defaultEnergyScale;
	}
}
