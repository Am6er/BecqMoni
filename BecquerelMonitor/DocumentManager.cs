using BecquerelMonitor.N42;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

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
            if (!this.CheckDocument(docEnergySpectrum.ResultDataFile))
            {
                string text = String.Format(Resources.ERRFileOpenFailure, filename, Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.No) return null;
                this.CheckDocument(docEnergySpectrum.ResultDataFile, doCorrections: true);
                docEnergySpectrum.Dirty = true;
            }
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

        bool CheckDocument(ResultDataFile resultDataFile, bool doCorrections = false)
        {
            try
            {
                foreach (ResultData data in resultDataFile.ResultDataList)
                {
                    //Check Spectrum
                    if (data.EnergySpectrum.Spectrum == null) return false;
                    if (data.EnergySpectrum.Spectrum.Length != data.EnergySpectrum.NumberOfChannels)
                    {
                        data.EnergySpectrum.NumberOfChannels = data.EnergySpectrum.Spectrum.Length;
                    }

                    //Check Calibration
                    if (data.EnergySpectrum.EnergyCalibration == null)
                    {
                        if (doCorrections)
                        {
                            data.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                        }
                        else
                        {
                            return false;
                        }
                    }
                    PolynomialEnergyCalibration pol = (PolynomialEnergyCalibration)data.EnergySpectrum.EnergyCalibration;
                    if (!pol.CheckCalibration(channels: data.EnergySpectrum.NumberOfChannels))
                    {
                        if (doCorrections)
                        {
                            int zerosCount = 0;
                            for (int i = pol.Coefficients.Length - 1; i >= 0; i--)
                            {
                                if(pol.Coefficients[i] == 0.0)
                                {
                                    zerosCount++;
                                } else
                                {
                                    break;
                                }
                            }
                            if (zerosCount > pol.Coefficients.Length - 2 || zerosCount == 0 || (pol.Coefficients.Length == 2 && zerosCount > 0))
                            {
                                data.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                            } else
                            {
                                data.EnergySpectrum.EnergyCalibration = pol.Downgrade(zerosCount);
                            }
                        } else
                        {
                            return false;
                        }
                    }

                    //Same checks for Background
                    if (data.BackgroundEnergySpectrum != null)
                    {
                        //Check Spectrum
                        if (data.BackgroundEnergySpectrum.Spectrum == null)
                        {
                            if (doCorrections)
                            {
                                data.BackgroundEnergySpectrum = null;
                                continue;
                            } else
                            {
                                return false;
                            }
                        }
                        if (data.BackgroundEnergySpectrum.Spectrum.Length != data.BackgroundEnergySpectrum.NumberOfChannels)
                        {
                            data.BackgroundEnergySpectrum.NumberOfChannels = data.BackgroundEnergySpectrum.Spectrum.Length;
                        }

                        //Check Calibration
                        if (data.BackgroundEnergySpectrum.EnergyCalibration == null)
                        {
                            if (doCorrections)
                            {
                                data.BackgroundEnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                            }
                            else
                            {
                                return false;
                            }
                        }
                        pol = (PolynomialEnergyCalibration)data.BackgroundEnergySpectrum.EnergyCalibration;
                        if (!pol.CheckCalibration(channels: data.BackgroundEnergySpectrum.NumberOfChannels))
                        {
                            if (doCorrections)
                            {
                                int zerosCount = 0;
                                for (int i = pol.Coefficients.Length - 1; i >= 0; i--)
                                {
                                    if (pol.Coefficients[i] == 0.0)
                                    {
                                        zerosCount++;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                if (zerosCount > pol.Coefficients.Length - 2 || zerosCount == 0)
                                {
                                    data.BackgroundEnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                                }
                                else if (zerosCount == 1)
                                {
                                    data.BackgroundEnergySpectrum.EnergyCalibration = pol.Downgrade(1);
                                }
                                else
                                {
                                    data.BackgroundEnergySpectrum.EnergyCalibration = pol.Downgrade(zerosCount);
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    // Add fwhm calibration if it doesn't exist
                    if (data.FwhmCalibration == null)
                    {
                        FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)data.PeakDetectionMethodConfig;
                        data.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, data.EnergySpectrum.EnergyCalibration);
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        /*
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
        */

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

                if (!this.CheckDocument(docEnergySpectrum2.ResultDataFile))
                {
                    string text = String.Format(Resources.ERRFileOpenFailure, filename, Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                    DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.No) return null;
                    this.CheckDocument(docEnergySpectrum2.ResultDataFile, doCorrections: true);
                    docEnergySpectrum2.Dirty = true;
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message + " " + ex.InnerException.Message));
                } else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message));
                }
                
                Cursor.Current = Cursors.Default;
                return null;
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
                if (resultData2.EnergySpectrum.TotalPulseCount == 0)
                {
                    resultData2.EnergySpectrum.TotalPulseCount = resultData2.EnergySpectrum.Spectrum.Sum(x => (long)x);
                    resultData2.EnergySpectrum.ValidPulseCount = resultData2.EnergySpectrum.TotalPulseCount;
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

                if (!this.CheckDocument(resultDataFile))
                {
                    string text = String.Format(Resources.ERRFileOpenFailure, pathname, Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                    DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.No) return null;
                    this.CheckDocument(resultDataFile, doCorrections: true);
                    doc.Dirty = true;
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, pathname, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, pathname, ex.Message));
                }
                Cursor.Current = Cursors.Default;
                return null;
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
                if (resultData2.EnergySpectrum.TotalPulseCount == 0)
                {
                    resultData2.EnergySpectrum.TotalPulseCount = resultData2.EnergySpectrum.Spectrum.Sum(x => (long)x);
                    resultData2.EnergySpectrum.ValidPulseCount = resultData2.EnergySpectrum.TotalPulseCount;
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
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message));
                }
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

        public void ImportDocumentGBS(DocEnergySpectrum doc, string filePath)
        {
            GC.Collect();

            SampleInfoData info = doc.ActiveResultData.SampleInfo;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                using (StreamReader streamReader = new StreamReader(filePath, Encoding.GetEncoding("UTF-8")))
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    info.Name = fileName;
                    doc.Filename = fileName + ".xml";
                    doc.Text = fileName;

                    // From https://www.gbs-elektronik.de/media/download_gallery/Ritecdat_e.pdf
                    // $SPEC_ID: or $APPLICATION_ID: or $DEVICE_ID:
                    string fileheader = streamReader.ReadLine();
                    if (fileheader != "$SPEC_ID:" && fileheader != "$APPLICATION_ID:" && fileheader != "$DEVICE_ID:")
                    {
                        throw new Exception(String.Format(Resources.ERROpenGBSFormat, fileheader));
                    }

                    string SpectrumSummaryText = streamReader.ReadLine();
                    info.Note = SpectrumSummaryText;

                    // $DATE_MEA:
                    fileheader = streamReader.ReadLine();
                    while (true)
                    {
                        if (fileheader == "$DATE_MEA:") break;
                        fileheader = streamReader.ReadLine();
                    }

                    // 11/30/2024 21:02:07
                    string Time1 = streamReader.ReadLine();
                    info.Time = DateTime.ParseExact(Time1, "MM/dd/yyyy H:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                    doc.ActiveResultData.StartTime = info.Time;

                    // $MEAS_TIM:
                    fileheader = streamReader.ReadLine();
                    while (true)
                    {
                        if (fileheader == "$MEAS_TIM:") break;
                        fileheader = streamReader.ReadLine();
                    }

                    string LiveTimeRealTime = streamReader.ReadLine();

                    // $DATA:
                    fileheader = streamReader.ReadLine();
                    while (true)
                    {
                        if (fileheader == "$DATA:") break;
                        fileheader = streamReader.ReadLine();
                    }

                    int numberOfChannels = XmlConvert.ToInt32(streamReader.ReadLine().Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries)[1].Trim()) + 1;

                    bool importWithEmtyConfig = GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig;
                    bool channelMismatch = doc.ActiveResultData.EnergySpectrum.NumberOfChannels != numberOfChannels;
                    if (channelMismatch || importWithEmtyConfig)
                    {
                        this.ResetSpectrumConfig(doc.ActiveResultData, numberOfChannels);
                    }

                    EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
                    energySpectrum.Initialize();
                    ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;

                    energySpectrum.LiveTime = XmlConvert.ToDouble(LiveTimeRealTime.Split(new string[] { "  " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim());
                    energySpectrum.MeasurementTime = XmlConvert.ToDouble(LiveTimeRealTime.Split(new string[] { "  " }, StringSplitOptions.RemoveEmptyEntries)[1].Trim());
                    resultDataStatus.TotalTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
                    resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(energySpectrum.MeasurementTime);
                    resultDataStatus.PresetTime = (int)energySpectrum.MeasurementTime;
                    doc.ActiveResultData.EndTime = doc.ActiveResultData.StartTime.AddSeconds(energySpectrum.MeasurementTime);

                    for (int i = 0; i < energySpectrum.Spectrum.Length; i++)
                    {
                        energySpectrum.Spectrum[i] = XmlConvert.ToInt32(streamReader.ReadLine());
                    }

                    // $ENER_DATA:
                    fileheader = streamReader.ReadLine();
                    while (true)
                    {
                        if (fileheader == "$ENER_DATA_X:") break;
                        fileheader = streamReader.ReadLine();
                    }

                    int numpoints = XmlConvert.ToInt32(streamReader.ReadLine().Trim());

                    // read base calibration
                    List<CalibrationPoint> points = new List<CalibrationPoint>();
                    for (int i = 0; i < numpoints; i++)
                    {
                        string calibrationData = streamReader.ReadLine();
                        int channel = XmlConvert.ToInt32(calibrationData.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim());
                        decimal energy = XmlConvert.ToDecimal(calibrationData.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries)[1].Trim());
                        int count = energySpectrum.Spectrum[channel];
                        CalibrationPoint point = new CalibrationPoint(channel, energy, count);
                        points.Add(point);
                    }

                    double[] matrix = Utils.CalibrationSolver.Solve(points, numpoints - 1);
                    PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
                    energyCalibration.Coefficients = new double[matrix.Length];
                    energyCalibration.PolynomialOrder = matrix.Length - 1;
                    energyCalibration.Coefficients = matrix;

                    if (!energyCalibration.CheckCalibration(channels: energySpectrum.NumberOfChannels))
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                    }

                    // $COUNTS:
                    fileheader = streamReader.ReadLine();
                    while (true)
                    {
                        if (fileheader == "$COUNTS:") break;
                        fileheader = streamReader.ReadLine();
                    }

                    long TotalPulseCount = XmlConvert.ToInt64(streamReader.ReadLine().Trim());
                    energySpectrum.TotalPulseCount = TotalPulseCount;
                    energySpectrum.ValidPulseCount = TotalPulseCount;

                    streamReader.Close();
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message + " " + ex.StackTrace));
                }
            }
            Cursor.Current = Cursors.Default;
        }

        public void ImportDocumentAtomSpectra(DocEnergySpectrum doc, string filePath)
        {
            GC.Collect();

            try
            {
                using (StreamReader streamReader = new StreamReader(filePath, Encoding.GetEncoding("UTF-8")))
                {
                    string NumOfChannels = streamReader.ReadLine();
                    for (int i = 0; i < 9; i++)
                    {
                        NumOfChannels = streamReader.ReadLine();
                    }

                    bool importWithEmtyConfig = GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig;
                    bool channelMismatch = doc.ActiveResultData.EnergySpectrum.NumberOfChannels != Convert.ToInt32(NumOfChannels);
                    if (importWithEmtyConfig || channelMismatch)
                    {
                        if (!importWithEmtyConfig)
                        {
                            MessageBox.Show(String.Format(Resources.ERRImportAtomSpectra,
                                doc.ActiveResultData.EnergySpectrum.NumberOfChannels,
                                NumOfChannels), Resources.Warning, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }

                        this.ResetSpectrumConfig(doc.ActiveResultData, Convert.ToInt32(NumOfChannels));
                    }
                }
                if (!this.CheckDocument(doc.ResultDataFile))
                {
                    string text = String.Format(Resources.ERRFileOpenFailure, filePath, Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                    DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.No) return;
                    this.CheckDocument(doc.ResultDataFile, doCorrections: true);
                    doc.Dirty = true;
                }
            } 
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message, ex.StackTrace));
                }
                return;
            }
            

            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();
            ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
            SampleInfoData info = doc.ActiveResultData.SampleInfo;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                using (StreamReader streamReader = new StreamReader(filePath, Encoding.GetEncoding("UTF-8")))
                {

                    string fileformat = streamReader.ReadLine();
                    if (fileformat != "FORMAT: 3")
                    {
                        throw new Exception(String.Format(Resources.ERROpenAtomSpectraFormat, fileformat));
                    }
                    else
                    {
                        string SpectrumSummaryText = streamReader.ReadLine();
                        long TotalPulseCount = long.Parse(SpectrumSummaryText.Split(new string[] { "," }, StringSplitOptions.None)[0].Split(new string[] { "Counts: " }, StringSplitOptions.None)[1]);

                        energySpectrum.TotalPulseCount = TotalPulseCount;
                        energySpectrum.ValidPulseCount = TotalPulseCount;

                        string Time1 = streamReader.ReadLine();
                        string Time2 = streamReader.ReadLine();
                        string Lattitude = streamReader.ReadLine();
                        string Longitude = streamReader.ReadLine();
                        string SpectrumName = streamReader.ReadLine();

                        TimeSpan time = TimeSpan.FromMilliseconds(double.Parse(Time1));
                        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                        info.Time = dateTime.Add(time).ToLocalTime();
                        

                        info.Note = SpectrumSummaryText;
                        info.Name = SpectrumName;
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        doc.Filename = fileName + ".xml";
                        doc.Text = fileName;
                        info.Location = Lattitude + ", " + Longitude;

                        if (streamReader.ReadLine() != "")
                        {
                            MessageBox.Show(String.Format(Resources.ERRExpectedLineSeparator, fileformat));
                            return;
                        }

                        int ElapsedTime = (int)XmlConvert.ToDouble(streamReader.ReadLine());
                        energySpectrum.MeasurementTime = ElapsedTime;
                        resultDataStatus.TotalTime = TimeSpan.FromSeconds(ElapsedTime);
                        resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(ElapsedTime);
                        resultDataStatus.PresetTime = ElapsedTime;

                        int NumberOfChanels = int.Parse(streamReader.ReadLine());
                        int PolynomialOrder = int.Parse(streamReader.ReadLine());

                        if (PolynomialOrder > 4)
                        {
                            throw new Exception(String.Format(Resources.ERRUnsupportedCalibrationOrder, PolynomialOrder));
                        }

                        double[] coefficients = new double[PolynomialOrder + 1];

                        for (int i = 0; i < coefficients.Length; i++)
                        {
                            coefficients[i] = XmlConvert.ToDouble(streamReader.ReadLine().Replace(',', '.'));
                        }

                        for (int i = 0; i < energySpectrum.Spectrum.Length; i++)
                        {
                            energySpectrum.Spectrum[i] = XmlConvert.ToInt32(streamReader.ReadLine());
                        }

                        streamReader.Close();

                        PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
                        energyCalibration.PolynomialOrder = PolynomialOrder;
                        energyCalibration.Coefficients = new double[PolynomialOrder + 1];
                        for (int i = 0; i < coefficients.Length; i++)
                        {
                            energyCalibration.Coefficients[i] = coefficients[i];
                        }

                        if (!energyCalibration.CheckCalibration(channels: energySpectrum.NumberOfChannels))
                        {
                            MessageBox.Show(Resources.CalibrationFunctionError);
                        }

                    }
                }
                Cursor.Current = Cursors.Default;
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filePath, ex.Message, ex.StackTrace));
                }
                return;
            }
        }

        public void ImportDocumentN42(DocEnergySpectrum doc, string filename)
        {
            GC.Collect();

            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();

            bool importWithEmtyConfig = GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig;
            if (importWithEmtyConfig)
            {
                this.ResetSpectrumConfig(doc.ActiveResultData, 1024);
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                XmlSerializer ser = new XmlSerializer(typeof(RadInstrumentData));

                //Add DHS namespace for Interspec compatibility
                XmlDocument xmldoc = new XmlDocument();
                XmlReaderSettings settings = new XmlReaderSettings { NameTable = new NameTable() };
                XmlNamespaceManager xmlns = new XmlNamespaceManager(settings.NameTable);
                xmlns.AddNamespace("DHS", "http://www.w3.org/2001/XMLSchema-instance");
                xmlns.AddNamespace("H3D", "http://hz/XMLSchema-instance");
                XmlParserContext context = new XmlParserContext(null, xmlns, "", XmlSpace.Default);
                //Add DHS namespace for Interspec compatibility

                RadInstrumentData radInstrumentData = new RadInstrumentData();
                using (XmlReader reader = XmlReader.Create(filename, settings, context))
                {
                    radInstrumentData = (RadInstrumentData)ser.Deserialize(reader);
                }
                Util util = new Util();
                util.ImportFromN42(radInstrumentData, doc, filename);

                if (!this.CheckDocument(doc.ResultDataFile))
                {
                    string text = String.Format(Resources.ERRFileOpenFailure, filename, Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                    DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.No) return;
                    this.CheckDocument(doc.ResultDataFile, doCorrections: true);
                    doc.Dirty = true;
                }

                Cursor.Current = Cursors.Default;
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message + " " + ex.InnerException.Message, ex.StackTrace));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, filename, ex.Message, ex.StackTrace));
                }
                return;
            }
        }

        public void ExportDocumentAtomSpectra(DocEnergySpectrum doc)
        {
            GC.Collect();
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = Resources.AtomSpectraExportDialogTitle;
            saveFileDialog.Filter = Resources.AtomSpectraFileFilter;
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.FileName = doc.Text.Trim(new char[] { '*', ' ' }) + ".txt";
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            Cursor.Current = Cursors.WaitCursor;
            string fileName = saveFileDialog.FileName;

            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            SampleInfoData info = doc.ActiveResultData.SampleInfo;
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)energySpectrum.EnergyCalibration;
            try
            {
                using (var writer = new System.IO.StreamWriter(fileName))
                {
                    //FORMAT: 3
                    writer.WriteLine("FORMAT: 3");
                    //2022.02.04 14:21:15 +0300 Counts: 41129508, ~cps: 227.430, Time: 180845.00 s, Coord: 55°40'56.189" N 37°35'40.792" E at 2022.02.04 14:20:47 +0300
                    string title = info.Time.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss zzzz");
                    title += " Counts: " + energySpectrum.TotalPulseCount;
                    title += ", ~cps: " + (energySpectrum.TotalPulseCount / energySpectrum.MeasurementTime).ToString("f3");
                    title += ", Time: " + energySpectrum.MeasurementTime + " s";
                    writer.WriteLine(title);
                    //1643973675060 Measurement time
                    double miliseconds = info.Time.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    writer.WriteLine(Math.Round(miliseconds));
                    //1643973647530 GPS taken time
                    writer.WriteLine("0");
                    //55.682275
                    writer.WriteLine("0");
                    //37.594665
                    writer.WriteLine("0");
                    //Am241
                    writer.WriteLine(doc.Text.Trim(new char[] { '*', ' ' }));
                    //Empty Line
                    writer.WriteLine("");
                    //180845.000000
                    writer.WriteLine(energySpectrum.MeasurementTime);
                    //8192
                    writer.WriteLine(energySpectrum.NumberOfChannels);
                    //4
                    writer.WriteLine(polynomialEnergyCalibration.PolynomialOrder);
                    //Write coefficients
                    for (int i = 0; i <= polynomialEnergyCalibration.PolynomialOrder; i++)
                    {
                        writer.WriteLine(polynomialEnergyCalibration.Coefficients[i]);
                    }
                    //Write Channels
                    for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
                    {
                        writer.WriteLine(energySpectrum.Spectrum[i]);
                    }
                    writer.Flush();
                }

            } catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, fileName, ex.Message));
            }
            Cursor.Current = Cursors.Default;
        }

        public void ExportDocumentN42(DocEnergySpectrum doc)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = Resources.N42ExportDialogTitle;
            saveFileDialog.Filter = Resources.N42FileFilter;
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.FileName = doc.Text.Trim(new char[] { '*', ' ' }) + ".N42";
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            Cursor.Current = Cursors.WaitCursor;
            string fileName = saveFileDialog.FileName;
            try
            {
                Util util = new Util();
                RadInstrumentData radN42Object = util.ExportToN42(doc);
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(RadInstrumentData));

                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                using (XmlWriter writer = XmlWriter.Create(fileStream, xmlSettings))
                {
                    xmlSerializer.Serialize(writer, radN42Object);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, fileName, ex.Message));
            }
            Cursor.Current = Cursors.Default;
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
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));

                using (FileStream fileStream = new FileStream(doc.Filename, FileMode.Create))
                using (XmlWriter writer = XmlWriter.Create(fileStream, xmlSettings))
                {
                    xmlSerializer.Serialize(writer, resultDataFile);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, doc.Filename, ex.Message));
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
            doc.Dirty = doc.ActiveResultData.ResultDataStatus.Recording;
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
            if (doc.Filename.Length > 1)
            {
                saveFileDialog.FileName = doc.Filename;

            }
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
                        FWHMPeakDetectionMethodConfig simplePeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)deviceConfigInfo.PeakDetectionMethodConfig;
                        resultData.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)simplePeakDetectionMethodConfig.Clone();
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

                    if (!this.CheckDocument(resultDataFile))
                    {
                        string text = String.Format(Resources.ERRFileOpenFailure, Path.GetFileName(backgroundSpectrumPathname),
                            Resources.ERRSpectrumCheck) + "\n" + Resources.CalcResetQuestion;
                        DialogResult res = MessageBox.Show(text, Resources.ResetCalibrationQuestion, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (res == DialogResult.No) return;
                        this.CheckDocument(resultDataFile, doCorrections: true);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        MessageBox.Show(string.Format(Resources.ERRBackgroundLoadFailure, backgroundSpectrumPathname, ex.Message));
                    }
                    catch
                    {
                        MessageBox.Show(Resources.ERRBackgroundLoadFailure);
                    }
                    
                    return;
                }
                if (resultDataFile.ResultDataList[0].EnergySpectrum.TotalPulseCount == 0)
                {
                    resultDataFile.ResultDataList[0].EnergySpectrum.TotalPulseCount = resultDataFile.ResultDataList[0].EnergySpectrum.Spectrum.Sum(x => (long)x);
                    resultDataFile.ResultDataList[0].EnergySpectrum.ValidPulseCount = resultDataFile.ResultDataList[0].EnergySpectrum.TotalPulseCount;
                }
                resultData.BackgroundEnergySpectrum = resultDataFile.ResultDataList[0].EnergySpectrum;
            }
        }

        public void SplitDocEnergySpectrum(DocEnergySpectrum doc)
        {
            ResultData resultData = doc.ActiveResultData.Clone();
            resultData.EnergySpectrum = resultData.BackgroundEnergySpectrum.Clone();
            resultData.SampleInfo.Name = Path.GetFileNameWithoutExtension(resultData.BackgroundSpectrumFile);
            resultData.MeasurementController = doc.ActiveResultData.MeasurementController;
            resultData.ROIConfig = doc.ActiveResultData.ROIConfig;
            resultData.ROIConfigReference = doc.ActiveResultData.ROIConfigReference;
            resultData.ResultDataStatus = doc.ActiveResultData.ResultDataStatus.Clone();
            resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(resultData.EnergySpectrum.MeasurementTime);
            resultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(resultData.EnergySpectrum.MeasurementTime);
            resultData.ResultDataStatus.TimeInSamples = resultData.EnergySpectrum.NumberOfSamples;
            resultData.BackgroundEnergySpectrum = null;
            resultData.BackgroundSpectrumFile = "";
            resultData.BackgroundSpectrumPathname = "";
            resultData.Dirty = true;
            doc.ResultDataFile.ResultDataList.Add(resultData);
            if (doc.ActiveResultData.SampleInfo.Name == "") doc.ActiveResultData.SampleInfo.Name = Path.GetFileNameWithoutExtension(doc.Filename);
            doc.ActiveResultData.BackgroundEnergySpectrum = null;
            doc.ActiveResultData.BackgroundSpectrumFile = "";
            doc.ActiveResultData.BackgroundSpectrumPathname = "";
            //doc.ActiveResultDataIndex = doc.ResultDataFile.ResultDataList.Count - 1;
            doc.Dirty = true;
        }

        // Token: 0x06000278 RID: 632 RVA: 0x0000A4F8 File Offset: 0x000086F8
        public void ExportDocumentToCsv(DocEnergySpectrum doc)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = Resources.CsvExportDialogTitle;
            saveFileDialog.Filter = Resources.CsvFileFilter;
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.FileName = doc.Text.Trim(new char[] { '*', ' ' }) + ".csv";
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            string fileName = saveFileDialog.FileName;
            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            try
            {
                using (StreamWriter streamWriter = new StreamWriter(fileName, false, Encoding.GetEncoding(65001)))
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

        public void ExportDocumentToECSV(DocEnergySpectrum doc, BackgroundMode bgMode, SmoothingMethod smoothMethod)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = Resources.CsvExportDialogTitle;
            saveFileDialog.Filter = Resources.CsvFileFilter;
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.FileName = doc.Text.Trim(new char[] { '*', ' ' }) + ".csv";
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            string fileName = saveFileDialog.FileName;
            PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)doc.ActiveResultData.EnergySpectrum.EnergyCalibration;

            EnergySpectrum energySpectrum;
            double[] dSpectrum = new double[doc.ActiveResultData.EnergySpectrum.NumberOfChannels];
            SpectrumAriphmetics sa = new SpectrumAriphmetics();
            if (bgMode == BackgroundMode.Substract && doc.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                sa = new SpectrumAriphmetics(doc.ActiveResultData.EnergySpectrum);
                energySpectrum = sa.Substract(doc.ActiveResultData.BackgroundEnergySpectrum);
            }
            else
            {
                energySpectrum = doc.ActiveResultData.EnergySpectrum.Clone();
            }
            int countlimit = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.CountLimit;
            bool progressiveSmooth = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.ProgresiveSmooth;
            switch (smoothMethod)
            {
                case SmoothingMethod.SimpleMovingAverage:
                    int points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfSMADataPoints;
                    dSpectrum = sa.SMA2(Array.ConvertAll(energySpectrum.Spectrum, i => (double)i), points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                case SmoothingMethod.WeightedMovingAverage:
                    points = GlobalConfigManager.GetInstance().GlobalConfig.ChartViewConfig.NumberOfWMADataPoints;
                    dSpectrum = sa.WMA2(Array.ConvertAll(energySpectrum.Spectrum, i => (double)i), points, countlimit: countlimit, progressive: progressiveSmooth);
                    break;
                default:
                    dSpectrum = Array.ConvertAll(energySpectrum.Spectrum, i => (double)i);
                    break;
            }

            try
            {
                using (StreamWriter streamWriter = new StreamWriter(fileName, false, Encoding.GetEncoding(65001)))
                {
                    streamWriter.WriteLine("channel, energy, count");
                    for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
                    {
                        streamWriter.WriteLine(i + "," + cal.ChannelToEnergy(i) + "," + dSpectrum[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.ERRFileSaveFailure, fileName, ex.Message));
            }
            sa.Dispose();
        }

        // Token: 0x06000279 RID: 633 RVA: 0x0000A5F0 File Offset: 0x000087F0
        public void ImportCsvToDocument(DocEnergySpectrum doc, int presetTime, string fileName)
        {
            List<int> list = new List<int>();
            long totalpulsecount = 0;
            try
            {
                using (StreamReader streamReader = new StreamReader(fileName, Encoding.GetEncoding(65001)))
                {
                    while (streamReader.Peek() != -1)
                    {
                        string[] array = streamReader.ReadLine().Split(new char[] { ',' });
                        if (array.Length >= 2)
                        {
                            int.Parse(array[0]);
                            int count = int.Parse(array[1]);
                            list.Add(count);
                            totalpulsecount += count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Message != null)
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, fileName, ex.Message + " " + ex.InnerException.Message));
                }
                else
                {
                    MessageBox.Show(string.Format(Resources.ERRFileOpenFailure, fileName, ex.Message));
                }
            }
            
            bool importWithEmtyConfig = GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig;
            if (importWithEmtyConfig)
            {
                this.ResetSpectrumConfig(doc.ActiveResultData, list.Count);
            }

            EnergySpectrum energySpectrum = doc.ActiveResultData.EnergySpectrum;
            energySpectrum.Initialize();
            long validpulsecount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (i < energySpectrum.NumberOfChannels)
                {
                    energySpectrum.Spectrum[i] = list[i];
                    validpulsecount += list[i];
                }
            }
            energySpectrum.MeasurementTime = (double)presetTime;
            energySpectrum.TotalPulseCount = totalpulsecount;
            energySpectrum.ValidPulseCount = validpulsecount;
            ResultDataStatus resultDataStatus = doc.ActiveResultData.ResultDataStatus;
            resultDataStatus.TotalTime = TimeSpan.FromSeconds((double)presetTime);
            resultDataStatus.ElapsedTime = TimeSpan.FromSeconds((double)presetTime);
        }

        private void ResetSpectrumConfig(ResultData data, int numberOfChannels)
        {
            data.EnergySpectrum = new EnergySpectrum(1.0, numberOfChannels);
            data.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
            data.BackgroundEnergySpectrum = null;
            data.BackgroundSpectrumPathname = null;
            data.BackgroundSpectrumFile = null;
            data.ROIConfig = null;
            data.ROIConfigReference = null;
            data.DeviceConfig = new DeviceConfigInfo();
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

        XmlWriterSettings xmlSettings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true
        };
    }
}
