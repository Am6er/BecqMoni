using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace BecquerelMonitor.N42
{
    public class Util
    {
        public Util()
        {

        }

        public RadInstrumentData ExportToN42(DocEnergySpectrum doc)
        {
            RadInstrumentData rad = new RadInstrumentData();

            //RadInstrumentInformation
            rad.RadInstrumentInformation = new RadInstrumentInformation[1];
            rad.RadInstrumentInformation[0] = new RadInstrumentInformation();
            rad.RadInstrumentInformation[0].RadInstrumentManufacturerName = "KB Radar";
            rad.RadInstrumentInformation[0].RadInstrumentModelName = "ATOM Spectra";
            rad.RadInstrumentInformation[0].RadInstrumentVersion = new RadInstrumentVersion[3];
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentName = "Hardware";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[0].RadInstrumentComponentVersion = doc.ActiveResultData.DeviceConfig.Name;
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentName = "SoftwareName";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[1].RadInstrumentComponentVersion = "BecqMoni";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2] = new RadInstrumentVersion();
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentName = "Software";
            rad.RadInstrumentInformation[0].RadInstrumentVersion[2].RadInstrumentComponentVersion = GlobalConfigManager.GetInstance().VersionString;

            int SpectrumCount = doc.ResultDataFile.ResultDataList.Count;

            int bgcount = 0;
            for (int i = 0; i < SpectrumCount; i++)
            {
                if (doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum != null)
                {
                    bgcount++;
                }
            }

            rad.EnergyCalibration = new EnergyCalibration[SpectrumCount + bgcount];
            rad.RadMeasurement = new RadMeasurement[SpectrumCount + bgcount];

            int j = 0;
            for (int i = 0; i < SpectrumCount; i++)
            {
                string calibrationId = "SpectrumCalibration-" + i;
                string idMeasurement = "SpectrumMeasurement-" + i;
                string measurementClassCode = "Foreground";
                string idSpectrum = "SpectrumData";
                string idGrossCounts = "GrossForeground";
                rad.EnergyCalibration[j] = AddCalibration(doc.ResultDataFile.ResultDataList[i].EnergySpectrum, calibrationId);
                rad.RadMeasurement[j] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
                j++;

                if (doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum != null)
                {
                    calibrationId = "BackgroundCalibration-" + i;
                    idMeasurement = "BackgroundMeasurement-" + i;
                    measurementClassCode = "Background";
                    idSpectrum = "BackgroundData";
                    idGrossCounts = "GrossBackground";
                    rad.EnergyCalibration[j] = AddCalibration(doc.ResultDataFile.ResultDataList[i].BackgroundEnergySpectrum, calibrationId);
                    rad.RadMeasurement[j] = AddMeasurement(doc.ResultDataFile.ResultDataList[i], idMeasurement, measurementClassCode, idSpectrum, calibrationId, idGrossCounts);
                    j++;
                }
            }

            return rad;
        }

        private RadMeasurement AddMeasurement(ResultData data, string idMeasurement, string measurementClassCode, string idSpectrum, string idEnergyCalibration, string idGrossCounts)
        {
            RadMeasurement rad;
            if (measurementClassCode == "Foreground")
            {
                rad = new RadMeasurement
                {
                    id = idMeasurement, //"SpectrumMeasurement",
                    MeasurementClassCode = measurementClassCode, // "Foreground",
                    StartDateTime = data.StartTime.ToString(),
                    RealTimeDuration = "PT" + data.EnergySpectrum.MeasurementTime + "S",
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    LiveTimeDuration = "PT" + data.EnergySpectrum.MeasurementTime + "S"
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.EnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossForeground",
                    TotalCounts = data.EnergySpectrum.TotalPulseCount.ToString()
                };
            } else
            {
                rad = new RadMeasurement
                {
                    id = idMeasurement, //"SpectrumMeasurement",
                    MeasurementClassCode = measurementClassCode, // "Background",
                    StartDateTime = data.StartTime.ToString(),
                    RealTimeDuration = "PT" + data.BackgroundEnergySpectrum.MeasurementTime + "S",
                    //RadMeasurement -> Spectrum
                    Spectrum = new Spectrum[1]
                };
                rad.Spectrum[0] = new Spectrum
                {
                    id = idSpectrum, //"SpectrumData",
                    energyCalibrationReference = idEnergyCalibration, //rad.EnergyCalibration[0].id,
                    LiveTimeDuration = "PT" + data.BackgroundEnergySpectrum.MeasurementTime + "S"
                };
                rad.Spectrum[0].ChannelData.SpectrumFromArray(data.BackgroundEnergySpectrum.Spectrum);

                //RadMeasurement -> GrossCounts
                rad.GrossCounts = new GrossCounts[1];
                rad.GrossCounts[0] = new GrossCounts
                {
                    id = idGrossCounts, //"GrossBackground",
                    TotalCounts = data.BackgroundEnergySpectrum.TotalPulseCount.ToString()
                };
            }
            return rad;
        }

        private EnergyCalibration AddCalibration(EnergySpectrum input, string id)
        {
            PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)input.EnergyCalibration;
            EnergyCalibration output = new EnergyCalibration();
            output.id = id;
            for (int i = 0; i < energyCalibration.Coefficients.Length; i++)
            {
                output.CoefficientValues = output.CoefficientValues + energyCalibration.Coefficients[i].ToString() + " ";

            }
            output.CoefficientValues = output.CoefficientValues.Replace(',', '.');
            return output;
        }

        public DocEnergySpectrum ImportFromN42(RadInstrumentData rad, DocEnergySpectrum doc, string filename)
        {
            int SpectrumCount = rad.RadMeasurement.Length;

            string SpectrumName = Path.GetFileNameWithoutExtension(filename);
            doc.Filename = SpectrumName + ".xml";
            doc.Text = SpectrumName;

            for (int i = 0; i < SpectrumCount; i++)
            {
                RadMeasurement radMeasurement = rad.RadMeasurement[i];
                EnergyCalibration radCalibration;
                if (rad.EnergyCalibration.Length <= i)
                {
                    radCalibration = rad.EnergyCalibration[0];
                } else
                {
                    radCalibration = rad.EnergyCalibration[i];
                }
                ResultData resultData = new ResultData();
                resultData.MeasurementController = doc.ActiveResultData.MeasurementController;
                resultData.SampleInfo.Time = DateTime.Now;

                try
                {
                    if (radMeasurement.StartDateTime != null && radMeasurement.StartDateTime != "")
                    {
                        resultData.SampleInfo.Time = XmlConvert.ToDateTime(radMeasurement.StartDateTime); //"10/13/2021 07:14:57"
                    }
                }
                catch { }

                resultData.SampleInfo.Name = SpectrumName;
                resultData.SampleInfo.Note = "";

                try
                {
                    foreach (RadInstrumentInformation radIterator in rad.RadInstrumentInformation)
                    {
                        resultData.SampleInfo.Note = resultData.SampleInfo.Note + radIterator.RadInstrumentManufacturerName + " - " + radIterator.RadInstrumentModelName + Environment.NewLine;
                        foreach (RadInstrumentVersion radInsIterator in radIterator.RadInstrumentVersion)
                        {
                            resultData.SampleInfo.Note = resultData.SampleInfo.Note + radInsIterator.RadInstrumentComponentName + " - " + radInsIterator.RadInstrumentComponentVersion + Environment.NewLine;
                        }

                    }

                    if (rad.RadDetectorInformation != null)
                    {
                        foreach (RadDetectorInformation radDetIterator in rad.RadDetectorInformation)
                        {
                            resultData.SampleInfo.Note = resultData.SampleInfo.Note + radDetIterator.RadDetectorCategoryCode + " - " + radDetIterator.RadDetectorKindCode + Environment.NewLine;
                        }
                    }
                }
                catch
                { }
                int ElapsedTime = 0;
                if (radMeasurement.Spectrum[0].LiveTimeDuration != null && radMeasurement.Spectrum[0].LiveTimeDuration != "")
                {
                    ElapsedTime = (int)XmlConvert.ToTimeSpan(radMeasurement.Spectrum[0].LiveTimeDuration).TotalSeconds;
                }
                if (ElapsedTime == 0)
                {
                    if (radMeasurement.RealTimeDuration != null && radMeasurement.RealTimeDuration != "")
                    {
                        ElapsedTime = (int)XmlConvert.ToTimeSpan(radMeasurement.RealTimeDuration).TotalSeconds;
                    }
                }
                resultData.EnergySpectrum.MeasurementTime = ElapsedTime;
                resultData.ResultDataStatus.TotalTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.ElapsedTime = TimeSpan.FromSeconds(ElapsedTime);
                resultData.ResultDataStatus.PresetTime = ElapsedTime;

                string[] n42SpectrumCounts = radMeasurement.Spectrum[0].ChannelData.Value.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
                n42SpectrumCounts = Array.FindAll(n42SpectrumCounts, isNotN42SpectrumValid);
                int NumberOfChanels = n42SpectrumCounts.Length;
                resultData.EnergySpectrum.Spectrum = new int[NumberOfChanels];
                resultData.EnergySpectrum.NumberOfChannels = NumberOfChanels;
                resultData.EnergySpectrum.ChannelPitch = 1;
                for (int k = 0; k < NumberOfChanels; k++)
                {
                    resultData.EnergySpectrum.Spectrum[k] = int.Parse(n42SpectrumCounts[k]);
                }
                resultData.EnergySpectrum.TotalPulseCount = 0;
                if (radMeasurement.GrossCounts != null)
                {
                    resultData.EnergySpectrum.TotalPulseCount = int.Parse(radMeasurement.GrossCounts[0].TotalCounts);
                } else
                {
                    resultData.EnergySpectrum.TotalPulseCount = resultData.EnergySpectrum.Spectrum.Sum();
                }
                resultData.EnergySpectrum.ValidPulseCount = resultData.EnergySpectrum.TotalPulseCount;

                try
                {
                    resultData.EnergySpectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                    string[] n42CalibrationCoeff = radCalibration.CoefficientValues.Replace("\n", string.Empty).Split(new string[] { " " }, StringSplitOptions.None);
                    n42CalibrationCoeff = Array.FindAll(n42CalibrationCoeff, isNotN42SpectrumValid);
                    if (n42CalibrationCoeff.Length == 0)
                    {
                        //Empty coefficients
                        throw new Exception();
                    }
                    int PolynomialOrder = n42CalibrationCoeff.Length - 1;

                    if (PolynomialOrder > 5)
                    {
                        throw new Exception("Unsupported calibration points number. Got polynom order = " + PolynomialOrder);
                    }

                    double[] coefficients = new double[PolynomialOrder + 1];

                    for (int k = 0; k < coefficients.Length; k++)
                    {
                        coefficients[k] = double.Parse(n42CalibrationCoeff[k]);
                    }
                    
                    PolynomialEnergyCalibration energyCalibration = (PolynomialEnergyCalibration)resultData.EnergySpectrum.EnergyCalibration;
                    energyCalibration.PolynomialOrder = PolynomialOrder;
                    energyCalibration.Coefficients = coefficients;

                    if (!energyCalibration.CheckCalibration(channels: resultData.EnergySpectrum.NumberOfChannels))
                    {
                        MessageBox.Show(Resources.CalibrationFunctionError);
                    }
                }
                catch
                {
                    MessageBox.Show("N42 EnergyBoundaryValues not supported. Only calibration coefficients supported. Using default calibration y=x.");
                }
                if (i == 0)
                {
                    doc.ResultDataFile.ResultDataList[0] = resultData;
                } else
                {
                    doc.ResultDataFile.ResultDataList.Add(resultData);
                }
            }
            doc.ResultDataFile.ResultDataList[0].Visible = true;
            return doc;
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
